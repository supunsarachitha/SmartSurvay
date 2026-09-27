using Microsoft.EntityFrameworkCore;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>
/// Summary KPIs: filtered response counts, completion rate (over all responses of the survey),
/// average completion time and first/last response timestamps.
/// </summary>
internal static class SummaryStatsBuilder
{
    /// <summary>Fills the KPI items.</summary>
    public static async Task BuildAsync(ReportRunContext ctx, WidgetResult result)
    {
        var responses = ctx.Responses;
        var completed = responses.Where(r => r.Status == ResponseStatus.Completed).ToList();

        result.ResponseCount = responses.Count;
        result.Stats.Add(new StatItem("Responses", ReportFormat.Count(responses.Count)));
        result.Stats.Add(new StatItem("Completed", ReportFormat.Count(completed.Count)));
        if (ctx.Filters.IncludeInProgress)
        {
            result.Stats.Add(new StatItem("In progress", ReportFormat.Count(responses.Count - completed.Count)));
        }

        result.Stats.Add(new StatItem("Completion rate", await CompletionRateAsync(ctx)));
        result.Stats.Add(new StatItem("Average completion time", AverageDuration(completed)));
        result.Stats.Add(new StatItem("First response", responses.Count == 0 ? ReportFormat.NotAvailable : ReportFormat.Timestamp(responses.Min(r => r.Timestamp))));
        result.Stats.Add(new StatItem("Last response", responses.Count == 0 ? ReportFormat.NotAvailable : ReportFormat.Timestamp(responses.Max(r => r.Timestamp))));
    }

    /// <summary>
    /// Completed ÷ (completed + in progress) over every response of the survey, independent of the
    /// report filters: it measures how many started responses were finished.
    /// </summary>
    private static async Task<string> CompletionRateAsync(ReportRunContext ctx)
    {
        var byStatus = await ctx.Db.Responses
            .AsNoTracking()
            .Where(r => r.SurveyId == ctx.Survey.Id)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ctx.CancellationToken);

        var total = byStatus.Sum(s => s.Count);
        var completed = byStatus.Where(s => s.Status == ResponseStatus.Completed).Sum(s => s.Count);
        return total == 0 ? ReportFormat.NotAvailable : ReportFormat.Share(completed, total);
    }

    /// <summary>Average of submitted − started over completed responses (mm:ss).</summary>
    private static string AverageDuration(IReadOnlyCollection<ResponseRow> completed)
    {
        var durations = completed
            .Where(r => r.SubmittedAt.HasValue && r.SubmittedAt.Value >= r.StartedAt)
            .Select(r => (r.SubmittedAt!.Value - r.StartedAt).TotalSeconds)
            .ToList();
        return durations.Count == 0 ? ReportFormat.NotAvailable : ReportFormat.Duration(TimeSpan.FromSeconds(durations.Average()));
    }
}
