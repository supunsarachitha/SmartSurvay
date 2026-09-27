using System.Globalization;
using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Logic;

/// <summary>
/// Evaluates a single condition (<em>question · operator · value</em>) against an answer.
/// Shared by conditional logic (<see cref="LogicEvaluator"/>) and report answer filters so both
/// behave identically.
/// </summary>
/// <remarks>
/// Semantics:
/// <list type="bullet">
/// <item><c>IsAnswered</c>/<c>IsNotAnswered</c> test for any value.</item>
/// <item>Choice questions compare against <c>optionId</c> (or <c>value</c> parsed as a GUID):
/// <c>Equals</c>/<c>Contains</c> = "option is selected", <c>NotEquals</c>/<c>NotContains</c> = "option is not selected".</item>
/// <item>Text is compared case-insensitively after trimming.</item>
/// <item>Numbers and dates (ISO <c>yyyy-MM-dd</c>) support all comparison operators.</item>
/// <item>Negative operators are the logical negation of their positive counterpart, therefore they
/// are <c>true</c> for unanswered questions ("the answer is not X").</item>
/// </list>
/// </remarks>
public static class ConditionMatcher
{
    private const double Epsilon = 1e-9;

    /// <summary>Returns true when the answer satisfies the condition.</summary>
    /// <param name="questionType">Type of the source question.</param>
    /// <param name="op">Operator.</param>
    /// <param name="optionId">Option to compare with (choice questions).</param>
    /// <param name="value">Value to compare with (text/number/date).</param>
    /// <param name="answer">The answer, or null when unanswered / hidden.</param>
    public static bool Matches(QuestionType questionType, ConditionOperator op, Guid? optionId, string? value, AnswerInputDto? answer)
    {
        var answered = answer is not null && answer.HasValue(questionType);

        switch (op)
        {
            case ConditionOperator.IsAnswered:
                return answered;
            case ConditionOperator.IsNotAnswered:
                return !answered;
        }

        if (questionType.IsChoice())
        {
            return MatchesChoice(op, optionId, value, answered ? answer : null);
        }

        if (!answered)
        {
            // Nothing to compare: only the negative operators hold.
            return IsNegative(op);
        }

        if (questionType.IsNumeric())
        {
            return MatchesNumber(op, answer!.Number!.Value, value);
        }

        if (questionType.IsDate())
        {
            return MatchesDate(op, answer!.Date!.Value, value);
        }

        return MatchesText(op, answer!.Text!, value);
    }

    /// <summary>True for NotEquals / NotContains.</summary>
    public static bool IsNegative(ConditionOperator op) =>
        op is ConditionOperator.NotEquals or ConditionOperator.NotContains;

    private static bool MatchesChoice(ConditionOperator op, Guid? optionId, string? value, AnswerInputDto? answer)
    {
        var target = optionId ?? (Guid.TryParse(value, out var parsed) ? parsed : null);
        if (target is null)
        {
            return false; // mis-configured condition never matches
        }

        var selected = answer is not null && answer.IsSelected(target.Value);
        return op switch
        {
            ConditionOperator.Equals or ConditionOperator.Contains => selected,
            ConditionOperator.NotEquals or ConditionOperator.NotContains => !selected,
            _ => false,
        };
    }

    private static bool MatchesNumber(ConditionOperator op, double actual, string? value)
    {
        if (!TryParseNumber(value, out var expected))
        {
            return IsNegative(op);
        }

        return op switch
        {
            ConditionOperator.Equals => Math.Abs(actual - expected) < Epsilon,
            ConditionOperator.NotEquals => Math.Abs(actual - expected) >= Epsilon,
            ConditionOperator.GreaterThan => actual > expected + Epsilon,
            ConditionOperator.GreaterThanOrEqual => actual > expected - Epsilon,
            ConditionOperator.LessThan => actual < expected - Epsilon,
            ConditionOperator.LessThanOrEqual => actual < expected + Epsilon,
            ConditionOperator.Contains => actual.ToString(CultureInfo.InvariantCulture).Contains(value!.Trim(), StringComparison.Ordinal),
            ConditionOperator.NotContains => !actual.ToString(CultureInfo.InvariantCulture).Contains(value!.Trim(), StringComparison.Ordinal),
            _ => false,
        };
    }

    private static bool MatchesDate(ConditionOperator op, DateOnly actual, string? value)
    {
        if (!TryParseDate(value, out var expected))
        {
            return IsNegative(op);
        }

        return op switch
        {
            ConditionOperator.Equals => actual == expected,
            ConditionOperator.NotEquals => actual != expected,
            ConditionOperator.GreaterThan => actual > expected,
            ConditionOperator.GreaterThanOrEqual => actual >= expected,
            ConditionOperator.LessThan => actual < expected,
            ConditionOperator.LessThanOrEqual => actual <= expected,
            _ => false,
        };
    }

    private static bool MatchesText(ConditionOperator op, string actual, string? value)
    {
        var a = actual.Trim();
        var v = (value ?? string.Empty).Trim();
        return op switch
        {
            ConditionOperator.Equals => string.Equals(a, v, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.NotEquals => !string.Equals(a, v, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.Contains => a.Contains(v, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.NotContains => !a.Contains(v, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.GreaterThan => string.Compare(a, v, StringComparison.OrdinalIgnoreCase) > 0,
            ConditionOperator.GreaterThanOrEqual => string.Compare(a, v, StringComparison.OrdinalIgnoreCase) >= 0,
            ConditionOperator.LessThan => string.Compare(a, v, StringComparison.OrdinalIgnoreCase) < 0,
            ConditionOperator.LessThanOrEqual => string.Compare(a, v, StringComparison.OrdinalIgnoreCase) <= 0,
            _ => false,
        };
    }

    /// <summary>Parses a number using the invariant culture (e.g. "3.5").</summary>
    public static bool TryParseNumber(string? value, out double number) =>
        double.TryParse(value?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
        && double.IsFinite(number);

    /// <summary>Parses an ISO date (yyyy-MM-dd); also accepts a full ISO date-time.</summary>
    public static bool TryParseDate(string? value, out DateOnly date)
    {
        var trimmed = value?.Trim();
        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
        {
            date = DateOnly.FromDateTime(dt);
            return true;
        }

        date = default;
        return false;
    }
}
