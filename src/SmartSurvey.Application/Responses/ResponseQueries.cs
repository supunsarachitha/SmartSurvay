using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Responses;

/// <summary>Reusable, provider-portable EF Core queries of the response use cases.</summary>
/// <remarks>
/// The Application layer references EF Core without the relational package, so <c>AsSplitQuery</c>
/// is not available here. Object graphs are therefore loaded with one query per collection level
/// (each with at most one collection include): the same round trips a split query makes, without
/// the cartesian explosion of a single multi-collection JOIN and without EF's multiple-collection
/// include warning.
/// </remarks>
internal static class ResponseQueries
{
    /// <summary>
    /// Loads a survey with its complete design (sections, questions with options, logic rules with
    /// conditions) as a read-only graph ready for <c>SurveyMapper.ToDefinitionDto</c>.
    /// </summary>
    /// <param name="db">Context.</param>
    /// <param name="predicate">Survey filter (by id or slug).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The survey, or null when none matches.</returns>
    public static async Task<Survey?> LoadSurveyGraphAsync(
        this IAppDbContext db, Expression<Func<Survey, bool>> predicate, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking().FirstOrDefaultAsync(predicate, ct);
        if (survey is null)
        {
            return null;
        }

        var surveyId = survey.Id;
        survey.Sections = await db.SurveySections.AsNoTracking()
            .Where(s => s.SurveyId == surveyId)
            .ToListAsync(ct);
        survey.Questions = await db.Questions.AsNoTracking()
            .Include(q => q.Options)
            .Where(q => q.SurveyId == surveyId)
            .ToListAsync(ct);
        survey.LogicRules = await db.LogicRules.AsNoTracking()
            .Include(r => r.Conditions)
            .Where(r => r.SurveyId == surveyId)
            .ToListAsync(ct);

        return survey;
    }

    /// <summary>
    /// Loads the first response selected by <paramref name="select"/> together with its answers and
    /// their selections.
    /// </summary>
    /// <param name="db">Context.</param>
    /// <param name="select">Filters/orders the responses; the first result is loaded.</param>
    /// <param name="tracked">True to track the graph for updates; false for a read-only graph.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The response, or null when none matches.</returns>
    public static async Task<SurveyResponse?> LoadResponseWithAnswersAsync(
        this IAppDbContext db,
        Func<IQueryable<SurveyResponse>, IQueryable<SurveyResponse>> select,
        bool tracked,
        CancellationToken ct)
    {
        var source = tracked ? db.Responses.AsTracking() : db.Responses.AsNoTracking();
        var response = await select(source).FirstOrDefaultAsync(ct);
        if (response is null)
        {
            return null;
        }

        var responseId = response.Id;
        var answers = db.Answers.Include(a => a.Selections).Where(a => a.ResponseId == responseId);
        if (tracked)
        {
            await answers.LoadAsync(ct); // change-tracker fix-up fills response.Answers
        }
        else
        {
            response.Answers = await answers.AsNoTracking().ToListAsync(ct);
        }

        return response;
    }

    /// <summary>A user's drafts for a survey, most recently saved first.</summary>
    public static IQueryable<SurveyResponse> DraftsOf(this IQueryable<SurveyResponse> responses, Guid surveyId, Guid userId) =>
        responses
            .Where(r => r.SurveyId == surveyId && r.RespondentId == userId && r.Status == ResponseStatus.InProgress)
            // COALESCE is never null, so ordering is identical on PostgreSQL and SQLite.
            .OrderByDescending(r => r.UpdatedAt ?? r.StartedAt)
            .ThenByDescending(r => r.StartedAt);

    /// <summary>Counts the completed responses of a survey.</summary>
    public static Task<int> CountCompletedAsync(this IAppDbContext db, Guid surveyId, CancellationToken ct) =>
        db.Responses.CountAsync(r => r.SurveyId == surveyId && r.Status == ResponseStatus.Completed, ct);
}
