using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.Identity;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Workspaces;

/// <summary>Workspace administration by super admins (real Identity over SQLite).</summary>
public sealed class PlatformWorkspaceServiceTests : IAsyncLifetime
{
    private static readonly Guid TheirAdminId = Guid.Parse("00000000-0000-0000-0000-0000000fa001");
    private IdentityTestHost _host = null!;

    public async Task InitializeAsync()
    {
        _host = await IdentityTestHost.CreateAsync();
        await _host.AddUserAsync(TestCurrentUser.AdminId, "admin@test.local", AppRoles.Admin, AppRoles.User);
        await _host.AddUserAsync(TestCurrentUser.RespondentId, "user@test.local", AppRoles.User);
        await _host.AddUserAsync(TheirAdminId, "admin@other.local", TestWorkspaces.OtherId, AppRoles.Admin, AppRoles.User);
        await _host.AddUserAsync(TestCurrentUser.SuperAdminId, "super@test.local", (Guid?)null, AppRoles.SuperAdmin);

        // A survey with one completed response in the other workspace (system scope: explicit workspace ids).
        await using var db = await _host.CreateDbContextAsync();
        var survey = new Survey { Title = "Theirs", Slug = "theirs", WorkspaceId = TestWorkspaces.OtherId, Status = SurveyStatus.Published };
        db.Surveys.Add(survey);
        db.Responses.Add(new SurveyResponse
        {
            SurveyId = survey.Id, WorkspaceId = TestWorkspaces.OtherId, Status = ResponseStatus.Completed,
            StartedAt = _host.Time.GetUtcNow().UtcDateTime, RespondentId = TheirAdminId,
        });
        db.AuditLogs.Add(new AuditLogEntry { WorkspaceId = TestWorkspaces.OtherId, Action = "test.theirs", EntityType = "Test", Timestamp = _host.Time.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync();

        _host.User.ActAsSuperAdmin();
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Platform<T>(Func<IPlatformWorkspaceService, Task<T>> action) => _host.WithAsync(action);

    private Task Platform(Func<IPlatformWorkspaceService, Task> action) => _host.WithAsync(action);

    private static CreateWorkspaceRequest NewWorkspace(string name = "Acme Research", string? slug = null, string email = "lead@acme.local") => new()
    {
        Name = name, Slug = slug, AdminEmail = email, AdminDisplayName = "Lead", AdminPassword = "S3cure-pass",
    };

    [Fact]
    public async Task Only_super_admins_manage_workspaces()
    {
        _host.User.ActAsAdmin();

        await Assert.ThrowsAsync<ForbiddenException>(() => Platform(s => s.ListAsync(new WorkspaceListQuery())));
        await Assert.ThrowsAsync<ForbiddenException>(() => Platform(s => s.DisableAsync(TestWorkspaces.OtherId, null)));
        await Assert.ThrowsAsync<ForbiddenException>(() => Platform(s => s.GetOverviewAsync()));
    }

    [Fact]
    public async Task The_list_shows_figures_but_no_content()
    {
        var page = await Platform(s => s.ListAsync(new WorkspaceListQuery()));
        var theirs = page.Items.Single(w => w.Id == TestWorkspaces.OtherId);
        var mine = page.Items.Single(w => w.Id == TestWorkspaces.DefaultId);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal((1, 1, 1, 1), (theirs.MemberCount, theirs.AdminCount, theirs.SurveyCount, theirs.ResponseCount));
        Assert.Equal(["admin@other.local"], theirs.AdminEmails);
        Assert.Equal((2, 1, 0), (mine.MemberCount, mine.AdminCount, mine.SurveyCount));

        Assert.Equal(["Other workspace"], (await Platform(s => s.ListAsync(new WorkspaceListQuery { Search = "OTH" }))).Items.Select(w => w.Name));
        Assert.Empty((await Platform(s => s.ListAsync(new WorkspaceListQuery { Status = WorkspaceStatus.Disabled }))).Items);
    }

    [Fact]
    public async Task Creating_a_workspace_creates_its_first_admin()
    {
        var created = await Platform(s => s.CreateAsync(NewWorkspace()));

        Assert.Equal("acme-research", created.Slug);
        Assert.Equal(WorkspaceStatus.Active, created.Status);
        Assert.Equal(["lead@acme.local"], created.AdminEmails);

        var admin = await _host.WithAsync<UserManager<ApplicationUser>, ApplicationUser?>(u => u.FindByEmailAsync("lead@acme.local"));
        Assert.Equal(created.Id, admin!.WorkspaceId);
        Assert.True(admin.EmailConfirmed);
        Assert.True(await _host.WithAsync<UserManager<ApplicationUser>, bool>(u => u.CheckPasswordAsync(admin, "S3cure-pass")));
        Assert.True(await AuditedAsync(AuditActions.WorkspaceCreated, created.Id, workspaceId: null));

        // Same name again: a unique address is generated; a requested address that is taken is a conflict.
        var second = await Platform(s => s.CreateAsync(NewWorkspace(email: "two@acme.local")));
        Assert.Equal("acme-research-2", second.Slug);
        await Assert.ThrowsAsync<ConflictException>(() => Platform(s => s.CreateAsync(NewWorkspace(slug: "acme-research", email: "three@acme.local"))));
    }

    [Fact]
    public async Task Creating_a_workspace_is_all_or_nothing()
    {
        await Assert.ThrowsAsync<ConflictException>(() => Platform(s => s.CreateAsync(NewWorkspace(email: "admin@other.local"))));
        var weak = NewWorkspace();
        weak.AdminPassword = "short";
        var error = await Assert.ThrowsAsync<AppValidationException>(() => Platform(s => s.CreateAsync(weak)));
        Assert.Contains(nameof(CreateWorkspaceRequest.AdminPassword), error.Errors.Keys);

        await using var db = await _host.CreateDbContextAsync();
        Assert.False(await db.Workspaces.AnyAsync(w => w.Slug == "acme-research")); // rolled back with the account
    }

    [Fact]
    public async Task Super_admins_rename_workspaces_and_change_their_address()
    {
        var updated = await Platform(s => s.UpdateAsync(TestWorkspaces.OtherId, new UpdateWorkspaceRequest
        {
            Name = "Renamed", Slug = "renamed", Description = "About", ContactEmail = "hi@other.local",
        }));

        Assert.Equal(("Renamed", "renamed", "hi@other.local"), (updated.Name, updated.Slug, updated.ContactEmail));
        await Assert.ThrowsAsync<ConflictException>(() => Platform(s => s.UpdateAsync(TestWorkspaces.OtherId, new UpdateWorkspaceRequest { Name = "X", Slug = "test" })));
        await Assert.ThrowsAsync<AppValidationException>(() => Platform(s => s.UpdateAsync(TestWorkspaces.OtherId, new UpdateWorkspaceRequest { Name = "X", Slug = "No Spaces" })));
    }

    [Fact]
    public async Task Disabling_and_enabling_is_recorded_for_the_system_and_the_workspace()
    {
        var disabled = await Platform(s => s.DisableAsync(TestWorkspaces.OtherId, "  Unpaid invoice "));
        Assert.Equal(WorkspaceStatus.Disabled, disabled.Status);
        Assert.Equal("Unpaid invoice", disabled.StatusReason);
        Assert.NotNull(disabled.StatusChangedAt);
        Assert.True(await AuditedAsync(AuditActions.WorkspaceDisabled, TestWorkspaces.OtherId, workspaceId: null));
        Assert.True(await AuditedAsync(AuditActions.WorkspaceDisabled, TestWorkspaces.OtherId, workspaceId: TestWorkspaces.OtherId));

        await Assert.ThrowsAsync<BusinessRuleException>(() => Platform(s => s.ApproveAsync(TestWorkspaces.OtherId)));
        var enabled = await Platform(s => s.EnableAsync(TestWorkspaces.OtherId));
        Assert.Equal(WorkspaceStatus.Active, enabled.Status);
        Assert.Null(enabled.StatusReason);
        Assert.True(await AuditedAsync(AuditActions.WorkspaceEnabled, TestWorkspaces.OtherId, workspaceId: null));

        await Assert.ThrowsAsync<AppValidationException>(() => Platform(s => s.DisableAsync(TestWorkspaces.OtherId, new string('x', 501))));
    }

    [Fact]
    public async Task Pending_workspaces_are_approved()
    {
        await using (var db = await _host.CreateDbContextAsync())
        {
            await db.Workspaces.Where(w => w.Id == TestWorkspaces.OtherId)
                .ExecuteUpdateAsync(w => w.SetProperty(x => x.Status, WorkspaceStatus.PendingApproval));
        }

        var overview = await Platform(s => s.GetOverviewAsync());
        Assert.Equal([TestWorkspaces.OtherId], overview.PendingWorkspaces.Select(w => w.Id));

        var approved = await Platform(s => s.ApproveAsync(TestWorkspaces.OtherId));
        Assert.Equal(WorkspaceStatus.Active, approved.Status);
        Assert.True(await AuditedAsync(AuditActions.WorkspaceApproved, TestWorkspaces.OtherId, workspaceId: null));
    }

    [Fact]
    public async Task Deleting_requires_a_disabled_workspace_and_its_exact_name()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Platform(s => s.DeleteAsync(TestWorkspaces.OtherId, "Other workspace")));
        await Platform(s => s.DisableAsync(TestWorkspaces.OtherId, null));
        await Assert.ThrowsAsync<AppValidationException>(() => Platform(s => s.DeleteAsync(TestWorkspaces.OtherId, "other workspace")));
        await Assert.ThrowsAsync<NotFoundException>(() => Platform(s => s.DeleteAsync(Guid.NewGuid(), "x")));
    }

    [Fact]
    public async Task Deleting_removes_all_data_and_accounts_of_that_workspace_only()
    {
        await Platform(s => s.DisableAsync(TestWorkspaces.OtherId, null));
        await Platform(s => s.DeleteAsync(TestWorkspaces.OtherId, " Other workspace "));

        await using var db = await _host.CreateDbContextAsync();
        Assert.False(await db.Workspaces.AnyAsync(w => w.Id == TestWorkspaces.OtherId));
        Assert.False(await db.Surveys.AnyAsync(s => s.WorkspaceId == TestWorkspaces.OtherId));
        Assert.False(await db.Responses.AnyAsync(r => r.WorkspaceId == TestWorkspaces.OtherId));
        Assert.False(await db.AuditLogs.AnyAsync(a => a.WorkspaceId == TestWorkspaces.OtherId));
        Assert.False(await db.Users.AnyAsync(u => u.Id == TheirAdminId));
        Assert.False(await db.UserRoles.AnyAsync(ur => ur.UserId == TheirAdminId));

        Assert.Equal(2, await db.Users.CountAsync(u => u.WorkspaceId == TestWorkspaces.DefaultId));
        Assert.True(await db.Users.AnyAsync(u => u.Id == TestCurrentUser.SuperAdminId));
        var deletion = await db.AuditLogs.SingleAsync(a => a.Action == AuditActions.WorkspaceDeleted);
        Assert.Null(deletion.WorkspaceId);
        Assert.Contains("1 surveys, 1 responses and 1 accounts", deletion.Details);
    }

    [Fact]
    public async Task The_overview_counts_every_workspace()
    {
        var overview = await Platform(s => s.GetOverviewAsync());

        Assert.Equal((2, 2, 0, 0), (overview.WorkspaceCount, overview.ActiveCount, overview.DisabledCount, overview.PendingCount));
        Assert.Equal((3, 1, 1, 1), (overview.MemberCount, overview.SuperAdminCount, overview.SurveyCount, overview.ResponseCount));
        Assert.Equal(2, overview.RecentWorkspaces.Count);
    }

    private async Task<bool> AuditedAsync(string action, Guid entityId, Guid? workspaceId)
    {
        await using var db = await _host.CreateDbContextAsync();
        var id = entityId.ToString();
        return await db.AuditLogs.AnyAsync(e => e.Action == action && e.EntityId == id && e.WorkspaceId == workspaceId);
    }
}
