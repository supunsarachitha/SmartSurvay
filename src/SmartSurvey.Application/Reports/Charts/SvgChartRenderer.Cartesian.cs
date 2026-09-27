using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace SmartSurvey.Application.Reports.Charts;

// Cartesian charts: vertical (grouped) bars, stacked bars, lines and horizontal bars.
public sealed partial class SvgChartRenderer
{
    private const double PlotTop = 22;          // headroom for value labels above the tallest bar
    private const double MinPlotSize = 40;
    private const int MaxLineMarkers = 60;      // more points than this → plain line without markers
    private const int MaxLineValueLabels = 24;  // more points than this → no value labels on lines

    /// <summary>Renders bar, stacked bar and line charts (categories along the x axis).</summary>
    private static string RenderVertical(PreparedChart chart, SvgChartOptions o, ChartKind kind)
    {
        var stacked = kind == ChartKind.StackedBar;
        var scale = stacked
            ? AxisScale.Create(0, MaxStackTotal(chart), chart.AllIntegers)
            : AxisScale.Create(chart.Values.Min(s => s.Min()), chart.Values.Max(s => s.Max()), chart.AllIntegers);
        var legendRows = o.ShowLegend && chart.Values.Count > 1
            ? LayoutLegendRows(chart.SeriesNames, o.Width - (2 * OuterPadding))
            : null;

        var tickWidth = scale.Ticks().Max(t => ChartText.Width(AxisScale.FormatTick(t), TickFontSize));
        var axisTitle = chart.ValueAxisTitle;
        var left = OuterPadding + tickWidth + 8 + (axisTitle.Length > 0 ? 18 : 0);
        var plotWidth = Math.Max(MinPlotSize, o.Width - left - OuterPadding);
        var band = plotWidth / chart.Labels.Count;
        var axis = LayoutCategoryAxis(chart.Labels, band);
        var legendHeight = LegendHeight(legendRows);
        var plotHeight = Math.Max(MinPlotSize, o.Height - PlotTop - axis.Height - legendHeight - 8);

        var canvas = new SvgCanvas(o.Width, PlotTop + plotHeight + axis.Height + legendHeight + 8, o);
        var plot = new PlotArea(left, PlotTop, plotWidth, plotHeight);
        DrawHorizontalGrid(canvas, o, scale, plot, axisTitle);

        switch (kind)
        {
            case ChartKind.StackedBar:
                DrawStackedBars(canvas, o, chart, scale, plot, band);
                break;
            case ChartKind.Line:
                DrawLines(canvas, o, chart, scale, plot, band);
                break;
            default:
                DrawGroupedBars(canvas, o, chart, scale, plot, band);
                break;
        }

        DrawCategoryLabels(canvas, chart, axis, plot.Left, band, plot.Bottom);
        if (legendRows is not null)
        {
            DrawLegendRows(canvas, o, legendRows, plot.Bottom + axis.Height + 4);
        }

        return canvas.ToSvg();
    }

    /// <summary>Largest stacked total (negative values are ignored in stacks).</summary>
    private static double MaxStackTotal(PreparedChart chart) =>
        Enumerable.Range(0, chart.Labels.Count).Max(i => chart.Values.Sum(s => Math.Max(0, s[i])));

    /// <summary>Horizontal grid lines, tick labels, zero line and optional rotated axis title.</summary>
    private static void DrawHorizontalGrid(SvgCanvas canvas, SvgChartOptions o, AxisScale scale, PlotArea plot, string axisTitle)
    {
        var group = canvas.Group();
        foreach (var tick in scale.Ticks())
        {
            var y = plot.Y(scale, tick);
            canvas.Line(group, plot.Left, y, plot.Right, y, o.GridColor);
            var label = canvas.Text(group, plot.Left - 8, y + 4, AxisScale.FormatTick(tick), TickFontSize, "end");
            label.Add(new XAttribute("fill-opacity", "0.8"));
        }

        var zero = plot.Y(scale, 0);
        canvas.Line(group, plot.Left, zero, plot.Right, zero, o.TextColor).Add(new XAttribute("stroke-opacity", "0.35"));

        if (axisTitle.Length > 0)
        {
            var x = OuterPadding;
            var y = plot.Top + (plot.Height / 2);
            var title = ChartText.Truncate(axisTitle, ChartText.CharsThatFit(plot.Height, TickFontSize));
            var text = canvas.Text(group, x, y, title, TickFontSize, "middle", fullText: axisTitle);
            text.Add(new XAttribute("transform", $"rotate(-90 {SvgCanvas.Num(x)} {SvgCanvas.Num(y)})"));
        }
    }

    /// <summary>Side-by-side bars per category (one bar per series).</summary>
    private static void DrawGroupedBars(SvgCanvas canvas, SvgChartOptions o, PreparedChart chart, AxisScale scale, PlotArea plot, double band)
    {
        var seriesCount = chart.Values.Count;
        var groupWidth = band * (seriesCount > 1 ? 0.8 : 0.62);
        var barWidth = groupWidth / seriesCount;
        var gap = seriesCount > 1 ? Math.Min(2, barWidth * 0.1) : 0;
        var showValues = o.ShowValues && barWidth >= 14;
        var zero = plot.Y(scale, 0);
        var bars = canvas.Group();
        var labels = canvas.Group();

        for (var i = 0; i < chart.Labels.Count; i++)
        {
            var groupLeft = plot.Left + (band * i) + ((band - groupWidth) / 2);
            for (var s = 0; s < seriesCount; s++)
            {
                var value = chart.Values[s][i];
                var y = plot.Y(scale, value);
                var top = Math.Min(y, zero);
                var height = Math.Abs(zero - y);
                var x = groupLeft + (s * barWidth);
                var rect = canvas.Rect(bars, x + (gap / 2), top, barWidth - gap, height, Color(o, s), Math.Min(3, barWidth / 4));
                SvgCanvas.AddTooltip(rect, Tooltip(chart, s, i));

                if (showValues)
                {
                    var labelY = value >= 0 ? top - 5 : top + height + ValueFontSize + 2;
                    canvas.Text(labels, x + (barWidth / 2), labelY, ChartText.FormatValue(value), ValueFontSize, "middle");
                }
            }
        }
    }

    /// <summary>Stacked bars per category with segment values and totals.</summary>
    private static void DrawStackedBars(SvgCanvas canvas, SvgChartOptions o, PreparedChart chart, AxisScale scale, PlotArea plot, double band)
    {
        var width = band * 0.62;
        var bars = canvas.Group();
        var labels = canvas.Group();

        for (var i = 0; i < chart.Labels.Count; i++)
        {
            var x = plot.Left + (band * i) + ((band - width) / 2);
            var cumulative = 0.0;
            for (var s = 0; s < chart.Values.Count; s++)
            {
                var value = Math.Max(0, chart.Values[s][i]);
                if (value <= 0)
                {
                    continue;
                }

                var top = plot.Y(scale, cumulative + value);
                var bottom = plot.Y(scale, cumulative);
                var rect = canvas.Rect(bars, x, top, width, bottom - top, Color(o, s));
                SvgCanvas.AddTooltip(rect, Tooltip(chart, s, i));

                // Segment value inside the segment (white on colour) when there is room for it.
                if (o.ShowValues && bottom - top >= 14 && width >= 18)
                {
                    canvas.Text(labels, x + (width / 2), ((top + bottom) / 2) + 4, ChartText.FormatValue(value), ValueFontSize, "middle", "#ffffff");
                }

                cumulative += value;
            }

            if (o.ShowValues && width >= 14 && cumulative > 0)
            {
                canvas.Text(labels, x + (width / 2), plot.Y(scale, cumulative) - 5, ChartText.FormatValue(cumulative), ValueFontSize, "middle", bold: true);
            }
        }
    }

    /// <summary>One polyline per series with point markers (and a soft area for single series).</summary>
    private static void DrawLines(SvgCanvas canvas, SvgChartOptions o, PreparedChart chart, AxisScale scale, PlotArea plot, double band)
    {
        var count = chart.Labels.Count;
        double X(int i) => plot.Left + (band * i) + (band / 2);

        for (var s = 0; s < chart.Values.Count; s++)
        {
            var color = Color(o, s);
            var group = canvas.Group();
            var line = new StringBuilder();
            for (var i = 0; i < count; i++)
            {
                line.Append(i == 0 ? "M" : " L").Append(SvgCanvas.Num(X(i))).Append(' ').Append(SvgCanvas.Num(plot.Y(scale, chart.Values[s][i])));
            }

            if (chart.Values.Count == 1 && count > 1)
            {
                var zero = SvgCanvas.Num(plot.Y(scale, 0));
                var area = $"{line} L{SvgCanvas.Num(X(count - 1))} {zero} L{SvgCanvas.Num(X(0))} {zero} Z";
                canvas.Path(group, area, color).Add(new XAttribute("fill-opacity", "0.12"));
            }

            var path = canvas.Path(group, line.ToString(), "none", color, 2.5);
            path.Add(new XAttribute("stroke-linejoin", "round"), new XAttribute("stroke-linecap", "round"));

            DrawLineMarkers(canvas, o, chart, scale, plot, group, s, X);
        }
    }

    /// <summary>Point markers with tooltips and (for short series) value labels.</summary>
    private static void DrawLineMarkers(
        SvgCanvas canvas, SvgChartOptions o, PreparedChart chart, AxisScale scale, PlotArea plot, XElement group, int series, Func<int, double> x)
    {
        var count = chart.Labels.Count;
        if (count > MaxLineMarkers)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var y = plot.Y(scale, chart.Values[series][i]);
            var marker = canvas.Circle(group, x(i), y, 3.5, Color(o, series));
            marker.Add(new XAttribute("stroke", "#ffffff"), new XAttribute("stroke-width", "1.5"));
            SvgCanvas.AddTooltip(marker, Tooltip(chart, series, i));

            if (o.ShowValues && count <= MaxLineValueLabels)
            {
                canvas.Text(group, x(i), y - 8, ChartText.FormatValue(chart.Values[series][i]), ValueFontSize, "middle");
            }
        }
    }

    /// <summary>Renders horizontal bars (categories along the y axis; grows taller for many categories).</summary>
    private static string RenderHorizontalBars(PreparedChart chart, SvgChartOptions o)
    {
        var seriesCount = chart.Values.Count;
        var count = chart.Labels.Count;
        var scale = AxisScale.Create(chart.Values.Min(s => s.Min()), chart.Values.Max(s => s.Max()), chart.AllIntegers);
        var legendRows = o.ShowLegend && seriesCount > 1
            ? LayoutLegendRows(chart.SeriesNames, o.Width - (2 * OuterPadding))
            : null;

        var longestLabel = chart.Labels.Max(l => ChartText.Width(l, LabelFontSize));
        var labelArea = Math.Clamp(longestLabel + 12, 60, o.Width * 0.35);
        var maxAbs = chart.Values.Max(s => s.Max(Math.Abs));
        var valueArea = o.ShowValues ? ChartText.Width(ChartText.FormatValue(maxAbs), ValueFontSize) + 12 : 0;
        var left = OuterPadding + labelArea;
        var plotWidth = Math.Max(MinPlotSize, o.Width - left - OuterPadding - valueArea);

        const double top = 12;
        var axisHeight = TickFontSize + 16;
        var legendHeight = LegendHeight(legendRows);
        var chrome = top + axisHeight + legendHeight + 8;
        // Each category needs a readable band; the canvas grows instead of squashing labels together.
        var minBand = Math.Max(22, seriesCount * 12);
        var plotHeight = Math.Clamp(Math.Max(o.Height - chrome, count * minBand), MinPlotSize, MaxCanvasSize - chrome);
        var band = plotHeight / count;

        var canvas = new SvgCanvas(o.Width, chrome + plotHeight, o);
        var plot = new PlotArea(left, top, plotWidth, plotHeight);
        DrawVerticalGrid(canvas, o, scale, plot);
        DrawHorizontalBarSeries(canvas, o, chart, scale, plot, band);
        DrawRowLabels(canvas, chart, plot, band, labelArea);

        if (legendRows is not null)
        {
            DrawLegendRows(canvas, o, legendRows, plot.Bottom + axisHeight + 2);
        }

        return canvas.ToSvg();
    }

    /// <summary>Vertical grid lines with tick labels under the plot, plus the zero line.</summary>
    private static void DrawVerticalGrid(SvgCanvas canvas, SvgChartOptions o, AxisScale scale, PlotArea plot)
    {
        var group = canvas.Group();
        foreach (var tick in scale.Ticks())
        {
            var x = plot.X(scale, tick);
            canvas.Line(group, x, plot.Top, x, plot.Bottom, o.GridColor);
            var label = canvas.Text(group, x, plot.Bottom + 16, AxisScale.FormatTick(tick), TickFontSize, "middle");
            label.Add(new XAttribute("fill-opacity", "0.8"));
        }

        var zero = plot.X(scale, 0);
        canvas.Line(group, zero, plot.Top, zero, plot.Bottom, o.TextColor).Add(new XAttribute("stroke-opacity", "0.35"));
    }

    /// <summary>Horizontal bars grouped per category.</summary>
    private static void DrawHorizontalBarSeries(SvgCanvas canvas, SvgChartOptions o, PreparedChart chart, AxisScale scale, PlotArea plot, double band)
    {
        var seriesCount = chart.Values.Count;
        var groupHeight = band * (seriesCount > 1 ? 0.8 : 0.62);
        var barHeight = groupHeight / seriesCount;
        var gap = seriesCount > 1 ? Math.Min(2, barHeight * 0.1) : 0;
        var zero = plot.X(scale, 0);
        var bars = canvas.Group();
        var labels = canvas.Group();

        for (var i = 0; i < chart.Labels.Count; i++)
        {
            var groupTop = plot.Top + (band * i) + ((band - groupHeight) / 2);
            for (var s = 0; s < seriesCount; s++)
            {
                var value = chart.Values[s][i];
                var x = plot.X(scale, value);
                var y = groupTop + (s * barHeight);
                var rect = canvas.Rect(bars, Math.Min(x, zero), y + (gap / 2), Math.Abs(x - zero), barHeight - gap, Color(o, s), Math.Min(3, barHeight / 4));
                SvgCanvas.AddTooltip(rect, Tooltip(chart, s, i));

                if (o.ShowValues && barHeight >= 9)
                {
                    var labelX = value >= 0 ? x + 6 : x - 6;
                    canvas.Text(labels, labelX, y + (barHeight / 2) + 4, ChartText.FormatValue(value), ValueFontSize, value >= 0 ? "start" : "end");
                }
            }
        }
    }

    /// <summary>Category labels to the left of horizontal bars.</summary>
    private static void DrawRowLabels(SvgCanvas canvas, PreparedChart chart, PlotArea plot, double band, double labelArea)
    {
        var group = canvas.Group();
        var maxChars = Math.Max(3, ChartText.CharsThatFit(labelArea - 12, LabelFontSize));
        for (var i = 0; i < chart.Labels.Count; i++)
        {
            var label = ChartText.Truncate(chart.Labels[i], maxChars);
            canvas.Text(group, plot.Left - 10, plot.Top + (band * i) + (band / 2) + 4, label, LabelFontSize, "end", fullText: chart.Labels[i]);
        }
    }

    /// <summary>Tooltip text for a data point: "Label: 12" or "Label · Series: 12".</summary>
    private static string Tooltip(PreparedChart chart, int series, int index)
    {
        var value = chart.Values[series][index].ToString("0.##", CultureInfo.InvariantCulture);
        return chart.Values.Count > 1
            ? $"{chart.Labels[index]} · {chart.SeriesNames[series]}: {value}"
            : $"{chart.Labels[index]}: {value}";
    }

    /// <summary>Plot rectangle with value → coordinate helpers.</summary>
    private sealed record PlotArea(double Left, double Top, double Width, double Height)
    {
        /// <summary>Right edge.</summary>
        public double Right => Left + Width;

        /// <summary>Bottom edge.</summary>
        public double Bottom => Top + Height;

        /// <summary>Y coordinate of a value on a vertical value axis.</summary>
        public double Y(AxisScale scale, double value) => Bottom - (scale.Fraction(value) * Height);

        /// <summary>X coordinate of a value on a horizontal value axis.</summary>
        public double X(AxisScale scale, double value) => Left + (scale.Fraction(value) * Width);
    }
}
