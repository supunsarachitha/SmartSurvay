using Bunit;
using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Web.Components.Pages;
using SurveysPage = SmartSurvey.Web.Components.Pages.Surveys;

namespace SmartSurvey.UnitTests.Ui;

public sealed class HomePageTests : UiTestBase
{
    [Fact]
    public void Uses_the_branded_product_name_and_invites_guests_to_create_a_workspace()
    {
        var page = Render<Home>();

        Assert.Contains($"{ProductName} helps you build surveys", page.Markup);
        Assert.NotEmpty(page.FindAll("a[href='signup']"));
        Assert.Empty(page.FindAll("a[href='admin/surveys']"));
    }

    [Fact]
    public void Without_self_service_sign_up_guests_are_only_invited_to_sign_in()
    {
        PlatformSettings.Settings = new() { AllowWorkspaceSignup = false };

        var page = Render<Home>();

        Assert.Empty(page.FindAll("a[href='signup']"));
        Assert.NotNull(page.Find("a[href='Account/Login']"));
    }

    [Fact]
    public void Super_admins_get_a_shortcut_to_the_system_console()
    {
        SignInAsSuperAdmin();

        var page = Render<Home>();

        Assert.NotNull(page.Find("a[href='system']"));
        Assert.Empty(page.FindAll("a[href='admin/surveys']"));
    }

    [Fact]
    public void Administrators_get_shortcuts_to_create_surveys()
    {
        SignInAsAdmin();

        var page = Render<Home>();

        Assert.NotNull(page.Find("a[href='admin/surveys']"));
        Assert.Empty(page.FindAll("a[href='signup']"));
    }
}

public sealed class FaqPageTests : UiTestBase
{
    [Fact]
    public void Lists_every_group_with_the_product_name()
    {
        var page = Render<Faq>();

        Assert.Equal(6, page.FindAll("div.accordion").Count);
        Assert.Contains($"What is {ProductName}?", page.Markup);
        Assert.Empty(page.FindAll(".accordion-collapse.show")); // collapsed until searched
    }

    [Fact]
    public void Search_filters_questions_and_expands_the_matches()
    {
        NavigateTo("faq?q=EXPORT");

        var page = Render<Faq>();

        var questions = page.FindAll(".accordion-button").Select(b => b.TextContent.Trim()).ToList();
        Assert.Contains("Which export formats are supported?", questions);
        Assert.DoesNotContain("Is there a dark mode?", questions);
        Assert.All(page.FindAll(".accordion-collapse"), c => Assert.Contains("show", c.ClassList));
        Assert.Contains($"{questions.Count} results for", page.Markup);
    }

    [Fact]
    public void Search_without_matches_shows_an_empty_state()
    {
        NavigateTo("faq?q=zzzz-nothing");

        var page = Render<Faq>();

        Assert.Contains("No matching questions", page.Markup);
        Assert.Empty(page.FindAll(".accordion"));
    }

    [Fact]
    public void Contact_address_is_shown_when_configured()
    {
        Support.ContactEmail = "help@acme.test";

        var page = Render<Faq>();

        Assert.NotNull(page.Find("a[href='mailto:help@acme.test']"));
    }
}

public sealed class BuyMeACoffeePageTests : UiTestBase
{
    [Fact]
    public void Links_to_the_configured_account_in_a_new_tab()
    {
        var page = Render<BuyMeACoffee>();

        var link = page.Find("a.btn-coffee");
        Assert.Equal("https://buymeacoffee.com/acme", link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("noopener", link.GetAttribute("rel"));
    }

    [Fact]
    public void Without_an_account_no_donation_link_is_shown()
    {
        Support.BuyMeACoffeeUsername = "";

        var page = Render<BuyMeACoffee>();

        Assert.Empty(page.FindAll("a.btn-coffee"));
        Assert.Contains("not set up", page.Markup);
    }

    [Fact]
    public void GitHub_link_appears_only_when_configured()
    {
        Assert.Empty(Render<BuyMeACoffee>().FindAll("a[href^='https://github.com']"));

        Support.GitHubUrl = "https://github.com/acme/surveys";
        Assert.NotNull(Render<BuyMeACoffee>().Find("a[href='https://github.com/acme/surveys']"));
    }
}

public sealed class SurveysPageTests : UiTestBase
{
    [Fact]
    public void Guests_are_pointed_to_survey_links_and_workspace_pages()
    {
        var page = Render<SurveysPage>();

        Assert.Contains("Got a survey link?", page.Markup);
        Assert.NotNull(page.Find("form[action='surveys'] input[name='workspace']"));
        Assert.NotNull(page.Find("a[href='signup']"));
        Assert.DoesNotContain("No open surveys right now", page.Markup);
    }

    [Fact]
    public void Members_see_their_workspace_name_and_its_surveys()
    {
        SignInAsRespondent();
        Responses.Available.Add(Survey("Team pulse", "pulse", canRespond: true));

        var page = Render<SurveysPage>();

        Assert.Contains("Surveys of Test workspace", page.Markup);
        Assert.NotNull(page.Find("a[href='s/pulse']"));
    }

    [Fact]
    public void Super_admins_are_sent_to_the_system_console()
    {
        SignInAsSuperAdmin();

        var page = Render<SurveysPage>();

        Assert.Contains("Super admins do not answer surveys", page.Markup);
        Assert.NotNull(page.Find("a[href='system']"));
    }

    [Fact]
    public void Cards_offer_start_continue_or_show_completion()
    {
        SignInAsRespondent();
        Responses.Available.AddRange(
        [
            Survey("New survey", "new", canRespond: true),
            Survey("Half done", "half", canRespond: true, hasDraft: true),
            Survey("All done", "done", canRespond: false, hasCompleted: true),
        ]);

        var page = Render<SurveysPage>();

        Assert.Contains("Start survey", page.Find("a[href='s/new']").TextContent);
        Assert.Contains("Continue", page.Find("a[href='s/half']").TextContent);
        Assert.Empty(page.FindAll("a[href='s/done']"));
        Assert.Contains("Thanks for taking part", page.Markup);
    }

    [Fact]
    public void Search_filters_by_title_and_description()
    {
        SignInAsRespondent();
        Responses.Available.AddRange([Survey("Customer feedback", "cf", canRespond: true), Survey("Team pulse", "tp", canRespond: true)]);
        NavigateTo("surveys?q=pulse");

        var page = Render<SurveysPage>();

        Assert.Single(page.FindAll("h2.h5"));
        Assert.Contains("Team pulse", page.Markup);
    }

    private static AvailableSurveyDto Survey(string title, string slug, bool canRespond, bool hasDraft = false, bool hasCompleted = false) => new()
    {
        SurveyId = Guid.NewGuid(),
        Title = title,
        Slug = slug,
        QuestionCount = 5,
        EstimatedMinutes = 2,
        AllowAnonymous = true,
        CanRespond = canRespond,
        HasDraft = hasDraft,
        HasCompleted = hasCompleted,
    };
}

public sealed class MyResponsesPageTests : UiTestBase
{
    [Fact]
    public void Empty_state_links_to_the_surveys()
    {
        SignInAsRespondent();

        var page = Render<MyResponses>();

        Assert.Contains("No responses yet", page.Markup);
    }

    [Fact]
    public void Drafts_can_be_continued_and_are_announced()
    {
        SignInAsRespondent();
        Responses.Mine.AddRange(
        [
            new MyResponseDto { ResponseId = Guid.NewGuid(), SurveyTitle = "Done survey", Slug = "done", Status = ResponseStatus.Completed, StartedAt = DateTime.UtcNow, SubmittedAt = DateTime.UtcNow },
            new MyResponseDto { ResponseId = Guid.NewGuid(), SurveyTitle = "Draft survey", Slug = "draft", Status = ResponseStatus.InProgress, StartedAt = DateTime.UtcNow, CanContinue = true },
        ]);

        var page = Render<MyResponses>();

        Assert.Equal(2, page.FindAll("tbody tr").Count);
        Assert.Single(page.FindAll("a[href='s/draft']"));
        Assert.Empty(page.FindAll("a[href='s/done']"));
        Assert.Contains("You have 1 unfinished survey", page.Markup);
    }
}
