using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Entities;

namespace SmartSurvey.Application.Workspaces;

// ------------------------------------------------------------------------------------------------
// Workspace (seen by its own members and admins)
// ------------------------------------------------------------------------------------------------

/// <summary>A workspace and its settings.</summary>
public record WorkspaceDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; init; }

    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Address used by the workspace page and join link (<c>/w/{slug}</c>).</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Introduction shown on the workspace page.</summary>
    public string? Description { get; init; }

    /// <summary>Contact address shown to members and respondents.</summary>
    public string? ContactEmail { get; init; }

    /// <summary>Lifecycle status.</summary>
    public WorkspaceStatus Status { get; init; }

    /// <summary>Message from the super admin when the workspace is not active.</summary>
    public string? StatusReason { get; init; }

    /// <summary>Last status change (UTC).</summary>
    public DateTime? StatusChangedAt { get; init; }

    /// <summary>People can create a member account with the join link.</summary>
    public bool AllowSelfRegistration { get; init; }

    /// <summary>The workspace page lists the surveys anyone may answer.</summary>
    public bool ShowPublicSurveyList { get; init; }

    /// <summary>Created (UTC).</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>Settings a workspace admin can change (branding and the address are managed by super admins).</summary>
public sealed class UpdateWorkspaceSettingsRequest
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Introduction shown on the workspace page.</summary>
    public string? Description { get; set; }

    /// <summary>Contact address.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>People can create a member account with the join link.</summary>
    public bool AllowSelfRegistration { get; set; }

    /// <summary>The workspace page lists the surveys anyone may answer.</summary>
    public bool ShowPublicSurveyList { get; set; }
}

/// <summary>What anyone may know about an active workspace (its public page and join form).</summary>
public sealed record PublicWorkspaceDto
{
    /// <summary>Id.</summary>
    public Guid Id { get; init; }

    /// <summary>Display name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Address.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Introduction.</summary>
    public string? Description { get; init; }

    /// <summary>Contact address.</summary>
    public string? ContactEmail { get; init; }

    /// <summary>People can create a member account with the join link.</summary>
    public bool AllowSelfRegistration { get; init; }

    /// <summary>The page lists public surveys.</summary>
    public bool ShowPublicSurveyList { get; init; }
}

/// <summary>The current user's workspace and its settings.</summary>
public interface IWorkspaceService
{
    /// <summary>The signed-in member's workspace. Throws <see cref="ForbiddenException"/> without one.</summary>
    Task<WorkspaceDto> GetCurrentAsync(CancellationToken ct = default);

    /// <summary>Updates the current workspace's settings (workspace admins).</summary>
    Task<WorkspaceDto> UpdateSettingsAsync(UpdateWorkspaceSettingsRequest request, CancellationToken ct = default);

    /// <summary>Public facts of an active workspace by slug; null when unknown or not active.</summary>
    Task<PublicWorkspaceDto?> FindPublicAsync(string slug, CancellationToken ct = default);
}

/// <summary>
/// Fast, cached lookup of workspace status — used on every request to refuse members of disabled
/// workspaces. Entries live for a few seconds and are dropped immediately when this process changes
/// a status (other instances pick the change up when their entry expires).
/// </summary>
public interface IWorkspaceStatusProvider
{
    /// <summary>Name, address and status of the workspace, or null when it does not exist.</summary>
    Task<WorkspaceInfo?> GetAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>Status of the workspace, or null when it does not exist.</summary>
    async Task<WorkspaceStatus?> GetStatusAsync(Guid workspaceId, CancellationToken ct = default) =>
        (await GetAsync(workspaceId, ct))?.Status;

    /// <summary>Drops the cached entry of a workspace (after its status, name or address changed).</summary>
    void Invalidate(Guid workspaceId);
}

/// <summary>Cached facts about a workspace (for menus and access checks).</summary>
/// <param name="Id">Id.</param>
/// <param name="Name">Display name.</param>
/// <param name="Slug">Address.</param>
/// <param name="Status">Status.</param>
public sealed record WorkspaceInfo(Guid Id, string Name, string Slug, WorkspaceStatus Status);

// ------------------------------------------------------------------------------------------------
// System administration (super admins)
// ------------------------------------------------------------------------------------------------

/// <summary>A workspace with the figures a super admin needs (never its content).</summary>
public sealed record WorkspaceSummaryDto : WorkspaceDto
{
    /// <summary>Member accounts.</summary>
    public int MemberCount { get; init; }

    /// <summary>Admin accounts.</summary>
    public int AdminCount { get; init; }

    /// <summary>Surveys (incl. templates).</summary>
    public int SurveyCount { get; init; }

    /// <summary>Completed responses.</summary>
    public int ResponseCount { get; init; }

    /// <summary>E-mail addresses of the workspace admins (to contact them).</summary>
    public IReadOnlyList<string> AdminEmails { get; init; } = [];
}

/// <summary>Filters of the workspace list.</summary>
public sealed class WorkspaceListQuery : PageRequest
{
    /// <summary>Search in name and slug.</summary>
    public string? Search { get; set; }

    /// <summary>Only workspaces with this status.</summary>
    public WorkspaceStatus? Status { get; set; }
}

/// <summary>A super admin creates a workspace together with its first admin account.</summary>
public sealed class CreateWorkspaceRequest
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Address; generated from the name when empty.</summary>
    public string? Slug { get; set; }

    /// <summary>E-mail of the first admin.</summary>
    public string AdminEmail { get; set; } = string.Empty;

    /// <summary>Display name of the first admin.</summary>
    public string? AdminDisplayName { get; set; }

    /// <summary>Initial password of the first admin.</summary>
    public string AdminPassword { get; set; } = string.Empty;
}

/// <summary>Workspace facts a super admin can change.</summary>
public sealed class UpdateWorkspaceRequest
{
    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Address (changing it changes the workspace page and join link).</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Introduction.</summary>
    public string? Description { get; set; }

    /// <summary>Contact address.</summary>
    public string? ContactEmail { get; set; }
}

/// <summary>Reason shown to members when a workspace is disabled.</summary>
public sealed class DisableWorkspaceRequest
{
    /// <summary>Optional message for the workspace's members.</summary>
    public string? Reason { get; set; }
}

/// <summary>Confirmation for deleting a workspace.</summary>
public sealed class DeleteWorkspaceRequest
{
    /// <summary>Must repeat the workspace's name exactly (guards against deleting the wrong one).</summary>
    public string ConfirmName { get; set; } = string.Empty;
}

/// <summary>System-wide figures for the super admin overview.</summary>
public sealed record SystemOverviewDto
{
    /// <summary>All workspaces.</summary>
    public int WorkspaceCount { get; init; }

    /// <summary>Active workspaces.</summary>
    public int ActiveCount { get; init; }

    /// <summary>Disabled workspaces.</summary>
    public int DisabledCount { get; init; }

    /// <summary>Workspaces waiting for approval.</summary>
    public int PendingCount { get; init; }

    /// <summary>Member accounts of all workspaces.</summary>
    public int MemberCount { get; init; }

    /// <summary>Super admin accounts.</summary>
    public int SuperAdminCount { get; init; }

    /// <summary>Surveys of all workspaces.</summary>
    public int SurveyCount { get; init; }

    /// <summary>Completed responses of all workspaces.</summary>
    public int ResponseCount { get; init; }

    /// <summary>Newest workspaces.</summary>
    public IReadOnlyList<WorkspaceSummaryDto> RecentWorkspaces { get; init; } = [];

    /// <summary>Workspaces waiting for approval (oldest first).</summary>
    public IReadOnlyList<WorkspaceSummaryDto> PendingWorkspaces { get; init; } = [];
}

/// <summary>
/// Workspace administration for super admins: list, create, change, enable/disable/approve and delete
/// workspaces, and the system overview. Never exposes workspace content. (The system audit log is
/// <see cref="IAuditService.ListSystemAsync"/>.)
/// </summary>
public interface IPlatformWorkspaceService
{
    /// <summary>Paged workspace list with figures.</summary>
    Task<PagedResult<WorkspaceSummaryDto>> ListAsync(WorkspaceListQuery query, CancellationToken ct = default);

    /// <summary>One workspace with figures.</summary>
    Task<WorkspaceSummaryDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates an active workspace and its first admin account.</summary>
    Task<WorkspaceSummaryDto> CreateAsync(CreateWorkspaceRequest request, CancellationToken ct = default);

    /// <summary>Changes name, address, description and contact.</summary>
    Task<WorkspaceSummaryDto> UpdateAsync(Guid id, UpdateWorkspaceRequest request, CancellationToken ct = default);

    /// <summary>Activates a disabled workspace.</summary>
    Task<WorkspaceSummaryDto> EnableAsync(Guid id, CancellationToken ct = default);

    /// <summary>Activates a workspace that waits for approval.</summary>
    Task<WorkspaceSummaryDto> ApproveAsync(Guid id, CancellationToken ct = default);

    /// <summary>Disables a workspace: members can no longer sign in and its surveys are unavailable.</summary>
    Task<WorkspaceSummaryDto> DisableAsync(Guid id, string? reason, CancellationToken ct = default);

    /// <summary>Permanently deletes a disabled workspace with all its data and accounts.</summary>
    Task DeleteAsync(Guid id, string confirmName, CancellationToken ct = default);

    /// <summary>System-wide figures.</summary>
    Task<SystemOverviewDto> GetOverviewAsync(CancellationToken ct = default);
}

/// <summary>System settings.</summary>
public sealed record PlatformSettingsDto
{
    /// <summary>Anyone can create a workspace at <c>/signup</c>.</summary>
    public bool AllowWorkspaceSignup { get; init; }

    /// <summary>Self-service workspaces wait for a super admin's approval.</summary>
    public bool RequireWorkspaceApproval { get; init; }

    /// <summary>Address shown when a workspace is unavailable.</summary>
    public string? SupportEmail { get; init; }
}

/// <summary>Change of the system settings.</summary>
public sealed class UpdatePlatformSettingsRequest
{
    /// <summary>Anyone can create a workspace.</summary>
    public bool AllowWorkspaceSignup { get; set; }

    /// <summary>Self-service workspaces wait for approval.</summary>
    public bool RequireWorkspaceApproval { get; set; }

    /// <summary>Support address.</summary>
    public string? SupportEmail { get; set; }
}

/// <summary>System settings: readable by everyone (sign-up page), changed by super admins.</summary>
public interface IPlatformSettingsService
{
    /// <summary>Current settings (defaults when never saved).</summary>
    Task<PlatformSettingsDto> GetAsync(CancellationToken ct = default);

    /// <summary>Saves the settings (super admins).</summary>
    Task<PlatformSettingsDto> UpdateAsync(UpdatePlatformSettingsRequest request, CancellationToken ct = default);
}

// ------------------------------------------------------------------------------------------------
// Self-service sign-up and joining
// ------------------------------------------------------------------------------------------------

/// <summary>Someone creates their own workspace and becomes its admin.</summary>
public sealed class WorkspaceSignupRequest
{
    /// <summary>Name of the new workspace.</summary>
    public string WorkspaceName { get; set; } = string.Empty;

    /// <summary>Address of the new workspace; generated from the name when empty.</summary>
    public string? WorkspaceSlug { get; set; }

    /// <summary>The creator's display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>The creator's e-mail (also the user name).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>The creator's password.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>Result of a sign-up.</summary>
/// <param name="WorkspaceId">New workspace.</param>
/// <param name="WorkspaceSlug">Its address.</param>
/// <param name="UserId">The new admin account.</param>
/// <param name="Status">Active, or PendingApproval when a super admin must approve it first.</param>
public sealed record WorkspaceSignupResult(Guid WorkspaceId, string WorkspaceSlug, Guid UserId, WorkspaceStatus Status);

/// <summary>Someone creates a member account in an existing workspace (join link).</summary>
public sealed class JoinWorkspaceRequest
{
    /// <summary>Display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>E-mail (also the user name).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Password.</summary>
    public string Password { get; set; } = string.Empty;
}

/// <summary>Result of joining a workspace.</summary>
/// <param name="WorkspaceId">The workspace joined.</param>
/// <param name="UserId">The new member account.</param>
public sealed record JoinWorkspaceResult(Guid WorkspaceId, Guid UserId);

/// <summary>
/// Creates accounts from the public sign-up and join forms. Sending confirmation e-mails and signing
/// the person in is left to the caller (UI or API).
/// </summary>
public interface IWorkspaceSignupService
{
    /// <summary>Creates a workspace and its first admin (when sign-up is allowed).</summary>
    Task<WorkspaceSignupResult> SignUpAsync(WorkspaceSignupRequest request, CancellationToken ct = default);

    /// <summary>Creates a member of an active workspace that allows self-registration.</summary>
    Task<JoinWorkspaceResult> JoinAsync(string workspaceSlug, JoinWorkspaceRequest request, CancellationToken ct = default);
}
