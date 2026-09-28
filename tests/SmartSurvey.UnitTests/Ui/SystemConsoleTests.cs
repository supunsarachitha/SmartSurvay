using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Users;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Identity;
using SmartSurvey.Web.Components.Admin.Audit;
using SmartSurvey.Web.Components.Pages.SystemConsole;
using SmartSurvey.Web.Components.Shared;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>The System console pages of super admins.</summary>
public sealed class SystemConsoleTests : UiTestBase
{
    private readonly FakePlatformWorkspaceService _platform = new();
    private readonly FakeUserAdminService _users = new();
    private readonly FakeAuditService _audit = new();
    private readonly WorkspaceSummaryDto _acme = new()
    {
        Id = Guid.NewGuid(), Name = "Acme Research", Slug = "acme", Status = WorkspaceStatus.Active,
        MemberCount = 4, AdminCount = 1, SurveyCount = 3, ResponseCount = 42, AdminEmails = ["lead@acme.test"],
    };

    public SystemConsoleTests()
    {
        Services.AddScoped<ToastService>();
        Services.AddSingleton<IPlatformWorkspaceService>(_platform);
        Services.AddSingleton<IUserAdminService>(_users);
        Services.AddSingleton<IAuditService>(_audit);
        _platform.Workspaces.Add(_acme);
        SignInAsSuperAdmin();
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    private static void Click<T>(IRenderedComponent<T> cut, string text, string selector = "button") where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll(selector).First(b => b.TextContent.Contains(text, StringComparison.Ordinal)).Click();

    [Fact]
    public void Overview_shows_the_figures_and_approves_waiting_workspaces_in_place()
    {
        var pending = _acme with { Id = Guid.NewGuid(), Name = "Book Club", Slug = "book-club", Status = WorkspaceStatus.PendingApproval };
        _platform.Workspaces.Add(pending);

        var page = Render<SystemOverview>();

        Assert.Contains("Waiting for approval", page.Markup);
        Assert.NotNull(page.Find($"a[href='system/workspaces/{_acme.Id}']"));
        Click(page, "Approve");
        Assert.Contains($"Approve:{pending.Id}", _platform.Calls);
        Assert.DoesNotContain(page.FindAll("button"), b => b.TextContent.Contains("Approve")); // the list is reloaded
    }

    [Fact]
    public void Workspace_list_filters_by_status()
    {
        var page = Render<WorkspaceList>();
        Assert.Contains("Acme Research", page.Markup);

        page.Find("select[aria-label='Filter by status']").Change(WorkspaceStatus.Disabled.ToString());

        Assert.Equal(WorkspaceStatus.Disabled, _platform.LastQuery!.Status);
        Assert.Contains("No workspaces match", page.Markup);
    }

    [Fact]
    public void Creating_a_workspace_opens_its_page()
    {
        var page = Render<WorkspaceCreate>();

        page.Find("#nw-name").Input("Field Research");
        page.Find("#nw-email").Input("lead@field.test");
        page.Find("#nw-password").Input("Field123!");
        Click(page, "Create workspace");

        Assert.Equal("lead@field.test", _platform.Created!.AdminEmail);
        Assert.StartsWith("http://localhost/system/workspaces/", Navigation.Uri);
    }

    [Fact]
    public void Creating_a_workspace_shows_field_errors()
    {
        var page = Render<WorkspaceCreate>();

        Click(page, "Create workspace");

        Assert.Contains("is-invalid", page.Find("#nw-name").ClassName);
        Assert.Contains("Please enter a name for the workspace.", page.Markup);
    }

    [Fact]
    public void Disabling_asks_for_a_reason_and_enabling_brings_it_back()
    {
        var page = Render<WorkspaceDetail>(p => p.Add(x => x.Id, _acme.Id));

        Click(page, "Disable workspace");
        page.Find("#wd-reason").Input("Unpaid invoice");
        page.FindAll(".ss-modal button").Last(b => b.TextContent.Contains("Disable")).Click();

        Assert.Contains($"Disable:{_acme.Id}", _platform.Calls);
        Assert.Contains("Unpaid invoice", page.Markup);
        Click(page, "Enable workspace");
        Assert.Contains($"Enable:{_acme.Id}", _platform.Calls);
    }

    [Fact]
    public void Delete_is_only_possible_for_disabled_workspaces_after_typing_the_name()
    {
        var page = Render<WorkspaceDetail>(p => p.Add(x => x.Id, _acme.Id));
        Assert.True(page.FindAll("button").Single(b => b.TextContent.Contains("Delete workspace")).HasAttribute("disabled"));

        _platform.Workspaces[0] = _acme with { Status = WorkspaceStatus.Disabled };
        page = Render<WorkspaceDetail>(p => p.Add(x => x.Id, _acme.Id));
        Click(page, "Delete workspace");
        var deleteButton = page.FindAll(".ss-modal button").Single(b => b.TextContent.Contains("Delete forever"));
        Assert.True(deleteButton.HasAttribute("disabled"));

        page.Find("#wd-confirm").Input("Acme Research");
        page.FindAll(".ss-modal button").Single(b => b.TextContent.Contains("Delete forever")).Click();

        Assert.Contains($"Delete:{_acme.Id}:Acme Research", _platform.Calls);
        Assert.EndsWith("/system/workspaces", Navigation.Uri);
    }

    [Fact]
    public void Details_are_saved()
    {
        var page = Render<WorkspaceDetail>(p => p.Add(x => x.Id, _acme.Id));

        page.Find("#wd-slug").Input("acme-research");
        Click(page, "Save changes");

        Assert.Contains($"Update:{_acme.Id}", _platform.Calls);
        Assert.Equal("acme-research", _platform.Workspaces[0].Slug);
    }

    [Fact]
    public void Unknown_workspaces_are_not_found() =>
        Assert.Contains("Workspace not found", Render<WorkspaceDetail>(p => p.Add(x => x.Id, Guid.NewGuid())).Markup);

    [Fact]
    public void Accounts_show_the_workspace_and_filter_by_it()
    {
        _users.Users.Add(new UserDto { Id = Guid.NewGuid(), Email = "lead@acme.test", Roles = [AppRoles.Admin, AppRoles.User], WorkspaceId = _acme.Id, WorkspaceName = "Acme Research" });
        _users.Users.Add(new UserDto { Id = Guid.NewGuid(), Email = "ops@test.local", Roles = [AppRoles.SuperAdmin] });

        NavigateTo($"system/accounts?workspace={_acme.Id}");
        var page = Render<Accounts>();

        Assert.Equal(_acme.Id, _users.LastQuery!.WorkspaceId);
        Assert.NotNull(page.Find($"a[href='system/workspaces/{_acme.Id}']"));
        Assert.Contains("Super admin", page.Markup);
        Assert.Contains("System", page.Markup);
    }

    [Fact]
    public void New_accounts_can_be_super_admins_or_members_of_a_chosen_workspace()
    {
        var page = Render<Accounts>();

        Click(page, "New account");
        page.Find("#nu-email").Input("member@acme.test");
        page.Find("#nu-password").Input("Member123!");
        Click(page, "Create account", ".ss-modal button");
        Assert.Contains("Please choose the workspace", page.Markup); // a member needs a workspace
        Assert.Null(_users.Created);

        page.Find("#nu-workspace").Change(_acme.Id.ToString());
        Click(page, "Create account", ".ss-modal button");
        Assert.Equal(_acme.Id, _users.Created!.WorkspaceId);
        Assert.Equal([AppRoles.User], _users.Created.Roles);

        Click(page, "New account");
        page.Find("#nu-kind").Change("superadmin");
        Assert.Empty(page.FindAll("#nu-workspace")); // super admins belong to no workspace
        page.Find("#nu-email").Input("ops@test.local");
        page.Find("#nu-password").Input("Ops12345!");
        Click(page, "Create account", ".ss-modal button");
        Assert.Null(_users.Created!.WorkspaceId);
        Assert.Equal([AppRoles.SuperAdmin], _users.Created.Roles);
    }

    [Fact]
    public void System_settings_are_saved()
    {
        var page = Render<SystemSettings>();

        page.Find("#st-approval").Change(true);
        page.Find("#st-support").Input("help@test.local");
        Click(page, "Save changes");

        Assert.True(PlatformSettings.Settings.RequireWorkspaceApproval);
        Assert.Equal("help@test.local", PlatformSettings.Settings.SupportEmail);
    }

    [Fact]
    public void The_system_audit_log_reads_system_events()
    {
        _audit.Entries.Add(new AuditLogDto { Id = Guid.NewGuid(), Action = AuditActions.WorkspaceDisabled, EntityType = "Workspace", EntityId = _acme.Id.ToString(), Timestamp = DateTime.UtcNow });

        var page = Render<AuditLogView>(p => p.Add(x => x.SystemMode, true));

        Assert.Contains("System audit log", page.Markup);
        Assert.NotNull(page.Find($"a[href='system/workspaces/{_acme.Id}']"));
    }
}
