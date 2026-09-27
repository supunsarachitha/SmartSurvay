using System.Globalization;
using SmartSurvey.Application.Surveys;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>
/// Culture-invariant formatting used by every widget, so a report reads the same on every server
/// and in every export format (numbers "0.#", percentages "0.0%", UTC timestamps).
/// </summary>
internal static class ReportFormat
{
    /// <summary>Placeholder for values that cannot be computed (e.g. an average without data).</summary>
    public const string NotAvailable = "n/a";

    /// <summary>Ellipsis appended to shortened texts.</summary>
    public const string Ellipsis = "…";

    /// <summary>Formats a count, e.g. <c>1234</c>.</summary>
    public static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a statistic with at most one decimal, e.g. <c>3.5</c> (never "-0").</summary>
    public static string Number(double value)
    {
        var rounded = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString("0.#", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a raw value with up to two decimals, e.g. bucket bounds <c>12.25</c>.</summary>
    public static string Value(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return (rounded == 0 ? 0 : rounded).ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>Formats a share (0..1) as a percentage with one decimal, e.g. <c>42.5%</c>.</summary>
    public static string Percent(double fraction) => fraction.ToString("0.0%", CultureInfo.InvariantCulture);

    /// <summary>Formats <paramref name="part"/> / <paramref name="whole"/> as a percentage (0.0% when the whole is zero).</summary>
    public static string Share(int part, int whole) => Percent(whole <= 0 ? 0 : part / (double)whole);

    /// <summary>Formats a UTC timestamp as <c>yyyy-MM-dd HH:mm</c>.</summary>
    public static string Timestamp(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Formats a date as ISO <c>yyyy-MM-dd</c>.</summary>
    public static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Formats a month bucket as <c>yyyy-MM</c>.</summary>
    public static string Month(DateOnly firstOfMonth) => firstOfMonth.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>
    /// Formats a duration as <c>mm:ss</c>; minutes keep counting past 59 (e.g. <c>75:05</c>) so the
    /// value stays sortable and unambiguous.
    /// </summary>
    public static string Duration(TimeSpan duration)
    {
        var totalSeconds = Math.Max(0, (long)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero));
        return string.Create(CultureInfo.InvariantCulture, $"{totalSeconds / 60:00}:{totalSeconds % 60:00}");
    }

    /// <summary>Shortens a single-line text to <paramref name="maxLength"/> characters (ellipsis included).</summary>
    public static string Truncate(string? text, int maxLength)
    {
        var value = (text ?? string.Empty).ReplaceLineEndings(" ").Trim();
        if (value.Length <= maxLength)
        {
            return value;
        }

        var cut = Math.Max(1, maxLength - 1);
        if (char.IsHighSurrogate(value[cut - 1]))
        {
            cut--; // never split a surrogate pair
        }

        return value[..cut].TrimEnd() + Ellipsis;
    }

    /// <summary>Short question label such as <c>Q1 · Did you enjoy…</c> (code omitted when empty).</summary>
    public static string QuestionLabel(QuestionDto question, int maxTextLength)
    {
        var text = Truncate(question.Text, maxTextLength);
        return string.IsNullOrWhiteSpace(question.Code) ? text : $"{question.Code} · {text}";
    }
}
