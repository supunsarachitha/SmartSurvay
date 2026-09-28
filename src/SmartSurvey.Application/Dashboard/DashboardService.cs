using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Dashboard;

/// <summary>
/// Computes the admin dashboard KPIs with a handful of small, provider-portable queries. Values that
/// cannot be aggregated portably in SQL (durations, per-day buckets) are shaped in memory from a
/// narrow projection of the completed responses.
/// </summary>
public sealed class DashboardService(
    IAppDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider time) : IDashboardService
{
    /// <summary>Number of days (including today) covered by <see cref="DashboardSummaryDto.ResponsesPerDay"/>.</summary>
    public const int TrendDays = 30;

    /// <summary>Number of days (including today) counted in <see cref="DashboardSummaryDto.ResponsesLast7Days"/>.</summary>
    public const int RecentDays = 7;

    /// <summary>Number of surveys in <see cref="DashboardSummaryDto.TopSurveys"/>.</summary>
    public const int TopSurveyCount = 5;

    /// <summary>Number of rows in <see cref="DashboardSummaryDto.RecentResponses"/>.</summary>
    public const int RecentResponseCount = 10;

    /// <summary>Respondent label used for anonymous responses (or deleted users).</summary>
    public const string AnonymousName = "Anonymous";

    /// <inheritdoc />
    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only administrators can view the dashboard.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        await using var db = await dbFactory.CreateAsync(ct);

        // 1. Survey counts per status (templates are design blueprints, not surveys).
        var statusCounts = await db.Surveys.AsNoTracking()
            .Where(s => !s.IsTemplate)
            .GroupBy(s => s.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        // 2. Timestamps of every completed response: drives the completed count, the average
        //    duration and the per-day trend. Date arithmetic is not portable across SQLite and
        //    PostgreSQL, so it is done in memory on this narrow projection.
        var completed = await db.Responses.AsNoTracking()
            .Where(r => r.Status == ResponseStatus.Completed && r.SubmittedAt != null)
            .Select(r => new CompletedTimes(r.StartedAt, r.SubmittedAt!.Value))
            .ToListAsync(ct);

        var inProgress = await db.Responses.CountAsync(r => r.Status == ResponseStatus.InProgress, ct);
        var totalUsers = await db.Users.CountAsync(ct); // the data scope limits accounts to the workspace
        var totalReports = await db.Reports.CountAsync(ct);
        var topSurveys = await LoadTopSurveysAsync(db, ct);
        var recentResponses = await LoadRecentResponsesAsync(db, ct);

        var recentStart = today.AddDays(-(RecentDays - 1)).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        return new DashboardSummaryDto
        {
            TotalSurveys = statusCounts.Values.Sum(),
            PublishedSurveys = statusCounts.GetValueOrDefault(SurveyStatus.Published),
            DraftSurveys = statusCounts.GetValueOrDefault(SurveyStatus.Draft),
            ClosedSurveys = statusCounts.GetValueOrDefault(SurveyStatus.Closed),
            CompletedResponses = completed.Count,
            InProgressResponses = inProgress,
            ResponsesLast7Days = completed.Count(c => c.SubmittedAt >= recentStart),
            CompletionRate = CompletionRate(completed.Count, inProgress),
            AverageDurationSeconds = AverageDurationSeconds(completed),
            TotalUsers = totalUsers,
            TotalReports = totalReports,
            ResponsesPerDay = BuildDailySeries(completed, today),
            TopSurveys = topSurveys,
            RecentResponses = recentResponses,
        };
    }

    /// <summary>Completed / (completed + in progress); 0 when there are no responses at all.</summary>
    internal static double CompletionRate(int completed, int inProgress)
    {
        var started = completed + inProgress;
        return started == 0 ? 0 : (double)completed / started;
    }

    /// <summary>Mean completion time in seconds; null when no completed response has a valid duration.</summary>
    internal static double? AverageDurationSeconds(IReadOnlyCollection<CompletedTimes> completed)
    {
        // Negative durations can only come from clock skew or imported data; they would distort the mean.
        var durations = completed
            .Select(c => (c.SubmittedAt - c.StartedAt).TotalSeconds)
            .Where(seconds => seconds >= 0)
            .ToList();

        return durations.Count == 0 ? null : durations.Average();
    }

    /// <summary>
    /// Buckets submissions by UTC calendar day for the last <see cref="TrendDays"/> days including
    /// <paramref name="today"/>. Days without submissions are included with a zero count so charts
    /// get a continuous axis; the list is ordered oldest first.
    /// </summary>
    internal static IReadOnlyList<DailyCountDto> BuildDailySeries(IEnumerable<CompletedTimes> completed, DateOnly today)
    {
        var firstDay = today.AddDays(-(TrendDays - 1));
        var countsByDay = completed
            .Select(c => DateOnly.FromDateTime(c.SubmittedAt))
            .Where(day => day >= firstDay && day <= today)
            .GroupBy(day => day)
            .ToDictionary(g => g.Key, g => g.Count());

        return Enumerable.Range(0, TrendDays)
            .Select(offset => firstDay.AddDays(offset))
            .Select(day => new DailyCountDto(day, countsByDay.GetValueOrDefault(day)))
            .ToList();
    }

    /// <summary>The surveys with the most completed responses (surveys without responses are omitted).</summary>
    private static async Task<IReadOnlyList<SurveySummaryDto>> LoadTopSurveysAsync(IAppDbContext db, CancellationToken ct)
    {
        // Correlated sub-queries keep this to a single round trip on every provider.
        var rows = await db.Surveys.AsNoTracking()
            .Where(s => !s.IsTemplate)
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.Description,
                s.Slug,
                s.Status,
                s.IsTemplate,
                s.AllowAnonymous,
                s.OpensAt,
                s.ClosesAt,
                s.CreatedAt,
                s.UpdatedAt,
                QuestionCount = s.Questions.Count,
                Completed = s.Responses.Count(r => r.Status == ResponseStatus.Completed),
                InProgress = s.Responses.Count(r => r.Status == ResponseStatus.InProgress),
                LastResponseAt = s.Responses
                    .Where(r => r.Status == ResponseStatus.Completed)
                    .Max(r => r.SubmittedAt),
            })
            .Where(x => x.Completed > 0)
            .OrderByDescending(x => x.Completed)
            .ThenBy(x => x.Title)
            .Take(TopSurveyCount)
            .ToListAsync(ct);

        return rows
            .Select(x => new SurveySummaryDto
            {
                Id = x.Id,
                Title = x.Title,
                Description = x.Description,
                Slug = x.Slug,
                Status = x.Status,
                IsTemplate = x.IsTemplate,
                AllowAnonymous = x.AllowAnonymous,
                QuestionCount = x.QuestionCount,
                CompletedResponses = x.Completed,
                InProgressResponses = x.InProgress,
                OpensAt = x.OpensAt,
                ClosesAt = x.ClosesAt,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                LastResponseAt = x.LastResponseAt,
            })
            .ToList();
    }

    /// <summary>The latest completed responses with survey title and respondent name.</summary>
    private static async Task<IReadOnlyList<RecentResponseDto>> LoadRecentResponsesAsync(IAppDbContext db, CancellationToken ct)
    {
        var rows = await db.Responses.AsNoTracking()
            .Where(r => r.Status == ResponseStatus.Completed && r.SubmittedAt != null)
            .OrderByDescending(r => r.SubmittedAt)
            .ThenByDescending(r => r.Id)
            .Take(RecentResponseCount)
            .Select(r => new
            {
                r.Id,
                r.SurveyId,
                SurveyTitle = r.Survey!.Title,
                DisplayName = r.Respondent != null ? r.Respondent.DisplayName : null,
                Email = r.Respondent != null ? r.Respondent.Email : null,
                SubmittedAt = r.SubmittedAt!.Value,
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new RecentResponseDto
            {
                ResponseId = r.Id,
                SurveyId = r.SurveyId,
                SurveyTitle = r.SurveyTitle,
                RespondentName = FirstNonBlank(r.DisplayName, r.Email) ?? AnonymousName,
                SubmittedAt = r.SubmittedAt,
            })
            .ToList();
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    /// <summary>Start and submission time of a completed response.</summary>
    /// <param name="StartedAt">UTC start.</param>
    /// <param name="SubmittedAt">UTC submission.</param>
    internal sealed record CompletedTimes(DateTime StartedAt, DateTime SubmittedAt);
}
