using SmartSurvey.Application.Responses;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Tests of the FluentValidation validators of the response area.</summary>
public class ResponseRequestValidatorTests
{
    private readonly SaveResponseRequestValidator _requestValidator = new();
    private readonly ResponseQueryValidator _queryValidator = new();

    [Fact]
    public void Well_formed_request_is_valid()
    {
        var request = new SaveResponseRequest
        {
            Answers = [new AnswerInputDto { QuestionId = Guid.NewGuid(), Selections = [new SelectionInputDto()] }],
        };

        Assert.True(_requestValidator.Validate(request).IsValid);
        Assert.True(_requestValidator.Validate(new SaveResponseRequest()).IsValid);
    }

    [Fact]
    public void Null_answer_list_is_rejected()
    {
        var result = _requestValidator.Validate(new SaveResponseRequest { Answers = null! });

        var error = Assert.Single(result.Errors);
        Assert.Equal("Answers", error.PropertyName);
        Assert.Equal("Answers are required.", error.ErrorMessage);
    }

    [Fact]
    public void Null_entries_and_null_selection_lists_are_rejected()
    {
        var result = _requestValidator.Validate(new SaveResponseRequest
        {
            Answers = [null!, new AnswerInputDto { Selections = null! }],
        });

        Assert.Contains(result.Errors, e => e.PropertyName == "Answers[0]");
        Assert.Contains(result.Errors, e => e.PropertyName == "Answers[1].Selections");
    }

    [Fact]
    public void Oversized_payloads_are_rejected()
    {
        var tooManyAnswers = new SaveResponseRequest
        {
            Answers = Enumerable.Range(0, SaveResponseRequestValidator.MaxAnswers + 1).Select(_ => new AnswerInputDto()).ToList(),
        };
        var tooManySelections = new SaveResponseRequest
        {
            Answers =
            [
                new AnswerInputDto
                {
                    Selections = Enumerable.Range(0, SaveResponseRequestValidator.MaxSelectionsPerAnswer + 1)
                        .Select(_ => new SelectionInputDto { OptionId = Guid.NewGuid() })
                        .ToList(),
                },
            ],
        };

        Assert.Contains(_requestValidator.Validate(tooManyAnswers).Errors, e => e.PropertyName == "Answers");
        Assert.Contains(_requestValidator.Validate(tooManySelections).Errors, e => e.PropertyName == "Answers[0].Selections");
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("2026-01-10", null, true)]
    [InlineData(null, "2026-01-10", true)]
    [InlineData("2026-01-10", "2026-01-10", true)]
    [InlineData("2026-01-10", "2026-01-11", true)]
    [InlineData("2026-01-11", "2026-01-10", false)]
    public void Date_range_must_not_be_inverted(string? from, string? to, bool valid)
    {
        var query = new ResponseQuery
        {
            From = from is null ? null : DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture),
            To = to is null ? null : DateOnly.Parse(to, System.Globalization.CultureInfo.InvariantCulture),
        };

        Assert.Equal(valid, _queryValidator.Validate(query).IsValid);
    }

    [Fact]
    public void Search_term_length_is_limited()
    {
        var ok = new ResponseQuery { Search = new string('x', ResponseQueryValidator.MaxSearchLength) };
        var tooLong = new ResponseQuery { Search = new string('x', ResponseQueryValidator.MaxSearchLength + 1) };

        Assert.True(_queryValidator.Validate(ok).IsValid);
        Assert.Equal("Search", Assert.Single(_queryValidator.Validate(tooLong).Errors).PropertyName);
    }
}
