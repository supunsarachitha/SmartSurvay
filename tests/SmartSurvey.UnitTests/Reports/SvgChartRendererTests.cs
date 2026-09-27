using System.Xml.Linq;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Reports.Charts;

namespace SmartSurvey.UnitTests.Reports;

public sealed class SvgChartRendererTests
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private readonly SvgChartRenderer _renderer = new();

    public static TheoryData<ChartKind> Kinds() => new(Enum.GetValues<ChartKind>());

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_renders_well_formed_svg_of_the_requested_size(ChartKind kind)
    {
        var svg = _renderer.Render(Data(kind), new SvgChartOptions { Width = 500, Height = 300 });

        var root = XDocument.Parse(svg).Root!;
        Assert.Equal(Svg + "svg", root.Name);
        Assert.Equal("0 0 500 300", root.Attribute("viewBox")?.Value);
        Assert.DoesNotContain("NaN", svg);
        Assert.DoesNotContain("Infinity", svg);
        Assert.Contains("Europe", svg);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Empty_and_all_zero_data_render_without_errors(ChartKind kind)
    {
        var empty = _renderer.Render(new ChartData { Kind = kind });
        var zeros = _renderer.Render(new ChartData
        {
            Kind = kind,
            Labels = ["A", "B"],
            Series = [new ChartSeries { Name = "Responses", Values = [0, 0] }],
        });

        XDocument.Parse(empty);
        XDocument.Parse(zeros);
        Assert.DoesNotContain("NaN", zeros);
    }

    [Fact]
    public void Labels_are_escaped_so_user_text_cannot_inject_markup()
    {
        var data = Data(ChartKind.Bar);
        data.Labels[0] = "<script>alert(1)</script>";
        data.Series[0].Name = "\" onload=\"alert(1)";

        var svg = _renderer.Render(data);

        var doc = XDocument.Parse(svg);
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName == "script");
        Assert.DoesNotContain(doc.Descendants().Attributes(), a => a.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Multiple_series_get_a_legend_entry_each()
    {
        var data = Data(ChartKind.StackedBar);
        data.Series.Add(new ChartSeries { Name = "Second series", Values = [1, 2, 3] });

        var svg = _renderer.Render(data, new SvgChartOptions { ShowLegend = true });

        Assert.Contains("Second series", svg);
        Assert.Contains("Responses", svg);
    }

    private static ChartData Data(ChartKind kind) => new()
    {
        Kind = kind,
        Labels = ["Europe", "Asia", "Other"],
        Series = [new ChartSeries { Name = "Responses", Values = [5, 3, 1] }],
        ValueAxisTitle = "Responses",
    };
}
