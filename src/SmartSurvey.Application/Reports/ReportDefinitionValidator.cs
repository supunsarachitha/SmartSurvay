using FluentValidation;
using FluentValidation.Results;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports;

/// <summary>Limits of report definitions (aligned with the database column sizes).</summary>
public static class ReportLimits
{
    /// <summary>Maximum report name length.</summary>
    public const int NameMaxLength = 200;

    /// <summary>Maximum report description length.</summary>
    public const int DescriptionMaxLength = 2000;

    /// <summary>Maximum widget title length.</summary>
    public const int WidgetTitleMaxLength = 200;

    /// <summary>Maximum number of widgets per report.</summary>
    public const int MaxWidgets = 100;

    /// <summary>Maximum number of answer filters per report.</summary>
    public const int MaxAnswerFilters = 50;

    /// <summary>Maximum length of an answer filter's comparison value.</summary>
    public const int FilterValueMaxLength = 500;

    /// <summary>Largest allowed <see cref="WidgetSettings.MaxRows"/>.</summary>
    public const int MaxRows = ReportEngine.MaxRowsLimit;
}

/// <summary>
/// Validates the self-contained rules of a report definition (name, description, widgets, filters).
/// Rules that need the survey design — the survey exists and every referenced question/option
/// belongs to it — are checked by <see cref="ReportService"/>. Error keys are property paths such as
/// <c>Widgets[2].Settings.MaxRows</c> so the builder can show each message next to its field.
/// </summary>
public sealed class ReportDefinitionValidator : AbstractValidator<ReportDefinitionDto>
{
    /// <summary>Creates the validator.</summary>
    public ReportDefinitionValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Please enter a name for the report.")
            .MaximumLength(ReportLimits.NameMaxLength).WithMessage($"The report name can be at most {ReportLimits.NameMaxLength} characters long.");

        RuleFor(x => x.Description)
            .MaximumLength(ReportLimits.DescriptionMaxLength).WithMessage($"The description can be at most {ReportLimits.DescriptionMaxLength} characters long.");

        RuleFor(x => x.SurveyId)
            .NotEmpty().WithMessage("Please choose the survey to report on.");

        RuleFor(x => x.Filters)
            .NotNull().WithMessage("The report filters are missing.")
            .SetValidator(new ReportFilterSetValidator());

        RuleFor(x => x.Widgets)
            .NotNull().WithMessage("The widget list is missing.")
            .Must(w => w is null || w.Count <= ReportLimits.MaxWidgets).WithMessage($"A report can contain at most {ReportLimits.MaxWidgets} widgets.");

        RuleForEach(x => x.Widgets).SetValidator(new ReportWidgetValidator());
    }
}

/// <summary>Validates the global response filters of a report.</summary>
internal sealed class ReportFilterSetValidator : AbstractValidator<ReportFilterSet>
{
    /// <summary>Creates the validator.</summary>
    public ReportFilterSetValidator()
    {
        RuleFor(x => x.To)
            .Must((filters, to) => filters.From is null || to is null || filters.From <= to)
            .WithMessage("The start date must be on or before the end date.");

        RuleFor(x => x.MatchType)
            .IsInEnum().WithMessage("Please choose how the answer filters are combined (all or any).");

        RuleFor(x => x.AnswerFilters)
            .NotNull().WithMessage("The answer filter list is missing.")
            .Must(f => f is null || f.Count <= ReportLimits.MaxAnswerFilters).WithMessage($"A report can have at most {ReportLimits.MaxAnswerFilters} answer filters.");

        RuleForEach(x => x.AnswerFilters).ChildRules(filter =>
        {
            filter.RuleFor(f => f.QuestionId).NotEmpty().WithMessage("Please choose the question to filter on.");
            filter.RuleFor(f => f.Operator).IsInEnum().WithMessage("Please choose a valid comparison.");
            filter.RuleFor(f => f.Value)
                .MaximumLength(ReportLimits.FilterValueMaxLength).WithMessage($"The filter value can be at most {ReportLimits.FilterValueMaxLength} characters long.");
        });
    }
}

/// <summary>Validates one widget definition.</summary>
internal sealed class ReportWidgetValidator : AbstractValidator<ReportWidgetDto>
{
    /// <summary>Creates the validator.</summary>
    public ReportWidgetValidator()
    {
        RuleFor(x => x.Title)
            .MaximumLength(ReportLimits.WidgetTitleMaxLength).WithMessage($"The widget title can be at most {ReportLimits.WidgetTitleMaxLength} characters long.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Please choose a valid widget type.");

        RuleFor(x => x.QuestionId)
            .NotEmpty().When(x => x.Type.RequiresQuestion()).WithMessage("Please choose a question for this widget.");

        // Two separate rules: a When() applies to every earlier validator of the same chain.
        RuleFor(x => x.SecondaryQuestionId)
            .NotEmpty().When(x => x.Type.RequiresSecondaryQuestion()).WithMessage("Please choose the second question for the cross-tabulation.");

        RuleFor(x => x.SecondaryQuestionId)
            .Must((w, secondary) => secondary != w.QuestionId)
            .When(x => x.Type.RequiresSecondaryQuestion() && x.SecondaryQuestionId is not null)
            .WithMessage("Please choose two different questions for the cross-tabulation.");

        RuleFor(x => x.Settings)
            .NotNull().WithMessage("The widget settings are missing.")
            .SetValidator(new WidgetSettingsValidator());
    }
}

/// <summary>Validates the display options of a widget.</summary>
internal sealed class WidgetSettingsValidator : AbstractValidator<WidgetSettings>
{
    /// <summary>Creates the validator.</summary>
    public WidgetSettingsValidator()
    {
        RuleFor(x => x.MaxRows)
            .InclusiveBetween(1, ReportLimits.MaxRows)
            .WithMessage($"The maximum number of rows must be between 1 and {ReportLimits.MaxRows}.");

        RuleFor(x => x.TopN)
            .GreaterThanOrEqualTo(1)
            .WithMessage("“Top N” must be at least 1 (leave it empty to show every option).");

        RuleFor(x => x.SortOrder)
            .IsInEnum().WithMessage("Please choose a valid sort order.");

        RuleFor(x => x.TimeGrouping)
            .IsInEnum().WithMessage("Please choose a valid time grouping.");
    }
}

/// <summary>Converts FluentValidation results into the error dictionary of <see cref="AppValidationException"/>.</summary>
internal static class ReportValidationErrors
{
    /// <summary>Groups failures by property path; identical messages for the same path are reported once.</summary>
    public static Dictionary<string, List<string>> From(ValidationResult result) =>
        result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToList());

    /// <summary>Adds an error message for a property path.</summary>
    public static void Add(this Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            errors[key] = messages = [];
        }

        if (!messages.Contains(message))
        {
            messages.Add(message);
        }
    }

    /// <summary>Throws an <see cref="AppValidationException"/> when there are errors.</summary>
    public static void ThrowIfAny(this Dictionary<string, List<string>> errors)
    {
        if (errors.Count > 0)
        {
            throw new AppValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }
}
