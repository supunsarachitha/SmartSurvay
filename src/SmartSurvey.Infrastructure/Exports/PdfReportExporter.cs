using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Reports.Charts;

namespace SmartSurvey.Infrastructure.Exports;

/// <summary>
/// Exports a report as an A4 PDF (QuestPDF): a title block with survey, date, response count and
/// filters, then every widget with its KPI tiles, chart (vector SVG from
/// <see cref="ISvgChartRenderer"/>) and tables. Each page carries the product name and page numbers.
/// </summary>
public sealed class PdfReportExporter(ISvgChartRenderer charts) : IReportExporter
{
    // Palette aligned with the web design system (slate + indigo).
    private const string Ink = "#1E293B";
    private const string Muted = "#64748B";
    private const string Accent = "#4F46E5";
    private const string Line = "#E2E8F0";
    private const string HeaderFill = "#EEF2FF";
    private const string Zebra = "#F8FAFC";
    private const string Danger = "#B91C1C";
    private const string DangerFill = "#FEF2F2";

    /// <summary>Tables with more columns than this use a smaller font.</summary>
    private const int WideTableColumns = 6;

    private static readonly SvgChartOptions ChartOptions = new()
    {
        Width = 640,
        Height = 320,
        FontFamily = "Lato, Segoe UI, Helvetica, Arial, sans-serif",
    };

    /// <summary>QuestPDF settings are process-wide; configure them once, before the first document.</summary>
    static PdfReportExporter()
    {
        // Free under the QuestPDF Community license (see docs/DOCUMENTATION.md, "Licensing notes").
        QuestPDF.Settings.License = LicenseType.Community;

        // Free-text answers may contain characters (emoji, other scripts) missing from the bundled
        // font: render them with fallback fonts instead of failing the export.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
    }

    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Pdf;

    /// <inheritdoc />
    public Task<byte[]> ExportAsync(ReportResult report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ct.ThrowIfCancellationRequested();

        var document = Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(style => style.FontSize(10).FontColor(Ink));

            page.Header().Element(c => RunningHeader(c, report));
            page.Content().PaddingVertical(8).Column(column =>
            {
                column.Spacing(16);
                column.Item().Element(c => TitleBlock(c, report));
                foreach (var widget in report.Widgets)
                {
                    column.Item().Element(c => Widget(c, widget));
                }
            });
            page.Footer().Element(c => Footer(c, report));
        }));

        document.WithMetadata(new DocumentMetadata
        {
            Title = report.ReportName,
            Subject = report.SurveyTitle,
            Author = report.ProductName,
            Creator = report.ProductName,
            Producer = report.ProductName,
            CreationDate = new DateTimeOffset(DateTime.SpecifyKind(report.GeneratedAt, DateTimeKind.Utc)),
        });

        return Task.FromResult(document.GeneratePdf());
    }

    private static void RunningHeader(IContainer container, ReportResult report) =>
        container.BorderBottom(1).BorderColor(Line).PaddingBottom(4).Row(row =>
        {
            row.RelativeItem().Text(report.ProductName).FontSize(9).SemiBold().FontColor(Accent);
            row.RelativeItem().AlignRight().Text(report.ReportName).FontSize(9).FontColor(Muted);
        });

    private static void Footer(IContainer container, ReportResult report) =>
        container.BorderTop(1).BorderColor(Line).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text(ExportText.GeneratedBy(report)).FontSize(8).FontColor(Muted);
            row.AutoItem().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Muted));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });

    private static void TitleBlock(IContainer container, ReportResult report) =>
        container.Column(column =>
        {
            column.Spacing(4);
            column.Item().Text(report.ReportName).FontSize(20).Bold();
            if (!string.IsNullOrWhiteSpace(report.Description))
            {
                column.Item().Text(report.Description).FontColor(Muted);
            }

            column.Item().PaddingTop(4).Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(9).FontColor(Muted));
                text.Span("Survey: ").SemiBold();
                text.Span(report.SurveyTitle);
                text.Span("   ·   Generated: ").SemiBold();
                text.Span(ExportText.Timestamp(report.GeneratedAt));
                text.Span("   ·   Responses: ").SemiBold();
                text.Span(report.TotalResponses.ToString(System.Globalization.CultureInfo.InvariantCulture));
            });

            if (report.FilterSummary.Count > 0)
            {
                column.Item().PaddingTop(6).Background(Zebra).Border(1).BorderColor(Line).Padding(8).Column(filters =>
                {
                    filters.Item().Text("Filters").FontSize(9).SemiBold();
                    foreach (var filter in report.FilterSummary)
                    {
                        filters.Item().Text($"• {filter}").FontSize(9).FontColor(Muted);
                    }
                });
            }
        });

    private void Widget(IContainer container, WidgetResult widget) =>
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().BorderLeft(3).BorderColor(Accent).PaddingLeft(8).Text(widget.Title).FontSize(13).SemiBold();
            if (!string.IsNullOrWhiteSpace(widget.QuestionText) && widget.QuestionText != widget.Title)
            {
                column.Item().Text(widget.QuestionText).FontSize(9).Italic().FontColor(Muted);
            }

            if (widget.Error is not null)
            {
                column.Item().Background(DangerFill).Border(1).BorderColor(Danger).Padding(8)
                    .Text(widget.Error).FontSize(9).FontColor(Danger);
                return;
            }

            if (widget.Stats.Count > 0)
            {
                column.Item().Element(c => StatTiles(c, widget.Stats));
            }

            var chartDrawn = false;
            if (widget.Chart is { } chart)
            {
                chartDrawn = TryChart(column, chart);
            }

            if (widget.Table is { } table)
            {
                column.Item().Element(c => Table(c, table));
            }
            else if (!chartDrawn && widget.Chart is { Labels.Count: > 0 } fallback)
            {
                column.Item().Element(c => Table(c, WidgetTables.FromChart(fallback)));
            }

            foreach (var extra in widget.ExtraTables)
            {
                column.Item().Element(c => Table(c, extra));
            }

            if (widget.Note is not null)
            {
                column.Item().Text(widget.Note).FontSize(8).Italic().FontColor(Muted);
            }
        });

    /// <summary>Adds the chart as vector SVG; returns false when there is nothing to draw or rendering failed.</summary>
    private bool TryChart(ColumnDescriptor column, ChartData chart)
    {
        if (chart.Labels.Count == 0 || chart.Series.All(s => s.Values.All(v => v == 0)))
        {
            column.Item().Text("No data to chart for the current filters.").FontSize(9).Italic().FontColor(Muted);
            return true;
        }

        string svg;
        try
        {
            svg = charts.Render(chart, ChartOptions);
        }
        catch (Exception)
        {
            return false; // fall back to the data table
        }

        column.Item().AlignCenter().MaxWidth(460).Svg(svg);
        return true;
    }

    private static void StatTiles(IContainer container, IReadOnlyList<StatItem> stats) =>
        container.Inlined(inlined =>
        {
            inlined.Spacing(8);
            foreach (var stat in stats)
            {
                inlined.Item().Width(118).Border(1).BorderColor(Line).Background(Zebra).Padding(8).Column(tile =>
                {
                    tile.Item().Text(stat.Label).FontSize(8).FontColor(Muted);
                    tile.Item().Text(stat.Value).FontSize(14).Bold();
                });
            }
        });

    private static void Table(IContainer container, TableData data)
    {
        var columnCount = Math.Max(data.Columns.Count, data.Rows.Select(r => r.Count).DefaultIfEmpty(0).Max());
        if (columnCount == 0)
        {
            return;
        }

        var numeric = data.NumericColumns.ToHashSet();
        var fontSize = columnCount > WideTableColumns ? 7 : 9;

        container.Column(column =>
        {
            if (!string.IsNullOrWhiteSpace(data.Title))
            {
                column.Item().PaddingBottom(4).Text(data.Title).FontSize(10).SemiBold();
            }

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    for (var c = 0; c < columnCount; c++)
                    {
                        columns.RelativeColumn();
                    }
                });

                table.Header(header =>
                {
                    for (var c = 0; c < columnCount; c++)
                    {
                        Cell(header.Cell().Background(HeaderFill), CellText(data.Columns, c), numeric.Contains(c), fontSize, bold: true);
                    }
                });

                for (var r = 0; r < data.Rows.Count; r++)
                {
                    var background = r % 2 == 1 ? Zebra : "#FFFFFF";
                    for (var c = 0; c < columnCount; c++)
                    {
                        Cell(table.Cell().Background(background), CellText(data.Rows[r], c), numeric.Contains(c), fontSize, bold: false);
                    }
                }

                if (data.Footer is { } footer)
                {
                    for (var c = 0; c < columnCount; c++)
                    {
                        Cell(table.Cell().BorderTop(1).BorderColor(Muted), CellText(footer, c), numeric.Contains(c), fontSize, bold: true);
                    }
                }
            });
        });
    }

    private static void Cell(IContainer container, string text, bool alignRight, int fontSize, bool bold)
    {
        var cell = container.BorderBottom(0.5f).BorderColor(Line).PaddingVertical(3).PaddingHorizontal(4);
        var descriptor = (alignRight ? cell.AlignRight() : cell.AlignLeft()).Text(text).FontSize(fontSize);
        if (bold)
        {
            descriptor.SemiBold();
        }
    }

    private static string CellText(IReadOnlyList<string> cells, int index) => index < cells.Count ? cells[index] ?? string.Empty : string.Empty;
}
