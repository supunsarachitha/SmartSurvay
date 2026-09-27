using FluentValidation;
using SmartSurvey.Domain.Enums;
using L = SmartSurvey.Application.Surveys.Validation.SurveyDesignLimits;

namespace SmartSurvey.Application.Surveys.Validation;

/// <summary>Field rules of a survey page (used by <see cref="SurveyDefinitionValidator"/>).</summary>
internal sealed class SectionDtoValidator : AbstractValidator<SectionDto>
{
    /// <summary>Creates the validator.</summary>
    public SectionDtoValidator()
    {
        RuleFor(s => s.Title)
            .NotEmpty().WithMessage("Please enter a title for this page.")
            .MaximumLength(L.SectionTitleMaxLength).WithMessage($"The page title can be at most {L.SectionTitleMaxLength} characters long.");

        RuleFor(s => s.Description)
            .MaximumLength(L.SectionDescriptionMaxLength).WithMessage($"The page description can be at most {L.SectionDescriptionMaxLength} characters long.");

        RuleForEach(s => s.Questions).SetValidator(new QuestionDtoValidator());
    }
}

/// <summary>
/// Field rules of a question: texts, code format, answer options of choice questions and the
/// type-specific settings. Settings that do not apply to the question's type are ignored (they are
/// kept when the designer switches types back and forth, so they must not block saving).
/// </summary>
internal sealed class QuestionDtoValidator : AbstractValidator<QuestionDto>
{
    /// <summary>Question codes start with a letter followed by letters, digits, '_' or '-'.</summary>
    internal const string CodePattern = "^[A-Za-z][A-Za-z0-9_\\-]*$";

    /// <summary>Creates the validator.</summary>
    public QuestionDtoValidator()
    {
        RuleFor(q => q.Type).IsInEnum().WithMessage("Please choose a valid question type.");

        RuleFor(q => q.Text)
            .NotEmpty().WithMessage("Please enter the question text.")
            .MaximumLength(L.QuestionTextMaxLength).WithMessage($"The question text can be at most {L.QuestionTextMaxLength} characters long.");

        RuleFor(q => q.Description)
            .MaximumLength(L.QuestionDescriptionMaxLength).WithMessage($"The help text can be at most {L.QuestionDescriptionMaxLength} characters long.");

        // An empty code means "generate one for me", so only supplied codes are checked.
        RuleFor(q => q.Code)
            .MaximumLength(L.QuestionCodeMaxLength).WithMessage($"The question code can be at most {L.QuestionCodeMaxLength} characters long.")
            .Matches(CodePattern).WithMessage("The question code must start with a letter and may only contain letters, digits, '_' and '-' (for example Q1 or age_group).")
            .When(q => !string.IsNullOrEmpty(q.Code));

        When(q => q.Type.IsChoice(), () =>
        {
            RuleFor(q => q.Options)
                .Must(options => options is { Count: >= L.MinChoiceOptions })
                .WithMessage($"Add at least {L.MinChoiceOptions} answer options.");

            RuleForEach(q => q.Options).SetValidator(new OptionDtoValidator());
        });

        RuleFor(q => q.Settings).NotNull().WithMessage("The question settings are missing.");
        When(q => q.Settings is not null, AddSettingsRules);
    }

    private void AddSettingsRules()
    {
        When(q => q.Type.IsText() || q.Type == QuestionType.Number, () =>
            RuleFor(q => q.Settings.Placeholder)
                .MaximumLength(L.PlaceholderMaxLength).WithMessage($"The placeholder can be at most {L.PlaceholderMaxLength} characters long."));

        When(q => q.Type.IsText(), AddTextLengthRules);
        When(q => q.Type == QuestionType.Number, AddNumberRangeRules);
        When(q => q.Type == QuestionType.Checkbox, AddSelectionRules);
        When(q => q.Type == QuestionType.Rating, AddRatingRules);
        When(q => q.Type == QuestionType.Scale, AddScaleRules);
        When(q => q.Type == QuestionType.Date, AddDateRangeRules);
    }

    private void AddTextLengthRules()
    {
        RuleFor(q => q.Settings.MinLength)
            .GreaterThanOrEqualTo(0).WithMessage("The minimum length cannot be negative.");

        RuleFor(q => q.Settings.MaxLength)
            .InclusiveBetween(1, L.TextAnswerMaxLength).WithMessage($"The maximum length must be between 1 and {L.TextAnswerMaxLength} characters.");

        RuleFor(q => q.Settings.MaxLength)
            .Must((q, max) => max is null || q.Settings.MinLength is null || q.Settings.MinLength <= max)
            .WithMessage("The maximum length must be greater than or equal to the minimum length.");
    }

    private void AddNumberRangeRules()
    {
        RuleFor(q => q.Settings.MaxValue)
            .Must((q, max) => max is null || q.Settings.MinValue is null || q.Settings.MinValue <= max)
            .WithMessage("The maximum value must be greater than or equal to the minimum value.");
    }

    private void AddSelectionRules()
    {
        RuleFor(q => q.Settings.MinSelections)
            .GreaterThanOrEqualTo(0).WithMessage("The minimum number of selections cannot be negative.");

        RuleFor(q => q.Settings.MinSelections)
            .Must((q, min) => min is null || q.Options is null || min <= q.Options.Count)
            .WithMessage("The minimum number of selections cannot be larger than the number of options.");

        RuleFor(q => q.Settings.MaxSelections)
            .GreaterThanOrEqualTo(1).WithMessage("The maximum number of selections must be at least 1.");

        RuleFor(q => q.Settings.MaxSelections)
            .Must((q, max) => max is null || q.Settings.MinSelections is null || q.Settings.MinSelections <= max)
            .WithMessage("The maximum number of selections must be greater than or equal to the minimum.");

        RuleFor(q => q.Settings.MaxSelections)
            .Must((q, max) => max is null || q.Options is null || max <= q.Options.Count)
            .WithMessage("The maximum number of selections cannot be larger than the number of options.");
    }

    private void AddRatingRules()
    {
        RuleFor(q => q.Settings.RatingMax)
            .InclusiveBetween(L.RatingMaxLowest, L.RatingMaxHighest)
            .WithMessage($"A rating must have between {L.RatingMaxLowest} and {L.RatingMaxHighest} stars.");
    }

    private void AddScaleRules()
    {
        RuleFor(q => q.Settings.ScaleMin)
            .InclusiveBetween(L.ScaleLowest, L.ScaleHighest)
            .WithMessage($"The lowest scale value must be between {L.ScaleLowest} and {L.ScaleHighest}.");

        RuleFor(q => q.Settings.ScaleMax)
            .InclusiveBetween(L.ScaleLowest, L.ScaleHighest)
            .WithMessage($"The highest scale value must be between {L.ScaleLowest} and {L.ScaleHighest}.");

        RuleFor(q => q.Settings.ScaleMax)
            .Must((q, max) => max > q.Settings.ScaleMin)
            .WithMessage("The highest scale value must be greater than the lowest value.");

        RuleFor(q => q.Settings.ScaleMax)
            .Must((q, max) => max - q.Settings.ScaleMin <= L.ScaleMaxSpan)
            .When(q => q.Settings.ScaleMax > q.Settings.ScaleMin)
            .WithMessage($"A scale can span at most {L.ScaleMaxSpan} points (for example 0 to 10).");

        RuleFor(q => q.Settings.ScaleMinLabel)
            .MaximumLength(L.ScaleLabelMaxLength).WithMessage($"The scale label can be at most {L.ScaleLabelMaxLength} characters long.");

        RuleFor(q => q.Settings.ScaleMaxLabel)
            .MaximumLength(L.ScaleLabelMaxLength).WithMessage($"The scale label can be at most {L.ScaleLabelMaxLength} characters long.");
    }

    private void AddDateRangeRules()
    {
        RuleFor(q => q.Settings.MaxDate)
            .Must((q, max) => max is null || q.Settings.MinDate is null || q.Settings.MinDate <= max)
            .WithMessage("The latest allowed date must be on or after the earliest allowed date.");
    }
}

/// <summary>Field rules of an answer option.</summary>
internal sealed class OptionDtoValidator : AbstractValidator<OptionDto>
{
    /// <summary>Creates the validator.</summary>
    public OptionDtoValidator()
    {
        RuleFor(o => o.Text)
            .NotEmpty().WithMessage("Please enter a label for this option.")
            .MaximumLength(L.OptionTextMaxLength).WithMessage($"The option label can be at most {L.OptionTextMaxLength} characters long.");

        RuleFor(o => o.Value)
            .MaximumLength(L.OptionValueMaxLength).WithMessage($"The option value can be at most {L.OptionValueMaxLength} characters long.");

        RuleFor(o => o.FreeTextPlaceholder)
            .MaximumLength(L.FreeTextPlaceholderMaxLength).WithMessage($"The placeholder can be at most {L.FreeTextPlaceholderMaxLength} characters long.");
    }
}
