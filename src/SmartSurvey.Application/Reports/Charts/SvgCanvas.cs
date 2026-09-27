using System.Globalization;
using System.Xml.Linq;

namespace SmartSurvey.Application.Reports.Charts;

/// <summary>
/// Minimal SVG document builder on top of LINQ to XML. Every text node and attribute value goes
/// through the XML writer, so user supplied labels are always escaped (no markup injection).
/// Only presentation attributes are emitted (no CSS, classes, filters or foreignObject) so the
/// output can be rasterised by Skia (QuestPDF) as well as inlined in HTML.
/// </summary>
internal sealed class SvgCanvas
{
    /// <summary>SVG namespace.</summary>
    public static readonly XNamespace Ns = "http://www.w3.org/2000/svg";

    private readonly SvgChartOptions _options;

    /// <summary>Creates an empty canvas of the given size.</summary>
    public SvgCanvas(double width, double height, SvgChartOptions options)
    {
        _options = options;
        Width = width;
        Height = height;
        Root = new XElement(
            Ns + "svg",
            new XAttribute("width", Num(width)),
            new XAttribute("height", Num(height)),
            new XAttribute("viewBox", $"0 0 {Num(width)} {Num(height)}"),
            new XAttribute("role", "img"),
            new XAttribute("font-family", ChartText.Clean(options.FontFamily)));
    }

    /// <summary>Canvas width (user units).</summary>
    public double Width { get; }

    /// <summary>Canvas height (user units).</summary>
    public double Height { get; }

    /// <summary>The root &lt;svg&gt; element.</summary>
    public XElement Root { get; }

    /// <summary>Formats a coordinate/length with at most two decimals (invariant culture).</summary>
    public static string Num(double value) =>
        Math.Round(AxisScale.SafeValue(value), 2).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Adds a group element.</summary>
    public XElement Group(XElement? parent = null)
    {
        var group = new XElement(Ns + "g");
        (parent ?? Root).Add(group);
        return group;
    }

    /// <summary>Adds a filled rectangle.</summary>
    public XElement Rect(XElement parent, double x, double y, double width, double height, string fill, double radius = 0)
    {
        var rect = new XElement(
            Ns + "rect",
            new XAttribute("x", Num(x)),
            new XAttribute("y", Num(y)),
            new XAttribute("width", Num(Math.Max(0, width))),
            new XAttribute("height", Num(Math.Max(0, height))),
            new XAttribute("fill", fill));
        if (radius > 0)
        {
            rect.Add(new XAttribute("rx", Num(radius)), new XAttribute("ry", Num(radius)));
        }

        parent.Add(rect);
        return rect;
    }

    /// <summary>Adds a straight line.</summary>
    public XElement Line(XElement parent, double x1, double y1, double x2, double y2, string stroke, double strokeWidth = 1)
    {
        var line = new XElement(
            Ns + "line",
            new XAttribute("x1", Num(x1)),
            new XAttribute("y1", Num(y1)),
            new XAttribute("x2", Num(x2)),
            new XAttribute("y2", Num(y2)),
            new XAttribute("stroke", stroke),
            new XAttribute("stroke-width", Num(strokeWidth)));
        parent.Add(line);
        return line;
    }

    /// <summary>Adds a path.</summary>
    public XElement Path(XElement parent, string data, string fill, string? stroke = null, double strokeWidth = 0)
    {
        var path = new XElement(Ns + "path", new XAttribute("d", data), new XAttribute("fill", fill));
        if (stroke is not null)
        {
            path.Add(new XAttribute("stroke", stroke), new XAttribute("stroke-width", Num(strokeWidth)));
        }

        parent.Add(path);
        return path;
    }

    /// <summary>Adds a circle.</summary>
    public XElement Circle(XElement parent, double cx, double cy, double r, string fill)
    {
        var circle = new XElement(
            Ns + "circle",
            new XAttribute("cx", Num(cx)),
            new XAttribute("cy", Num(cy)),
            new XAttribute("r", Num(Math.Max(0, r))),
            new XAttribute("fill", fill));
        parent.Add(circle);
        return circle;
    }

    /// <summary>
    /// Adds a text element using the chart font. When the visible text had to be shortened the full
    /// text is attached as a &lt;title&gt; tooltip.
    /// </summary>
    /// <param name="parent">Parent element.</param>
    /// <param name="x">Anchor x.</param>
    /// <param name="y">Baseline y.</param>
    /// <param name="text">Visible text (already truncated if needed).</param>
    /// <param name="fontSize">Font size in user units.</param>
    /// <param name="anchor">start | middle | end.</param>
    /// <param name="fill">Colour; defaults to the configured text colour.</param>
    /// <param name="bold">Bold weight.</param>
    /// <param name="fullText">Untruncated text (tooltip when different from <paramref name="text"/>).</param>
    public XElement Text(
        XElement parent,
        double x,
        double y,
        string text,
        double fontSize,
        string anchor = "start",
        string? fill = null,
        bool bold = false,
        string? fullText = null)
    {
        var element = new XElement(
            Ns + "text",
            new XAttribute("x", Num(x)),
            new XAttribute("y", Num(y)),
            new XAttribute("font-family", ChartText.Clean(_options.FontFamily)),
            new XAttribute("font-size", Num(fontSize)),
            new XAttribute("text-anchor", anchor),
            new XAttribute("fill", fill ?? _options.TextColor));
        if (bold)
        {
            element.Add(new XAttribute("font-weight", "bold"));
        }

        if (fullText is not null && fullText != text)
        {
            AddTooltip(element, fullText);
        }

        element.Add(ChartText.Clean(text));
        parent.Add(element);
        return element;
    }

    /// <summary>Attaches a &lt;title&gt; child (native browser tooltip / accessible name).</summary>
    public static void AddTooltip(XElement element, string text) =>
        element.AddFirst(new XElement(Ns + "title", ChartText.Clean(text)));

    /// <summary>Serialises the document without indentation.</summary>
    public string ToSvg() => Root.ToString(SaveOptions.DisableFormatting);
}
