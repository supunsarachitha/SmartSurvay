using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Users;
using SmartSurvey.Domain.Identity;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Admin;

/// <summary>
/// Workspace rules of <see cref="IUserAdminService"/>: workspace admins only see and manage their own
/// workspace's members; super admins manage every account and the super admin accounts.
/// </summary>
public sealed class UserAdminWorkspaceTests : IAsyncLifetime
{
    private static readonly Guid TheirAdminId = Guid.Parse("00000000-0000-0000-0000-0000000fa001");
    private static readonly Guid TheirMemberId = Guid.Parse("00000000-0000-0000-0000-0000000fb001");
    private IdentityTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await IdentityTestHost.CreateAsync();
        await _host.AddUserAsync(TestCurrentUser.AdminId, "admin@test.local", AppRoles.Admin, AppRoles.User);
        await _host.AddUserAsync(TestCurrentUser.RespondentId, "user@test.local", AppRoles.User);
        await _host.AddUserAsync(TheirAdminId, "admin@other.local", TestWorkspaces.OtherId, AppRoles.Admin, AppRoles.User);
        await _host.AddUserAsync(TheirMemberId, "member@other.local", TestWorkspaces.OtherId, AppRoles.User);
        await _host.AddUserAsync(TestCurrentUser.SuperAdminId, "super@test.local", (Guid?)null, AppRoles.SuperAdmin);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Workspace_admins_list_only_their_own_members()
    {
        var page = await Users(s => s.ListAsync(new UserQuery { WorkspaceId = TestWorkspaces.OtherId })); // filter is ignored

        Assert.Equal(["admin@test.local", "user@test.local"], page.Items.Select(u => u.Email).Order());
        Assert.All(page.Items, u => Assert.Equal(TestWorkspaces.DefaultId, u.WorkspaceId));
    }

    [Fact]
    public async Task Accounts_of_other_workspaces_do_not_exist_for_a_workspace_admin()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Users(s => s.GetAsync(TheirMemberId)));
        await Assert.ThrowsAsync<NotFoundException>(() => Users(s => s.LockAsync(TheirMemberId)));
        await Assert.ThrowsAsync<NotFoundException>(() => Users(s => s.SetPasswordAsync(TheirMemberId, "N3w-Secret!")));
        await Assert.ThrowsAsync<NotFoundException>(() => Users(s => s.SetRolesAsync(TheirMemberId, [AppRoles.Admin])));
        await Assert.ThrowsAsync<NotFoundException>(() => Users(s => s.DeleteAsync(TheirMemberId)));
        await Assert.ThrowsAsync<NotFoundException>(() => Users(s => s.GetAsync(TestCurrentUser.SuperAdminId)));

        Assert.False((await IdentityAsync(m => m.FindByIdAsync(TheirMemberId.ToString())))!.LockoutEnd.HasValue);
    }

    [Fact]
    public async Task Workspace_admins_create_members_of_their_own_workspace_only()
    {
        var created = await Users(s => s.CreateAsync(new CreateUserRequest { Email = "new@test.local", Password = "S3cure-pass" }));

        Assert.Equal(TestWorkspaces.DefaultId, created.WorkspaceId);
        await Assert.ThrowsAsync<ForbiddenException>(() => Users(s => s.CreateAsync(
            new CreateUserRequest { Email = "x@test.local", Password = "S3cure-pass", WorkspaceId = TestWorkspaces.OtherId })));
    }

    [Fact]
    public async Task Workspace_admins_cannot_hand_out_the_super_admin_role()
    {
        await Assert.ThrowsAsync<AppValidationException>(() => Users(s => s.SetRolesAsync(TestCurrentUser.RespondentId, [AppRoles.SuperAdmin])));
        await Assert.ThrowsAsync<AppValidationException>(() => Users(s => s.CreateAsync(
            new CreateUserRequest { Email = "boss@test.local", Password = "S3cure-pass", Roles = [AppRoles.SuperAdmin] })));
    }

    [Fact]
    public async Task The_last_admin_rule_is_counted_per_workspace()
    {
        // Two admins exist system-wide, but the other workspace has only one: demoting it must fail until it has a second.
        _host.User.ActAsSuperAdmin();
        await Assert.ThrowsAsync<BusinessRuleException>(() => Users(s => s.SetRolesAsync(TheirAdminId, [AppRoles.User])));

        await Users(s => s.SetRolesAsync(TheirMemberId, [AppRoles.Admin, AppRoles.User]));
        var demoted = await Users(s => s.SetRolesAsync(TheirAdminId, [AppRoles.User]));
        Assert.Equal([AppRoles.User], demoted.Roles);
    }

    [Fact]
    public async Task Super_admins_see_every_account_with_its_workspace()
    {
        _host.User.ActAsSuperAdmin();

        var all = await Users(s => s.ListAsync(new UserQuery()));
        var theirs = await Users(s => s.ListAsync(new UserQuery { WorkspaceId = TestWorkspaces.OtherId }));

        Assert.Equal(5, all.TotalCount);
        Assert.Equal("Other workspace", all.Items.Single(u => u.Id == TheirMemberId).WorkspaceName);
        Assert.Null(all.Items.Single(u => u.Id == TestCurrentUser.SuperAdminId).WorkspaceId);
        Assert.Equal(["admin@other.local", "member@other.local"], theirs.Items.Select(u => u.Email).Order());
    }

    [Fact]
    public async Task Super_admins_create_workspace_members_and_other_super_admins()
    {
        _host.User.ActAsSuperAdmin();

        var member = await Users(s => s.CreateAsync(new CreateUserRequest
        {
            Email = "lead@other.local", Password = "S3cure-pass", WorkspaceId = TestWorkspaces.OtherId, Roles = [AppRoles.Admin, AppRoles.User],
        }));
        var super = await Users(s => s.CreateAsync(new CreateUserRequest { Email = "ops@test.local", Password = "S3cure-pass" }));

        Assert.Equal(TestWorkspaces.OtherId, member.WorkspaceId);
        Assert.Equal([AppRoles.Admin, AppRoles.User], member.Roles);
        Assert.Null(super.WorkspaceId);
        Assert.Equal([AppRoles.SuperAdmin], super.Roles);

        // System events: super admin actions are not written into a workspace's audit log.
        await using var db = await _host.CreateDbContextAsync();
        var entry = await db.AuditLogs.SingleAsync(e => e.Action == AuditActions.UserCreated && e.EntityId == member.Id.ToString());
        Assert.Null(entry.WorkspaceId);
    }

    [Fact]
    public async Task Super_admin_accounts_hold_only_their_role_and_members_never_hold_it()
    {
        _host.User.ActAsSuperAdmin();

        await Assert.ThrowsAsync<AppValidationException>(() => Users(s => s.CreateAsync(
            new CreateUserRequest { Email = "mixed@test.local", Password = "S3cure-pass", Roles = [AppRoles.SuperAdmin, AppRoles.Admin] })));
        await Assert.ThrowsAsync<AppValidationException>(() => Users(s => s.CreateAsync(
            new CreateUserRequest { Email = "m@other.local", Password = "S3cure-pass", WorkspaceId = TestWorkspaces.OtherId, Roles = [AppRoles.SuperAdmin] })));
        await Assert.ThrowsAsync<AppValidationException>(() => Users(s => s.SetRolesAsync(TheirMemberId, [AppRoles.SuperAdmin])));
        await Assert.ThrowsAsync<AppValidationException>(() => Users(s => s.CreateAsync(
            new CreateUserRequest { Email = "ghost@test.local", Password = "S3cure-pass", WorkspaceId = Guid.NewGuid() })));
    }

    [Fact]
    public async Task The_last_super_admin_cannot_be_removed_and_nobody_demotes_themselves()
    {
        _host.User.ActAsSuperAdmin();
        var second = await Users(s => s.CreateAsync(new CreateUserRequest { Email = "ops@test.local", Password = "S3cure-pass" }));

        await Assert.ThrowsAsync<BusinessRuleException>(() => Users(s => s.SetRolesAsync(TestCurrentUser.SuperAdminId, [])));
        await Users(s => s.DeleteAsync(second.Id));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Users(s => s.DeleteAsync(TestCurrentUser.SuperAdminId))); // self

        // Acting as another (not yet existing) super admin: the only remaining super admin is protected.
        _host.User.ActAs(Guid.NewGuid(), "someone@test.local", AppRoles.SuperAdmin);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Users(s => s.LockAsync(TestCurrentUser.SuperAdminId)));
    }

    private Task<T> Users<T>(Func<IUserAdminService, Task<T>> action) => _host.WithAsync(action);

    private Task Users(Func<IUserAdminService, Task> action) => _host.WithAsync(action);

    private Task<T> IdentityAsync<T>(Func<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>, Task<T>> action) => _host.WithAsync(action);
}
