using FluentValidation;

namespace SmartSurvey.Application.Responses;

/// <summary>
/// Structural checks for draft/submission payloads (answer <em>content</em> is validated per
/// question by <see cref="ResponseValidator"/>). Guards against malformed JSON such as
/// <c>"answers": null</c> and oversized payloads.
/// </summary>
internal sealed class SaveResponseRequestValidator : AbstractValidator<SaveResponseRequest>
{
    /// <summary>Upper bound for answers in one payload.</summary>
    public const int MaxAnswers = 1_000;

    /// <summary>Upper bound for selected options in one answer.</summary>
    public const int MaxSelectionsPerAnswer = 200;

    /// <summary>Creates the rules.</summary>
    public SaveResponseRequestValidator()
    {
        RuleFor(x => x.Answers)
            .NotNull().WithMessage("Answers are required.")
            .Must(answers => answers.Count <= MaxAnswers)
            .When(x => x.Answers is not null, ApplyConditionTo.CurrentValidator)
            .WithMessage($"A response can contain at most {MaxAnswers} answers.");

        // RuleForEach skips a null collection (reported by the rule above).
        RuleForEach(x => x.Answers)
            .NotNull().WithMessage("Answers must not contain empty entries.")
            .ChildRules(answer => answer.RuleFor(a => a.Selections)
                .NotNull().WithMessage("Selections are required (use an empty list when nothing is selected).")
                .Must(selections => selections.Count <= MaxSelectionsPerAnswer)
                .When(a => a.Selections is not null, ApplyConditionTo.CurrentValidator)
                .WithMessage($"An answer can contain at most {MaxSelectionsPerAnswer} selections."));
    }
}

/// <summary>Validates the filters of the admin responses list.</summary>
internal sealed class ResponseQueryValidator : AbstractValidator<ResponseQuery>
{
    /// <summary>Maximum search term length.</summary>
    public const int MaxSearchLength = 200;

    /// <summary>Creates the rules.</summary>
    public ResponseQueryValidator()
    {
        RuleFor(x => x.Search)
            .MaximumLength(MaxSearchLength)
            .WithMessage($"The search term can be at most {MaxSearchLength} characters.");

        RuleFor(x => x.To)
            .Must((query, to) => to >= query.From)
            .When(x => x.From.HasValue && x.To.HasValue)
            .WithMessage("The end date must be on or after the start date.");
    }
}
