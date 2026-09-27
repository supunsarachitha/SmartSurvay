using SmartSurvey.Application.Logic;
using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.UnitTests.Core;

public class ConditionMatcherTests
{
    private static readonly Guid Red = Guid.NewGuid();
    private static readonly Guid Blue = Guid.NewGuid();

    private static AnswerInputDto Selected(params Guid[] options) => new()
    {
        Selections = options.Select(o => new SelectionInputDto { OptionId = o }).ToList(),
    };

    [Theory]
    [InlineData(ConditionOperator.Equals, true)]
    [InlineData(ConditionOperator.Contains, true)]
    [InlineData(ConditionOperator.NotEquals, false)]
    [InlineData(ConditionOperator.NotContains, false)]
    [InlineData(ConditionOperator.IsAnswered, true)]
    [InlineData(ConditionOperator.IsNotAnswered, false)]
    public void Choice_operators_test_selected_option(ConditionOperator op, bool expected)
    {
        var result = ConditionMatcher.Matches(QuestionType.Checkbox, op, Red, null, Selected(Red, Blue));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Negative_operators_are_true_for_unanswered_questions()
    {
        Assert.True(ConditionMatcher.Matches(QuestionType.Radio, ConditionOperator.NotEquals, Red, null, null));
        Assert.True(ConditionMatcher.Matches(QuestionType.ShortText, ConditionOperator.NotContains, null, "x", null));
        Assert.False(ConditionMatcher.Matches(QuestionType.ShortText, ConditionOperator.Equals, null, "x", null));
        Assert.False(ConditionMatcher.Matches(QuestionType.Number, ConditionOperator.GreaterThan, null, "1", null));
    }

    [Fact]
    public void Choice_option_can_be_given_as_guid_value()
    {
        Assert.True(ConditionMatcher.Matches(QuestionType.Radio, ConditionOperator.Equals, null, Red.ToString(), Selected(Red)));
        Assert.False(ConditionMatcher.Matches(QuestionType.Radio, ConditionOperator.Equals, null, "not-a-guid", Selected(Red)));
    }

    [Theory]
    [InlineData(ConditionOperator.Equals, "hello world", true)]
    [InlineData(ConditionOperator.Equals, "HELLO WORLD ", true)]
    [InlineData(ConditionOperator.Contains, "WORLD", true)]
    [InlineData(ConditionOperator.NotContains, "planet", true)]
    [InlineData(ConditionOperator.NotEquals, "hello", true)]
    [InlineData(ConditionOperator.Contains, "planet", false)]
    public void Text_comparisons_are_case_insensitive_and_trimmed(ConditionOperator op, string value, bool expected)
    {
        var answer = new AnswerInputDto { Text = "  Hello World " };
        Assert.Equal(expected, ConditionMatcher.Matches(QuestionType.ShortText, op, null, value, answer));
    }

    [Theory]
    [InlineData(ConditionOperator.Equals, "7", true)]
    [InlineData(ConditionOperator.GreaterThan, "6.5", true)]
    [InlineData(ConditionOperator.GreaterThanOrEqual, "7", true)]
    [InlineData(ConditionOperator.LessThan, "7", false)]
    [InlineData(ConditionOperator.LessThanOrEqual, "7.0", true)]
    [InlineData(ConditionOperator.NotEquals, "8", true)]
    [InlineData(ConditionOperator.GreaterThan, "abc", false)]
    public void Numeric_comparisons_use_invariant_culture(ConditionOperator op, string value, bool expected)
    {
        var answer = new AnswerInputDto { Number = 7 };
        Assert.Equal(expected, ConditionMatcher.Matches(QuestionType.Scale, op, null, value, answer));
    }

    [Theory]
    [InlineData(ConditionOperator.Equals, "2026-03-01", true)]
    [InlineData(ConditionOperator.GreaterThan, "2026-02-28", true)]
    [InlineData(ConditionOperator.LessThan, "2026-03-01", false)]
    [InlineData(ConditionOperator.LessThanOrEqual, "2026-03-01T00:00:00Z", true)]
    public void Date_comparisons_accept_iso_dates(ConditionOperator op, string value, bool expected)
    {
        var answer = new AnswerInputDto { Date = new DateOnly(2026, 3, 1) };
        Assert.Equal(expected, ConditionMatcher.Matches(QuestionType.Date, op, null, value, answer));
    }
}
