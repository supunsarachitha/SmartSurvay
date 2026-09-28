using SmartSurvey.Application.Common;

namespace SmartSurvey.Application.Audit;

/// <summary>Audit log entry for display.</summary>
public sealed record AuditLogDto
{
    /// <summary>Entry id.</summary>
    public Guid Id { get; init; }

    /// <summary>UTC timestamp.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Acting user id.</summary>
    public Guid? UserId { get; init; }

    /// <summary>Acting user name.</summary>
    public string? UserName { get; init; }

    /// <summary>Action code.</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>Entity type.</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Entity id.</summary>
    public string? EntityId { get; init; }

    /// <summary>Details.</summary>
    public string? Details { get; init; }
}

/// <summary>Audit log filters.</summary>
public sealed class AuditQuery : PageRequest
{
    /// <summary>Search in action, entity type, user name and details.</summary>
    public string? Search { get; set; }

    /// <summary>Exact entity type.</summary>
    public string? EntityType { get; set; }

    /// <summary>Exact entity id.</summary>
    public string? EntityId { get; set; }

    /// <summary>On/after (UTC date).</summary>
    public DateOnly? From { get; set; }

    /// <summary>On/before (UTC date).</summary>
    public DateOnly? To { get; set; }
}

/// <summary>Writes and reads the audit trail.</summary>
public interface IAuditService
{
    /// <summary>
    /// Appends an entry for the current user. Never throws: failures are logged and swallowed so
    /// auditing can never break a business operation.
    /// </summary>
    Task LogAsync(string action, string entityType, string? entityId, string? details = null, CancellationToken ct = default);

    /// <summary>
    /// Like <see cref="LogAsync"/>, but records the entry in the given workspace — for actions of
    /// people who are not signed in to it (e.g. a response submitted through a share link). The
    /// actor is only named when they are a member of that workspace.
    /// </summary>
    Task LogInWorkspaceAsync(Guid workspaceId, string action, string entityType, string? entityId, string? details = null, CancellationToken ct = default);

    /// <summary>Paged, newest-first list of the current workspace's entries (workspace admins).</summary>
    Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery query, CancellationToken ct = default);

    /// <summary>Paged, newest-first list of system events — entries without a workspace (super admins).</summary>
    Task<PagedResult<AuditLogDto>> ListSystemAsync(AuditQuery query, CancellationToken ct = default);
}

/// <summary>Well-known audit action codes.</summary>
public static class AuditActions
{
    /// <summary>Survey created.</summary>
    public const string SurveyCreated = "survey.created";

    /// <summary>Survey design updated.</summary>
    public const string SurveyUpdated = "survey.updated";

    /// <summary>Survey deleted.</summary>
    public const string SurveyDeleted = "survey.deleted";

    /// <summary>Survey status changed.</summary>
    public const string SurveyStatusChanged = "survey.status_changed";

    /// <summary>Survey duplicated.</summary>
    public const string SurveyDuplicated = "survey.duplicated";

    /// <summary>Survey imported.</summary>
    public const string SurveyImported = "survey.imported";

    /// <summary>Survey definition exported.</summary>
    public const string SurveyExported = "survey.exported";

    /// <summary>Response submitted.</summary>
    public const string ResponseSubmitted = "response.submitted";

    /// <summary>Response deleted.</summary>
    public const string ResponseDeleted = "response.deleted";

    /// <summary>Responses exported.</summary>
    public const string ResponsesExported = "responses.exported";

    /// <summary>Report created.</summary>
    public const string ReportCreated = "report.created";

    /// <summary>Report updated.</summary>
    public const string ReportUpdated = "report.updated";

    /// <summary>Report deleted.</summary>
    public const string ReportDeleted = "report.deleted";

    /// <summary>Report exported.</summary>
    public const string ReportExported = "report.exported";

    /// <summary>User created.</summary>
    public const string UserCreated = "user.created";

    /// <summary>User roles changed.</summary>
    public const string UserRolesChanged = "user.roles_changed";

    /// <summary>User password set by an administrator.</summary>
    public const string UserPasswordReset = "user.password_reset";

    /// <summary>User locked.</summary>
    public const string UserLocked = "user.locked";

    /// <summary>User unlocked.</summary>
    public const string UserUnlocked = "user.unlocked";

    /// <summary>User deleted.</summary>
    public const string UserDeleted = "user.deleted";

    /// <summary>Product branding (name, tagline, icon, logo) changed.</summary>
    public const string BrandingUpdated = "branding.updated";

    /// <summary>A workspace admin saved the workspace settings.</summary>
    public const string WorkspaceSettingsUpdated = "workspace.settings_updated";

    /// <summary>A workspace was created (by a super admin or by self-service sign-up).</summary>
    public const string WorkspaceCreated = "workspace.created";

    /// <summary>A super admin changed a workspace's name, address or contact.</summary>
    public const string WorkspaceUpdated = "workspace.updated";

    /// <summary>A super admin enabled a workspace.</summary>
    public const string WorkspaceEnabled = "workspace.enabled";

    /// <summary>A super admin approved a workspace that was waiting for approval.</summary>
    public const string WorkspaceApproved = "workspace.approved";

    /// <summary>A super admin disabled a workspace.</summary>
    public const string WorkspaceDisabled = "workspace.disabled";

    /// <summary>A super admin deleted a workspace with all its data.</summary>
    public const string WorkspaceDeleted = "workspace.deleted";

    /// <summary>Someone joined a workspace with its join link.</summary>
    public const string MemberJoined = "workspace.member_joined";

    /// <summary>A super admin changed the system settings.</summary>
    public const string PlatformSettingsUpdated = "system.settings_updated";
}
