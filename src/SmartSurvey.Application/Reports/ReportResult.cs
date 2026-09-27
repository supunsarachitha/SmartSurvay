using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Reports;

/// <summary>
/// Output of the report engine: a presentation-neutral model that the Blazor viewer renders and
/// every exporter (PDF, CSV, TXT, XLSX, JSON) serialises. Nothing in here is persisted.
/// </summary>
public sealed class ReportResult
{
    /// <summary>Report id (null for unsaved previews).</summary>
    public Guid? ReportId { get; set; }

    /// <summary>Report name.</summary>
    public string ReportName { get; set; } = string.Empty;

    /// <summary>Report description.</summary>
    public string? Description { get; set; }

    /// <summary>Survey id.</summary>
    public Guid SurveyId { get; set; }

    /// <summary>Survey title.</summary>
    public string SurveyTitle { get; set; } = string.Empty;

    /// <summary>UTC generation timestamp.</summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>Number of responses after filtering.</summary>
    public int TotalResponses { get; set; }

    /// <summary>Human-readable description of the applied filters (one line per filter).</summary>
    public List<string> FilterSummary { get; set; } = [];

    /// <summary>Computed widgets in display order.</summary>
    public List<WidgetResult> Widgets { get; set; } = [];
}

/// <summary>A computed widget. Any combination of <see cref="Stats"/>, <see cref="Chart"/> and <see cref="Table"/> may be present.</summary>
public sealed class WidgetResult
{
    /// <summary>Widget id.</summary>
    public Guid WidgetId { get; set; }

    /// <summary>Title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Widget type.</summary>
    public WidgetType Type { get; set; }

    /// <summary>Source question text (if any).</summary>
    public string? QuestionText { get; set; }

    /// <summary>Number of responses that answered the question (or total responses).</summary>
    public int ResponseCount { get; set; }

    /// <summary>KPI tiles / numeric statistics.</summary>
    public List<StatItem> Stats { get; set; } = [];

    /// <summary>Chart data (chart widgets).</summary>
    public ChartData? Chart { get; set; }

    /// <summary>Primary data table.</summary>
    public TableData? Table { get; set; }

    /// <summary>Additional tables, e.g. "Other" free-text answers under a choice distribution.</summary>
    public List<TableData> ExtraTables { get; set; } = [];

    /// <summary>Informational note (e.g. "Showing first 100 of 340 answers").</summary>
    public string? Note { get; set; }

    /// <summary>Error message when the widget could not be computed (e.g. its question was deleted).</summary>
    public string? Error { get; set; }
}

/// <summary>A label/value KPI.</summary>
/// <param name="Label">Label, e.g. "Completion rate".</param>
/// <param name="Value">Formatted value, e.g. "87.5 %".</param>
public sealed record StatItem(string Label, string Value);

/// <summary>A simple table: header + rows of pre-formatted cells (invariant culture).</summary>
public sealed class TableData
{
    /// <summary>Optional caption.</summary>
    public string? Title { get; set; }

    /// <summary>Column headers.</summary>
    public List<string> Columns { get; set; } = [];

    /// <summary>Rows; each row has <see cref="Columns"/>.Count cells.</summary>
    public List<List<string>> Rows { get; set; } = [];

    /// <summary>Optional totals row.</summary>
    public List<string>? Footer { get; set; }

    /// <summary>
    /// Zero-based indexes of numeric columns (right-aligned in renderers, written as numbers in XLSX).
    /// </summary>
    public List<int> NumericColumns { get; set; } = [];
}

/// <summary>Chart types supported by the SVG renderer.</summary>
public enum ChartKind
{
    /// <summary>Vertical bars (supports multiple series = grouped bars).</summary>
    Bar = 0,

    /// <summary>Horizontal bars.</summary>
    HorizontalBar = 1,

    /// <summary>Pie (first series only).</summary>
    Pie = 2,

    /// <summary>Doughnut (first series only).</summary>
    Doughnut = 3,

    /// <summary>Line (supports multiple series).</summary>
    Line = 4,

    /// <summary>Stacked vertical bars (cross-tab).</summary>
    StackedBar = 5,
}

/// <summary>Chart data: category labels plus one or more numeric series.</summary>
public sealed class ChartData
{
    /// <summary>Chart type.</summary>
    public ChartKind Kind { get; set; }

    /// <summary>Category labels (x-axis / slices).</summary>
    public List<string> Labels { get; set; } = [];

    /// <summary>Series; each has <see cref="Labels"/>.Count values.</summary>
    public List<ChartSeries> Series { get; set; } = [];

    /// <summary>Optional axis title for values.</summary>
    public string? ValueAxisTitle { get; set; }
}

/// <summary>A named series of values.</summary>
public sealed class ChartSeries
{
    /// <summary>Series name (legend).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Values aligned with <see cref="ChartData.Labels"/>.</summary>
    public List<double> Values { get; set; } = [];
}
