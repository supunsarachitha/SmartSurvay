using System.Globalization;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Responses;

/// <summary>
/// Turns answers into short, human-readable strings (response detail pages, exports, reports).
/// Culture-invariant so the output is identical on every server and in every export format.
/// </summary>
/// <remarks>
/// Formats per question type:
/// <list type="bullet">
/// <item>Choice: option labels in design order joined with <see cref="SelectionSeparator"/>; free text is
/// appended as <c>Label: text</c> (e.g. <c>Red; Other: teal</c>).</item>
/// <item>Rating: <c>value / max</c> (e.g. <c>4 / 5</c>).</item>
/// <item>Number and Scale: invariant number (e.g. <c>3.5</c>).</item>
/// <item>Date: ISO <c>yyyy-MM-dd</c>.</item>
/// <item>Text types: the text as entered.</item>
/// <item>Unanswered: empty string.</item>
/// </list>
/// </remarks>
public static class AnswerFormatter
{
    /// <summary>Separator between selected options.</summary>
    public const string SelectionSeparator = "; ";

    /// <summary>Formats an answer for display; returns an empty string when unanswered.</summary>
    /// <param name="question">The answered question (type, settings and options are used).</param>
    /// <param name="answer">The answer, or null when the question was not answered.</param>
    public static string Format(QuestionDto question, AnswerInputDto? answer)
    {
        ArgumentNullException.ThrowIfNull(question);

        var type = question.Type;
        if (answer is null || !answer.HasValue(type))
        {
            return string.Empty;
        }

        return type switch
        {
            _ when type.IsChoice() => FormatSelections(question.Options, answer.Selections),
            QuestionType.Rating => FormatRating(answer.Number.GetValueOrDefault(), question.Settings.RatingMax),
            _ when type.IsNumeric() => FormatNumber(answer.Number.GetValueOrDefault()),
            _ when type.IsDate() => FormatDate(answer.Date.GetValueOrDefault()),
            _ => answer.Text ?? string.Empty,
        };
    }

    /// <summary>
    /// Formats selected options in the order they were designed. Selections referring to unknown
    /// (e.g. deleted) options are skipped.
    /// </summary>
    /// <param name="options">All options of the question.</param>
    /// <param name="selections">The selected options.</param>
    public static string FormatSelections(IEnumerable<OptionDto> options, IEnumerable<SelectionInputDto> selections)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(selections);

        // First selection per option wins (sanitised answers never contain duplicates anyway).
        var freeTextByOption = new Dictionary<Guid, string?>();
        foreach (var selection in selections)
        {
            freeTextByOption.TryAdd(selection.OptionId, selection.FreeText);
        }

        var parts = options
            .OrderBy(o => o.Order)
            .Where(o => freeTextByOption.ContainsKey(o.Id))
            .Select(o => FormatOption(o.Text, freeTextByOption[o.Id]));

        return string.Join(SelectionSeparator, parts);
    }

    /// <summary>Formats one selected option: <c>Label</c> or <c>Label: free text</c>.</summary>
    public static string FormatOption(string label, string? freeText) =>
        string.IsNullOrWhiteSpace(freeText) ? label : $"{label}: {freeText.Trim()}";

    /// <summary>Formats a star rating, e.g. <c>4 / 5</c> (a non-positive maximum is treated as 1).</summary>
    public static string FormatRating(double value, int ratingMax) =>
        $"{FormatNumber(value)} / {Math.Max(1, ratingMax).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Formats a number with the invariant culture, e.g. <c>3.5</c>.</summary>
    public static string FormatNumber(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a date as ISO <c>yyyy-MM-dd</c>.</summary>
    public static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
