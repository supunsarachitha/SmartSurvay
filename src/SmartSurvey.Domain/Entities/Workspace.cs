using SmartSurvey.Domain.Common;

namespace SmartSurvey.Domain.Entities;

/// <summary>
/// An isolated room of the application: its own admins, members, surveys, responses, reports and
/// audit log. Nothing is shared between workspaces. Created by self-service sign-up (the creator
/// becomes its admin) or by a super admin, who can also disable, approve and delete workspaces.
/// </summary>
public class Workspace : AuditableEntity
{
    /// <summary>Maximum length of <see cref="Name"/>.</summary>
    public const int NameMaxLength = 100;

    /// <summary>Maximum length of <see cref="Slug"/>.</summary>
    public const int SlugMaxLength = 60;

    /// <summary>Display name, e.g. "Acme Research".</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>URL-friendly unique identifier used in the workspace page and join link: <c>/w/{slug}</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Optional introduction shown on the workspace page.</summary>
    public string? Description { get; set; }

    /// <summary>Optional contact address shown to members and respondents.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>Whether members can sign in and the workspace's surveys accept responses.</summary>
    public WorkspaceStatus Status { get; set; } = WorkspaceStatus.Active;

    /// <summary>Optional message from the super admin shown when the workspace is not active.</summary>
    public string? StatusReason { get; set; }

    /// <summary>UTC timestamp of the last status change.</summary>
    public DateTime? StatusChangedAt { get; set; }

    /// <summary>When true, people can create a member account with the workspace's join link.</summary>
    public bool AllowSelfRegistration { get; set; } = true;

    /// <summary>When true, <c>/w/{slug}</c> lists the published surveys that anyone may answer.</summary>
    public bool ShowPublicSurveyList { get; set; } = true;

    /// <summary>Account that created the workspace (null when created by the system or a super admin).</summary>
    public Guid? OwnerId { get; set; }

    /// <summary>True when members may sign in and surveys accept responses.</summary>
    public bool IsActive => Status == WorkspaceStatus.Active;
}

/// <summary>Lifecycle of a <see cref="Workspace"/>.</summary>
public enum WorkspaceStatus
{
    /// <summary>In use.</summary>
    Active = 0,

    /// <summary>Switched off by a super admin: nobody can sign in, survey links are unavailable, data is kept.</summary>
    Disabled = 1,

    /// <summary>Created by self-service sign-up while approval is required; waits for a super admin.</summary>
    PendingApproval = 2,
}
