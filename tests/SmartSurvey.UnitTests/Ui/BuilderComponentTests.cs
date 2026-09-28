using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Components.Admin.Builder;

namespace SmartSurvey.UnitTests.Ui;

public sealed class BuilderComponentTests : UiTestBase
{
    private readonly SampleSurvey _s = SampleSurveys.CustomerFeedback();

    [Fact]
    public void Logic_editor_explains_that_the_first_question_cannot_have_rules()
    {
        var editor = Render<LogicRulesEditor>(p => p.Add(x => x.Survey, _s.Definition).Add(x => x.TargetQuestion, _s.Enjoy));

        Assert.Contains("Rules can only use questions that come before", editor.Markup);
        Assert.Empty(editor.FindAll("button"));
    }

    [Fact]
    public void Adding_a_rule_uses_the_nearest_earlier_question()
    {
        var changes = 0;
        var editor = Render<LogicRulesEditor>(p => p
            .Add(x => x.Survey, _s.Definition)
            .Add(x => x.TargetQuestion, _s.Rating)
            .Add(x => x.OnChanged, () => changes++));

        editor.Find("button.btn-soft-primary").Click();

        var rule = Assert.Single(_s.Definition.LogicRules, r => r.TargetQuestionId == _s.Rating.Id);
        Assert.Equal(LogicAction.Show, rule.Action);
        Assert.Equal(_s.WhyNot.Id, Assert.Single(rule.Conditions).SourceQuestionId);
        Assert.Equal(1, changes);
        Assert.Contains("stays hidden until the rule matches", editor.Markup);
    }

    [Fact]
    public void Existing_rule_shows_the_option_to_compare_with()
    {
        var editor = Render<LogicRulesEditor>(p => p.Add(x => x.Survey, _s.Definition).Add(x => x.TargetQuestion, _s.WhyNot));

        var selects = editor.FindAll(".logic-rule select");
        Assert.Equal(5, selects.Count); // action, match, question, operator, option
        Assert.Equal(_s.No.Id.ToString(), selects[4].GetAttribute("value"));
    }

    [Fact]
    public void Read_only_logic_editor_has_no_edit_buttons()
    {
        var editor = Render<LogicRulesEditor>(p => p
            .Add(x => x.Survey, _s.Definition).Add(x => x.TargetQuestion, _s.WhyNot).Add(x => x.ReadOnly, true));

        Assert.Empty(editor.FindAll("button"));
        Assert.All(editor.FindAll("select"), s => Assert.True(s.HasAttribute("disabled")));
    }

    [Fact]
    public void Changing_a_question_type_resets_conditions_that_use_it()
    {
        var editor = Render<QuestionEditor>(p => p
            .Add(x => x.Survey, _s.Definition).Add(x => x.Question, _s.Enjoy).Add(x => x.Number, 1).Add(x => x.Active, true));

        editor.Find("select[id^='qtype-']").Change(QuestionType.Number.ToString());

        var condition = _s.Definition.LogicRules.Single().Conditions.Single();
        Assert.Equal(ConditionOperator.Equals, condition.Operator);
        Assert.Null(condition.OptionId);
        Assert.Contains("answer options will be removed", editor.Markup);
    }

    [Fact]
    public void Collapsed_question_shows_summary_badges()
    {
        var editor = Render<QuestionEditor>(p => p
            .Add(x => x.Survey, _s.Definition).Add(x => x.Question, _s.WhyNot).Add(x => x.Number, 3)
            .Add(x => x.Errors, new[] { "Question text is required." }));

        Assert.Contains("Q3", editor.Markup);
        Assert.Contains("1 rule", editor.Markup);
        Assert.Contains("Needs attention", editor.Markup);
        Assert.Empty(editor.FindAll(".qe-body"));
    }

    [Fact]
    public void Password_protection_asks_for_a_password_and_remembers_an_existing_one()
    {
        Services.AddSingleton<SmartSurvey.Application.Surveys.ISurveyService>(new FakeSurveyService()); // slug availability checks
        var survey = _s.Definition;
        var settings = Render<SurveySettingsForm>(p => p.Add(x => x.Survey, survey));
        Assert.Empty(settings.FindAll("#s-password-value"));

        settings.Find("#s-password").Change(true);
        settings.Find("#s-password-value").Input("team-2026");

        Assert.True(survey.PasswordProtected);
        Assert.Equal("team-2026", survey.AccessPassword);

        var saved = SampleSurveys.CustomerFeedback().Definition;
        saved.PasswordProtected = true; // as loaded from the server: a password exists, but is never sent
        var existing = Render<SurveySettingsForm>(p => p.Add(x => x.Survey, saved));
        Assert.Contains("A password is set", existing.Markup);
        Assert.Equal("Leave empty to keep the current password", existing.Find("#s-password-value").GetAttribute("placeholder"));

        existing.Find("#s-password").Change(false);
        Assert.False(saved.PasswordProtected);
        Assert.Null(saved.AccessPassword);
    }
}
