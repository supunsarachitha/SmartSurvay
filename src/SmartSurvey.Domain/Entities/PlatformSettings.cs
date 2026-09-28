using SmartSurvey.Domain.Common;

namespace SmartSurvey.Domain.Entities;

/// <summary>
/// System-wide settings managed by super admins (single row with the id <see cref="SingletonId"/>).
/// Branding is kept separately in <see cref="BrandingSettings"/>.
/// </summary>
public class PlatformSettings : AuditableEntity
{
    /// <summary>Id of the one and only settings row.</summary>
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-0000000057a7");

    /// <summary>Creates the settings row with the singleton id.</summary>
    public PlatformSettings()
    {
        Id = SingletonId;
    }

    /// <summary>When true, anyone can create a workspace at <c>/signup</c>.</summary>
    public bool AllowWorkspaceSignup { get; set; } = true;

    /// <summary>When true, self-service workspaces start as <see cref="WorkspaceStatus.PendingApproval"/>.</summary>
    public bool RequireWorkspaceApproval { get; set; }

    /// <summary>Optional address shown when a workspace is unavailable or waiting for approval.</summary>
    public string? SupportEmail { get; set; }
}
