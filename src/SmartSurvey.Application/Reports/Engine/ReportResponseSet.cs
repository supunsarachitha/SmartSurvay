using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Logic;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>An answer filter whose question exists in the survey.</summary>
/// <param name="Filter">The filter.</param>
/// <param name="Question">The filtered question.</param>
internal sealed record ActiveAnswerFilter(AnswerFilter Filter, QuestionDto Question);

/// <summary>The filtered response set of a report run.</summary>
/// <param name="Rows">Matching responses.</param>
/// <param name="Ids">Ids of <paramref name="Rows"/> as a composable query.</param>
/// <param name="ActiveFilters">Answer filters that were applied.</param>
/// <param name="IgnoredFilterCount">Answer filters ignored because their question no longer exists.</param>
internal sealed record ReportResponseSelection(
    IReadOnlyList<ResponseRow> Rows,
    IQueryable<Guid> Ids,
    IReadOnlyList<ActiveAnswerFilter> ActiveFilters,
    int IgnoredFilterCount);

/// <summary>
/// Applies <see cref="ReportFilterSet"/> to a survey's responses. Status and date range are
/// evaluated in the database; answer filters are evaluated in memory with
/// <see cref="ConditionMatcher"/> so they behave exactly like conditional logic.
/// </summary>
internal static class ReportResponseSet
{
    /// <summary>Loads the responses that match the filters.</summary>
    public static async Task<ReportResponseSelection> LoadAsync(
        IAppDbContext db, SurveyDefinitionDto survey, ReportFilterSet filters, CancellationToken ct)
    {
        var candidates = BaseQuery(db, survey.Id, filters);
        var rows = await candidates
            .Select(r => new ResponseRow(r.Id, r.Status, r.StartedAt, r.SubmittedAt, r.RespondentId))
            .ToListAsync(ct);

        var requested = filters.AnswerFilters ?? [];
        var active = requested
            .Select(f => (Filter: f, Question: survey.FindQuestion(f.QuestionId)))
            .Where(x => x.Question is not null)
            .Select(x => new ActiveAnswerFilter(x.Filter, x.Question!))
            .ToList();
        var ignored = requested.Count - active.Count;

        if (active.Count == 0 || rows.Count == 0)
        {
            return new ReportResponseSelection(rows, candidates.Select(r => r.Id), active, ignored);
        }

        var answers = await LoadFilterAnswersAsync(db, candidates, active, ct);
        var matching = rows
            .Where(r => Matches(active, filters.MatchType, answers.GetValueOrDefault(r.Id)))
            .ToList();

        // The matching ids only exist in memory now; EF Core sends them as one array parameter.
        var ids = matching.Select(r => r.Id).ToList();
        return new ReportResponseSelection(matching, db.Responses.Where(r => ids.Contains(r.Id)).Select(r => r.Id), active, ignored);
    }

    /// <summary>
    /// Status and date filters. The date range is inclusive on whole UTC days and applies to the
    /// submission timestamp, or the start timestamp for drafts (<c>To</c> ⇒ before the next midnight).
    /// </summary>
    public static IQueryable<SurveyResponse> BaseQuery(IAppDbContext db, Guid surveyId, ReportFilterSet filters)
    {
        var query = db.Responses.AsNoTracking().Where(r => r.SurveyId == surveyId);
        if (!filters.IncludeInProgress)
        {
            query = query.Where(r => r.Status == ResponseStatus.Completed);
        }

        if (filters.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => (r.SubmittedAt ?? r.StartedAt) >= start);
        }

        if (filters.To is { } to)
        {
            var endExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => (r.SubmittedAt ?? r.StartedAt) < endExclusive);
        }

        return query;
    }

    /// <summary>Answers to the filter questions of every candidate, keyed by response and question.</summary>
    private static async Task<Dictionary<Guid, Dictionary<Guid, AnswerInputDto>>> LoadFilterAnswersAsync(
        IAppDbContext db, IQueryable<SurveyResponse> candidates, IReadOnlyList<ActiveAnswerFilter> active, CancellationToken ct)
    {
        var questionIds = active.Select(f => f.Question.Id).Distinct().ToList();
        var candidateIds = candidates.Select(r => r.Id);
        var answers = await db.Answers
            .AsNoTracking()
            .Include(a => a.Selections)
            .Where(a => questionIds.Contains(a.QuestionId) && candidateIds.Contains(a.ResponseId))
            .ToListAsync(ct);

        return answers
            .GroupBy(a => a.ResponseId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(a => a.QuestionId, AnswerMapper.ToInputDto));
    }

    /// <summary>Combines the filter results with AND (All) or OR (Any).</summary>
    private static bool Matches(
        IReadOnlyList<ActiveAnswerFilter> filters, LogicMatchType matchType, Dictionary<Guid, AnswerInputDto>? answers)
    {
        bool IsMatch(ActiveAnswerFilter f) => ConditionMatcher.Matches(
            f.Question.Type, f.Filter.Operator, f.Filter.OptionId, f.Filter.Value, answers?.GetValueOrDefault(f.Question.Id));

        return matchType == LogicMatchType.Any ? filters.Any(IsMatch) : filters.All(IsMatch);
    }
}
