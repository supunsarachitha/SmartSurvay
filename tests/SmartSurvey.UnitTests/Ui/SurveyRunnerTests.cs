using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Components.Pages;
using SmartSurvey.Web.Components.Runner;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>bUnit tests of the respondent experience: <see cref="SurveyRunner"/>, <see cref="QuestionField"/> and the thank-you page.</summary>
public sealed class SurveyRunnerTests : UiTestBase
{
    private const string Slug = "customer-feedback";
    private readonly SampleSurvey _s = SampleSurveys.CustomerFeedback();
    private readonly BunitPersistentComponentState _state;

    public SurveyRunnerTests()
    {
        _state = this.AddBunitPersistentComponentState();
        _s.Definition.Id = Guid.NewGuid();
        _s.Definition.Slug = Slug;
        Responses.Session = new SurveySessionDto { Eligibility = SurveyEligibility.Eligible, Survey = _s.Definition };
    }

    private string CurrentUri => Services.GetRequiredService<BunitNavigationManager>().Uri;

    private IRenderedComponent<SurveyRunner> RenderRunner(bool embedded = false) =>
        Render<SurveyRunner>(p => p.Add(x => x.Slug, Slug).Add(x => x.Embedded, embedded));

    private static void Choose(IRenderedComponent<SurveyRunner> cut, OptionDto option) => cut.Find($"input[value='{option.Id}']").Change(true);

    private static void Click(IRenderedComponent<SurveyRunner> cut, string text) =>
        cut.FindAll("button").First(b => b.TextContent.Contains(text, StringComparison.Ordinal)).Click();

    [Fact]
    public void First_page_shows_the_survey_intro_progress_and_its_questions()
    {
        _s.Definition.WelcomeMessage = "Welcome aboard!";

        var cut = RenderRunner();

        Assert.Equal("Customer feedback", cut.Find("h1").TextContent.Trim());
        Assert.Contains("Welcome aboard!", cut.Markup);
        Assert.Contains("Page 1 of 2", cut.Markup);
        Assert.Equal(2, cut.FindAll(".question-card").Count); // "Why not?" is hidden by logic
        Assert.Contains("1.", cut.Find(".question-number").TextContent);
        Assert.Contains("Next", cut.Find(".survey-nav .btn-primary").TextContent);
        Assert.Equal([Slug], Responses.StartedSlugs);
    }

    [Fact]
    public void Next_without_a_required_answer_shows_the_error_and_stays_on_the_page()
    {
        var cut = RenderRunner();

        Click(cut, "Next");

        Assert.Contains("This question is required.", cut.Find($"#q-{_s.Enjoy.Id}").TextContent);
        Assert.Single(cut.FindAll(".question-card.has-error"));
        Assert.Contains("Please check the highlighted question.", cut.Markup);
        Assert.Contains("Page 1 of 2", cut.Markup);
    }

    [Fact]
    public void Answering_shows_follow_up_questions_immediately()
    {
        var cut = RenderRunner();

        Choose(cut, _s.No);

        Assert.Contains("Why not?", cut.Markup);
        Assert.Contains("choice-item selected", cut.Markup);
    }

    [Fact]
    public void Guest_answers_and_submits_then_goes_to_the_thank_you_page()
    {
        var cut = RenderRunner();

        Choose(cut, _s.Yes);
        Click(cut, "Next");
        cut.FindAll(".rating-stars button")[3].Click();
        Click(cut, "Submit");

        var request = Assert.Single(Responses.Submissions);
        Assert.Equal([_s.Enjoy.Id, _s.Rating.Id], request.Answers.Select(a => a.QuestionId));
        Assert.Equal(4, request.Answers[1].Number);
        Assert.Null(request.ResponseId);
        Assert.Empty(Responses.Drafts); // guests have no drafts
        Assert.EndsWith($"/s/{Slug}/thank-you", CurrentUri);
    }

    [Fact]
    public void Embedded_runner_goes_to_the_embedded_thank_you_page()
    {
        _s.Definition.Sections.Remove(_s.Page2);
        var cut = RenderRunner(embedded: true);

        Choose(cut, _s.Yes);
        Click(cut, "Submit");

        Assert.EndsWith($"/embed/s/{Slug}/thank-you", CurrentUri);
        Assert.DoesNotContain("Save &amp; finish later", cut.Markup);
    }

    [Fact]
    public void Signed_in_respondent_progress_is_saved_on_every_page_change()
    {
        SignInAsRespondent();
        var cut = RenderRunner();

        Choose(cut, _s.Yes);
        Click(cut, "Next");

        var draft = Assert.Single(Responses.Drafts);
        Assert.Equal(1, draft.CurrentSectionIndex);
        Assert.Null(draft.ResponseId);
        Assert.Contains("Progress saved", cut.Markup);

        Click(cut, "Back");

        Assert.Equal(2, Responses.Drafts.Count);
        Assert.Equal(Responses.DraftId, Responses.Drafts[1].ResponseId); // later saves update the same draft
    }

    [Fact]
    public void Save_and_finish_later_confirms_where_to_continue()
    {
        SignInAsRespondent();
        var cut = RenderRunner();

        Click(cut, "Save & finish later");

        Assert.Single(Responses.Drafts);
        Assert.Contains("Your progress is saved", cut.Markup);
        Assert.NotNull(cut.Find("a[href='my/responses']"));
    }

    [Fact]
    public void Resumed_draft_restores_answers_page_and_offers_to_start_over()
    {
        SignInAsRespondent();
        var draftId = Guid.NewGuid();
        Responses.Session.DraftResponseId = draftId;
        Responses.Session.CurrentSectionIndex = 1;
        Responses.Session.Answers = [new AnswerInputDto { QuestionId = _s.Rating.Id, Number = 3 }];

        var cut = RenderRunner();

        Assert.Contains("Welcome back!", cut.Markup);
        Assert.Contains("Page 2 of 2", cut.Markup);
        Assert.Equal(3, cut.FindAll(".rating-stars button.on").Count);

        cut.FindAll(".rating-stars button")[4].Click();
        Click(cut, "Submit"); // page 1 is incomplete → back to it
        Assert.Contains("Page 1 of 2", cut.Markup);
        Assert.Empty(Responses.Submissions);
    }

    [Fact]
    public void Server_validation_errors_are_shown_on_their_questions()
    {
        Responses.SubmitError = new AppValidationException(new Dictionary<string, string[]>
        {
            [_s.Rating.Id.ToString()] = ["Please rate from 1 to 5."],
        });
        var cut = RenderRunner();

        Choose(cut, _s.Yes);
        Click(cut, "Next");
        cut.FindAll(".rating-stars button")[0].Click();
        Click(cut, "Submit");

        Assert.Contains("Please rate from 1 to 5.", cut.Find($"#q-{_s.Rating.Id}").TextContent);
        Assert.DoesNotContain("thank-you", CurrentUri);
    }

    [Fact]
    public void Survey_that_stopped_accepting_responses_shows_an_alert()
    {
        Responses.SubmitError = new BusinessRuleException("This survey is closed and no longer accepts responses.");
        _s.Definition.Sections.Remove(_s.Page2);
        var cut = RenderRunner();

        Choose(cut, _s.Yes);
        Click(cut, "Submit");

        Assert.Contains("This survey is closed", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Rapid_repeated_submissions_on_one_circuit_are_throttled()
    {
        _s.Definition.Sections.Remove(_s.Page2);
        var throttle = Services.GetRequiredService<SmartSurvey.Web.Infrastructure.SubmissionThrottle>();
        for (var i = 0; i < SmartSurvey.Web.Infrastructure.SubmissionThrottle.Limit; i++)
        {
            Assert.True(throttle.TryAcquire());
        }

        var cut = RenderRunner();
        Choose(cut, _s.Yes);
        Click(cut, "Submit");

        Assert.Empty(Responses.Submissions);
        Assert.Contains("sending responses very quickly", cut.Find(".alert-danger").TextContent);
    }

    [Fact]
    public void Password_prompt_opens_the_survey_with_the_right_password()
    {
        Responses.Password = "secret";
        var cut = RenderRunner();

        Assert.Equal("Customer feedback", cut.Find("h1").TextContent.Trim());
        Assert.Empty(cut.FindAll(".question-card"));

        cut.Find("#survey-password").Input("guess");
        cut.Find("form").Submit();
        Assert.Contains("That password is not correct", cut.Find("#survey-password-error").TextContent);

        cut.Find("#survey-password").Input("secret");
        cut.Find("form").Submit();

        Assert.Equal(2, cut.FindAll(".question-card").Count);
        Assert.Equal("valid-key", Responses.AccessKeys[^1]);
    }

    [Fact]
    public void Protected_surveys_send_the_key_and_thank_in_place()
    {
        Responses.Password = "secret";
        _s.Definition.PasswordProtected = true;
        _s.Definition.Sections.Remove(_s.Page2);
        var cut = RenderRunner();
        cut.Find("#survey-password").Input("secret");
        cut.Find("form").Submit();

        Choose(cut, _s.Yes);
        Click(cut, "Submit");

        Assert.Equal("valid-key", Assert.Single(Responses.Submissions).AccessKey);
        Assert.Contains("Thank you!", cut.Markup);
        Assert.Contains("Thanks!", cut.Markup);
        Assert.DoesNotContain("thank-you", CurrentUri); // the public thank-you page doesn't show protected surveys' messages
    }

    [Fact]
    public void Too_many_wrong_passwords_are_throttled()
    {
        Responses.Password = "secret";
        var throttle = Services.GetRequiredService<SmartSurvey.Web.Infrastructure.PasswordAttemptThrottle>();
        while (throttle.TryAcquire())
        {
        }

        var cut = RenderRunner();
        cut.Find("#survey-password").Input("secret");
        cut.Find("form").Submit();

        Assert.Contains("Too many attempts", cut.Markup);
        Assert.Empty(cut.FindAll(".question-card"));
    }

    [Theory]
    [InlineData(SurveyEligibility.Closed, "This survey is closed")]
    [InlineData(SurveyEligibility.NotOpenYet, "This survey isn't open yet")]
    [InlineData(SurveyEligibility.QuotaReached, "This survey is full")]
    [InlineData(SurveyEligibility.NotFound, "Survey not found")]
    public void Unavailable_surveys_explain_why(SurveyEligibility eligibility, string title)
    {
        Responses.Session = new SurveySessionDto { Eligibility = eligibility, Message = "Explanation from the server." };

        var cut = RenderRunner();

        Assert.Equal(title, cut.Find("h1").TextContent.Trim());
        Assert.Contains("Explanation from the server.", cut.Markup);
        Assert.Empty(cut.FindAll(".question-card"));
        Assert.NotNull(cut.Find("a[href='surveys']"));
    }

    [Fact]
    public void Members_only_survey_asks_guests_to_sign_in_and_come_back()
    {
        Responses.Session = new SurveySessionDto { Eligibility = SurveyEligibility.LoginRequired, Message = "Please log in to answer this survey." };

        var cut = RenderRunner();

        Assert.NotNull(cut.Find($"a[href='Account/Login?ReturnUrl=%2Fs%2F{Slug}']"));
        Assert.NotNull(cut.Find($"a[href='Account/Register?ReturnUrl=%2Fs%2F{Slug}']"));
    }

    [Fact]
    public void Create_account_joins_the_workspace_that_runs_the_survey()
    {
        Responses.Session = new SurveySessionDto
        {
            Eligibility = SurveyEligibility.LoginRequired, Message = "Please log in.", WorkspaceName = "Acme Research", WorkspaceSlug = "acme",
        };

        var cut = RenderRunner();

        Assert.NotNull(cut.Find($"a[href='Account/Register?workspace=acme&ReturnUrl=%2Fs%2F{Slug}']"));
        Assert.Contains("More from Acme Research", cut.Find("a[href='w/acme']").TextContent);
    }

    [Fact]
    public void Members_of_another_workspace_are_told_why_instead_of_being_asked_to_sign_in()
    {
        Responses.Session = new SurveySessionDto
        {
            Eligibility = SurveyEligibility.OtherWorkspace, Message = "This survey is only for members.", WorkspaceName = "Acme Research", WorkspaceSlug = "acme",
        };

        var cut = RenderRunner();

        Assert.Contains("This survey is for another workspace", cut.Markup);
        Assert.Empty(cut.FindAll("a[href^='Account/Login']"));
        Assert.Empty(cut.FindAll("a[href^='Account/Register']"));
    }

    [Fact]
    public void Embedded_members_only_survey_opens_the_full_page_in_a_new_tab()
    {
        Responses.Session = new SurveySessionDto { Eligibility = SurveyEligibility.LoginRequired, Message = "Please log in." };

        var cut = RenderRunner(embedded: true);

        var link = cut.Find($"a[href='s/{Slug}']");
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Empty(cut.FindAll("a[href='surveys']"));
    }

    [Fact]
    public void Preview_never_touches_the_service_and_thanks_in_place()
    {
        _s.Definition.Sections.Remove(_s.Page2);
        var cut = Render<SurveyRunner>(p => p.Add(x => x.PreviewSurvey, _s.Definition));

        Assert.Contains("nothing is saved", cut.Markup);
        Choose(cut, _s.Yes);
        Click(cut, "Submit");

        Assert.Contains("Thank you!", cut.Markup);
        Assert.Contains("Thanks!", cut.Markup);
        Assert.Empty(Responses.StartedSlugs);
        Assert.Empty(Responses.Submissions);

        Click(cut, "Restart preview");
        Assert.Equal(2, cut.FindAll(".question-card").Count);
    }

    [Fact]
    public void Prerendered_session_is_handed_to_the_interactive_render()
    {
        RenderRunner();
        _state.TriggerOnPersisting();

        RenderRunner();

        Assert.Single(Responses.StartedSlugs);
    }

    [Fact]
    public void Checkbox_limits_selections_and_shows_the_other_text_box()
    {
        _s.Features.Settings.MaxSelections = 2;
        var cut = RenderRunner();

        Choose(cut, _s.Reports);
        Choose(cut, _s.OtherFeature);

        Assert.True(cut.Find($"input[value='{_s.Logic.Id}']").HasAttribute("disabled"));
        Assert.Contains("Select up to 2.", cut.Markup);
        cut.Find("input[placeholder='Please specify']").Input("Exports");
        Choose(cut, _s.Yes);
        Click(cut, "Next");
        Click(cut, "Back");
        Assert.Equal("Exports", cut.Find("input[placeholder='Please specify']").GetAttribute("value"));
    }

    [Fact]
    public void Optional_single_choice_answers_can_be_cleared()
    {
        var question = new QuestionDto { Type = QuestionType.Scale, Text = "How likely?", Settings = { ScaleMin = 0, ScaleMax = 10, ScaleMinLabel = "Never", ScaleMaxLabel = "Surely" } };
        var answer = new AnswerInputDto { QuestionId = question.Id, Number = 7 };
        var changes = 0;

        var cut = Render<QuestionField>(p => p
            .Add(x => x.Question, question).Add(x => x.Answer, answer).Add(x => x.OnChanged, () => changes++));

        Assert.Equal(11, cut.FindAll(".scale-options button").Count);
        Assert.Contains("Never", cut.Find(".scale-labels").TextContent);
        cut.FindAll("button").Single(b => b.TextContent.Contains("Clear answer")).Click();

        Assert.Null(answer.Number);
        Assert.Equal(1, changes);
        Assert.Empty(cut.FindAll("button.btn-link"));
    }

    [Fact]
    public void Question_errors_mark_the_input_invalid_and_are_announced()
    {
        var cut = Render<QuestionField>(p => p
            .Add(x => x.Question, _s.Email).Add(x => x.Answer, new AnswerInputDto { QuestionId = _s.Email.Id, Text = "nope" })
            .Add(x => x.Number, 5).Add(x => x.Errors, new[] { "Please enter a valid e-mail address." }));

        var input = cut.Find("input[type='email']");
        Assert.Contains("is-invalid", input.ClassName);
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Equal($"q-{_s.Email.Id}-error", input.GetAttribute("aria-describedby"));
        Assert.Contains("Please enter a valid e-mail address.", cut.Find("[role='alert']").TextContent);
        Assert.Contains("5.", cut.Markup);
    }

    [Fact]
    public void Thank_you_page_offers_another_response_only_when_allowed()
    {
        Responses.Completion = new SurveyCompletionDto(Guid.NewGuid(), "Customer feedback", Slug, "See you soon.", CanRespondAgain: true);

        var cut = Render<ThankYou>(p => p.Add(x => x.Slug, Slug));

        Assert.Contains("Customer feedback", cut.Markup);
        Assert.Contains("See you soon.", cut.Markup);
        Assert.NotNull(cut.Find($"a[href='s/{Slug}']"));
        Assert.Empty(cut.FindAll("a[href='my/responses']")); // guest
    }

    [Fact]
    public void Thank_you_page_without_a_known_survey_is_generic()
    {
        SignInAsRespondent();

        var cut = Render<ThankYou>(p => p.Add(x => x.Slug, "unknown"));

        Assert.Contains("Your response has been recorded.", cut.Markup);
        Assert.NotNull(cut.Find("a[href='my/responses']"));
        Assert.Empty(cut.FindAll($"a[href^='s/']"));
    }
}
