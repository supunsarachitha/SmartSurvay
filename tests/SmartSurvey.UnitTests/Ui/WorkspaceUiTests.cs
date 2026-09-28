using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Users;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Components.Layout;
using SmartSurvey.Web.Components.Pages;
using SmartSurvey.Web.Components.Pages.Admin;
using SmartSurvey.Web.Components.Pages.Admin.Users;
using SmartSurvey.Web.Components.Shared;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>Workspace UI: the public workspace page, the workspace settings page, menus and the join link.</summary>
public sealed class WorkspaceUiTests : UiTestBase
{
    private static readonly PublicWorkspaceDto Acme = new()
    {
        Id = TestWorkspaces.OtherId, Name = "Acme Research", Slug = "acme", Description = "We ask good questions.",
        ContactEmail = "team@acme.test", AllowSelfRegistration = true, ShowPublicSurveyList = true,
    };

    public WorkspaceUiTests()
    {
        Services.AddScoped<ToastService>();
        Services.AddSingleton<IUserAdminService>(new FakeUserAdminService());
        Workspaces.Public["acme"] = Acme;
    }

    private static AvailableSurveyDto Survey(string title, string slug) => new()
    {
        SurveyId = Guid.NewGuid(), Title = title, Slug = slug, QuestionCount = 3, EstimatedMinutes = 1, AllowAnonymous = true, CanRespond = true,
    };

    [Fact]
    public void Workspace_page_introduces_the_workspace_lists_its_surveys_and_invites_guests_to_join()
    {
        Responses.Available.Add(Survey("Offsite feedback", "offsite"));

        var page = Render<WorkspacePage>(p => p.Add(x => x.Slug, "acme"));

        Assert.Contains("Acme Research", page.Find("h1").TextContent);
        Assert.Contains("We ask good questions.", page.Markup);
        Assert.NotNull(page.Find("a[href='mailto:team@acme.test']"));
        Assert.NotNull(page.Find("a[href='s/offsite']"));
        Assert.NotNull(page.Find("a[href='Account/Register?workspace=acme&ReturnUrl=%2Fw%2Facme']"));
    }

    [Fact]
    public void Members_see_a_badge_instead_of_the_join_button()
    {
        Workspaces.Public["acme"] = Acme with { Id = TestWorkspaces.DefaultId };
        SignInAsRespondent();

        var page = Render<WorkspacePage>(p => p.Add(x => x.Slug, "acme"));

        Assert.Contains("You are a member", page.Markup);
        Assert.Empty(page.FindAll("a[href^='Account/Register']"));
    }

    [Fact]
    public void A_workspace_without_a_public_list_points_to_survey_links()
    {
        Workspaces.Public["acme"] = Acme with { ShowPublicSurveyList = false, AllowSelfRegistration = false };
        Responses.Available.Add(Survey("Hidden", "hidden"));

        var page = Render<WorkspacePage>(p => p.Add(x => x.Slug, "acme"));

        Assert.Contains("Surveys are shared by link", page.Markup);
        Assert.Empty(page.FindAll("a[href='s/hidden']"));
        Assert.Empty(page.FindAll("a[href^='Account/Register']"));
    }

    [Fact]
    public void Unknown_workspaces_are_not_found() =>
        Assert.Contains("Workspace not found", Render<WorkspacePage>(p => p.Add(x => x.Slug, "nope")).Markup);

    [Fact]
    public void Admins_save_workspace_settings_and_see_the_join_link()
    {
        SignInAsAdmin();
        var page = Render<WorkspaceSettings>();

        Assert.Contains("Account/Register?workspace=test", page.Find("input[aria-label='Join link']").GetAttribute("value"));
        page.Find("#ws-description").Input("Our research team");
        page.FindAll("button").First(b => b.TextContent.Contains("Save changes")).Click();

        var update = Assert.Single(Workspaces.Updates);
        Assert.Equal("Our research team", update.Description);
        Assert.Contains("Saved", page.Markup);
    }

    [Fact]
    public void Settings_show_validation_errors_next_to_the_field()
    {
        SignInAsAdmin();
        var page = Render<WorkspaceSettings>();

        page.Find("#ws-name").Input(" ");
        page.FindAll("button").First(b => b.TextContent.Contains("Save changes")).Click();

        Assert.Contains("is-invalid", page.Find("#ws-name").ClassName);
        Assert.Contains("Please enter a name for the workspace.", page.Markup);
    }

    [Fact]
    public void Turning_off_joining_hides_the_join_link()
    {
        SignInAsAdmin();
        var page = Render<WorkspaceSettings>();

        page.Find("#ws-join").Change(false);

        Assert.Empty(page.FindAll("input[aria-label='Join link']"));
        Assert.Contains("Only you and other admins can add members", page.Markup);
    }

    [Fact]
    public void Users_page_shows_the_join_link_only_when_joining_is_on()
    {
        SignInAsAdmin();
        Assert.NotEmpty(Render<UserList>().FindAll("input[aria-label='Join link']"));

        Workspaces.Current = Workspaces.Current with { AllowSelfRegistration = false };
        Assert.Empty(Render<UserList>().FindAll("input[aria-label='Join link']"));
    }

    [Fact]
    public void The_user_menu_names_the_members_workspace()
    {
        SignInAsAdmin();

        var menu = Render<UserMenu>();

        Assert.Contains("Test workspace", menu.Markup);
        Assert.NotNull(menu.Find("a[href='admin']"));
        Assert.Empty(menu.FindAll("a[href='system']"));
    }

    [Fact]
    public void The_user_menu_of_a_super_admin_leads_to_the_system_console_only()
    {
        SignInAsSuperAdmin();

        var menu = Render<UserMenu>();

        Assert.Contains("Super admin", menu.Markup);
        Assert.NotNull(menu.Find("a[href='system']"));
        Assert.Empty(menu.FindAll("a[href='admin']"));
        Assert.Empty(menu.FindAll("a[href='my/responses']"));
    }

    [Fact]
    public void Guests_are_offered_to_create_a_workspace() =>
        Assert.NotNull(Render<UserMenu>().Find("a[href='signup']"));

    [Fact]
    public void The_join_link_copies_the_absolute_register_address()
    {
        var box = Render<JoinLinkBox>(p => p.Add(x => x.Slug, "acme"));

        Assert.Equal("http://localhost/Account/Register?workspace=acme", box.Find("input").GetAttribute("value"));
    }
}
