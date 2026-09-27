using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>
/// Computes question-based widgets (question table, bar, horizontal bar, pie and doughnut charts):
/// option distributions for choice questions, zero-filled value distributions plus statistics for
/// ratings/scales, histograms for numbers, monthly distributions for dates and answer lists for text.
/// </summary>
internal static class QuestionWidgetBuilder
{
    /// <summary>Error for chart widgets on text questions.</summary>
    public const string TextChartError =
        "Charts are not available for text questions. Use a Text responses widget (or a question table) instead.";

    /// <summary>Maximum months listed for date questions before zero-filling is skipped.</summary>
    private const int MaxZeroFilledMonths = 240;

    /// <summary>Fills <paramref name="result"/> for a question widget.</summary>
    public static async Task BuildAsync(ReportRunContext ctx, QuestionDto question, WidgetSettings settings, WidgetResult result)
    {
        var type = question.Type;
        if (type.IsText())
        {
            await BuildTextAsync(ctx, question, settings, result);
        }
        else if (type.IsChoice())
        {
            await BuildChoiceAsync(ctx, question, settings, result);
        }
        else if (type.IsDate())
        {
            await BuildDateAsync(ctx, question, settings, result);
        }
        else if (type is QuestionType.Rating or QuestionType.Scale)
        {
            await BuildScaleAsync(ctx, question, settings, result);
        }
        else
        {
            await BuildNumberAsync(ctx, question, settings, result);
        }
    }

    /// <summary>
    /// Option distribution: counts per option, percentages of the respondents who answered, sort
    /// order and top-N, plus (optionally) the free texts of "Other"-style options.
    /// </summary>
    private static async Task BuildChoiceAsync(ReportRunContext ctx, QuestionDto question, WidgetSettings settings, WidgetResult result)
    {
        var respondents = await ReportAnswerQueries.AnsweredCountAsync(ctx, question);
        var counts = await ReportAnswerQueries.ChoiceCountsAsync(ctx, question.Id);
        var options = question.Options.OrderBy(o => o.Order).ToList();
        var rows = options.Select(o => new CategoryCount(o.Text, counts.GetValueOrDefault(o.Id))).ToList();

        var notes = new List<string?>();
        if (question.Type.IsMultiSelect())
        {
            notes.Add("Respondents could select more than one option, so percentages can add up to more than 100%.");
        }

        if (settings.TopN is { } topN && topN > 0 && rows.Count > topN)
        {
            var total = rows.Count;
            rows = KeepLargest(rows, topN);
            notes.Add($"Showing the top {topN} of {total} options ({total - topN} hidden); the total includes every option.");
        }

        rows = Sort(rows, settings.SortOrder);
        result.ResponseCount = respondents;
        WidgetOutput.ApplyDistribution(result, settings, "Option", rows, respondents);

        if (settings.IncludeFreeText)
        {
            var freeTextTable = await FreeTextTableAsync(ctx, question, settings.MaxRows, notes);
            if (freeTextTable is not null)
            {
                result.ExtraTables.Add(freeTextTable);
            }
        }

        result.Note = WidgetOutput.JoinNotes(notes);
    }

    /// <summary>Free texts of AllowsFreeText options (Option, Answer; latest first).</summary>
    private static async Task<TableData?> FreeTextTableAsync(ReportRunContext ctx, QuestionDto question, int maxRows, List<string?> notes)
    {
        var freeTextOptions = question.Options.Where(o => o.AllowsFreeText).ToDictionary(o => o.Id, o => o.Text);
        if (freeTextOptions.Count == 0)
        {
            return null;
        }

        var page = await ReportAnswerQueries.LatestFreeTextsAsync(ctx, question.Id, freeTextOptions.Keys, maxRows);
        notes.Add(WidgetOutput.TruncationNote(page.Items.Count, page.Total, "free-text answers"));
        return new TableData
        {
            Title = "Free-text answers",
            Columns = ["Option", "Answer"],
            Rows = page.Items.Select(t => new List<string> { freeTextOptions.GetValueOrDefault(t.OptionId ?? Guid.Empty, string.Empty), t.Text }).ToList(),
        };
    }

    /// <summary>Rating / scale: every integer value of the range (zero-filled) plus statistics (and NPS for 0–10 scales).</summary>
    private static async Task BuildScaleAsync(ReportRunContext ctx, QuestionDto question, WidgetSettings settings, WidgetResult result)
    {
        var values = await ReportAnswerQueries.NumberValuesAsync(ctx, question.Id);
        var (min, max) = question.Type == QuestionType.Rating
            ? (1, Math.Max(1, question.Settings.RatingMax))
            : (question.Settings.ScaleMin, question.Settings.ScaleMax);

        result.ResponseCount = values.Count;
        if (ReportStatistics.Describe(values) is { } stats)
        {
            result.Stats = WidgetOutput.StatItems(stats);
            if (question.Type == QuestionType.Scale && min == 0 && max == 10)
            {
                result.Stats.Add(new StatItem("NPS", ReportFormat.Number(ReportStatistics.NetPromoterScore(values))));
            }
        }

        WidgetOutput.ApplyDistribution(result, settings, "Value", ReportStatistics.IntegerDistribution(values, min, max), values.Count);
    }

    /// <summary>Number: statistics plus a histogram with at most ten equal-width buckets.</summary>
    private static async Task BuildNumberAsync(ReportRunContext ctx, QuestionDto question, WidgetSettings settings, WidgetResult result)
    {
        var values = await ReportAnswerQueries.NumberValuesAsync(ctx, question.Id);
        result.ResponseCount = values.Count;
        if (ReportStatistics.Describe(values) is { } stats)
        {
            result.Stats = WidgetOutput.StatItems(stats);
        }

        WidgetOutput.ApplyDistribution(result, settings, "Range", ReportStatistics.Histogram(values), values.Count);
    }

    /// <summary>Date: earliest/latest plus answers per month (zero-filled between the first and last month).</summary>
    private static async Task BuildDateAsync(ReportRunContext ctx, QuestionDto question, WidgetSettings settings, WidgetResult result)
    {
        var dates = await ReportAnswerQueries.DateValuesAsync(ctx, question.Id);
        result.ResponseCount = dates.Count;

        var rows = new List<CategoryCount>();
        if (dates.Count > 0)
        {
            result.Stats =
            [
                new StatItem("Count", ReportFormat.Count(dates.Count)),
                new StatItem("Earliest", ReportFormat.Date(dates.Min())),
                new StatItem("Latest", ReportFormat.Date(dates.Max())),
            ];
            rows = MonthlyDistribution(dates);
        }

        WidgetOutput.ApplyDistribution(result, settings, "Month", rows, dates.Count);
    }

    /// <summary>Text: answer count and the latest answers (charts are not meaningful for free text).</summary>
    private static async Task BuildTextAsync(ReportRunContext ctx, QuestionDto question, WidgetSettings settings, WidgetResult result)
    {
        if (result.Type.IsChart())
        {
            result.Error = TextChartError;
            return;
        }

        // A question table on a text question is the same list as a Text responses widget.
        await ResponseListBuilder.BuildTextAsync(ctx, question, settings, result);
    }

    /// <summary>Answers per month; months without answers are listed with zero unless the span is huge.</summary>
    private static List<CategoryCount> MonthlyDistribution(IReadOnlyCollection<DateOnly> dates)
    {
        var counts = dates.GroupBy(d => new DateOnly(d.Year, d.Month, 1)).ToDictionary(g => g.Key, g => g.Count());
        var first = counts.Keys.Min();
        var last = counts.Keys.Max();
        var span = ((last.Year - first.Year) * 12) + last.Month - first.Month + 1;
        if (span > MaxZeroFilledMonths)
        {
            return counts.OrderBy(c => c.Key).Select(c => new CategoryCount(ReportFormat.Month(c.Key), c.Value)).ToList();
        }

        return Enumerable.Range(0, span)
            .Select(i => first.AddMonths(i))
            .Select(m => new CategoryCount(ReportFormat.Month(m), counts.GetValueOrDefault(m)))
            .ToList();
    }

    /// <summary>Keeps the <paramref name="n"/> most frequent categories (ties keep design order) in their original order.</summary>
    private static List<CategoryCount> KeepLargest(List<CategoryCount> rows, int n) =>
        rows
            .Select((row, index) => (row, index))
            .OrderByDescending(x => x.row.Count)
            .ThenBy(x => x.index)
            .Take(n)
            .OrderBy(x => x.index)
            .Select(x => x.row)
            .ToList();

    /// <summary>Applies the widget's category ordering (stable, so ties keep design order).</summary>
    private static List<CategoryCount> Sort(List<CategoryCount> rows, WidgetSortOrder order) => order switch
    {
        WidgetSortOrder.CountDescending => rows.OrderByDescending(r => r.Count).ToList(),
        WidgetSortOrder.CountAscending => rows.OrderBy(r => r.Count).ToList(),
        WidgetSortOrder.LabelAscending => rows.OrderBy(r => r.Label, StringComparer.InvariantCultureIgnoreCase).ToList(),
        _ => rows,
    };
}
