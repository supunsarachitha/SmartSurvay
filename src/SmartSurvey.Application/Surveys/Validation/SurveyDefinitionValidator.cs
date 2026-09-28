using FluentValidation;
using FluentValidation.Results;
using SmartSurvey.Application.Common;
using L = SmartSurvey.Application.Surveys.Validation.SurveyDesignLimits;

namespace SmartSurvey.Application.Surveys.Validation;

/// <summary>
/// Validates a complete survey design before it is saved: survey settings, pages, questions,
/// answer options, type-specific question settings, survey-wide uniqueness of ids and question
/// codes, and conditional logic. Error keys are property paths such as
/// <c>Sections[0].Questions[1].Options</c> or <c>LogicRules[2].Conditions[0].SourceQuestionId</c>
/// so the builder can show each message next to the offending field.
/// </summary>
/// <remarks>
/// <c>SurveyService</c> runs this validator on the normalised design (ids assigned, orders
/// renumbered, codes generated, strings trimmed). It can also be run by the builder on the raw
/// design for live feedback: empty ids and empty codes are accepted because the server fills them in.
/// </remarks>
public sealed class SurveyDefinitionValidator : AbstractValidator<SurveyDefinitionDto>
{
    /// <summary>Creates the validator.</summary>
    public SurveyDefinitionValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Please enter a title for the survey.")
            .MaximumLength(L.TitleMaxLength).WithMessage($"The title can be at most {L.TitleMaxLength} characters long.");

        RuleFor(x => x.Description)
            .MaximumLength(L.DescriptionMaxLength).WithMessage($"The description can be at most {L.DescriptionMaxLength} characters long.");

        RuleFor(x => x.WelcomeMessage)
            .MaximumLength(L.MessageMaxLength).WithMessage($"The welcome message can be at most {L.MessageMaxLength} characters long.");

        RuleFor(x => x.ThankYouMessage)
            .MaximumLength(L.MessageMaxLength).WithMessage($"The thank-you message can be at most {L.MessageMaxLength} characters long.");

        RuleFor(x => x.Slug)
            .Must(SlugGenerator.IsValid)
            .When(x => !string.IsNullOrWhiteSpace(x.Slug))
            .WithMessage($"The link name may only contain lowercase letters, digits and single hyphens (for example customer-feedback-2026) and can be at most {L.SlugMaxLength} characters long.");

        RuleFor(x => x.ClosesAt)
            .Must((x, closesAt) => closesAt is null || x.OpensAt is null || x.OpensAt < closesAt)
            .WithMessage("The closing date must be after the opening date.");

        RuleFor(x => x.AccessPassword)
            .Length(L.AccessPasswordMinLength, L.AccessPasswordMaxLength)
            .When(x => x.PasswordProtected && !string.IsNullOrEmpty(x.AccessPassword))
            .WithMessage($"The survey password must be {L.AccessPasswordMinLength} to {L.AccessPasswordMaxLength} characters long.");

        RuleFor(x => x.MaxResponses)
            .GreaterThan(0).WithMessage("The response limit must be at least 1 (leave it empty for no limit).");

        RuleFor(x => x.Sections)
            .Must(sections => sections is { Count: > 0 }).WithMessage("The survey needs at least one page.");

        RuleForEach(x => x.Sections).SetValidator(new SectionDtoValidator());

        // Survey-wide rules need the whole design; they report failures with full property paths.
        RuleFor(x => x.Sections).Custom((_, context) => SurveyUniquenessChecks.Check(context.InstanceToValidate, context.AddFailure));
        RuleFor(x => x.LogicRules).Custom((_, context) => SurveyLogicChecks.Check(context.InstanceToValidate, context.AddFailure));
    }
}

/// <summary>Converts FluentValidation results of survey designs into application exceptions.</summary>
internal static class SurveyValidationResultExtensions
{
    /// <summary>
    /// Throws an <see cref="AppValidationException"/> keyed by property path when the result has
    /// errors; identical messages for the same path are reported once.
    /// </summary>
    public static void ThrowIfInvalid(this ValidationResult result)
    {
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        throw new AppValidationException(errors);
    }
}
