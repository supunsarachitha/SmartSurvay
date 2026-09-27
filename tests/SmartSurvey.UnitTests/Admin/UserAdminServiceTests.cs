using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Users;
using SmartSurvey.Domain.Identity;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Admin;

/// <summary>User administration with real ASP.NET Core Identity over SQLite. The acting admin has id <see cref="TestCurrentUser.AdminId"/>.</summary>
public sealed class UserAdminServiceTests : IAsyncLifetime
{
    private static readonly Guid OtherAdminId = Guid.Parse("00000000-0000-0000-0000-00000000a002");
    private IdentityTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await IdentityTestHost.CreateAsync();
        await _host.AddUserAsync(TestCurrentUser.AdminId, "admin@test.local", AppRoles.Admin, AppRoles.User);
        await _host.AddUserAsync(TestCurrentUser.RespondentId, "user@test.local", AppRoles.User);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Create_adds_a_user_with_roles_and_audits_it()
    {
        var created = await Users(s => s.CreateAsync(new CreateUserRequest
        {
            Email = "  new@test.local ",
            DisplayName = "New Person",
            Password = "S3cure-pass",
            Roles = [AppRoles.User],
        }));

        Assert.Equal("new@test.local", created.Email);
        Assert.Equal("New Person", created.DisplayName);
        Assert.Equal([AppRoles.User], created.Roles);
        Assert.False(created.IsLockedOut);
        Assert.True(await AuditedAsync(AuditActions.UserCreated, created.Id));
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_email() =>
        await Assert.ThrowsAsync<ConflictException>(() => Users(s => s.CreateAsync(new CreateUserRequest
        {
            Email = "USER@test.local",
            Password = "S3cure-pass",
            Roles = [AppRoles.User],
        })));

    [Fact]
    public async Task Create_rejects_a_weak_password() =>
        await Assert.ThrowsAsync<AppValidationException>(() => Users(s => s.CreateAsync(new CreateUserRequest
        {
            Email = "weak@test.local",
            Password = "short",
            Roles = [AppRoles.User],
        })));

    [Fact]
    public async Task SetRoles_promotes_and_demotes_other_users()
    {
        var promoted = await Users(s => s.SetRolesAsync(TestCurrentUser.RespondentId, [AppRoles.Admin, AppRoles.User]));
        var demoted = await Users(s => s.SetRolesAsync(TestCurrentUser.RespondentId, [AppRoles.User]));

        Assert.Contains(AppRoles.Admin, promoted.Roles);
        Assert.DoesNotContain(AppRoles.Admin, demoted.Roles);
        Assert.True(await AuditedAsync(AuditActions.UserRolesChanged, TestCurrentUser.RespondentId));
    }

    [Fact]
    public async Task SetRoles_rejects_unknown_roles() =>
        await Assert.ThrowsAsync<AppValidationException>(() => Users(s => s.SetRolesAsync(TestCurrentUser.RespondentId, ["SuperUser"])));

    [Fact]
    public async Task Admins_cannot_remove_their_own_admin_role() =>
        await Assert.ThrowsAsync<BusinessRuleException>(() => Users(s => s.SetRolesAsync(TestCurrentUser.AdminId, [AppRoles.User])));

    [Fact]
    public async Task Lock_and_unlock_another_user()
    {
        var locked = await Users(s => s.LockAsync(TestCurrentUser.RespondentId));
        var unlocked = await Users(s => s.UnlockAsync(TestCurrentUser.RespondentId));

        Assert.True(locked.IsLockedOut);
        Assert.False(unlocked.IsLockedOut);
        Assert.True(await AuditedAsync(AuditActions.UserLocked, TestCurrentUser.RespondentId));
        Assert.True(await AuditedAsync(AuditActions.UserUnlocked, TestCurrentUser.RespondentId));
    }

    [Fact]
    public async Task Admins_cannot_lock_or_delete_themselves()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Users(s => s.LockAsync(TestCurrentUser.AdminId)));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Users(s => s.DeleteAsync(TestCurrentUser.AdminId)));
    }

    [Fact]
    public async Task The_last_administrator_cannot_be_deleted()
    {
        // The acting admin lost the role since signing in; the other account is the only admin left.
        await _host.AddUserAsync(OtherAdminId, "second@test.local", AppRoles.Admin);
        await _host.WithAsync<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>(async users =>
            await users.RemoveFromRoleAsync((await users.FindByIdAsync(TestCurrentUser.AdminId.ToString()))!, AppRoles.Admin));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Users(s => s.DeleteAsync(OtherAdminId)));

        Assert.Contains("last administrator", ex.Message);
    }

    [Fact]
    public async Task Delete_removes_another_user()
    {
        await Users(s => s.DeleteAsync(TestCurrentUser.RespondentId));

        await Assert.ThrowsAsync<NotFoundException>(() => Users(s => s.GetAsync(TestCurrentUser.RespondentId)));
        Assert.True(await AuditedAsync(AuditActions.UserDeleted, TestCurrentUser.RespondentId));
    }

    [Fact]
    public async Task List_searches_and_filters_by_role()
    {
        var all = await Users(s => s.ListAsync(new UserQuery()));
        var admins = await Users(s => s.ListAsync(new UserQuery { Role = AppRoles.Admin }));
        var search = await Users(s => s.ListAsync(new UserQuery { Search = "USER@" }));

        Assert.Equal(2, all.TotalCount);
        Assert.Equal("admin@test.local", Assert.Single(admins.Items).Email);
        Assert.Equal("user@test.local", Assert.Single(search.Items).Email);
    }

    [Fact]
    public async Task Get_of_an_unknown_user_throws_not_found() =>
        await Assert.ThrowsAsync<NotFoundException>(() => Users(s => s.GetAsync(Guid.NewGuid())));

    [Fact]
    public async Task Only_administrators_can_manage_users()
    {
        _host.User.ActAsRespondent();

        await Assert.ThrowsAsync<ForbiddenException>(() => Users(s => s.ListAsync(new UserQuery())));
        await Assert.ThrowsAsync<ForbiddenException>(() => Users(s => s.LockAsync(TestCurrentUser.AdminId)));
    }

    private Task<T> Users<T>(Func<IUserAdminService, Task<T>> action) => _host.WithAsync(action);

    private Task Users(Func<IUserAdminService, Task> action) => _host.WithAsync(action);

    private async Task<bool> AuditedAsync(string action, Guid userId)
    {
        await using var db = await _host.CreateDbContextAsync();
        var id = userId.ToString();
        return await db.AuditLogs.AnyAsync(e => e.Action == action && e.EntityId == id);
    }
}
