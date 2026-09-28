using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.Identity;
using SmartSurvey.Infrastructure.Identity;
using SmartSurvey.Infrastructure.Persistence;

namespace SmartSurvey.Infrastructure.Workspaces;

/// <summary>
/// Workspace administration for super admins. Reads run in system scope but only ever return
/// workspace metadata and counts — never surveys, responses or reports.
/// </summary>
/// <remarks>
/// Status changes are recorded twice: in the system log (with the acting super admin) and in the
/// workspace's own log (anonymously), so its admins learn why their workspace was switched off or on.
/// </remarks>
public sealed class PlatformWorkspaceService(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    IAuditService audit,
    IWorkspaceStatusProvider statuses,
    TimeProvider time) : IPlatformWorkspaceService
{
    /// <summary>Maximum length of a disable reason (matches the column).</summary>
    public const int MaxReasonLength = 500;

    private const int RecentCount = 5;
    private const int PendingCount = 10;

    private static readonly CreateWorkspaceRequestValidator CreateValidator = new();
    private static readonly UpdateWorkspaceRequestValidator UpdateValidator = new();
    private static readonly string AdminRole = AppRoles.Admin.ToUpperInvariant();
    private static readonly string SuperAdminRole = AppRoles.SuperAdmin.ToUpperInvariant();

    private DateTime UtcNow => time.GetUtcNow().UtcDateTime;

    /// <inheritdoc />
    public async Task<PagedResult<WorkspaceSummaryDto>> ListAsync(WorkspaceListQuery query, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        ArgumentNullException.ThrowIfNull(query);

        await using var db = await OpenAsync(ct);
        var workspaces = db.Workspaces.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            workspaces = workspaces.Where(w => w.Name.ToLower().Contains(term) || w.Slug.Contains(term));
        }

        if (query.Status is { } status)
        {
            workspaces = workspaces.Where(w => w.Status == status);
        }

        var total = await workspaces.CountAsync(ct);
        var page = workspaces
            .OrderByDescending(w => w.CreatedAt)
            .ThenBy(w => w.Name)
            .ThenBy(w => w.Id)
            .Skip(query.Skip)
            .Take(query.PageSize);

        return new PagedResult<WorkspaceSummaryDto>(await SummarizeAsync(db, page, ct), total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<WorkspaceSummaryDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        await using var db = await OpenAsync(ct);
        return (await SummarizeAsync(db, db.Workspaces.AsNoTracking().Where(w => w.Id == id), ct)).SingleOrDefault()
            ?? throw new NotFoundException(WorkspaceMapping.EntityType, id);
    }

    /// <inheritdoc />
    public async Task<WorkspaceSummaryDto> CreateAsync(CreateWorkspaceRequest request, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        CreateValidator.ValidateOrThrow(request);
        var email = request.AdminEmail.Trim();

        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await EnsureEmailFreeAsync(users, email);

        Workspace workspace;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var slug = await WorkspaceSlugAllocator.AllocateAsync(db, request.Slug, request.Name, nameof(CreateWorkspaceRequest.Slug), null, ct);
            workspace = new Workspace { Name = request.Name.Trim(), Slug = slug, StatusChangedAt = UtcNow };
            db.Workspaces.Add(workspace);
            await db.SaveChangesAsync(ct);

            var admin = new ApplicationUser
            {
                WorkspaceId = workspace.Id,
                UserName = email,
                Email = email,
                EmailConfirmed = true, // created by a super admin: no confirmation round-trip needed
                DisplayName = string.IsNullOrWhiteSpace(request.AdminDisplayName) ? null : request.AdminDisplayName.Trim(),
                CreatedAt = UtcNow,
                LockoutEnabled = true,
            };
            await EnsureRolesExistAsync(scope.ServiceProvider);
            IdentityErrors.ThrowIfFailed(await users.CreateAsync(admin, request.AdminPassword),
                nameof(CreateWorkspaceRequest.AdminEmail), nameof(CreateWorkspaceRequest.AdminPassword));
            IdentityErrors.ThrowIfFailed(await users.AddToRolesAsync(admin, AppRoles.WorkspaceRoles));
            await transaction.CommitAsync(ct);
        }

        await audit.LogAsync(AuditActions.WorkspaceCreated, WorkspaceMapping.EntityType, workspace.Id.ToString(),
            $"Created workspace \"{workspace.Name}\" ({workspace.Slug}) with the admin {email}.", ct);
        return await GetAsync(workspace.Id, ct);
    }

    /// <inheritdoc />
    public async Task<WorkspaceSummaryDto> UpdateAsync(Guid id, UpdateWorkspaceRequest request, CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        UpdateValidator.ValidateOrThrow(request);

        await using var db = await OpenAsync(ct);
        var workspace = await FindAsync(db, id, ct);
        var oldSlug = workspace.Slug;
        workspace.Slug = await WorkspaceSlugAllocator.AllocateAsync(db, request.Slug, request.Name, nameof(UpdateWorkspaceRequest.Slug), id, ct);
        workspace.Name = request.Name.Trim();
        workspace.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        workspace.ContactEmail = string.IsNullOrWhiteSpace(request.ContactEmail) ? null : request.ContactEmail.Trim();
        await db.SaveChangesAsync(ct);
        statuses.Invalidate(id);

        var slugNote = oldSlug == workspace.Slug ? string.Empty : $" Address changed from {oldSlug} to {workspace.Slug}.";
        await audit.LogAsync(AuditActions.WorkspaceUpdated, WorkspaceMapping.EntityType, id.ToString(),
            $"Workspace \"{workspace.Name}\" updated.{slugNote}", ct);
        return await GetAsync(id, ct);
    }

    /// <inheritdoc />
    public Task<WorkspaceSummaryDto> EnableAsync(Guid id, CancellationToken ct = default) =>
        ChangeStatusAsync(id, WorkspaceStatus.Active, reason: null, [WorkspaceStatus.Disabled, WorkspaceStatus.PendingApproval], ct);

    /// <inheritdoc />
    public Task<WorkspaceSummaryDto> ApproveAsync(Guid id, CancellationToken ct = default) =>
        ChangeStatusAsync(id, WorkspaceStatus.Active, reason: null, [WorkspaceStatus.PendingApproval], ct);

    /// <inheritdoc />
    public Task<WorkspaceSummaryDto> DisableAsync(Guid id, string? reason, CancellationToken ct = default)
    {
        EnsureSuperAdmin(); // before validating anything
        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmed is { Length: > MaxReasonLength })
        {
            throw new AppValidationException(nameof(DisableWorkspaceRequest.Reason), $"Use at most {MaxReasonLength} characters.");
        }

        return ChangeStatusAsync(id, WorkspaceStatus.Disabled, trimmed, [WorkspaceStatus.Active, WorkspaceStatus.PendingApproval], ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, string confirmName, CancellationToken ct = default)
    {
        EnsureSuperAdmin();

        await using var db = await OpenAsync(ct);
        var workspace = await FindAsync(db, id, ct);
        if (workspace.Status != WorkspaceStatus.Disabled)
        {
            throw new BusinessRuleException("Disable the workspace before deleting it.");
        }

        if (!string.Equals(confirmName?.Trim(), workspace.Name, StringComparison.Ordinal))
        {
            throw new AppValidationException(nameof(DeleteWorkspaceRequest.ConfirmName), "Type the workspace name exactly as shown to confirm.");
        }

        var surveys = await db.Surveys.CountAsync(s => s.WorkspaceId == id, ct);
        var responses = await db.Responses.CountAsync(r => r.WorkspaceId == id, ct);
        var accounts = await db.Users.CountAsync(u => u.WorkspaceId == id, ct);

        // Children first; answers, questions, widgets and Identity rows follow through database cascades.
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await db.Responses.Where(r => r.WorkspaceId == id).ExecuteDeleteAsync(ct);
            await db.Reports.Where(r => r.WorkspaceId == id).ExecuteDeleteAsync(ct);
            await db.Surveys.Where(s => s.WorkspaceId == id).ExecuteDeleteAsync(ct);
            await db.AuditLogs.Where(a => a.WorkspaceId == id).ExecuteDeleteAsync(ct);
            await db.Users.Where(u => u.WorkspaceId == id).ExecuteDeleteAsync(ct);
            await db.Workspaces.Where(w => w.Id == id).ExecuteDeleteAsync(ct);
            await transaction.CommitAsync(ct);
        }

        statuses.Invalidate(id);
        await audit.LogAsync(AuditActions.WorkspaceDeleted, WorkspaceMapping.EntityType, id.ToString(),
            $"Deleted workspace \"{workspace.Name}\" ({workspace.Slug}) with {surveys} surveys, {responses} responses and {accounts} accounts.", ct);
    }

    /// <inheritdoc />
    public async Task<SystemOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        EnsureSuperAdmin();
        await using var db = await OpenAsync(ct);

        var byStatus = await db.Workspaces.GroupBy(w => w.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int CountOf(WorkspaceStatus status) => byStatus.FirstOrDefault(x => x.Key == status)?.Count ?? 0;

        var recent = db.Workspaces.AsNoTracking().OrderByDescending(w => w.CreatedAt).ThenBy(w => w.Id).Take(RecentCount);
        var pending = db.Workspaces.AsNoTracking().Where(w => w.Status == WorkspaceStatus.PendingApproval)
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.Id).Take(PendingCount);

        return new SystemOverviewDto
        {
            WorkspaceCount = byStatus.Sum(x => x.Count),
            ActiveCount = CountOf(WorkspaceStatus.Active),
            DisabledCount = CountOf(WorkspaceStatus.Disabled),
            PendingCount = CountOf(WorkspaceStatus.PendingApproval),
            MemberCount = await db.Users.CountAsync(u => u.WorkspaceId != null, ct),
            SuperAdminCount = await db.UserRoles.CountAsync(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == SuperAdminRole), ct),
            SurveyCount = await db.Surveys.CountAsync(ct),
            ResponseCount = await db.Responses.CountAsync(r => r.Status == ResponseStatus.Completed, ct),
            RecentWorkspaces = await SummarizeAsync(db, recent, ct),
            PendingWorkspaces = await SummarizeAsync(db, pending, ct),
        };
    }

    // ---------------------------------------------------------------- helpers

    private async Task<WorkspaceSummaryDto> ChangeStatusAsync(
        Guid id, WorkspaceStatus target, string? reason, WorkspaceStatus[] allowedFrom, CancellationToken ct)
    {
        EnsureSuperAdmin();

        await using var db = await OpenAsync(ct);
        var workspace = await FindAsync(db, id, ct);
        var from = workspace.Status;
        if (from == target && target != WorkspaceStatus.Disabled)
        {
            return await GetAsync(id, ct); // already active: nothing to do
        }

        // Only Approve restricts the source status (a disabled workspace is enabled, not approved).
        // Disabling an already disabled workspace updates the reason.
        if (from != target && !allowedFrom.Contains(from))
        {
            throw new BusinessRuleException("This workspace is not waiting for approval.");
        }

        workspace.Status = target;
        workspace.StatusReason = target == WorkspaceStatus.Disabled ? reason : null;
        workspace.StatusChangedAt = UtcNow;
        await db.SaveChangesAsync(ct);
        statuses.Invalidate(id);

        var action = target == WorkspaceStatus.Disabled
            ? AuditActions.WorkspaceDisabled
            : from == WorkspaceStatus.PendingApproval ? AuditActions.WorkspaceApproved : AuditActions.WorkspaceEnabled;
        var details = action switch
        {
            AuditActions.WorkspaceDisabled => $"Workspace \"{workspace.Name}\" disabled{(reason is null ? "." : $": {reason}")}",
            AuditActions.WorkspaceApproved => $"Workspace \"{workspace.Name}\" approved.",
            _ => $"Workspace \"{workspace.Name}\" enabled.",
        };
        await audit.LogAsync(action, WorkspaceMapping.EntityType, id.ToString(), details, ct);
        await audit.LogInWorkspaceAsync(id, action, WorkspaceMapping.EntityType, id.ToString(), details, ct);

        return await GetAsync(id, ct);
    }

    /// <summary>Workspaces with their figures; counts are correlated sub-queries, admin addresses one extra query.</summary>
    private static async Task<List<WorkspaceSummaryDto>> SummarizeAsync(AppDbContext db, IQueryable<Workspace> workspaces, CancellationToken ct)
    {
        var rows = await workspaces
            .Select(w => new
            {
                Workspace = w,
                Members = db.Users.Count(u => u.WorkspaceId == w.Id),
                Surveys = db.Surveys.Count(s => s.WorkspaceId == w.Id),
                Responses = db.Responses.Count(r => r.WorkspaceId == w.Id && r.Status == ResponseStatus.Completed),
            })
            .ToListAsync(ct);

        var ids = rows.Select(r => (Guid?)r.Workspace.Id).ToList();
        var admins = await (
                from user in db.Users
                join userRole in db.UserRoles on user.Id equals userRole.UserId
                join role in db.Roles on userRole.RoleId equals role.Id
                where role.NormalizedName == AdminRole && ids.Contains(user.WorkspaceId)
                select new { user.WorkspaceId, user.Email })
            .ToListAsync(ct);
        var adminsByWorkspace = admins.ToLookup(a => a.WorkspaceId, a => a.Email ?? string.Empty);

        return rows
            .Select(r =>
            {
                var emails = adminsByWorkspace[r.Workspace.Id].Order(StringComparer.OrdinalIgnoreCase).ToList();
                return r.Workspace.ToSummary(r.Members, emails.Count, r.Surveys, r.Responses, emails);
            })
            .ToList();
    }

    private async Task<AppDbContext> OpenAsync(CancellationToken ct) =>
        (await dbFactory.CreateDbContextAsync(ct)).UseScope(DataScope.System);

    private static async Task<Workspace> FindAsync(AppDbContext db, Guid id, CancellationToken ct) =>
        await db.Workspaces.FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException(WorkspaceMapping.EntityType, id);

    private void EnsureSuperAdmin()
    {
        if (!currentUser.IsSuperAdmin)
        {
            throw new ForbiddenException("Only super admins can manage workspaces.");
        }
    }

    /// <summary>E-mail addresses are unique system-wide (one account = one workspace).</summary>
    internal static async Task EnsureEmailFreeAsync(UserManager<ApplicationUser> users, string email)
    {
        if (await users.FindByEmailAsync(email) is not null || await users.FindByNameAsync(email) is not null)
        {
            throw new ConflictException($"An account with the e-mail address '{email}' already exists.");
        }
    }

    /// <summary>Creates missing workspace roles (normally seeded at start-up).</summary>
    internal static async Task EnsureRolesExistAsync(IServiceProvider services)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in AppRoles.WorkspaceRoles)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                IdentityErrors.ThrowIfFailed(await roles.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() }));
            }
        }
    }
}
