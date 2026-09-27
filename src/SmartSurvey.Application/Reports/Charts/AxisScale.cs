using System.Globalization;

namespace SmartSurvey.Application.Reports.Charts;

/// <summary>
/// A "nice" linear value axis: bounds and tick step are rounded to 1, 2 or 5 × 10ⁿ so tick labels
/// read naturally (0, 5, 10, 15 … rather than 0, 4.3, 8.6 …).
/// </summary>
/// <param name="Min">Lower bound (a multiple of <paramref name="Step"/>, never above zero).</param>
/// <param name="Max">Upper bound (a multiple of <paramref name="Step"/>, always above <paramref name="Min"/>).</param>
/// <param name="Step">Distance between ticks.</param>
internal readonly record struct AxisScale(double Min, double Max, double Step)
{
    /// <summary>Default number of intervals the axis aims for.</summary>
    public const int DefaultTickTarget = 5;

    /// <summary>Range covered by the axis.</summary>
    public double Span => Max - Min;

    /// <summary>
    /// Creates a scale that covers <paramref name="dataMin"/>..<paramref name="dataMax"/> and always
    /// includes zero (bars grow from the zero line).
    /// </summary>
    /// <param name="dataMin">Smallest data value.</param>
    /// <param name="dataMax">Largest data value.</param>
    /// <param name="integersOnly">When every value is a whole number (counts), fractional steps are avoided.</param>
    /// <param name="tickTarget">Approximate number of intervals.</param>
    public static AxisScale Create(double dataMin, double dataMax, bool integersOnly, int tickTarget = DefaultTickTarget)
    {
        var min = Math.Min(0, SafeValue(dataMin));
        var max = Math.Max(0, SafeValue(dataMax));
        if (max - min <= 0)
        {
            max = 1; // all zero: draw an empty 0..1 axis instead of dividing by zero
        }

        var step = NiceStep((max - min) / Math.Max(1, tickTarget));
        if (integersOnly && step < 1)
        {
            step = 1;
        }

        var niceMin = Math.Floor(min / step) * step;
        var niceMax = Math.Ceiling(max / step) * step;
        if (niceMax <= niceMin)
        {
            niceMax = niceMin + step;
        }

        return new AxisScale(Round(niceMin), Round(niceMax), step);
    }

    /// <summary>Rounds a raw step up to the nearest 1, 2 or 5 × 10ⁿ.</summary>
    public static double NiceStep(double rawStep)
    {
        if (!(rawStep > 0) || !double.IsFinite(rawStep))
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var fraction = rawStep / magnitude;
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return Round(nice * magnitude);
    }

    /// <summary>Tick values from <see cref="Min"/> to <see cref="Max"/> inclusive.</summary>
    public IReadOnlyList<double> Ticks()
    {
        var count = (int)Math.Round(Span / Step);
        var ticks = new List<double>(count + 1);
        for (var i = 0; i <= count; i++)
        {
            ticks.Add(Round(Min + (i * Step)));
        }

        return ticks;
    }

    /// <summary>Relative position (0..1) of a value on the axis.</summary>
    public double Fraction(double value) => (SafeValue(value) - Min) / Span;

    /// <summary>
    /// Formats a tick label compactly: <c>950</c>, <c>1.5k</c>, <c>2M</c> (invariant culture).
    /// </summary>
    public static string FormatTick(double value)
    {
        var abs = Math.Abs(value);
        return abs switch
        {
            >= 1_000_000 => (value / 1_000_000).ToString("0.##", CultureInfo.InvariantCulture) + "M",
            >= 1_000 => (value / 1_000).ToString("0.##", CultureInfo.InvariantCulture) + "k",
            _ => value.ToString("0.##", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>Replaces NaN/∞ with zero so malformed input can never break the layout.</summary>
    public static double SafeValue(double value) => double.IsFinite(value) ? value : 0;

    // Removes binary floating point noise such as 0.30000000000000004.
    private static double Round(double value) => Math.Round(value, 10);
}
