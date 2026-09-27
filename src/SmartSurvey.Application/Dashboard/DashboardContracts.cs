using SmartSurvey.Application.Surveys;

namespace SmartSurvey.Application.Dashboard;

/// <summary>KPIs for the admin dashboard.</summary>
public sealed record DashboardSummaryDto
{
    /// <summary>All non-template surveys.</summary>
    public int TotalSurveys { get; init; }

    /// <summary>Published surveys.</summary>
    public int PublishedSurveys { get; init; }

    /// <summary>Draft surveys.</summary>
    public int DraftSurveys { get; init; }

    /// <summary>Closed surveys.</summary>
    public int ClosedSurveys { get; init; }

    /// <summary>Completed responses (all time).</summary>
    public int CompletedResponses { get; init; }

    /// <summary>Drafts in progress.</summary>
    public int InProgressResponses { get; init; }

    /// <summary>Completed responses in the last 7 days.</summary>
    public int ResponsesLast7Days { get; init; }

    /// <summary>Completed / (completed + in progress), 0..1.</summary>
    public double CompletionRate { get; init; }

    /// <summary>Average completion time in seconds (completed responses).</summary>
    public double? AverageDurationSeconds { get; init; }

    /// <summary>Registered users.</summary>
    public int TotalUsers { get; init; }

    /// <summary>Saved reports.</summary>
    public int TotalReports { get; init; }

    /// <summary>Completed responses per day for the last 30 days (oldest first, zero-filled).</summary>
    public IReadOnlyList<DailyCountDto> ResponsesPerDay { get; init; } = [];

    /// <summary>Top 5 surveys by completed responses.</summary>
    public IReadOnlyList<SurveySummaryDto> TopSurveys { get; init; } = [];

    /// <summary>10 most recent completed responses.</summary>
    public IReadOnlyList<RecentResponseDto> RecentResponses { get; init; } = [];
}

/// <summary>Count for a day.</summary>
/// <param name="Date">UTC date.</param>
/// <param name="Count">Count.</param>
public sealed record DailyCountDto(DateOnly Date, int Count);

/// <summary>A recent response.</summary>
public sealed record RecentResponseDto
{
    /// <summary>Response id.</summary>
    public Guid ResponseId { get; init; }

    /// <summary>Survey id.</summary>
    public Guid SurveyId { get; init; }

    /// <summary>Survey title.</summary>
    public string SurveyTitle { get; init; } = string.Empty;

    /// <summary>Respondent name or "Anonymous".</summary>
    public string RespondentName { get; init; } = "Anonymous";

    /// <summary>Submitted (UTC).</summary>
    public DateTime SubmittedAt { get; init; }
}

/// <summary>Dashboard queries.</summary>
public interface IDashboardService
{
    /// <summary>Computes the dashboard KPIs.</summary>
    Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default);
}
