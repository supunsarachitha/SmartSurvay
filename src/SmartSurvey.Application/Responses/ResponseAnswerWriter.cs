using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Responses;

/// <summary>
/// Cleans incoming answers against a survey design and writes them onto a (tracked) response.
/// </summary>
internal static class ResponseAnswerWriter
{
    /// <summary>
    /// Sanitises raw answers: answers to unknown questions are ignored, every answer is cleaned with
    /// <see cref="ResponseValidator.Sanitize"/> and empty results are dropped. When a question is
    /// answered more than once the last answer wins (a trailing empty answer clears the question).
    /// </summary>
    /// <param name="questions">Questions of the survey keyed by id.</param>
    /// <param name="answers">Raw answers from the client.</param>
    /// <returns>Clean answers keyed by question id.</returns>
    public static Dictionary<Guid, AnswerInputDto> Sanitize(
        IReadOnlyDictionary<Guid, QuestionDto> questions, IEnumerable<AnswerInputDto?> answers)
    {
        var result = new Dictionary<Guid, AnswerInputDto>();
        foreach (var raw in answers)
        {
            if (raw is null || !questions.TryGetValue(raw.QuestionId, out var question))
            {
                continue;
            }

            var clean = ResponseValidator.Sanitize(question, raw);
            if (clean is null)
            {
                result.Remove(raw.QuestionId);
            }
            else
            {
                result[raw.QuestionId] = clean;
            }
        }

        return result;
    }

    /// <summary>
    /// Makes the answers of <paramref name="response"/> equal to <paramref name="answers"/>.
    /// </summary>
    /// <remarks>
    /// Existing rows are updated in place rather than deleted and re-inserted: the unique indexes on
    /// (ResponseId, QuestionId) and (AnswerId, OptionId) would otherwise depend on the provider
    /// executing deletes before inserts within one SaveChanges. The response must be tracked by
    /// <paramref name="db"/> with its answers and selections loaded (or be new with no answers).
    /// </remarks>
    /// <param name="db">Context tracking the response.</param>
    /// <param name="response">Response to update.</param>
    /// <param name="answers">Sanitised answers keyed by question id.</param>
    /// <param name="questions">Questions of the survey keyed by id.</param>
    public static void Replace(
        IAppDbContext db,
        SurveyResponse response,
        IReadOnlyDictionary<Guid, AnswerInputDto> answers,
        IReadOnlyDictionary<Guid, QuestionDto> questions)
    {
        var pending = new Dictionary<Guid, AnswerInputDto>(answers);

        foreach (var existing in response.Answers.ToList())
        {
            if (pending.Remove(existing.QuestionId, out var input)
                && questions.TryGetValue(existing.QuestionId, out var question))
            {
                Update(db, existing, input, question.Type);
            }
            else
            {
                db.Answers.Remove(existing); // tracked selections are cascade-deleted by EF
            }
        }

        foreach (var input in pending.Values)
        {
            if (!questions.TryGetValue(input.QuestionId, out var question))
            {
                continue;
            }

            var answer = AnswerMapper.ToEntity(input, question.Type);
            answer.ResponseId = response.Id;
            db.Answers.Add(answer);
        }
    }

    private static void Update(IAppDbContext db, Answer existing, AnswerInputDto input, QuestionType type)
    {
        AnswerMapper.CopyValues(input, type, existing);

        var wanted = type.IsChoice()
            ? input.Selections.ToDictionary(s => s.OptionId)
            : new Dictionary<Guid, SelectionInputDto>();

        foreach (var selection in existing.Selections.ToList())
        {
            if (wanted.Remove(selection.OptionId, out var keep))
            {
                selection.FreeText = keep.FreeText;
            }
            else
            {
                db.AnswerSelections.Remove(selection);
            }
        }

        foreach (var added in wanted.Values)
        {
            db.AnswerSelections.Add(new AnswerSelection
            {
                AnswerId = existing.Id,
                OptionId = added.OptionId,
                FreeText = added.FreeText,
            });
        }
    }
}
