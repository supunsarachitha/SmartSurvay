using ClosedXML.Excel;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;

namespace SmartSurvey.Infrastructure.Exports;

/// <summary>
/// Exports a report as an Excel workbook: a "Summary" sheet (report details, filters and a list of
/// widgets) plus one sheet per widget. Numeric columns are written as numbers so they can be
/// charted and summed in Excel.
/// </summary>
public sealed class XlsxReportExporter : IReportExporter
{
    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Xlsx;

    /// <inheritdoc />
    public Task<byte[]> ExportAsync(ReportResult report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        using var workbook = new XLWorkbook();
        workbook.Properties.Title = report.ReportName;
        workbook.Properties.Author = report.ProductName;
        workbook.Properties.Subject = report.SurveyTitle;

        var names = new SheetNames();
        var summary = workbook.Worksheets.Add(names.Next("Summary"));
        var writer = new SheetWriter(summary);
        writer.Title(report.ReportName);
        foreach (var (label, value) in WidgetTables.HeaderLines(report).Skip(1))
        {
            writer.LabelValue(label, value);
        }

        // Contents: one linked row per widget sheet.
        var sheetNames = report.Widgets.Select((w, i) => names.Next($"{i + 1}. {w.Title}")).ToList();
        writer.Skip();
        writer.Table(new TableData
        {
            Title = "Contents",
            Columns = ["#", "Widget", "Sheet"],
            NumericColumns = [0],
            Rows = report.Widgets.Select((w, i) => new List<string> { (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), w.Title, sheetNames[i] }).ToList(),
        });
        var firstContentsRow = writer.Row - report.Widgets.Count;
        for (var i = 0; i < sheetNames.Count; i++)
        {
            summary.Cell(firstContentsRow + i, 3).SetHyperlink(new XLHyperlink($"'{sheetNames[i]}'!A1"));
        }

        writer.Skip();
        writer.Muted(ExportText.GeneratedBy(report));
        writer.FitColumns();

        for (var i = 0; i < report.Widgets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            WriteWidget(workbook.Worksheets.Add(sheetNames[i]), report.Widgets[i]);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return Task.FromResult(stream.ToArray());
    }

    private static void WriteWidget(IXLWorksheet sheet, WidgetResult widget)
    {
        var writer = new SheetWriter(sheet);
        writer.Title(widget.Title);
        if (!string.IsNullOrWhiteSpace(widget.QuestionText) && widget.QuestionText != widget.Title)
        {
            writer.Muted(widget.QuestionText);
        }

        if (widget.Error is not null)
        {
            writer.Skip();
            writer.LabelValue("Error", widget.Error);
            writer.FitColumns();
            return;
        }

        foreach (var table in WidgetTables.For(widget))
        {
            writer.Skip();
            writer.Table(table);
        }

        if (widget.Note is not null)
        {
            writer.Skip();
            writer.Muted(widget.Note);
        }

        writer.FitColumns();
    }
}

/// <summary>Sequential writer for a worksheet with the export's styling conventions.</summary>
internal sealed class SheetWriter(IXLWorksheet sheet)
{
    /// <summary>Header fill (indigo 50).</summary>
    public static readonly XLColor HeaderFill = XLColor.FromHtml("#EEF2FF");

    /// <summary>Muted text colour (slate 500).</summary>
    public static readonly XLColor MutedText = XLColor.FromHtml("#64748B");

    private const double MaxColumnWidth = 60;
    private readonly Dictionary<int, int> _widths = [];

    /// <summary>Next row to write (1-based).</summary>
    public int Row { get; private set; } = 1;

    /// <summary>Leaves an empty row.</summary>
    public void Skip() => Row++;

    /// <summary>Large bold title.</summary>
    public void Title(string text)
    {
        var cell = sheet.Cell(Row++, 1);
        cell.Value = text;
        cell.Style.Font.Bold = true;
        cell.Style.Font.FontSize = 14;
    }

    /// <summary>Grey single-cell line.</summary>
    public void Muted(string text)
    {
        var cell = sheet.Cell(Row++, 1);
        cell.Value = text;
        cell.Style.Font.FontColor = MutedText;
    }

    /// <summary>Bold label with its value in the next column.</summary>
    public void LabelValue(string label, string value)
    {
        sheet.Cell(Row, 1).Value = label;
        sheet.Cell(Row, 1).Style.Font.Bold = true;
        SetText(Row, 2, value);
        Track(1, label);
        Row++;
    }

    /// <summary>Writes a table (caption, styled header, rows, bold footer).</summary>
    public void Table(TableData table)
    {
        if (!string.IsNullOrWhiteSpace(table.Title))
        {
            var caption = sheet.Cell(Row++, 1);
            caption.Value = table.Title;
            caption.Style.Font.Bold = true;
        }

        var numeric = table.NumericColumns.ToHashSet();
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var cell = sheet.Cell(Row, c + 1);
            cell.Value = table.Columns[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = HeaderFill;
            Track(c + 1, table.Columns[c]);
        }

        Row++;
        foreach (var row in table.Rows)
        {
            WriteCells(row, numeric);
            Row++;
        }

        if (table.Footer is { } footer)
        {
            WriteCells(footer, numeric);
            sheet.Row(Row).Style.Font.Bold = true;
            Row++;
        }
    }

    /// <summary>Sets column widths from the longest value written (capped, never below 8).</summary>
    public void FitColumns()
    {
        foreach (var (column, length) in _widths)
        {
            sheet.Column(column).Width = Math.Clamp(length + 2, 8, MaxColumnWidth);
        }
    }

    private void WriteCells(IReadOnlyList<string> cells, IReadOnlySet<int> numeric)
    {
        for (var c = 0; c < cells.Count; c++)
        {
            if (numeric.Contains(c) && ExportText.TryParseNumber(cells[c], out var number))
            {
                sheet.Cell(Row, c + 1).Value = number;
                Track(c + 1, cells[c]);
            }
            else
            {
                SetText(Row, c + 1, cells[c]);
            }
        }
    }

    /// <summary>Writes text as a string cell (never evaluated as a formula).</summary>
    private void SetText(int row, int column, string? value)
    {
        sheet.Cell(row, column).Value = value ?? string.Empty;
        Track(column, value);
    }

    private void Track(int column, string? value)
    {
        var length = value?.Length ?? 0;
        _widths[column] = Math.Max(_widths.GetValueOrDefault(column), length);
    }
}

/// <summary>Produces unique, Excel-safe worksheet names (max 31 characters, no <c>[]:*?/\</c>).</summary>
internal sealed class SheetNames
{
    private const int MaxLength = 31;
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns a sanitized, unique name based on <paramref name="wanted"/>.</summary>
    public string Next(string wanted)
    {
        var clean = new string((wanted ?? string.Empty).Select(ch => "[]:*?/\\".Contains(ch) || char.IsControl(ch) ? ' ' : ch).ToArray())
            .Trim().Trim('\'');
        if (clean.Length == 0)
        {
            clean = "Sheet";
        }

        var name = Shorten(clean, MaxLength);
        for (var n = 2; !_used.Add(name); n++)
        {
            var suffix = $" ({n})";
            name = Shorten(clean, MaxLength - suffix.Length) + suffix;
        }

        return name;
    }

    private static string Shorten(string value, int max) => value.Length <= max ? value : value[..max].TrimEnd();
}
