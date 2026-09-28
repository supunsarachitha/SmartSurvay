using Microsoft.AspNetCore.Identity;

namespace SmartSurvey.Domain.Identity;

/// <summary>Application user (ASP.NET Core Identity) with a GUID key and profile fields.</summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Creates a user with a new client-generated id.</summary>
    public ApplicationUser()
    {
        Id = Guid.NewGuid();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    /// <summary>Friendly name shown in the UI and reports (falls back to the e-mail).</summary>
    public string? DisplayName { get; set; }

    /// <summary>UTC registration timestamp.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last successful login.</summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Workspace the account belongs to (one account = one workspace). Null only for super admins,
    /// who manage the system and belong to no workspace.
    /// </summary>
    public Guid? WorkspaceId { get; set; }

    /// <summary>Soft-disable flag managed by administrators (in addition to Identity lockout).</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Role names used throughout the application.</summary>
public static class AppRoles
{
    /// <summary>
    /// Runs the system: workspaces (create, enable/disable, approve, delete), accounts, branding and
    /// system settings. Belongs to no workspace and cannot see workspace content.
    /// </summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>Workspace admin — full access to their own workspace: surveys, responses, reports, members, audit log, settings.</summary>
    public const string Admin = "Admin";

    /// <summary>Workspace member (respondent).</summary>
    public const string User = "User";

    /// <summary>All roles (seeded at start-up).</summary>
    public static readonly IReadOnlyList<string> All = [SuperAdmin, Admin, User];

    /// <summary>Roles that can be held inside a workspace.</summary>
    public static readonly IReadOnlyList<string> WorkspaceRoles = [Admin, User];
}

/// <summary>Custom claim types issued at sign-in.</summary>
public static class AppClaimTypes
{
    /// <summary>Id of the user's workspace (absent for super admins).</summary>
    public const string WorkspaceId = "workspace_id";
}
