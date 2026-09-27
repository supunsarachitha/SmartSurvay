using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application.Responses;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Components.Survey;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>Runner behaviour on the customer feedback sample (page 1: Q1 radio, Q2 checkbox, Q3 shown when Q1 = No; page 2: Q4–Q7).</summary>
public sealed class SurveyRunnerTests : UiTestBase
{
    private readonly SampleSurvey _s = SampleSurveys.CustomerFeedback();

    public SurveyRunnerTests() => Services.AddSingleton<TimeProvider>(TimeProvider.System);

    [Fact]
    public void Welcome_screen_summarises_the_survey()
    {
        var runner = Render(preview: false);

        Assert.Contains("Customer feedback", runner.Markup);
        Assert.Contains("7 questions", runner.Markup);
        Assert.Contains("2 pages", runner.Markup);
        Assert.NotNull(runner.Find("button.btn-lg"));
    }

    [Fact]
    public void Conditional_question_appears_after_the_triggering_answer()
    {
        var runner = Render();
        runner.Find("button.btn-lg").Click();

        Assert.DoesNotContain("Why not?", runner.Markup);
        runner.FindAll("label.choice-item input[type=radio]")[1].Change(true); // "No"

        Assert.Contains("Why not?", runner.Markup);
    }

    [Fact]
    public void Next_is_blocked_until_required_questions_are_answered()
    {
        var runner = Render();
        runner.Find("button.btn-lg").Click();

        runner.Find("button.btn-primary").Click(); // Next

        Assert.Contains("Please check 1 answer on this page.", runner.Markup);
        Assert.Single(runner.FindAll(".question-card.has-error"));
        Assert.Contains("Page 1 of 2", runner.Markup);

        runner.FindAll("label.choice-item input[type=radio]")[0].Change(true); // "Yes"
        runner.Find("button.btn-primary").Click();

        Assert.Contains("Page 2 of 2", runner.Markup);
        Assert.Contains("Rate us", runner.Markup);
    }

    [Fact]
    public void Other_option_reveals_a_text_box()
    {
        var runner = Render();
        runner.Find("button.btn-lg").Click();

        Assert.Empty(runner.FindAll("input.choice-other-input"));
        runner.FindAll("label.choice-item input[type=checkbox]")[2].Change(true); // "Other"

        Assert.Single(runner.FindAll("input.choice-other-input"));
    }

    [Fact]
    public void Preview_submission_shows_the_thank_you_screen_without_saving()
    {
        var runner = Render(preview: true);
        runner.Find("button.btn-lg").Click();
        runner.FindAll("label.choice-item input[type=radio]")[0].Change(true);
        runner.Find("button.btn-primary").Click(); // Next → page 2

        // Page 2: rating (required) then submit.
        runner.FindAll(".rating-stars button")[3].Click();
        runner.Find("button.btn-primary").Click();

        Assert.Contains("Thank you!", runner.Markup);
        Assert.Contains("the response was not stored", runner.Markup);
    }

    [Fact]
    public void Resumed_draft_continues_on_the_saved_page_with_its_answers()
    {
        var runner = RenderComponent<SurveyRunner>(p => p
            .Add(x => x.Survey, _s.Definition)
            .Add(x => x.DraftResponseId, Guid.NewGuid())
            .Add(x => x.InitialSectionIndex, 1)
            .Add(x => x.IsAuthenticated, true)
            .Add(x => x.InitialAnswers, new[] { new AnswerInputDto { QuestionId = _s.Rating.Id, Number = 3 } }));

        Assert.Contains("Page 2 of 2", runner.Markup);
        Assert.Equal(3, runner.FindAll(".rating-stars button.on").Count);
        Assert.Contains("Save and finish later", runner.Markup);
    }

    private IRenderedComponent<SurveyRunner> Render(bool preview = true) =>
        RenderComponent<SurveyRunner>(p => p.Add(x => x.Survey, _s.Definition).Add(x => x.Preview, preview));
}
