using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Identity;
using SmartSurvey.Infrastructure.Identity;
using SmartSurvey.Infrastructure.Persistence;

namespace SmartSurvey.Infrastructure.Workspaces;

/// <summary>
/// Public account creation: self-service workspace sign-up (the creator becomes its admin) and joining
/// an existing workspace with its link. Each runs in one transaction on a fresh DI scope.
/// </summary>
public sealed class WorkspaceSignupService(
    IServiceScopeFactory scopeFactory,
    IPlatformSettingsService settings,
    IAuditService audit,
    TimeProvider time) : IWorkspaceSignupService
{
    /// <summary>Shown when sign-up is switched off.</summary>
    public const string SignupClosedMessage = "Creating new workspaces is currently switched off. Please contact the site administrator.";

    /// <summary>Shown when a workspace does not accept members through its link.</summary>
    public const string JoinClosedMessage = "This workspace does not accept new members through its link. Please ask its administrator for an account.";

    private static readonly WorkspaceSignupRequestValidator SignupValidator = new();
    private static readonly JoinWorkspaceRequestValidator JoinValidator = new();

    /// <inheritdoc />
    public async Task<WorkspaceSignupResult> SignUpAsync(WorkspaceSignupRequest request, CancellationToken ct = default)
    {
        SignupValidator.ValidateOrThrow(request);
        var policy = await settings.GetAsync(ct);
        if (!policy.AllowWorkspaceSignup)
        {
            throw new BusinessRuleException(SignupClosedMessage);
        }

        var email = request.Email.Trim();
        var now = time.GetUtcNow().UtcDateTime;
        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await PlatformWorkspaceService.EnsureEmailFreeAsync(users, email);

        Workspace workspace;
        ApplicationUser owner;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var slug = await WorkspaceSlugAllocator.AllocateAsync(
                db, request.WorkspaceSlug, request.WorkspaceName, nameof(WorkspaceSignupRequest.WorkspaceSlug), null, ct);
            workspace = new Workspace
            {
                Name = request.WorkspaceName.Trim(),
                Slug = slug,
                Status = policy.RequireWorkspaceApproval ? WorkspaceStatus.PendingApproval : WorkspaceStatus.Active,
                StatusChangedAt = now,
            };
            db.Workspaces.Add(workspace);
            await db.SaveChangesAsync(ct);

            owner = NewAccount(workspace.Id, email, request.DisplayName, now);
            await PlatformWorkspaceService.EnsureRolesExistAsync(scope.ServiceProvider);
            IdentityErrors.ThrowIfFailed(await users.CreateAsync(owner, request.Password));
            IdentityErrors.ThrowIfFailed(await users.AddToRolesAsync(owner, AppRoles.WorkspaceRoles));

            workspace.OwnerId = owner.Id;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }

        var details = $"Workspace \"{workspace.Name}\" ({workspace.Slug}) created by sign-up of {email}"
            + (workspace.Status == WorkspaceStatus.PendingApproval ? "; waiting for approval." : ".");
        await audit.LogAsync(AuditActions.WorkspaceCreated, WorkspaceMapping.EntityType, workspace.Id.ToString(), details, ct);
        await audit.LogInWorkspaceAsync(workspace.Id, AuditActions.WorkspaceCreated, WorkspaceMapping.EntityType, workspace.Id.ToString(), details, ct);

        return new WorkspaceSignupResult(workspace.Id, workspace.Slug, owner.Id, workspace.Status);
    }

    /// <inheritdoc />
    public async Task<JoinWorkspaceResult> JoinAsync(string workspaceSlug, JoinWorkspaceRequest request, CancellationToken ct = default)
    {
        JoinValidator.ValidateOrThrow(request);
        var slug = workspaceSlug?.Trim().ToLowerInvariant() ?? string.Empty;
        var email = request.Email.Trim();

        await using var scope = scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Disabled and pending workspaces are reported as unknown.
        var workspace = await db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Slug == slug && w.Status == WorkspaceStatus.Active, ct)
            ?? throw new NotFoundException(WorkspaceMapping.EntityType, slug);
        if (!workspace.AllowSelfRegistration)
        {
            throw new ForbiddenException(JoinClosedMessage);
        }

        await PlatformWorkspaceService.EnsureEmailFreeAsync(users, email);
        var member = NewAccount(workspace.Id, email, request.DisplayName, time.GetUtcNow().UtcDateTime);
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await PlatformWorkspaceService.EnsureRolesExistAsync(scope.ServiceProvider);
            IdentityErrors.ThrowIfFailed(await users.CreateAsync(member, request.Password));
            IdentityErrors.ThrowIfFailed(await users.AddToRoleAsync(member, AppRoles.User));
            await transaction.CommitAsync(ct);
        }

        await audit.LogInWorkspaceAsync(workspace.Id, AuditActions.MemberJoined, "User", member.Id.ToString(),
            $"{email} joined the workspace with its link.", ct);
        return new JoinWorkspaceResult(workspace.Id, member.Id);
    }

    /// <summary>A new account; the e-mail address is confirmed by the caller's confirmation flow (when required).</summary>
    private static ApplicationUser NewAccount(Guid workspaceId, string email, string? displayName, DateTime now) => new()
    {
        WorkspaceId = workspaceId,
        UserName = email,
        Email = email,
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim(),
        CreatedAt = now,
        LockoutEnabled = true,
    };
}
