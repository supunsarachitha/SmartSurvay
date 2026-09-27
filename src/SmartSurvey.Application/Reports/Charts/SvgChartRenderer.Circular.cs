using System.Globalization;
using System.Xml.Linq;

namespace SmartSurvey.Application.Reports.Charts;

// Circular charts: pie and doughnut (first series only).
public sealed partial class SvgChartRenderer
{
    private const double DoughnutInnerRatio = 0.58;
    private const double MinSliceLabelShare = 0.05; // smaller slices get no in-slice label
    private const double MaxLegendWidth = 280;
    private const double FullCircleDegrees = 359.999;

    /// <summary>Renders a pie or doughnut of the first series with an optional legend on the right.</summary>
    private static string RenderCircular(PreparedChart chart, SvgChartOptions o, bool doughnut)
    {
        var values = chart.Values[0].Select(v => Math.Max(0, v)).ToArray();
        var total = values.Sum();
        if (total <= 0)
        {
            return RenderPlaceholder(o); // an all-zero pie has no slices to draw
        }

        var canvas = new SvgCanvas(o.Width, o.Height, o);
        var legendWidth = o.ShowLegend ? Math.Min(o.Width * 0.45, MaxLegendWidth) : 0;
        var areaWidth = o.Width - legendWidth;
        var radius = Math.Max(20, (Math.Min(areaWidth, o.Height) / 2) - OuterPadding);
        var geometry = new CircleGeometry(areaWidth / 2, o.Height / 2.0, radius, doughnut ? radius * DoughnutInnerRatio : 0);

        DrawSlices(canvas, o, chart, values, total, geometry);
        if (doughnut)
        {
            DrawDoughnutCenter(canvas, total, geometry);
        }

        if (o.ShowLegend)
        {
            DrawCircularLegend(canvas, o, chart, values, total, areaWidth, legendWidth);
        }

        return canvas.ToSvg();
    }

    /// <summary>Draws slices clockwise from 12 o'clock, with in-slice value/percentage labels.</summary>
    private static void DrawSlices(SvgCanvas canvas, SvgChartOptions o, PreparedChart chart, double[] values, double total, CircleGeometry g)
    {
        var slices = canvas.Group();
        var labels = canvas.Group();
        var positiveCount = values.Count(v => v > 0);
        var angle = -90.0;

        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] <= 0)
            {
                continue;
            }

            var share = values[i] / total;
            var sweep = share * 360;
            var shape = positiveCount == 1 || sweep >= FullCircleDegrees
                ? DrawFullCircle(canvas, slices, g, Color(o, i))
                : canvas.Path(slices, SlicePath(g, angle, angle + sweep), Color(o, i), "#ffffff", 1.5);
            SvgCanvas.AddTooltip(shape, $"{chart.Labels[i]}: {ChartText.FormatValue(values[i])} ({ChartText.FormatPercent(share)})");

            if (o.ShowValues && share >= MinSliceLabelShare)
            {
                var labelRadius = g.Inner > 0 ? (g.Radius + g.Inner) / 2 : g.Radius * 0.62;
                var (x, y) = g.Point(labelRadius, angle + (sweep / 2));
                var text = o.ShowPercentages ? ChartText.FormatPercent(share) : ChartText.FormatValue(values[i]);
                canvas.Text(labels, x, y + 4, text, ValueFontSize, "middle", "#ffffff", bold: true);
            }

            angle += sweep;
        }
    }

    /// <summary>
    /// A single 100 % slice cannot be drawn as an arc (start = end), so it is a circle, or for
    /// doughnuts a thick stroked ring.
    /// </summary>
    private static XElement DrawFullCircle(SvgCanvas canvas, XElement parent, CircleGeometry g, string color)
    {
        if (g.Inner <= 0)
        {
            return canvas.Circle(parent, g.Cx, g.Cy, g.Radius, color);
        }

        var ring = canvas.Circle(parent, g.Cx, g.Cy, (g.Radius + g.Inner) / 2, "none");
        ring.Add(new XAttribute("stroke", color), new XAttribute("stroke-width", SvgCanvas.Num(g.Radius - g.Inner)));
        return ring;
    }

    /// <summary>SVG path of a pie slice or (when <see cref="CircleGeometry.Inner"/> &gt; 0) a ring segment.</summary>
    private static string SlicePath(CircleGeometry g, double startDegrees, double endDegrees)
    {
        var large = endDegrees - startDegrees > 180 ? 1 : 0;
        var (x1, y1) = g.Point(g.Radius, startDegrees);
        var (x2, y2) = g.Point(g.Radius, endDegrees);
        var r = SvgCanvas.Num(g.Radius);

        if (g.Inner <= 0)
        {
            return $"M{SvgCanvas.Num(g.Cx)} {SvgCanvas.Num(g.Cy)} L{SvgCanvas.Num(x1)} {SvgCanvas.Num(y1)} " +
                   $"A{r} {r} 0 {large} 1 {SvgCanvas.Num(x2)} {SvgCanvas.Num(y2)} Z";
        }

        var (ix1, iy1) = g.Point(g.Inner, startDegrees);
        var (ix2, iy2) = g.Point(g.Inner, endDegrees);
        var ir = SvgCanvas.Num(g.Inner);
        return $"M{SvgCanvas.Num(x1)} {SvgCanvas.Num(y1)} A{r} {r} 0 {large} 1 {SvgCanvas.Num(x2)} {SvgCanvas.Num(y2)} " +
               $"L{SvgCanvas.Num(ix2)} {SvgCanvas.Num(iy2)} A{ir} {ir} 0 {large} 0 {SvgCanvas.Num(ix1)} {SvgCanvas.Num(iy1)} Z";
    }

    /// <summary>Total in the doughnut hole.</summary>
    private static void DrawDoughnutCenter(SvgCanvas canvas, double total, CircleGeometry g)
    {
        if (g.Inner < 28)
        {
            return; // too small to hold readable text
        }

        var group = canvas.Group();
        canvas.Text(group, g.Cx, g.Cy + 3, ChartText.FormatValue(total), g.Inner > 50 ? 22 : 16, "middle", bold: true);
        canvas.Text(group, g.Cx, g.Cy + 20, "Total", TickFontSize, "middle").Add(new XAttribute("fill-opacity", "0.7"));
    }

    /// <summary>
    /// Vertical legend right of the chart: swatch, category name and value (plus percentage). When
    /// the categories do not fit, the last row says "+N more".
    /// </summary>
    private static void DrawCircularLegend(
        SvgCanvas canvas, SvgChartOptions o, PreparedChart chart, double[] values, double total, double left, double width)
    {
        var group = canvas.Group();
        var count = chart.Labels.Count;
        var capacity = Math.Max(1, (int)((o.Height - (2 * OuterPadding)) / LegendRowHeight));
        var shown = count <= capacity ? count : capacity - 1;
        var rows = count <= capacity ? count : capacity;
        var y = (o.Height - (rows * LegendRowHeight)) / 2;
        var right = left + width - 8;

        for (var i = 0; i < shown; i++)
        {
            var valueText = o.ShowPercentages
                ? $"{ChartText.FormatValue(values[i])} ({ChartText.FormatPercent(values[i] / total)})"
                : ChartText.FormatValue(values[i]);
            var labelWidth = width - LegendSwatch - 14 - ChartText.Width(valueText, LegendFontSize) - 10;
            var label = ChartText.Truncate(chart.Labels[i], Math.Max(3, ChartText.CharsThatFit(labelWidth, LegendFontSize)));

            canvas.Rect(group, left + 4, y + 5, LegendSwatch, LegendSwatch, Color(o, i), 2);
            canvas.Text(group, left + LegendSwatch + 10, y + 14, label, LegendFontSize, fullText: chart.Labels[i]);
            canvas.Text(group, right, y + 14, valueText, LegendFontSize, "end").Add(new XAttribute("fill-opacity", "0.8"));
            y += LegendRowHeight;
        }

        if (shown < count)
        {
            var more = (count - shown).ToString(CultureInfo.InvariantCulture);
            canvas.Text(group, left + LegendSwatch + 10, y + 14, $"+{more} more", LegendFontSize).Add(new XAttribute("fill-opacity", "0.7"));
        }
    }

    /// <summary>Centre, outer radius and inner (hole) radius of a circular chart.</summary>
    private sealed record CircleGeometry(double Cx, double Cy, double Radius, double Inner)
    {
        /// <summary>Point on a circle of radius <paramref name="r"/> at an angle (degrees, 0 = 3 o'clock).</summary>
        public (double X, double Y) Point(double r, double degrees)
        {
            var radians = degrees * Math.PI / 180;
            return (Cx + (r * Math.Cos(radians)), Cy + (r * Math.Sin(radians)));
        }
    }
}
