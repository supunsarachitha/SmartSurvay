using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>Descriptive statistics of a numeric answer set.</summary>
/// <param name="Count">Number of values.</param>
/// <param name="Mean">Arithmetic mean.</param>
/// <param name="Median">Median (average of the two middle values for even counts).</param>
/// <param name="Min">Smallest value.</param>
/// <param name="Max">Largest value.</param>
/// <param name="StdDev">Sample standard deviation (n − 1); 0 for a single value.</param>
internal sealed record DescriptiveStats(int Count, double Mean, double Median, double Min, double Max, double StdDev);

/// <summary>A labelled category with its count (a table row / chart bar).</summary>
/// <param name="Label">Category label.</param>
/// <param name="Count">Number of answers / responses.</param>
internal sealed record CategoryCount(string Label, int Count);

/// <summary>Pure statistical helpers used by the widget builders (unit-testable without a database).</summary>
internal static class ReportStatistics
{
    /// <summary>Maximum number of histogram buckets for Number questions.</summary>
    public const int MaxHistogramBuckets = 10;

    /// <summary>Computes descriptive statistics (null for an empty set).</summary>
    public static DescriptiveStats? Describe(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var sorted = values.Order().ToArray();
        var n = sorted.Length;
        var mean = sorted.Average();
        var median = n % 2 == 1 ? sorted[n / 2] : (sorted[(n / 2) - 1] + sorted[n / 2]) / 2;
        var variance = n > 1 ? sorted.Sum(v => (v - mean) * (v - mean)) / (n - 1) : 0;
        return new DescriptiveStats(n, mean, median, sorted[0], sorted[^1], Math.Sqrt(variance));
    }

    /// <summary>
    /// Net Promoter Score on a 0–10 scale: % promoters (9–10) minus % detractors (0–6), from −100 to 100.
    /// </summary>
    public static double NetPromoterScore(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var promoters = values.Count(v => v >= 9);
        var detractors = values.Count(v => v <= 6);
        return (promoters - detractors) * 100.0 / values.Count;
    }

    /// <summary>
    /// Zero-filled distribution over every integer value of a rating/scale range. The range is widened
    /// to include out-of-range answers (e.g. after the scale was changed) so no answer is dropped.
    /// </summary>
    /// <param name="values">Answers.</param>
    /// <param name="min">Designed minimum.</param>
    /// <param name="max">Designed maximum.</param>
    /// <param name="maxCategories">Safety cap on the number of categories.</param>
    public static List<CategoryCount> IntegerDistribution(IReadOnlyCollection<double> values, int min, int max, int maxCategories = 101)
    {
        var buckets = values.GroupBy(v => (int)Math.Round(v, MidpointRounding.AwayFromZero)).ToDictionary(g => g.Key, g => g.Count());
        var low = buckets.Count == 0 ? min : Math.Min(min, buckets.Keys.Min());
        var high = buckets.Count == 0 ? max : Math.Max(max, buckets.Keys.Max());
        if (high < low)
        {
            (low, high) = (high, low);
        }

        if (high - low + 1 > maxCategories)
        {
            // Pathological ranges: only list the values that were actually given.
            return buckets.OrderBy(b => b.Key).Select(b => new CategoryCount(ReportFormat.Count(b.Key), b.Value)).ToList();
        }

        return Enumerable.Range(low, high - low + 1)
            .Select(v => new CategoryCount(ReportFormat.Count(v), buckets.GetValueOrDefault(v)))
            .ToList();
    }

    /// <summary>
    /// Histogram with at most <see cref="MaxHistogramBuckets"/> equal-width buckets. Whole numbers
    /// spanning ≤ 10 values get one bucket per value; other whole numbers get integer ranges
    /// (<c>0–11</c>, <c>12–23</c> …); decimals get half-open ranges with the last one closed.
    /// </summary>
    public static List<CategoryCount> Histogram(IReadOnlyCollection<double> values)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var min = values.Min();
        var max = values.Max();
        if (max - min < 1e-9)
        {
            return [new CategoryCount(ReportFormat.Value(min), values.Count)];
        }

        var wholeNumbers = values.All(v => Math.Abs(v - Math.Round(v)) < 1e-9);
        return wholeNumbers ? IntegerHistogram(values, (long)Math.Round(min), (long)Math.Round(max)) : DecimalHistogram(values, min, max);
    }

    /// <summary>Start of the time bucket that contains <paramref name="timestamp"/> (weeks start on Monday).</summary>
    public static DateOnly BucketStart(DateTime timestamp, TimeGrouping grouping)
    {
        var day = DateOnly.FromDateTime(timestamp);
        return grouping switch
        {
            TimeGrouping.Week => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
            TimeGrouping.Month => new DateOnly(day.Year, day.Month, 1),
            _ => day,
        };
    }

    /// <summary>The bucket following <paramref name="bucket"/>.</summary>
    public static DateOnly NextBucket(DateOnly bucket, TimeGrouping grouping) => grouping switch
    {
        TimeGrouping.Week => bucket.AddDays(7),
        TimeGrouping.Month => bucket.AddMonths(1),
        _ => bucket.AddDays(1),
    };

    /// <summary>Label of a time bucket: <c>yyyy-MM-dd</c> for days/weeks, <c>yyyy-MM</c> for months.</summary>
    public static string BucketLabel(DateOnly bucket, TimeGrouping grouping) =>
        grouping == TimeGrouping.Month ? ReportFormat.Month(bucket) : ReportFormat.Date(bucket);

    private static List<CategoryCount> IntegerHistogram(IReadOnlyCollection<double> values, long min, long max)
    {
        var span = max - min + 1;
        var width = (long)Math.Ceiling(span / (double)MaxHistogramBuckets);
        var bucketCount = (int)Math.Ceiling(span / (double)width);
        var counts = new int[bucketCount];
        foreach (var value in values)
        {
            counts[(int)((Math.Round(value) - min) / width)]++;
        }

        return Enumerable.Range(0, bucketCount).Select(i =>
        {
            var from = min + (i * width);
            var to = Math.Min(max, from + width - 1);
            var label = from == to ? ReportFormat.Value(from) : $"{ReportFormat.Value(from)}–{ReportFormat.Value(to)}";
            return new CategoryCount(label, counts[i]);
        }).ToList();
    }

    private static List<CategoryCount> DecimalHistogram(IReadOnlyCollection<double> values, double min, double max)
    {
        var width = (max - min) / MaxHistogramBuckets;
        var counts = new int[MaxHistogramBuckets];
        foreach (var value in values)
        {
            // The maximum belongs to the last (closed) bucket.
            counts[Math.Min(MaxHistogramBuckets - 1, (int)((value - min) / width))]++;
        }

        return Enumerable.Range(0, MaxHistogramBuckets)
            .Select(i => new CategoryCount(
                $"{ReportFormat.Value(min + (i * width))}–{ReportFormat.Value(i == MaxHistogramBuckets - 1 ? max : min + ((i + 1) * width))}",
                counts[i]))
            .ToList();
    }
}
