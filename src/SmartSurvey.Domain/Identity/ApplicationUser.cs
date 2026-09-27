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

    /// <summary>Soft-disable flag managed by administrators (in addition to Identity lockout).</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>Role names used throughout the application.</summary>
public static class AppRoles
{
    /// <summary>Full access: survey design, responses, reports, users, audit log.</summary>
    public const string Admin = "Admin";

    /// <summary>Regular respondent.</summary>
    public const string User = "User";

    /// <summary>All roles (seeded at start-up).</summary>
    public static readonly IReadOnlyList<string> All = [Admin, User];
}
