using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>Builds the presentation parts of a <see cref="WidgetResult"/> (tables, charts, KPI items).</summary>
internal static class WidgetOutput
{
    /// <summary>Series name used for single-series count charts.</summary>
    public const string ResponsesSeries = "Responses";

    /// <summary>Chart kind rendered for a widget type (null for non-chart widgets).</summary>
    public static ChartKind? ChartKindFor(WidgetType type) => type switch
    {
        WidgetType.BarChart => ChartKind.Bar,
        WidgetType.HorizontalBarChart => ChartKind.HorizontalBar,
        WidgetType.PieChart => ChartKind.Pie,
        WidgetType.DoughnutChart => ChartKind.Doughnut,
        WidgetType.LineChart => ChartKind.Line,
        WidgetType.CrossTab => ChartKind.StackedBar,
        _ => null,
    };

    /// <summary>
    /// Fills a distribution widget: question tables get the table only; chart widgets get the chart
    /// plus the data table when <see cref="WidgetSettings.ShowDataTable"/> is set.
    /// </summary>
    /// <param name="result">Widget to fill.</param>
    /// <param name="settings">Widget settings.</param>
    /// <param name="categoryHeader">Header of the category column (e.g. "Option", "Value", "Month").</param>
    /// <param name="rows">Categories in display order.</param>
    /// <param name="respondents">Denominator of the percentages (respondents who answered).</param>
    public static void ApplyDistribution(
        WidgetResult result, WidgetSettings settings, string categoryHeader, IReadOnlyList<CategoryCount> rows, int respondents)
    {
        var table = DistributionTable(categoryHeader, rows, respondents, settings.ShowPercentages);
        if (ChartKindFor(result.Type) is not { } kind)
        {
            result.Table = table;
            return;
        }

        result.Chart = new ChartData
        {
            Kind = kind,
            Labels = rows.Select(r => r.Label).ToList(),
            Series = [new ChartSeries { Name = ResponsesSeries, Values = rows.Select(r => (double)r.Count).ToList() }],
            ValueAxisTitle = kind is ChartKind.Pie or ChartKind.Doughnut ? null : ResponsesSeries,
        };
        result.Table = settings.ShowDataTable ? table : null;
    }

    /// <summary>
    /// Category / Count / Percent table with a total footer. Percentages are relative to the
    /// respondents who answered (so multiple-choice percentages can add up to more than 100%).
    /// </summary>
    public static TableData DistributionTable(string categoryHeader, IReadOnlyList<CategoryCount> rows, int respondents, bool showPercentages)
    {
        var table = new TableData
        {
            Columns = showPercentages ? [categoryHeader, "Count", "Percent"] : [categoryHeader, "Count"],
            NumericColumns = showPercentages ? [1, 2] : [1],
        };

        foreach (var row in rows)
        {
            table.Rows.Add(showPercentages
                ? [row.Label, ReportFormat.Count(row.Count), ReportFormat.Share(row.Count, respondents)]
                : [row.Label, ReportFormat.Count(row.Count)]);
        }

        table.Footer = showPercentages
            ? ["Total", ReportFormat.Count(respondents), ReportFormat.Percent(respondents > 0 ? 1 : 0)]
            : ["Total", ReportFormat.Count(respondents)];
        return table;
    }

    /// <summary>KPI items for numeric statistics.</summary>
    public static List<StatItem> StatItems(DescriptiveStats stats) =>
    [
        new("Count", ReportFormat.Count(stats.Count)),
        new("Mean", ReportFormat.Number(stats.Mean)),
        new("Median", ReportFormat.Number(stats.Median)),
        new("Min", ReportFormat.Number(stats.Min)),
        new("Max", ReportFormat.Number(stats.Max)),
        new("Std dev", ReportFormat.Number(stats.StdDev)),
    ];

    /// <summary>Two-column list table (e.g. submitted timestamp + answer).</summary>
    public static TableData ListTable(string firstHeader, string secondHeader, IEnumerable<(string First, string Second)> rows, string? title = null) => new()
    {
        Title = title,
        Columns = [firstHeader, secondHeader],
        Rows = rows.Select(r => new List<string> { r.First, r.Second }).ToList(),
    };

    /// <summary>"Showing the latest 100 of 340 answers." (null when nothing was cut).</summary>
    public static string? TruncationNote(int shown, int total, string noun) =>
        total > shown ? $"Showing the latest {ReportFormat.Count(shown)} of {ReportFormat.Count(total)} {noun}." : null;

    /// <summary>Joins non-empty notes into one line.</summary>
    public static string? JoinNotes(IEnumerable<string?> notes)
    {
        var parts = notes.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
