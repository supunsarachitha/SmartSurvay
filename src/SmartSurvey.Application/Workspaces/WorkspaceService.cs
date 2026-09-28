using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Entities;

namespace SmartSurvey.Application.Workspaces;

/// <summary>The current user's workspace: read by members, settings changed by its admins; public lookup by slug.</summary>
public sealed class WorkspaceService(
    IAppDbContextFactory dbFactory, ICurrentUser currentUser, IAuditService audit, IWorkspaceStatusProvider? cache = null) : IWorkspaceService
{
    private static readonly UpdateWorkspaceSettingsRequestValidator SettingsValidator = new();

    /// <inheritdoc />
    public async Task<WorkspaceDto> GetCurrentAsync(CancellationToken ct = default)
    {
        var workspaceId = currentUser.WorkspaceId ?? throw new ForbiddenException("You are not a member of a workspace.");
        await using var db = await dbFactory.CreateAsync(ct);
        var workspace = await db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workspaceId, ct)
            ?? throw new NotFoundException("Workspace", workspaceId);
        return workspace.ToDto();
    }

    /// <inheritdoc />
    public async Task<WorkspaceDto> UpdateSettingsAsync(UpdateWorkspaceSettingsRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only workspace admins can change the workspace settings.");
        }

        SettingsValidator.ValidateOrThrow(request);
        var workspaceId = currentUser.WorkspaceId!.Value;

        await using var db = await dbFactory.CreateAsync(ct);
        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct)
            ?? throw new NotFoundException("Workspace", workspaceId);

        workspace.Name = request.Name.Trim();
        workspace.Description = TrimToNull(request.Description);
        workspace.ContactEmail = TrimToNull(request.ContactEmail);
        workspace.AllowSelfRegistration = request.AllowSelfRegistration;
        workspace.ShowPublicSurveyList = request.ShowPublicSurveyList;
        await db.SaveChangesAsync(ct);
        cache?.Invalidate(workspaceId); // the name shown in menus

        await audit.LogAsync(AuditActions.WorkspaceSettingsUpdated, WorkspaceMapping.EntityType, workspaceId.ToString(),
            $"Workspace settings saved: name \"{workspace.Name}\", self-registration {(workspace.AllowSelfRegistration ? "on" : "off")}, public survey page {(workspace.ShowPublicSurveyList ? "on" : "off")}.", ct);
        return workspace.ToDto();
    }

    /// <inheritdoc />
    public async Task<PublicWorkspaceDto?> FindPublicAsync(string slug, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var normalized = slug.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateSystemAsync(ct);
        return await db.Workspaces.AsNoTracking()
            .Where(w => w.Slug == normalized && w.Status == WorkspaceStatus.Active)
            .Select(w => new PublicWorkspaceDto
            {
                Id = w.Id,
                Name = w.Name,
                Slug = w.Slug,
                Description = w.Description,
                ContactEmail = w.ContactEmail,
                AllowSelfRegistration = w.AllowSelfRegistration,
                ShowPublicSurveyList = w.ShowPublicSurveyList,
            })
            .FirstOrDefaultAsync(ct);
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Mapping helpers for workspaces.</summary>
public static class WorkspaceMapping
{
    /// <summary>Entity type written to the audit log.</summary>
    public const string EntityType = "Workspace";

    /// <summary>Maps an entity to <see cref="WorkspaceDto"/>.</summary>
    public static WorkspaceDto ToDto(this Workspace w) => new()
    {
        Id = w.Id,
        Name = w.Name,
        Slug = w.Slug,
        Description = w.Description,
        ContactEmail = w.ContactEmail,
        Status = w.Status,
        StatusReason = w.StatusReason,
        StatusChangedAt = w.StatusChangedAt,
        AllowSelfRegistration = w.AllowSelfRegistration,
        ShowPublicSurveyList = w.ShowPublicSurveyList,
        CreatedAt = w.CreatedAt,
    };

    /// <summary>Maps an entity plus its figures to <see cref="WorkspaceSummaryDto"/>.</summary>
    public static WorkspaceSummaryDto ToSummary(
        this Workspace w, int memberCount, int adminCount, int surveyCount, int responseCount, IReadOnlyList<string> adminEmails) => new()
    {
        Id = w.Id,
        Name = w.Name,
        Slug = w.Slug,
        Description = w.Description,
        ContactEmail = w.ContactEmail,
        Status = w.Status,
        StatusReason = w.StatusReason,
        StatusChangedAt = w.StatusChangedAt,
        AllowSelfRegistration = w.AllowSelfRegistration,
        ShowPublicSurveyList = w.ShowPublicSurveyList,
        CreatedAt = w.CreatedAt,
        MemberCount = memberCount,
        AdminCount = adminCount,
        SurveyCount = surveyCount,
        ResponseCount = responseCount,
        AdminEmails = adminEmails,
    };
}

/// <summary>
/// Process-wide cache of workspace status (<see cref="Ttl"/>) backing the per-request "is this
/// workspace still active?" check. Loads through a system-scoped context in its own DI scope.
/// </summary>
public sealed class WorkspaceStatusCache(IServiceScopeFactory scopeFactory, TimeProvider time) : IWorkspaceStatusProvider
{
    /// <summary>How long a status is trusted before it is read again.</summary>
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<Guid, (WorkspaceInfo? Info, DateTimeOffset Expires)> _entries = new();

    /// <inheritdoc />
    public async Task<WorkspaceInfo?> GetAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        if (_entries.TryGetValue(workspaceId, out var entry) && entry.Expires > now)
        {
            return entry.Info;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        await using var db = await scope.ServiceProvider.GetRequiredService<IAppDbContextFactory>().CreateSystemAsync(ct);
        var info = await db.Workspaces.AsNoTracking()
            .Where(w => w.Id == workspaceId)
            .Select(w => new WorkspaceInfo(w.Id, w.Name, w.Slug, w.Status))
            .FirstOrDefaultAsync(ct);

        _entries[workspaceId] = (info, now + Ttl);
        return info;
    }

    /// <inheritdoc />
    public void Invalidate(Guid workspaceId) => _entries.TryRemove(workspaceId, out _);
}

/// <summary>System settings (single row; defaults until a super admin saves them).</summary>
public sealed class PlatformSettingsService(IAppDbContextFactory dbFactory, ICurrentUser currentUser, IAuditService audit) : IPlatformSettingsService
{
    private static readonly UpdatePlatformSettingsRequestValidator Validator = new();

    /// <inheritdoc />
    public async Task<PlatformSettingsDto> GetAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateSystemAsync(ct);
        var row = await db.PlatformSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, ct);
        return ToDto(row ?? new PlatformSettings());
    }

    /// <inheritdoc />
    public async Task<PlatformSettingsDto> UpdateAsync(UpdatePlatformSettingsRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsSuperAdmin)
        {
            throw new ForbiddenException("Only super admins can change the system settings.");
        }

        Validator.ValidateOrThrow(request);
        await using var db = await dbFactory.CreateSystemAsync(ct);
        var row = await db.PlatformSettings.FirstOrDefaultAsync(s => s.Id == PlatformSettings.SingletonId, ct);
        if (row is null)
        {
            row = new PlatformSettings();
            db.PlatformSettings.Add(row);
        }

        row.AllowWorkspaceSignup = request.AllowWorkspaceSignup;
        row.RequireWorkspaceApproval = request.RequireWorkspaceApproval;
        row.SupportEmail = string.IsNullOrWhiteSpace(request.SupportEmail) ? null : request.SupportEmail.Trim();
        await db.SaveChangesAsync(ct);

        await audit.LogAsync(AuditActions.PlatformSettingsUpdated, "PlatformSettings", PlatformSettings.SingletonId.ToString(),
            $"System settings saved: workspace sign-up {(row.AllowWorkspaceSignup ? "on" : "off")}, approval {(row.RequireWorkspaceApproval ? "required" : "not required")}.", ct);
        return ToDto(row);
    }

    private static PlatformSettingsDto ToDto(PlatformSettings s) => new()
    {
        AllowWorkspaceSignup = s.AllowWorkspaceSignup,
        RequireWorkspaceApproval = s.RequireWorkspaceApproval,
        SupportEmail = s.SupportEmail,
    };
}
