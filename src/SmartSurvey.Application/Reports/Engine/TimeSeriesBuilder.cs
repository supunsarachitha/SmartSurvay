using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>
/// "Responses over time" line chart: responses per day, week (starting Monday) or month on the
/// submission timestamp (start timestamp for drafts), zero-filled between the first and last bucket.
/// </summary>
internal static class TimeSeriesBuilder
{
    /// <summary>Maximum number of buckets; longer periods keep the most recent buckets.</summary>
    public const int MaxBuckets = 400;

    /// <summary>Fills the chart and table.</summary>
    public static void Build(ReportRunContext ctx, WidgetSettings settings, WidgetResult result)
    {
        var grouping = Enum.IsDefined(settings.TimeGrouping) ? settings.TimeGrouping : TimeGrouping.Day;
        var counts = ctx.Responses
            .GroupBy(r => ReportStatistics.BucketStart(r.Timestamp, grouping))
            .ToDictionary(g => g.Key, g => g.Count());

        var buckets = Buckets(counts.Keys, grouping, out var truncated);
        var labels = buckets.Select(b => ReportStatistics.BucketLabel(b, grouping)).ToList();
        var values = buckets.Select(b => counts.GetValueOrDefault(b)).ToList();

        result.ResponseCount = ctx.Responses.Count;
        result.Chart = new ChartData
        {
            Kind = ChartKind.Line,
            Labels = labels,
            Series = [new ChartSeries { Name = WidgetOutput.ResponsesSeries, Values = values.Select(v => (double)v).ToList() }],
            ValueAxisTitle = WidgetOutput.ResponsesSeries,
        };
        result.Table = new TableData
        {
            Columns = [BucketHeader(grouping), WidgetOutput.ResponsesSeries],
            Rows = labels.Select((label, i) => new List<string> { label, ReportFormat.Count(values[i]) }).ToList(),
            Footer = ["Total", ReportFormat.Count(values.Sum())],
            NumericColumns = [1],
        };

        if (ctx.Responses.Count == 0)
        {
            result.Note = "No responses match the report filters.";
        }
        else if (truncated)
        {
            result.Note = $"Showing the latest {MaxBuckets} {BucketNoun(grouping)}; choose a coarser time grouping to see the whole period.";
        }
    }

    /// <summary>Every bucket from the first to the last one (capped to the latest <see cref="MaxBuckets"/>).</summary>
    private static List<DateOnly> Buckets(ICollection<DateOnly> used, TimeGrouping grouping, out bool truncated)
    {
        truncated = false;
        if (used.Count == 0)
        {
            return [];
        }

        var buckets = new List<DateOnly>();
        var last = used.Max();
        for (var bucket = used.Min(); bucket <= last; bucket = ReportStatistics.NextBucket(bucket, grouping))
        {
            buckets.Add(bucket);
        }

        if (buckets.Count > MaxBuckets)
        {
            truncated = true;
            buckets = buckets.GetRange(buckets.Count - MaxBuckets, MaxBuckets);
        }

        return buckets;
    }

    private static string BucketHeader(TimeGrouping grouping) => grouping switch
    {
        TimeGrouping.Week => "Week starting",
        TimeGrouping.Month => "Month",
        _ => "Day",
    };

    private static string BucketNoun(TimeGrouping grouping) => grouping switch
    {
        TimeGrouping.Week => "weeks",
        TimeGrouping.Month => "months",
        _ => "days",
    };
}
