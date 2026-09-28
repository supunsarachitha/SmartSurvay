using FluentValidation.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Users;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.Identity;
using SmartSurvey.Infrastructure.Persistence;

namespace SmartSurvey.Infrastructure.Identity;

/// <summary>
/// Account administration implemented with ASP.NET Core Identity, scoped by workspace.
/// </summary>
/// <remarks>
/// <para><b>Who manages whom.</b> A workspace admin sees and changes only the accounts of their own
/// workspace (others are reported as not found) and can grant the roles Admin and User. A super admin
/// sees every account and can create members of any workspace or further super admins. Accounts never
/// move between workspaces; super admin accounts hold only the SuperAdmin role. Every workspace keeps at
/// least one admin and the system at least one super admin.</para>
/// <para>Services are scoped per Blazor circuit, which can live for hours. A <see cref="UserManager{TUser}"/>
/// caches entities in its (scoped) <see cref="AppDbContext"/>, so holding one for the lifetime of a circuit
/// would serve stale data and grow without bound. Every mutation therefore creates its own DI scope and
/// resolves a fresh <see cref="UserManager{TUser}"/> / <see cref="RoleManager{TRole}"/>; mutations that
/// write more than once run in a transaction on that scope's context so they are all-or-nothing.</para>
/// <para>Read operations use short-lived contexts from <see cref="IDbContextFactory{TContext}"/> and
/// load users, role names and response counts in a few set-based queries.</para>
/// </remarks>
public sealed class UserAdminService(
    IServiceScopeFactory scopeFactory,
    IDbContextFactory<AppDbContext> dbFactory,
    ICurrentUser currentUser,
    IAuditService audit,
    TimeProvider time) : IUserAdminService
{
    /// <summary>Entity type written to the audit log.</summary>
    public const string AuditEntityType = "User";

    private const string EntityName = "User";

    private static readonly CreateUserRequestValidator CreateValidator = new();
    private static readonly UserQueryValidator QueryValidator = new();

    /// <inheritdoc />
    public async Task<PagedResult<UserDto>> ListAsync(UserQuery query, CancellationToken ct = default)
    {
        EnsureAdmin();
        ArgumentNullException.ThrowIfNull(query);
        ThrowIfInvalid(QueryValidator.Validate(query));

        await using var db = await OpenReadContextAsync(ct);
        var users = ApplyFilters(db, VisibleUsers(db), query);
        if (currentUser.IsSuperAdmin && query.WorkspaceId is { } workspaceId)
        {
            users = users.Where(u => u.WorkspaceId == workspaceId);
        }

        var total = await users.CountAsync(ct);
        var page = users
            .OrderByDescending(u => u.CreatedAt)
            .ThenBy(u => u.Email)
            .ThenBy(u => u.Id) // stable paging when timestamps are equal
            .Skip(query.Skip)
            .Take(query.PageSize);
        var rows = await ProjectRows(db, page).ToListAsync(ct);

        var roles = await LoadRoleNamesAsync(db, rows.Select(r => r.Id).ToList(), ct);
        var now = time.GetUtcNow();

        return new PagedResult<UserDto>(rows.Select(r => ToDto(r, roles, now)).ToList(), total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<UserDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();

        await using var db = await OpenReadContextAsync(ct);
        var row = await ProjectRows(db, VisibleUsers(db).Where(u => u.Id == id)).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(EntityName, id);

        var roles = await LoadRoleNamesAsync(db, [id], ct);
        return ToDto(row, roles, time.GetUtcNow());
    }

    /// <inheritdoc />
    public async Task<UserDto> CreateAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        EnsureAdmin();
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfInvalid(CreateValidator.Validate(request));

        var email = request.Email.Trim();
        var workspaceId = await ResolveNewUserWorkspaceAsync(request.WorkspaceId, ct);
        IReadOnlyList<string> roles = request.Roles is { Count: > 0 }
            ? AppRoleNames.Normalize(request.Roles)
            : [workspaceId is null ? AppRoles.SuperAdmin : AppRoles.User];
        EnsureRolesFit(workspaceId, roles, nameof(CreateUserRequest.Roles));

        await using var scope = scopeFactory.CreateAsyncScope();
        var identity = IdentityServices.From(scope.ServiceProvider);

        // Checked up front for a clear message; Identity's own duplicate check still guards races.
        if (await identity.Users.FindByEmailAsync(email) is not null || await identity.Users.FindByNameAsync(email) is not null)
        {
            throw new ConflictException($"A user with the e-mail address '{email}' already exists.");
        }

        var user = new ApplicationUser
        {
            WorkspaceId = workspaceId,
            UserName = email,
            Email = email,
            EmailConfirmed = true, // created by an administrator: no confirmation round-trip needed
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
            CreatedAt = time.GetUtcNow().UtcDateTime,
            LockoutEnabled = true,
        };

        await using (var transaction = await identity.Db.Database.BeginTransactionAsync(ct))
        {
            await EnsureRolesExistAsync(identity.Roles, roles);
            ThrowIfFailed(await identity.Users.CreateAsync(user, request.Password));
            ThrowIfFailed(await identity.Users.AddToRolesAsync(user, roles));
            await transaction.CommitAsync(ct);
        }

        await audit.LogAsync(AuditActions.UserCreated, AuditEntityType, user.Id.ToString(),
            $"Created user {email} with roles: {FormatRoles(roles)}.", ct);

        return await GetAsync(user.Id, ct);
    }

    /// <inheritdoc />
    public async Task<UserDto> SetRolesAsync(Guid id, IReadOnlyCollection<string> roles, CancellationToken ct = default)
    {
        EnsureAdmin();
        ArgumentNullException.ThrowIfNull(roles);

        var unknown = AppRoleNames.Unknown(roles);
        if (unknown.Count > 0)
        {
            throw new AppValidationException(nameof(SetUserRolesRequest.Roles), AppRoleNames.UnknownRolesMessage(unknown));
        }

        var requested = AppRoleNames.Normalize(roles);

        await using var scope = scopeFactory.CreateAsyncScope();
        var identity = IdentityServices.From(scope.ServiceProvider);
        var user = await FindUserAsync(identity.Users, id);
        EnsureRolesFit(user.WorkspaceId, requested, nameof(SetUserRolesRequest.Roles));

        var current = await identity.Users.GetRolesAsync(user);
        var toRemove = current.Where(r => !requested.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        var toAdd = requested.Where(r => !current.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();

        if (toRemove.Count == 0 && toAdd.Count == 0)
        {
            return await GetAsync(id, ct); // nothing changes: no write, no audit entry
        }

        if (toRemove.Contains(AppRoles.Admin, StringComparer.OrdinalIgnoreCase)
            || toRemove.Contains(AppRoles.SuperAdmin, StringComparer.OrdinalIgnoreCase))
        {
            if (IsSelf(id))
            {
                throw new BusinessRuleException("You cannot remove your own administrator role. Ask another administrator to do it.");
            }

            await EnsureNotLastAdminAsync(identity.Users, user, "remove the administrator role from");
        }

        await using (var transaction = await identity.Db.Database.BeginTransactionAsync(ct))
        {
            await EnsureRolesExistAsync(identity.Roles, toAdd);
            if (toRemove.Count > 0)
            {
                ThrowIfFailed(await identity.Users.RemoveFromRolesAsync(user, toRemove));
            }

            if (toAdd.Count > 0)
            {
                ThrowIfFailed(await identity.Users.AddToRolesAsync(user, toAdd));
            }

            // A new security stamp makes existing sign-ins re-validate, so the new roles take effect
            // (and a removed Admin role stops working) without waiting for the cookie to expire.
            ThrowIfFailed(await identity.Users.UpdateSecurityStampAsync(user));
            await transaction.CommitAsync(ct);
        }

        await audit.LogAsync(AuditActions.UserRolesChanged, AuditEntityType, id.ToString(),
            $"Roles of {user.Email} changed from {FormatRoles(current)} to {FormatRoles(requested)}.", ct);

        return await GetAsync(id, ct);
    }

    /// <inheritdoc />
    public async Task<UserDto> SetPasswordAsync(Guid id, string password, CancellationToken ct = default)
    {
        EnsureAdmin();
        if (string.IsNullOrEmpty(password))
        {
            throw new AppValidationException(nameof(SetUserPasswordRequest.Password), "Please enter the new password.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var identity = IdentityServices.From(scope.ServiceProvider);
        var user = await FindUserAsync(identity.Users, id);

        // The reset-token path runs the password validators and rotates the security stamp (signs the user out).
        var token = await identity.Users.GeneratePasswordResetTokenAsync(user);
        ThrowIfFailed(await identity.Users.ResetPasswordAsync(user, token, password));

        // A temporary lock-out from failed sign-ins ends with the new password; an admin lock (no end) stays.
        if (await identity.Users.GetLockoutEndDateAsync(user) is { } lockoutEnd && lockoutEnd != DateTimeOffset.MaxValue)
        {
            ThrowIfFailed(await identity.Users.SetLockoutEndDateAsync(user, null));
        }

        ThrowIfFailed(await identity.Users.ResetAccessFailedCountAsync(user));

        await audit.LogAsync(AuditActions.UserPasswordReset, AuditEntityType, id.ToString(), $"New password set for {user.Email}.", ct);

        return await GetAsync(id, ct);
    }

    /// <inheritdoc />
    public async Task<UserDto> LockAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();
        if (IsSelf(id))
        {
            throw new BusinessRuleException("You cannot lock your own account.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var identity = IdentityServices.From(scope.ServiceProvider);
        var user = await FindUserAsync(identity.Users, id);

        await EnsureNotLastAdminAsync(identity.Users, user, "lock");

        await using (var transaction = await identity.Db.Database.BeginTransactionAsync(ct))
        {
            ThrowIfFailed(await identity.Users.SetLockoutEnabledAsync(user, true));
            ThrowIfFailed(await identity.Users.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue));

            // Invalidates cookies and tokens issued before the lock.
            ThrowIfFailed(await identity.Users.UpdateSecurityStampAsync(user));
            await transaction.CommitAsync(ct);
        }

        await audit.LogAsync(AuditActions.UserLocked, AuditEntityType, id.ToString(), $"Locked user {user.Email}.", ct);

        return await GetAsync(id, ct);
    }

    /// <inheritdoc />
    public async Task<UserDto> UnlockAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();

        await using var scope = scopeFactory.CreateAsyncScope();
        var identity = IdentityServices.From(scope.ServiceProvider);
        var user = await FindUserAsync(identity.Users, id);

        await using (var transaction = await identity.Db.Database.BeginTransactionAsync(ct))
        {
            if (await identity.Users.GetLockoutEnabledAsync(user))
            {
                ThrowIfFailed(await identity.Users.SetLockoutEndDateAsync(user, null));
            }
            else if (user.LockoutEnd is not null)
            {
                // SetLockoutEndDateAsync refuses to run while lockout is disabled; clear the stale value directly.
                user.LockoutEnd = null;
                ThrowIfFailed(await identity.Users.UpdateAsync(user));
            }

            ThrowIfFailed(await identity.Users.ResetAccessFailedCountAsync(user));
            await transaction.CommitAsync(ct);
        }

        await audit.LogAsync(AuditActions.UserUnlocked, AuditEntityType, id.ToString(), $"Unlocked user {user.Email}.", ct);

        return await GetAsync(id, ct);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();
        if (IsSelf(id))
        {
            throw new BusinessRuleException("You cannot delete your own account.");
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var identity = IdentityServices.From(scope.ServiceProvider);
        var user = await FindUserAsync(identity.Users, id);

        await EnsureNotLastAdminAsync(identity.Users, user, "delete");

        // Responses reference the user with ON DELETE SET NULL: they are kept and become anonymous.
        ThrowIfFailed(await identity.Users.DeleteAsync(user));

        await audit.LogAsync(AuditActions.UserDeleted, AuditEntityType, id.ToString(),
            $"Deleted user {user.Email}; their responses were kept as anonymous responses.", ct);
    }

    // ---------------------------------------------------------------- queries

    /// <summary>Applies the (validated) search and role filters with provider-portable expressions.</summary>
    private static IQueryable<ApplicationUser> ApplyFilters(AppDbContext db, IQueryable<ApplicationUser> users, UserQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ToLower() on both sides gives a case-insensitive search on SQLite and PostgreSQL alike.
            var term = query.Search.Trim().ToLowerInvariant();
            users = users.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(term))
                || (u.DisplayName != null && u.DisplayName.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            // Identity stores role names normalised with ToUpperInvariant (UpperInvariantLookupNormalizer).
            var normalizedRole = query.Role.Trim().ToUpperInvariant();
            users = users.Where(u => db.UserRoles.Any(ur =>
                ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == normalizedRole)));
        }

        return users;
    }

    /// <summary>Projects users to list rows; the completed-response count is a correlated sub-query.</summary>
    private static IQueryable<UserRow> ProjectRows(AppDbContext db, IQueryable<ApplicationUser> users) =>
        users.Select(u => new UserRow
        {
            Id = u.Id,
            Email = u.Email,
            UserName = u.UserName,
            DisplayName = u.DisplayName,
            EmailConfirmed = u.EmailConfirmed,
            CreatedAt = u.CreatedAt,
            LastLoginAt = u.LastLoginAt,
            LockoutEnabled = u.LockoutEnabled,
            LockoutEnd = u.LockoutEnd,
            ResponseCount = db.Responses.Count(r => r.RespondentId == u.Id && r.Status == ResponseStatus.Completed),
            WorkspaceId = u.WorkspaceId,
            WorkspaceName = db.Workspaces.Where(w => w.Id == u.WorkspaceId).Select(w => w.Name).FirstOrDefault(),
        });

    /// <summary>Role names of the given users in one query, keyed by user id.</summary>
    private static async Task<ILookup<Guid, string>> LoadRoleNamesAsync(AppDbContext db, List<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return Array.Empty<UserRoleName>().ToLookup(p => p.UserId, p => p.Role);
        }

        var pairs = await (
                from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                where userIds.Contains(userRole.UserId) && role.Name != null
                select new UserRoleName(userRole.UserId, role.Name!))
            .ToListAsync(ct);

        return pairs.ToLookup(p => p.UserId, p => p.Role);
    }

    private static UserDto ToDto(UserRow row, ILookup<Guid, string> roles, DateTimeOffset now) => new()
    {
        Id = row.Id,
        Email = row.Email ?? row.UserName ?? string.Empty,
        DisplayName = row.DisplayName,
        Roles = OrderRoles(roles[row.Id]),

        // Mirrors UserManager.IsLockedOutAsync; evaluated in memory because DateTimeOffset
        // comparisons cannot be translated by every provider (SQLite).
        IsLockedOut = row.LockoutEnabled && row.LockoutEnd is { } end && end > now,
        EmailConfirmed = row.EmailConfirmed,
        CreatedAt = row.CreatedAt,
        LastLoginAt = row.LastLoginAt,
        ResponseCount = row.ResponseCount,
        WorkspaceId = row.WorkspaceId,
        WorkspaceName = row.WorkspaceName,
    };

    /// <summary>Known roles in the order of <see cref="AppRoles.All"/>, then any other role alphabetically.</summary>
    private static List<string> OrderRoles(IEnumerable<string> roles) => roles
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(RoleRank)
        .ThenBy(r => r, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static int RoleRank(string role)
    {
        for (var i = 0; i < AppRoles.All.Count; i++)
        {
            if (string.Equals(AppRoles.All[i], role, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return AppRoles.All.Count;
    }

    // ---------------------------------------------------------------- rules & helpers

    private void EnsureAdmin()
    {
        if (!currentUser.IsAdmin && !currentUser.IsSuperAdmin)
        {
            throw new ForbiddenException("Only administrators can manage users.");
        }
    }

    private bool IsSelf(Guid id) => currentUser.UserId == id;

    /// <summary>
    /// Read context: a workspace admin's workspace (response counts stay within it) or, for super
    /// admins, every workspace.
    /// </summary>
    private async Task<AppDbContext> OpenReadContextAsync(CancellationToken ct)
    {
        var db = await dbFactory.CreateDbContextAsync(ct);
        return db.UseScope(currentUser.IsSuperAdmin ? DataScope.System : DataScope.ForWorkspace(currentUser.WorkspaceId!.Value));
    }

    /// <summary>Accounts the caller may see: their workspace's members, or every account for super admins.</summary>
    private IQueryable<ApplicationUser> VisibleUsers(AppDbContext db)
    {
        var users = db.Users.AsNoTracking();
        if (currentUser.IsSuperAdmin)
        {
            return users;
        }

        var workspaceId = currentUser.WorkspaceId;
        return users.Where(u => u.WorkspaceId == workspaceId);
    }

    /// <summary>Loads an account the caller may manage; accounts of other workspaces are reported as not found.</summary>
    private async Task<ApplicationUser> FindUserAsync(UserManager<ApplicationUser> users, Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null || (!currentUser.IsSuperAdmin && user.WorkspaceId != currentUser.WorkspaceId))
        {
            throw new NotFoundException(EntityName, id);
        }

        return user;
    }

    /// <summary>
    /// Workspace of a new account: always the caller's own for workspace admins; for super admins the
    /// requested workspace (which must exist) or none (a new super admin).
    /// </summary>
    private async Task<Guid?> ResolveNewUserWorkspaceAsync(Guid? requested, CancellationToken ct)
    {
        if (!currentUser.IsSuperAdmin)
        {
            return requested is null || requested == currentUser.WorkspaceId
                ? currentUser.WorkspaceId
                : throw new ForbiddenException("You can only add people to your own workspace.");
        }

        if (requested is { } workspaceId)
        {
            await using var db = (await dbFactory.CreateDbContextAsync(ct)).UseScope(DataScope.System);
            if (!await db.Workspaces.AnyAsync(w => w.Id == workspaceId, ct))
            {
                throw new AppValidationException(nameof(CreateUserRequest.WorkspaceId), "The selected workspace does not exist.");
            }
        }

        return requested;
    }

    /// <summary>
    /// Members of a workspace hold Admin/User; super admin accounts (no workspace) hold only SuperAdmin,
    /// which only super admins can grant.
    /// </summary>
    private void EnsureRolesFit(Guid? workspaceId, IReadOnlyCollection<string> roles, string property)
    {
        var allowed = workspaceId is null ? [AppRoles.SuperAdmin] : AppRoles.WorkspaceRoles;
        var misfits = roles.Where(r => !allowed.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        if (misfits.Count > 0)
        {
            throw new AppValidationException(property, workspaceId is null
                ? "Super admin accounts belong to no workspace and can only hold the SuperAdmin role."
                : $"Workspace members can hold these roles: {string.Join(", ", AppRoles.WorkspaceRoles)}.");
        }

        if (workspaceId is null && !currentUser.IsSuperAdmin)
        {
            throw new ForbiddenException("Only super admins can manage super admin accounts.");
        }
    }

    /// <summary>
    /// Prevents operations that would leave a workspace without an admin, or the system without a super
    /// admin. Accounts without such a role pass.
    /// </summary>
    /// <param name="users">User manager of the current scope.</param>
    /// <param name="user">The account about to lose access or its admin role.</param>
    /// <param name="operation">Verb phrase used in the message, e.g. "delete".</param>
    private static async Task EnsureNotLastAdminAsync(UserManager<ApplicationUser> users, ApplicationUser user, string operation)
    {
        if (await users.IsInRoleAsync(user, AppRoles.SuperAdmin))
        {
            if ((await users.GetUsersInRoleAsync(AppRoles.SuperAdmin)).All(a => a.Id == user.Id))
            {
                throw new BusinessRuleException(
                    $"You cannot {operation} {user.Email}: it is the last super admin account. Make another account a super admin first.");
            }

            return;
        }

        if (!await users.IsInRoleAsync(user, AppRoles.Admin))
        {
            return;
        }

        var admins = await users.GetUsersInRoleAsync(AppRoles.Admin);
        if (admins.Where(a => a.WorkspaceId == user.WorkspaceId).All(a => a.Id == user.Id))
        {
            throw new BusinessRuleException(
                $"You cannot {operation} {user.Email}: it is the last administrator of the workspace. Make another member an administrator first.");
        }
    }

    /// <summary>Creates missing roles of the fixed role set (normally already seeded at start-up).</summary>
    private static async Task EnsureRolesExistAsync(RoleManager<IdentityRole<Guid>> roleManager, IEnumerable<string> roles)
    {
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                ThrowIfFailed(await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() }));
            }
        }
    }

    /// <summary>
    /// Translates a failed <see cref="IdentityResult"/> into an application exception: duplicates and
    /// concurrency failures are conflicts, password / e-mail problems are validation errors keyed
    /// "Password" / "Email", anything else violates a business rule.
    /// </summary>
    private static void ThrowIfFailed(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        var errors = result.Errors.ToList();
        var conflict = errors.FirstOrDefault(e => e.Code is "DuplicateEmail" or "DuplicateUserName" or "ConcurrencyFailure");
        if (conflict is not null)
        {
            throw new ConflictException(conflict.Description);
        }

        var validation = errors
            .Select(e => (Key: ValidationKey(e.Code), e.Description))
            .Where(e => e.Key is not null)
            .GroupBy(e => e.Key!)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).Distinct().ToArray());
        if (validation.Count > 0)
        {
            throw new AppValidationException(validation);
        }

        throw new BusinessRuleException(string.Join(" ", errors.Select(e => e.Description)));
    }

    /// <summary>Maps Identity error codes (see <see cref="IdentityErrorDescriber"/>) to request properties.</summary>
    private static string? ValidationKey(string code) => code switch
    {
        _ when code.StartsWith("Password", StringComparison.Ordinal) => nameof(CreateUserRequest.Password),
        "InvalidEmail" or "InvalidUserName" => nameof(CreateUserRequest.Email),
        _ => null,
    };

    /// <summary>Converts FluentValidation failures into an <see cref="AppValidationException"/> keyed by property.</summary>
    private static void ThrowIfInvalid(ValidationResult result)
    {
        if (!result.IsValid)
        {
            throw new AppValidationException(result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()));
        }
    }

    private static string FormatRoles(IEnumerable<string> roles)
    {
        var list = OrderRoles(roles);
        return list.Count == 0 ? "(none)" : string.Join(", ", list);
    }

    /// <summary>Identity services resolved from one per-operation scope (they share its <see cref="AppDbContext"/>).</summary>
    private sealed record IdentityServices(
        UserManager<ApplicationUser> Users,
        RoleManager<IdentityRole<Guid>> Roles,
        AppDbContext Db)
    {
        public static IdentityServices From(IServiceProvider services) => new(
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            services.GetRequiredService<RoleManager<IdentityRole<Guid>>>(),
            services.GetRequiredService<AppDbContext>());
    }

    /// <summary>A user id with one of its role names.</summary>
    private sealed record UserRoleName(Guid UserId, string Role);

    /// <summary>Flat projection of a user (roles are loaded separately).</summary>
    private sealed class UserRow
    {
        public Guid Id { get; init; }

        public string? Email { get; init; }

        public string? UserName { get; init; }

        public Guid? WorkspaceId { get; init; }

        public string? WorkspaceName { get; init; }

        public string? DisplayName { get; init; }

        public bool EmailConfirmed { get; init; }

        public DateTime CreatedAt { get; init; }

        public DateTime? LastLoginAt { get; init; }

        public bool LockoutEnabled { get; init; }

        public DateTimeOffset? LockoutEnd { get; init; }

        public int ResponseCount { get; init; }
    }
}
