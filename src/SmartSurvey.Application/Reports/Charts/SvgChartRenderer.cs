using System.Xml.Linq;

namespace SmartSurvey.Application.Reports.Charts;

/// <summary>
/// Server-side SVG chart renderer (bar, horizontal bar, stacked bar, line, pie and doughnut).
/// </summary>
/// <remarks>
/// <para>The renderer is pure and deterministic: the same data and options always produce the same
/// markup, which keeps it trivially unit-testable and lets the Blazor viewer and the PDF exporter
/// show identical charts.</para>
/// <para>Output rules: a single &lt;svg&gt; root with <c>viewBox</c>, <c>width</c> and <c>height</c>;
/// presentation attributes only (no CSS, classes, filters or foreignObject) so Skia can embed it in
/// PDFs; every text is XML-escaped; long labels are truncated with an ellipsis and carry a
/// &lt;title&gt; tooltip with the full text; empty data renders a "No data" placeholder.</para>
/// </remarks>
public sealed partial class SvgChartRenderer : ISvgChartRenderer
{
    private const double LabelFontSize = 12;
    private const double TickFontSize = 11;
    private const double ValueFontSize = 11;
    private const double LegendFontSize = 12;
    private const double LegendRowHeight = 20;
    private const double LegendSwatch = 10;
    private const int LegendMaxChars = 28;
    private const double OuterPadding = 16;
    private const int MinCanvasSize = 120;
    private const int MaxCanvasSize = 4000;
    private const string NoDataText = "No data";

    /// <inheritdoc />
    public string Render(ChartData data, SvgChartOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var o = Normalize(options);
        var chart = PreparedChart.From(data);

        if (chart.IsEmpty)
        {
            return RenderPlaceholder(o);
        }

        return data.Kind switch
        {
            ChartKind.Pie => RenderCircular(chart, o, doughnut: false),
            ChartKind.Doughnut => RenderCircular(chart, o, doughnut: true),
            ChartKind.HorizontalBar => RenderHorizontalBars(chart, o),
            ChartKind.Line => RenderVertical(chart, o, ChartKind.Line),
            ChartKind.StackedBar => RenderVertical(chart, o, ChartKind.StackedBar),
            _ => RenderVertical(chart, o, ChartKind.Bar),
        };
    }

    /// <summary>
    /// Copies the options, clamping the size to sane bounds and falling back to defaults for empty
    /// colours / palette so a bad configuration can never produce invalid output.
    /// </summary>
    private static SvgChartOptions Normalize(SvgChartOptions? options)
    {
        var source = options ?? new SvgChartOptions();
        var defaults = new SvgChartOptions();
        return new SvgChartOptions
        {
            Width = Math.Clamp(source.Width, MinCanvasSize, MaxCanvasSize),
            Height = Math.Clamp(source.Height, MinCanvasSize, MaxCanvasSize),
            ShowLegend = source.ShowLegend,
            ShowValues = source.ShowValues,
            ShowPercentages = source.ShowPercentages,
            FontFamily = string.IsNullOrWhiteSpace(source.FontFamily) ? defaults.FontFamily : source.FontFamily,
            TextColor = string.IsNullOrWhiteSpace(source.TextColor) ? defaults.TextColor : source.TextColor,
            GridColor = string.IsNullOrWhiteSpace(source.GridColor) ? defaults.GridColor : source.GridColor,
            Palette = source.Palette is { Count: > 0 } palette && palette.All(c => !string.IsNullOrWhiteSpace(c))
                ? palette
                : ChartPalette.Default,
        };
    }

    /// <summary>Placeholder shown for empty data (dashed frame + centred "No data").</summary>
    private static string RenderPlaceholder(SvgChartOptions o)
    {
        var canvas = new SvgCanvas(o.Width, o.Height, o);
        var frame = canvas.Rect(canvas.Root, 1, 1, o.Width - 2, o.Height - 2, "none", 8);
        frame.Add(
            new XAttribute("stroke", o.GridColor),
            new XAttribute("stroke-width", "1.5"),
            new XAttribute("stroke-dasharray", "6 4"));
        var text = canvas.Text(canvas.Root, o.Width / 2.0, (o.Height / 2.0) + 5, NoDataText, 14, "middle");
        text.Add(new XAttribute("fill-opacity", "0.7"));
        return canvas.ToSvg();
    }

    /// <summary>Colour of the n-th series / slice (palette cycles).</summary>
    private static string Color(SvgChartOptions o, int index) => o.Palette[index % o.Palette.Count];

    /// <summary>
    /// Lays out legend entries left-to-right, wrapping into rows that fit <paramref name="availableWidth"/>.
    /// </summary>
    private static List<List<LegendEntry>> LayoutLegendRows(IReadOnlyList<string> names, double availableWidth)
    {
        var rows = new List<List<LegendEntry>> { new() };
        var rowWidth = 0.0;
        for (var i = 0; i < names.Count; i++)
        {
            var label = ChartText.Truncate(names[i], LegendMaxChars);
            var width = LegendSwatch + 6 + ChartText.Width(label, LegendFontSize) + 18;
            if (rowWidth > 0 && rowWidth + width > availableWidth)
            {
                rows.Add([]);
                rowWidth = 0;
            }

            rows[^1].Add(new LegendEntry(i, label, names[i], width));
            rowWidth += width;
        }

        return rows;
    }

    /// <summary>Height needed by a horizontal legend (0 when there is no legend).</summary>
    private static double LegendHeight(List<List<LegendEntry>>? rows) =>
        rows is null ? 0 : (rows.Count * LegendRowHeight) + 8;

    /// <summary>Draws a horizontal (wrapping, centred) legend starting at <paramref name="top"/>.</summary>
    private static void DrawLegendRows(SvgCanvas canvas, SvgChartOptions o, List<List<LegendEntry>> rows, double top)
    {
        var group = canvas.Group();
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var x = (canvas.Width - row.Sum(e => e.Width) + 18) / 2;
            var y = top + (r * LegendRowHeight);
            foreach (var entry in row)
            {
                canvas.Rect(group, x, y + 4, LegendSwatch, LegendSwatch, Color(o, entry.Index), 2);
                canvas.Text(group, x + LegendSwatch + 6, y + 13, entry.Label, LegendFontSize, fullText: entry.FullLabel);
                x += entry.Width;
            }
        }
    }

    /// <summary>
    /// Decides how category labels under a vertical chart are drawn: horizontally (truncated to the
    /// band width) when that leaves enough characters, otherwise rotated by 45° and thinned out so
    /// neighbouring labels never overlap.
    /// </summary>
    private static CategoryAxisLayout LayoutCategoryAxis(IReadOnlyList<string> labels, double band)
    {
        var longest = labels.Count == 0 ? 0 : labels.Max(l => l.Length);
        var fitting = ChartText.CharsThatFit(band - 6, LabelFontSize);
        if (fitting >= Math.Min(longest, 8))
        {
            return new CategoryAxisLayout(false, Math.Max(fitting, 1), 1, LabelFontSize + 14);
        }

        const int rotatedMaxChars = 16;
        var chars = Math.Min(longest, rotatedMaxChars);
        var step = Math.Max(1, (int)Math.Ceiling((LabelFontSize + 4) / band));
        // A 45° rotated label needs sin(45°) × its length vertically.
        var height = (ChartText.Width(new string('x', chars), LabelFontSize) * 0.72) + LabelFontSize + 10;
        return new CategoryAxisLayout(true, chars, step, height);
    }

    /// <summary>Draws category labels centred under each band (rotated when the layout says so).</summary>
    private static void DrawCategoryLabels(
        SvgCanvas canvas, PreparedChart chart, CategoryAxisLayout layout, double left, double band, double baselineY)
    {
        var group = canvas.Group();
        for (var i = 0; i < chart.Labels.Count; i += layout.Step)
        {
            var x = left + (band * i) + (band / 2);
            var label = ChartText.Truncate(chart.Labels[i], layout.MaxChars);
            if (layout.Rotated)
            {
                var y = baselineY + 14;
                var text = canvas.Text(group, x, y, label, LabelFontSize, "end", fullText: chart.Labels[i]);
                text.Add(new XAttribute("transform", $"rotate(-45 {SvgCanvas.Num(x)} {SvgCanvas.Num(y)})"));
            }
            else
            {
                canvas.Text(group, x, baselineY + 18, label, LabelFontSize, "middle", fullText: chart.Labels[i]);
            }
        }
    }

    /// <summary>A legend entry with its measured width.</summary>
    private sealed record LegendEntry(int Index, string Label, string FullLabel, double Width);

    /// <summary>How category labels are drawn.</summary>
    /// <param name="Rotated">Labels rotated by -45°.</param>
    /// <param name="MaxChars">Maximum visible characters per label.</param>
    /// <param name="Step">Draw every n-th label.</param>
    /// <param name="Height">Vertical space needed below the plot.</param>
    private sealed record CategoryAxisLayout(bool Rotated, int MaxChars, int Step, double Height);

    /// <summary>
    /// Sanitised chart data: labels cleaned, every series padded/truncated to the label count and
    /// non-finite values replaced by zero.
    /// </summary>
    private sealed class PreparedChart
    {
        private PreparedChart(List<string> labels, List<string> seriesNames, List<double[]> values, string valueAxisTitle)
        {
            Labels = labels;
            SeriesNames = seriesNames;
            Values = values;
            ValueAxisTitle = valueAxisTitle;
        }

        /// <summary>Category labels.</summary>
        public List<string> Labels { get; }

        /// <summary>Series names (never empty strings).</summary>
        public List<string> SeriesNames { get; }

        /// <summary>Values per series, aligned with <see cref="Labels"/>.</summary>
        public List<double[]> Values { get; }

        /// <summary>Value axis title (empty when none).</summary>
        public string ValueAxisTitle { get; }

        /// <summary>True when there is nothing to draw.</summary>
        public bool IsEmpty => Labels.Count == 0 || Values.Count == 0;

        /// <summary>True when every value is a whole number (e.g. counts).</summary>
        public bool AllIntegers => Values.All(s => s.All(v => Math.Abs(v - Math.Round(v)) < 1e-9));

        /// <summary>Sanitises raw chart data.</summary>
        public static PreparedChart From(ChartData data)
        {
            var labels = (data.Labels ?? []).Select(l => ChartText.Clean(l)).ToList();
            var series = (data.Series ?? []).Where(s => s is not null).ToList();
            var names = series
                .Select((s, i) => ChartText.Clean(s.Name) is { Length: > 0 } name ? name : $"Series {i + 1}")
                .ToList();
            var values = series
                .Select(s => Enumerable.Range(0, labels.Count)
                    .Select(i => s.Values is not null && i < s.Values.Count ? AxisScale.SafeValue(s.Values[i]) : 0)
                    .ToArray())
                .ToList();
            return new PreparedChart(labels, names, values, ChartText.Clean(data.ValueAxisTitle));
        }
    }
}
