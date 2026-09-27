using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Reports;

/// <summary>
/// Formats answers for raw data grids (the RawResponses report widget and raw response exports).
/// Unlike <see cref="AnswerFormatter"/> (display oriented, e.g. ratings as <c>4 / 5</c>) every value
/// stays machine readable: numbers are plain invariant numbers, dates ISO <c>yyyy-MM-dd</c>, choices
/// the option labels joined with <c>"; "</c> (free text as <c>Label: text</c>).
/// </summary>
public static class RawAnswerFormatter
{
    /// <summary>Formats an answer; returns an empty string when the question was not answered.</summary>
    /// <param name="question">Answered question.</param>
    /// <param name="answer">The answer, or null.</param>
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
            _ when type.IsChoice() => AnswerFormatter.FormatSelections(question.Options, answer.Selections),
            _ when type.IsNumeric() => AnswerFormatter.FormatNumber(answer.Number!.Value),
            _ when type.IsDate() => AnswerFormatter.FormatDate(answer.Date!.Value),
            _ => answer.Text ?? string.Empty,
        };
    }
}
