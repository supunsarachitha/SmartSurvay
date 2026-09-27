using System.Globalization;
using System.Text;
using System.Xml;

namespace SmartSurvey.Application.Reports.Charts;

/// <summary>Text helpers for chart rendering: sanitising, truncation and width estimation.</summary>
internal static class ChartText
{
    /// <summary>Ellipsis appended to truncated labels.</summary>
    public const string Ellipsis = "…";

    // Average glyph width relative to the font size for typical proportional UI fonts.
    private const double AverageGlyphWidth = 0.56;

    /// <summary>
    /// Makes arbitrary user text safe for a single-line SVG text node: removes characters that are
    /// not allowed in XML (control characters, lone surrogates) and collapses line breaks/tabs into
    /// spaces. Markup characters (&lt; &gt; &amp; quotes) are kept here and escaped by the XML writer.
    /// </summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '\r' or '\n' or '\t')
            {
                sb.Append(' ');
            }
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], c))
            {
                sb.Append(c).Append(text[i + 1]);
                i++;
            }
            else if (XmlConvert.IsXmlChar(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Shortens <paramref name="text"/> to at most <paramref name="maxChars"/> characters, ending
    /// with an ellipsis when something was cut. Surrogate pairs are never split.
    /// </summary>
    public static string Truncate(string text, int maxChars)
    {
        if (maxChars < 1)
        {
            maxChars = 1;
        }

        if (text.Length <= maxChars)
        {
            return text;
        }

        var cut = maxChars - 1;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut].TrimEnd() + Ellipsis;
    }

    /// <summary>Estimated rendered width of a text in user units.</summary>
    public static double Width(string text, double fontSize) => text.Length * fontSize * AverageGlyphWidth;

    /// <summary>Number of characters that fit into <paramref name="width"/> user units.</summary>
    public static int CharsThatFit(double width, double fontSize) =>
        Math.Max(0, (int)Math.Floor(width / (fontSize * AverageGlyphWidth)));

    /// <summary>
    /// Formats a data value for labels: up to two decimals, thousands abbreviated (12.5k) from 10 000.
    /// </summary>
    public static string FormatValue(double value) =>
        Math.Abs(value) >= 10_000
            ? AxisScale.FormatTick(value)
            : value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Formats a share (0..1) as a percentage with one decimal, e.g. <c>42.5%</c>.</summary>
    public static string FormatPercent(double fraction) =>
        (fraction * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
