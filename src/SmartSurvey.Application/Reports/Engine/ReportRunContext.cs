using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>
/// State shared by all widget builders during one report execution: the open database context,
/// the survey design and the filtered response set (both as rows in memory and as a composable
/// id query for database-side aggregation).
/// </summary>
internal sealed class ReportRunContext
{
    private readonly Dictionary<Guid, QuestionDto> _questions;

    /// <summary>Creates the context.</summary>
    public ReportRunContext(
        IAppDbContext db,
        SurveyDefinitionDto survey,
        ReportFilterSet filters,
        IReadOnlyList<ResponseRow> responses,
        IQueryable<Guid> responseIds,
        CancellationToken cancellationToken)
    {
        Db = db;
        Survey = survey;
        Filters = filters;
        Responses = responses;
        ResponseIds = responseIds;
        CancellationToken = cancellationToken;
        Questions = survey.AllQuestions().ToList();
        _questions = Questions.ToDictionary(q => q.Id);
    }

    /// <summary>Database context owned by the engine for the duration of the run.</summary>
    public IAppDbContext Db { get; }

    /// <summary>Survey design.</summary>
    public SurveyDefinitionDto Survey { get; }

    /// <summary>Applied report filters.</summary>
    public ReportFilterSet Filters { get; }

    /// <summary>All questions in display order.</summary>
    public IReadOnlyList<QuestionDto> Questions { get; }

    /// <summary>The filtered responses (lightweight rows).</summary>
    public IReadOnlyList<ResponseRow> Responses { get; }

    /// <summary>
    /// Ids of the filtered responses as a query that EF Core composes into an
    /// <c>IN (subquery)</c>, so aggregations run in the database on PostgreSQL and SQLite alike.
    /// </summary>
    public IQueryable<Guid> ResponseIds { get; }

    /// <summary>Cancellation token of the run.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Finds a question of the survey (null for unknown/deleted questions or a null id).</summary>
    public QuestionDto? FindQuestion(Guid? id) =>
        id is { } value && _questions.TryGetValue(value, out var question) ? question : null;
}

/// <summary>A filtered response reduced to the columns the widgets need.</summary>
/// <param name="Id">Response id.</param>
/// <param name="Status">Draft or completed.</param>
/// <param name="StartedAt">UTC start.</param>
/// <param name="SubmittedAt">UTC submission (completed responses).</param>
/// <param name="RespondentId">Respondent (null = anonymous).</param>
internal sealed record ResponseRow(Guid Id, ResponseStatus Status, DateTime StartedAt, DateTime? SubmittedAt, Guid? RespondentId)
{
    /// <summary>Reference timestamp used for date filters, ordering and time series (submitted, else started).</summary>
    public DateTime Timestamp => SubmittedAt ?? StartedAt;
}
