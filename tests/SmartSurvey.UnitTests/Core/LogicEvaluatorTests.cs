using SmartSurvey.Application.Logic;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Core;

public class LogicEvaluatorTests
{
    private static Dictionary<Guid, AnswerInputDto> Answers(params AnswerInputDto[] answers) =>
        LogicEvaluator.ToDictionary(answers);

    private static AnswerInputDto Choice(QuestionDto q, params OptionDto[] options) => new()
    {
        QuestionId = q.Id,
        Selections = options.Select(o => new SelectionInputDto { OptionId = o.Id }).ToList(),
    };

    [Fact]
    public void Question_with_show_rule_is_hidden_until_condition_matches()
    {
        var s = SampleSurveys.CustomerFeedback();

        var none = LogicEvaluator.Evaluate(s.Definition, Answers());
        var yes = LogicEvaluator.Evaluate(s.Definition, Answers(Choice(s.Enjoy, s.Yes)));
        var no = LogicEvaluator.Evaluate(s.Definition, Answers(Choice(s.Enjoy, s.No)));

        Assert.False(none.IsQuestionVisible(s.WhyNot.Id));
        Assert.False(yes.IsQuestionVisible(s.WhyNot.Id));
        Assert.True(no.IsQuestionVisible(s.WhyNot.Id));
        Assert.True(no.IsQuestionVisible(s.Enjoy.Id));
    }

    [Fact]
    public void Questions_without_rules_are_always_visible()
    {
        var s = SampleSurveys.CustomerFeedback();
        var visibility = LogicEvaluator.Evaluate(s.Definition, Answers());

        Assert.True(visibility.IsQuestionVisible(s.Enjoy.Id));
        Assert.True(visibility.IsQuestionVisible(s.Rating.Id));
        Assert.Equal(2, visibility.VisibleSections(s.Definition).Count);
    }

    [Fact]
    public void Hide_rule_wins_over_show_rule()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.LogicRules.Add(new LogicRuleDto
        {
            TargetQuestionId = s.WhyNot.Id,
            Action = LogicAction.Hide,
            Conditions = [new LogicConditionDto { SourceQuestionId = s.Enjoy.Id, Operator = ConditionOperator.IsAnswered }],
        });

        var visibility = LogicEvaluator.Evaluate(s.Definition, Answers(Choice(s.Enjoy, s.No)));

        Assert.False(visibility.IsQuestionVisible(s.WhyNot.Id));
    }

    [Fact]
    public void Hidden_section_hides_all_its_questions()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Definition.LogicRules.Add(new LogicRuleDto
        {
            TargetSectionId = s.Page2.Id,
            Action = LogicAction.Hide,
            Conditions = [new LogicConditionDto { SourceQuestionId = s.Enjoy.Id, Operator = ConditionOperator.Equals, OptionId = s.No.Id }],
        });

        var visibility = LogicEvaluator.Evaluate(s.Definition, Answers(Choice(s.Enjoy, s.No)));

        Assert.False(visibility.IsSectionVisible(s.Page2.Id));
        Assert.False(visibility.IsQuestionVisible(s.Rating.Id));
        Assert.Single(visibility.VisibleSections(s.Definition));
    }

    [Fact]
    public void Chained_logic_treats_answers_of_hidden_questions_as_unanswered()
    {
        var s = SampleSurveys.CustomerFeedback();
        // Q5 (email) shown only when Q3 ("why not") is answered; Q3 itself depends on Q1 = No.
        s.Definition.LogicRules.Add(new LogicRuleDto
        {
            TargetQuestionId = s.Email.Id,
            Action = LogicAction.Show,
            Conditions = [new LogicConditionDto { SourceQuestionId = s.WhyNot.Id, Operator = ConditionOperator.IsAnswered }],
        });
        var whyNot = new AnswerInputDto { QuestionId = s.WhyNot.Id, Text = "Too slow" };

        var whenNo = LogicEvaluator.Evaluate(s.Definition, Answers(Choice(s.Enjoy, s.No), whyNot));
        var whenYes = LogicEvaluator.Evaluate(s.Definition, Answers(Choice(s.Enjoy, s.Yes), whyNot));

        Assert.True(whenNo.IsQuestionVisible(s.Email.Id));
        Assert.False(whenYes.IsQuestionVisible(s.WhyNot.Id));
        Assert.False(whenYes.IsQuestionVisible(s.Email.Id)); // stale answer to hidden Q3 is ignored
    }

    [Fact]
    public void Any_match_type_requires_only_one_condition()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.WhyNotRule.MatchType = LogicMatchType.Any;
        s.WhyNotRule.Conditions.Add(new LogicConditionDto
        {
            SourceQuestionId = s.Features.Id, Operator = ConditionOperator.Contains, OptionId = s.OtherFeature.Id,
        });

        var visibility = LogicEvaluator.Evaluate(s.Definition, Answers(Choice(s.Enjoy, s.Yes), Choice(s.Features, s.OtherFeature)));

        Assert.True(visibility.IsQuestionVisible(s.WhyNot.Id));
    }

    [Fact]
    public void All_match_type_requires_every_condition()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.WhyNotRule.Conditions.Add(new LogicConditionDto
        {
            SourceQuestionId = s.Features.Id, Operator = ConditionOperator.Contains, OptionId = s.Reports.Id,
        });

        var onlyOne = LogicEvaluator.Evaluate(s.Definition, Answers(Choice(s.Enjoy, s.No)));
        var both = LogicEvaluator.Evaluate(s.Definition, Answers(Choice(s.Enjoy, s.No), Choice(s.Features, s.Reports)));

        Assert.False(onlyOne.IsQuestionVisible(s.WhyNot.Id));
        Assert.True(both.IsQuestionVisible(s.WhyNot.Id));
    }

    [Fact]
    public void Rule_referencing_deleted_question_never_matches()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.WhyNotRule.Conditions[0].SourceQuestionId = Guid.NewGuid();

        var visibility = LogicEvaluator.Evaluate(s.Definition, Answers(Choice(s.Enjoy, s.No)));

        Assert.False(visibility.IsQuestionVisible(s.WhyNot.Id));
    }

    [Fact]
    public void Rules_without_conditions_are_ignored()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.WhyNotRule.Conditions.Clear();

        var visibility = LogicEvaluator.Evaluate(s.Definition, Answers());

        Assert.True(visibility.IsQuestionVisible(s.WhyNot.Id));
    }
}
