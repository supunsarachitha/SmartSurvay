using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>
/// Describes the applied filters in plain language, one line per filter, e.g.
/// <c>Completed responses only</c>, <c>Submitted between 2026-01-01 and 2026-01-31</c> or
/// <c>Q1 “Did you enjoy the product?” is “No”</c>. Printed in report headers and exports.
/// </summary>
internal static class ReportFilterSummary
{
    private const int QuestionTextLength = 40;
    private const int ValueLength = 60;

    /// <summary>Builds the summary lines.</summary>
    public static List<string> Describe(ReportFilterSet filters, ReportResponseSelection selection)
    {
        var lines = new List<string>
        {
            filters.IncludeInProgress ? "Completed and in-progress responses" : "Completed responses only",
        };

        if (DescribeDateRange(filters.From, filters.To) is { } range)
        {
            lines.Add(range);
        }

        if (selection.ActiveFilters.Count > 1)
        {
            lines.Add(filters.MatchType == LogicMatchType.Any
                ? "Responses matching ANY of the following answer filters:"
                : "Responses matching ALL of the following answer filters:");
        }

        lines.AddRange(selection.ActiveFilters.Select(DescribeAnswerFilter));

        if (selection.IgnoredFilterCount > 0)
        {
            lines.Add(selection.IgnoredFilterCount == 1
                ? "1 answer filter was ignored because its question no longer exists."
                : $"{selection.IgnoredFilterCount} answer filters were ignored because their questions no longer exist.");
        }

        return lines;
    }

    /// <summary>Describes the date range (null when neither bound is set).</summary>
    private static string? DescribeDateRange(DateOnly? from, DateOnly? to) => (from, to) switch
    {
        ({ } f, { } t) => $"Submitted between {ReportFormat.Date(f)} and {ReportFormat.Date(t)}",
        ({ } f, null) => $"Submitted on or after {ReportFormat.Date(f)}",
        (null, { } t) => $"Submitted on or before {ReportFormat.Date(t)}",
        _ => null,
    };

    /// <summary>E.g. <c>Q1 “Did you enjoy the product?” is “No”</c>.</summary>
    private static string DescribeAnswerFilter(ActiveAnswerFilter active)
    {
        var (filter, question) = (active.Filter, active.Question);
        var subject = string.IsNullOrWhiteSpace(question.Code)
            ? $"“{ReportFormat.Truncate(question.Text, QuestionTextLength)}”"
            : $"{question.Code} “{ReportFormat.Truncate(question.Text, QuestionTextLength)}”";
        var verb = Verb(filter.Operator, question.Type.IsChoice());

        if (filter.Operator.IsUnary())
        {
            return $"{subject} {verb}";
        }

        return $"{subject} {verb} “{DescribeValue(active)}”";
    }

    /// <summary>Operator phrase; choice questions read as "is / includes" instead of "equals / contains".</summary>
    private static string Verb(ConditionOperator op, bool isChoice) => op switch
    {
        ConditionOperator.Equals => isChoice ? "is" : "equals",
        ConditionOperator.NotEquals => isChoice ? "is not" : "does not equal",
        ConditionOperator.Contains => isChoice ? "includes" : "contains",
        ConditionOperator.NotContains => isChoice ? "does not include" : "does not contain",
        ConditionOperator.GreaterThan => "is greater than",
        ConditionOperator.GreaterThanOrEqual => "is at least",
        ConditionOperator.LessThan => "is less than",
        ConditionOperator.LessThanOrEqual => "is at most",
        ConditionOperator.IsAnswered => "is answered",
        ConditionOperator.IsNotAnswered => "is not answered",
        _ => op.ToString(),
    };

    /// <summary>The compared value: the option label for choice questions, otherwise the entered value.</summary>
    private static string DescribeValue(ActiveAnswerFilter active)
    {
        var (filter, question) = (active.Filter, active.Question);
        if (!question.Type.IsChoice())
        {
            return ReportFormat.Truncate(filter.Value, ValueLength);
        }

        var optionId = filter.OptionId ?? (Guid.TryParse(filter.Value, out var parsed) ? parsed : null);
        var option = question.Options.FirstOrDefault(o => o.Id == optionId);
        return option is null ? "(unknown option)" : ReportFormat.Truncate(option.Text, ValueLength);
    }
}
