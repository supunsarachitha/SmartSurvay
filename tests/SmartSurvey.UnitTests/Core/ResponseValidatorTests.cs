using SmartSurvey.Application.Logic;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Core;

public class ResponseValidatorTests
{
    private static Dictionary<Guid, List<string>> Validate(SampleSurvey s, params AnswerInputDto[] answers)
    {
        var dict = LogicEvaluator.ToDictionary(answers);
        var visibility = LogicEvaluator.Evaluate(s.Definition, dict);
        return ResponseValidator.Validate(s.Definition, dict, visibility);
    }

    private static AnswerInputDto Choice(QuestionDto q, params (OptionDto Option, string? Text)[] selections) => new()
    {
        QuestionId = q.Id,
        Selections = selections.Select(x => new SelectionInputDto { OptionId = x.Option.Id, FreeText = x.Text }).ToList(),
    };

    [Fact]
    public void Required_visible_questions_must_be_answered()
    {
        var s = SampleSurveys.CustomerFeedback();

        var errors = Validate(s);

        Assert.Contains(s.Enjoy.Id, errors.Keys);
        Assert.Contains(s.Rating.Id, errors.Keys);
        Assert.DoesNotContain(s.WhyNot.Id, errors.Keys); // required but hidden
        Assert.DoesNotContain(s.Email.Id, errors.Keys); // optional
    }

    [Fact]
    public void Hidden_required_question_becomes_mandatory_when_shown()
    {
        var s = SampleSurveys.CustomerFeedback();

        var errors = Validate(s, Choice(s.Enjoy, (s.No, null)), new AnswerInputDto { QuestionId = s.Rating.Id, Number = 4 });

        Assert.Equal(["This question is required."], errors[s.WhyNot.Id]);
        Assert.Single(errors);
    }

    [Fact]
    public void Valid_complete_response_has_no_errors()
    {
        var s = SampleSurveys.CustomerFeedback();

        var errors = Validate(s,
            Choice(s.Enjoy, (s.Yes, null)),
            Choice(s.Features, (s.Reports, null), (s.OtherFeature, "Exports")),
            new AnswerInputDto { QuestionId = s.Rating.Id, Number = 5 },
            new AnswerInputDto { QuestionId = s.Email.Id, Text = "jane@example.com" },
            new AnswerInputDto { QuestionId = s.Age.Id, Number = 42 },
            Choice(s.Region, (s.Europe, null)));

        Assert.Empty(errors);
    }

    [Fact]
    public void Other_option_requires_free_text()
    {
        var s = SampleSurveys.CustomerFeedback();

        var errors = ResponseValidator.ValidateQuestion(s.Features, Choice(s.Features, (s.OtherFeature, "  ")));

        Assert.Contains(errors, e => e.Contains("Please specify", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(6, false)]
    [InlineData(3.5, false)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    public void Rating_must_be_whole_number_within_range(double value, bool valid)
    {
        var s = SampleSurveys.CustomerFeedback();
        var errors = ResponseValidator.ValidateQuestion(s.Rating, new AnswerInputDto { QuestionId = s.Rating.Id, Number = value });
        Assert.Equal(valid, errors.Count == 0);
    }

    [Theory]
    [InlineData("jane@example.com", true)]
    [InlineData("jane@example", false)]
    [InlineData("jane example.com", false)]
    [InlineData("@example.com", false)]
    public void Email_format_is_validated(string email, bool valid)
    {
        var s = SampleSurveys.CustomerFeedback();
        var errors = ResponseValidator.ValidateQuestion(s.Email, new AnswerInputDto { QuestionId = s.Email.Id, Text = email });
        Assert.Equal(valid, errors.Count == 0);
    }

    [Fact]
    public void Number_respects_min_max_and_whole_number_settings()
    {
        var s = SampleSurveys.CustomerFeedback();

        Assert.NotEmpty(ResponseValidator.ValidateQuestion(s.Age, new AnswerInputDto { Number = -1 }));
        Assert.NotEmpty(ResponseValidator.ValidateQuestion(s.Age, new AnswerInputDto { Number = 121 }));
        Assert.NotEmpty(ResponseValidator.ValidateQuestion(s.Age, new AnswerInputDto { Number = 30.5 }));
        Assert.Empty(ResponseValidator.ValidateQuestion(s.Age, new AnswerInputDto { Number = 30 }));
    }

    [Fact]
    public void Checkbox_min_and_max_selections_are_enforced()
    {
        var s = SampleSurveys.CustomerFeedback();
        s.Features.Settings.MinSelections = 2;
        s.Features.Settings.MaxSelections = 2;

        Assert.NotEmpty(ResponseValidator.ValidateQuestion(s.Features, Choice(s.Features, (s.Reports, null))));
        Assert.NotEmpty(ResponseValidator.ValidateQuestion(s.Features,
            Choice(s.Features, (s.Reports, null), (s.Logic, null), (s.OtherFeature, "x"))));
        Assert.Empty(ResponseValidator.ValidateQuestion(s.Features, Choice(s.Features, (s.Reports, null), (s.Logic, null))));
    }

    [Fact]
    public void Single_choice_rejects_multiple_or_unknown_options()
    {
        var s = SampleSurveys.CustomerFeedback();

        Assert.NotEmpty(ResponseValidator.ValidateQuestion(s.Enjoy, Choice(s.Enjoy, (s.Yes, null), (s.No, null))));
        Assert.NotEmpty(ResponseValidator.ValidateQuestion(s.Enjoy,
            new AnswerInputDto { Selections = [new SelectionInputDto { OptionId = Guid.NewGuid() }] }));
    }

    [Fact]
    public void Text_length_limits_are_enforced()
    {
        var q = new QuestionDto { Type = QuestionType.ShortText, Settings = { MinLength = 3, MaxLength = 5 } };

        Assert.NotEmpty(ResponseValidator.ValidateQuestion(q, new AnswerInputDto { Text = "ab" }));
        Assert.NotEmpty(ResponseValidator.ValidateQuestion(q, new AnswerInputDto { Text = "abcdef" }));
        Assert.Empty(ResponseValidator.ValidateQuestion(q, new AnswerInputDto { Text = " abcd " }));
    }

    [Fact]
    public void Sanitize_keeps_only_relevant_fields_and_valid_options()
    {
        var s = SampleSurveys.CustomerFeedback();
        var dirty = new AnswerInputDto
        {
            QuestionId = Guid.NewGuid(),
            Text = "ignored",
            Number = 3,
            Selections =
            [
                new SelectionInputDto { OptionId = s.Yes.Id, FreeText = "not allowed" },
                new SelectionInputDto { OptionId = s.No.Id },
                new SelectionInputDto { OptionId = Guid.NewGuid() },
            ],
        };

        var clean = ResponseValidator.Sanitize(s.Enjoy, dirty);

        Assert.NotNull(clean);
        Assert.Equal(s.Enjoy.Id, clean.QuestionId);
        Assert.Null(clean.Text);
        Assert.Null(clean.Number);
        var selection = Assert.Single(clean.Selections); // radio keeps first valid option only
        Assert.Equal(s.Yes.Id, selection.OptionId);
        Assert.Null(selection.FreeText); // option does not allow free text
    }

    [Fact]
    public void Sanitize_returns_null_for_empty_answers()
    {
        var s = SampleSurveys.CustomerFeedback();
        Assert.Null(ResponseValidator.Sanitize(s.Email, new AnswerInputDto { Text = "   " }));
    }
}
