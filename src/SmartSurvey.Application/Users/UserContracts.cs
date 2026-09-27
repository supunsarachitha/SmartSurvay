using SmartSurvey.Application.Common;

namespace SmartSurvey.Application.Users;

/// <summary>User as shown in the admin user list.</summary>
public sealed record UserDto
{
    /// <summary>User id.</summary>
    public Guid Id { get; init; }

    /// <summary>E-mail / user name.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>Display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Roles.</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];

    /// <summary>Currently locked out.</summary>
    public bool IsLockedOut { get; init; }

    /// <summary>E-mail confirmed.</summary>
    public bool EmailConfirmed { get; init; }

    /// <summary>Registered (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Last login (UTC).</summary>
    public DateTime? LastLoginAt { get; init; }

    /// <summary>Completed responses submitted by the user.</summary>
    public int ResponseCount { get; init; }
}

/// <summary>User list filters.</summary>
public sealed class UserQuery : PageRequest
{
    /// <summary>Search e-mail / display name.</summary>
    public string? Search { get; set; }

    /// <summary>Only users in this role.</summary>
    public string? Role { get; set; }
}

/// <summary>Admin request to create a user.</summary>
public sealed class CreateUserRequest
{
    /// <summary>E-mail (also the user name).</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Initial password (must satisfy the Identity password policy).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Roles to assign (default: User).</summary>
    public List<string> Roles { get; set; } = [];
}

/// <summary>Request to replace a user's roles.</summary>
public sealed class SetUserRolesRequest
{
    /// <summary>New role set (subset of AppRoles.All).</summary>
    public List<string> Roles { get; set; } = [];
}

/// <summary>User administration (admin only). Implemented with ASP.NET Core Identity.</summary>
public interface IUserAdminService
{
    /// <summary>Paged user list.</summary>
    Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct = default);

    /// <summary>Single user. Throws <see cref="NotFoundException"/>.</summary>
    Task<UserDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a confirmed user with roles.</summary>
    Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default);

    /// <summary>Replaces roles. An admin cannot remove their own Admin role (prevents lock-out).</summary>
    Task<UserDto> SetRolesAsync(Guid id, IReadOnlyCollection<string> roles, CancellationToken ct = default);

    /// <summary>Locks the account indefinitely. Admins cannot lock themselves.</summary>
    Task<UserDto> LockAsync(Guid id, CancellationToken ct = default);

    /// <summary>Removes a lock-out.</summary>
    Task<UserDto> UnlockAsync(Guid id, CancellationToken ct = default);

    /// <summary>Deletes a user (their responses become anonymous). Admins cannot delete themselves.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
