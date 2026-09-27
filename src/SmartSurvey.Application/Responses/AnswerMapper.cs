using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Responses;

/// <summary>
/// Maps between stored answers (<see cref="Answer"/> / <see cref="AnswerSelection"/>) and the editable
/// <see cref="AnswerInputDto"/> used by the runner, the logic evaluator and the validator.
/// </summary>
/// <remarks>
/// Storage columns per question type: text types → <see cref="Answer.TextValue"/>, numeric types →
/// <see cref="Answer.NumberValue"/>, date → <see cref="Answer.DateValue"/>, choice types →
/// <see cref="Answer.Selections"/> (with optional free text). Only the column matching the type is
/// ever written; the others are cleared.
/// </remarks>
public static class AnswerMapper
{
    /// <summary>Converts a stored answer (selections must be loaded) into an input DTO.</summary>
    public static AnswerInputDto ToInputDto(Answer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        return new AnswerInputDto
        {
            QuestionId = answer.QuestionId,
            Text = answer.TextValue,
            Number = answer.NumberValue,
            Date = answer.DateValue,
            Selections = answer.Selections
                .Select(s => new SelectionInputDto { OptionId = s.OptionId, FreeText = s.FreeText })
                .ToList(),
        };
    }

    /// <summary>
    /// Creates a new (untracked) answer entity with its selections from an already sanitised input.
    /// </summary>
    /// <param name="input">Sanitised answer (see <see cref="ResponseValidator.Sanitize"/>).</param>
    /// <param name="type">Type of the answered question.</param>
    public static Answer ToEntity(AnswerInputDto input, QuestionType type)
    {
        ArgumentNullException.ThrowIfNull(input);

        var answer = new Answer { QuestionId = input.QuestionId };
        CopyValues(input, type, answer);

        if (type.IsChoice())
        {
            answer.Selections = input.Selections
                .Select(s => new AnswerSelection { AnswerId = answer.Id, OptionId = s.OptionId, FreeText = s.FreeText })
                .ToList();
        }

        return answer;
    }

    /// <summary>
    /// Writes the value column matching <paramref name="type"/> and clears the others. Selections are
    /// not touched (they are separate entities; see <c>ResponseAnswerWriter</c>).
    /// </summary>
    public static void CopyValues(AnswerInputDto input, QuestionType type, Answer target)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(target);

        target.TextValue = type.IsText() ? input.Text : null;
        target.NumberValue = type.IsNumeric() ? input.Number : null;
        target.DateValue = type.IsDate() ? input.Date : null;
    }
}
