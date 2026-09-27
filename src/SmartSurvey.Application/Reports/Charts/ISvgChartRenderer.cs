namespace SmartSurvey.Application.Reports.Charts;

/// <summary>
/// Renders <see cref="ChartData"/> as a standalone SVG document string. The same SVG is inlined in
/// the Blazor report viewer and embedded in PDF exports (QuestPDF <c>.Svg()</c>), so charts look
/// identical on screen and on paper. Implementations must escape all text (XSS-safe) and produce
/// valid SVG for empty data (a "No data" placeholder).
/// </summary>
public interface ISvgChartRenderer
{
    /// <summary>Renders the chart.</summary>
    string Render(ChartData data, SvgChartOptions? options = null);
}

/// <summary>Rendering options.</summary>
public sealed class SvgChartOptions
{
    /// <summary>Width in user units (the SVG scales responsively via viewBox).</summary>
    public int Width { get; set; } = 640;

    /// <summary>Height in user units.</summary>
    public int Height { get; set; } = 360;

    /// <summary>Show a legend (multi-series, pie and doughnut charts).</summary>
    public bool ShowLegend { get; set; } = true;

    /// <summary>Print values on bars / slices.</summary>
    public bool ShowValues { get; set; } = true;

    /// <summary>Show percentages on pie/doughnut slices instead of raw values.</summary>
    public bool ShowPercentages { get; set; } = true;

    /// <summary>Font family used for all text.</summary>
    public string FontFamily { get; set; } = "Segoe UI, Helvetica, Arial, sans-serif";

    /// <summary>Text colour (hex).</summary>
    public string TextColor { get; set; } = "#334155";

    /// <summary>Grid line colour (hex).</summary>
    public string GridColor { get; set; } = "#e2e8f0";

    /// <summary>Series / slice colours (hex), cycled when there are more categories.</summary>
    public IReadOnlyList<string> Palette { get; set; } = ChartPalette.Default;
}

/// <summary>Colour palettes.</summary>
public static class ChartPalette
{
    /// <summary>Accessible, professional default palette (indigo-first to match the UI theme).</summary>
    public static readonly IReadOnlyList<string> Default =
    [
        "#4f46e5", "#0ea5e9", "#10b981", "#f59e0b", "#ef4444",
        "#8b5cf6", "#14b8a6", "#f97316", "#64748b", "#ec4899",
    ];
}
