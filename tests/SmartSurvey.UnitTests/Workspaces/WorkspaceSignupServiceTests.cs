using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Identity;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Workspaces;

/// <summary>Self-service sign-up and joining a workspace with its link (real Identity over SQLite).</summary>
public sealed class WorkspaceSignupServiceTests : IAsyncLifetime
{
    private IdentityTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await IdentityTestHost.CreateAsync();
        await _host.AddUserAsync(TestCurrentUser.AdminId, "admin@test.local", AppRoles.Admin, AppRoles.User);
        _host.User.ActAsAnonymous(); // the forms are used by visitors
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Signup<T>(Func<IWorkspaceSignupService, Task<T>> action) => _host.WithAsync(action);

    private static WorkspaceSignupRequest NewSignup(string email = "founder@new.local", string? slug = null) => new()
    {
        WorkspaceName = "New Ventures", WorkspaceSlug = slug, DisplayName = "Founder", Email = email, Password = "S3cure-pass",
    };

    private static JoinWorkspaceRequest NewJoin(string email = "joiner@test.local") => new() { DisplayName = "Joiner", Email = email, Password = "S3cure-pass" };

    [Fact]
    public async Task Signing_up_creates_an_active_workspace_owned_by_its_new_admin()
    {
        var result = await Signup(s => s.SignUpAsync(NewSignup()));

        Assert.Equal(WorkspaceStatus.Active, result.Status);
        Assert.Equal("new-ventures", result.WorkspaceSlug);
        await using var db = await _host.CreateDbContextAsync();
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == result.WorkspaceId);
        var owner = await db.Users.SingleAsync(u => u.Id == result.UserId);
        Assert.Equal(owner.Id, workspace.OwnerId);
        Assert.Equal(workspace.Id, owner.WorkspaceId);
        Assert.Equal("Founder", owner.DisplayName);
        Assert.Equal(AppRoles.WorkspaceRoles.Order(), (await _host.WithAsync<UserManager<ApplicationUser>, IList<string>>(u => u.GetRolesAsync(owner))).Order());
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.WorkspaceCreated && a.WorkspaceId == null));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.WorkspaceCreated && a.WorkspaceId == workspace.Id));
    }

    [Fact]
    public async Task With_approval_required_new_workspaces_wait_for_a_super_admin()
    {
        await SaveSettingsAsync(new UpdatePlatformSettingsRequest { AllowWorkspaceSignup = true, RequireWorkspaceApproval = true });

        var result = await Signup(s => s.SignUpAsync(NewSignup()));

        Assert.Equal(WorkspaceStatus.PendingApproval, result.Status);
    }

    [Fact]
    public async Task Sign_up_can_be_switched_off()
    {
        await SaveSettingsAsync(new UpdatePlatformSettingsRequest { AllowWorkspaceSignup = false });

        await Assert.ThrowsAsync<BusinessRuleException>(() => Signup(s => s.SignUpAsync(NewSignup())));
    }

    [Fact]
    public async Task Sign_up_rejects_taken_addresses_and_accounts_and_leaves_nothing_behind()
    {
        await Assert.ThrowsAsync<ConflictException>(() => Signup(s => s.SignUpAsync(NewSignup(email: "admin@test.local"))));
        await Assert.ThrowsAsync<ConflictException>(() => Signup(s => s.SignUpAsync(NewSignup(slug: "test"))));
        var weak = NewSignup();
        weak.Password = "123";
        var error = await Assert.ThrowsAsync<AppValidationException>(() => Signup(s => s.SignUpAsync(weak)));
        Assert.Contains(nameof(WorkspaceSignupRequest.Password), error.Errors.Keys);
        await Assert.ThrowsAsync<AppValidationException>(() => Signup(s => s.SignUpAsync(new WorkspaceSignupRequest())));

        await using var db = await _host.CreateDbContextAsync();
        Assert.Equal(2, await db.Workspaces.CountAsync());
        Assert.False(await db.Users.AnyAsync(u => u.Email == "founder@new.local"));
    }

    [Fact]
    public async Task Joining_creates_a_member_of_that_workspace()
    {
        var result = await Signup(s => s.JoinAsync(" TEST ", NewJoin()));

        Assert.Equal(TestWorkspaces.DefaultId, result.WorkspaceId);
        await using var db = await _host.CreateDbContextAsync();
        var member = await db.Users.SingleAsync(u => u.Id == result.UserId);
        Assert.Equal(TestWorkspaces.DefaultId, member.WorkspaceId);
        Assert.Equal([AppRoles.User], await _host.WithAsync<UserManager<ApplicationUser>, IList<string>>(u => u.GetRolesAsync(member)));
        Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.MemberJoined && a.WorkspaceId == TestWorkspaces.DefaultId));
    }

    [Fact]
    public async Task Joining_needs_an_active_workspace_that_accepts_members()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Signup(s => s.JoinAsync("missing", NewJoin())));
        await Assert.ThrowsAsync<ConflictException>(() => Signup(s => s.JoinAsync("test", NewJoin("admin@test.local"))));

        await using (var db = await _host.CreateDbContextAsync())
        {
            await db.Workspaces.Where(w => w.Id == TestWorkspaces.OtherId).ExecuteUpdateAsync(w => w.SetProperty(x => x.AllowSelfRegistration, false));
            await db.Workspaces.Where(w => w.Id == TestWorkspaces.DefaultId).ExecuteUpdateAsync(w => w.SetProperty(x => x.Status, WorkspaceStatus.Disabled));
        }

        await Assert.ThrowsAsync<ForbiddenException>(() => Signup(s => s.JoinAsync("other", NewJoin())));
        await Assert.ThrowsAsync<NotFoundException>(() => Signup(s => s.JoinAsync("test", NewJoin())));
    }

    private async Task SaveSettingsAsync(UpdatePlatformSettingsRequest request)
    {
        _host.User.ActAsSuperAdmin();
        await _host.WithAsync<IPlatformSettingsService, PlatformSettingsDto>(s => s.UpdateAsync(request));
        _host.User.ActAsAnonymous();
    }
}
