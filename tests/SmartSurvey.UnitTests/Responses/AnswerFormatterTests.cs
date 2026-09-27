using System.Globalization;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;
using static SmartSurvey.UnitTests.Responses.ResponseTestData;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Tests of <see cref="AnswerFormatter"/> display values.</summary>
public class AnswerFormatterTests
{
    [Fact]
    public void Choices_are_joined_in_design_order_with_free_text()
    {
        var s = SampleSurveys.CustomerFeedback();
        var answer = ChoiceWithText(s.Features, (s.OtherFeature, " teal "), (s.Reports, null));

        Assert.Equal("Reports; Other: teal", AnswerFormatter.Format(s.Features, answer));
    }

    [Fact]
    public void Single_choice_shows_the_option_label()
    {
        var s = SampleSurveys.CustomerFeedback();

        Assert.Equal("No", AnswerFormatter.Format(s.Enjoy, Choice(s.Enjoy, s.No)));
        Assert.Equal("Other: Oceania", AnswerFormatter.Format(s.Region, ChoiceWithText(s.Region, (s.OtherRegion, "Oceania"))));
    }

    [Fact]
    public void Blank_free_text_shows_only_the_label()
    {
        var s = SampleSurveys.CustomerFeedback();

        Assert.Equal("Other", AnswerFormatter.Format(s.Features, ChoiceWithText(s.Features, (s.OtherFeature, "   "))));
    }

    [Fact]
    public void Selections_of_unknown_options_are_skipped()
    {
        var s = SampleSurveys.CustomerFeedback();
        var answer = new AnswerInputDto
        {
            QuestionId = s.Features.Id,
            Selections = [new SelectionInputDto { OptionId = Guid.NewGuid() }, new SelectionInputDto { OptionId = s.Logic.Id }],
        };

        Assert.Equal("Conditional logic", AnswerFormatter.Format(s.Features, answer));
    }

    [Theory]
    [InlineData(4, 5, "4 / 5")]
    [InlineData(7, 10, "7 / 10")]
    [InlineData(1, 0, "1 / 1")]
    public void Rating_shows_value_out_of_maximum(double value, int max, string expected)
    {
        var question = new QuestionDto { Type = QuestionType.Rating, Settings = { RatingMax = max } };

        Assert.Equal(expected, AnswerFormatter.Format(question, new AnswerInputDto { Number = value }));
    }

    [Theory]
    [InlineData(QuestionType.Number, 3.5, "3.5")]
    [InlineData(QuestionType.Number, 42, "42")]
    [InlineData(QuestionType.Number, -0.25, "-0.25")]
    [InlineData(QuestionType.Scale, 9, "9")]
    public void Numbers_use_the_invariant_culture(QuestionType type, double value, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE"); // would print "3,5"
        try
        {
            var question = new QuestionDto { Type = type };
            Assert.Equal(expected, AnswerFormatter.Format(question, new AnswerInputDto { Number = value }));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Dates_use_iso_format()
    {
        var question = new QuestionDto { Type = QuestionType.Date };

        Assert.Equal("2026-03-01", AnswerFormatter.Format(question, new AnswerInputDto { Date = new DateOnly(2026, 3, 1) }));
    }

    [Theory]
    [InlineData(QuestionType.ShortText)]
    [InlineData(QuestionType.LongText)]
    [InlineData(QuestionType.Email)]
    public void Text_is_shown_as_entered(QuestionType type)
    {
        var question = new QuestionDto { Type = type };

        Assert.Equal("Line one\nLine two", AnswerFormatter.Format(question, new AnswerInputDto { Text = "Line one\nLine two" }));
    }

    [Fact]
    public void Unanswered_questions_format_as_empty_strings()
    {
        var s = SampleSurveys.CustomerFeedback();

        Assert.Equal(string.Empty, AnswerFormatter.Format(s.Features, null));
        Assert.Equal(string.Empty, AnswerFormatter.Format(s.Features, new AnswerInputDto()));
        Assert.Equal(string.Empty, AnswerFormatter.Format(s.Rating, new AnswerInputDto { Text = "ignored for numeric types" }));
        Assert.Equal(string.Empty, AnswerFormatter.Format(s.Email, new AnswerInputDto { Text = "   " }));
    }
}
