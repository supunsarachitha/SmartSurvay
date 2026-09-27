using System.Text;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;

namespace SmartSurvey.Infrastructure.Exports;

/// <summary>
/// Exports a report as CSV (RFC 4180, UTF-8 with BOM, CRLF): a header block (report, survey,
/// filters), then each widget as a titled section with its tables separated by blank lines.
/// </summary>
public sealed class CsvReportExporter : IReportExporter
{
    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Csv;

    /// <inheritdoc />
    public Task<byte[]> ExportAsync(ReportResult report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        var csv = new CsvBuilder();
        foreach (var (label, value) in WidgetTables.HeaderLines(report))
        {
            csv.Row(label, value);
        }

        foreach (var widget in report.Widgets)
        {
            ct.ThrowIfCancellationRequested();
            csv.Blank().Row(widget.Title);
            if (!string.IsNullOrWhiteSpace(widget.QuestionText) && widget.QuestionText != widget.Title)
            {
                csv.Row("Question", widget.QuestionText);
            }

            if (widget.Error is not null)
            {
                csv.Row("Error", widget.Error);
                continue;
            }

            var first = true;
            foreach (var table in WidgetTables.For(widget))
            {
                if (!first)
                {
                    csv.Blank();
                }

                csv.Table(table);
                first = false;
            }

            if (widget.Note is not null)
            {
                csv.Row("Note", widget.Note);
            }
        }

        csv.Blank().Row(ExportText.GeneratedBy(report));
        return Task.FromResult(csv.ToBytes());
    }
}

/// <summary>Minimal RFC 4180 writer with spreadsheet formula-injection protection.</summary>
internal sealed class CsvBuilder
{
    private const string NewLine = "\r\n";
    private readonly StringBuilder _text = new();

    /// <summary>Appends a row.</summary>
    public CsvBuilder Row(params string?[] cells) => Row((IEnumerable<string?>)cells);

    /// <summary>Appends a row.</summary>
    public CsvBuilder Row(IEnumerable<string?> cells)
    {
        _text.AppendJoin(',', cells.Select(Escape)).Append(NewLine);
        return this;
    }

    /// <summary>Appends an empty line.</summary>
    public CsvBuilder Blank()
    {
        _text.Append(NewLine);
        return this;
    }

    /// <summary>Appends a table: optional caption row, header row, data rows and footer row.</summary>
    public CsvBuilder Table(TableData table)
    {
        if (!string.IsNullOrWhiteSpace(table.Title))
        {
            Row(table.Title);
        }

        Row(table.Columns);
        foreach (var row in table.Rows)
        {
            Row(row);
        }

        if (table.Footer is { } footer)
        {
            Row(footer);
        }

        return this;
    }

    /// <summary>The document as UTF-8 with BOM.</summary>
    public byte[] ToBytes() => ExportText.ToUtf8WithBom(_text.ToString());

    /// <summary>
    /// Quotes a field when it contains a comma, quote or line break (quotes doubled). Text that a
    /// spreadsheet would run as a formula (leading <c>= + - @</c>, tab or CR) is prefixed with an
    /// apostrophe; plain numbers such as <c>-5</c> are left alone.
    /// </summary>
    public static string Escape(string? value)
    {
        var text = NeutralizeFormula(value ?? string.Empty);
        return text.AsSpan().IndexOfAny(",\"\r\n") >= 0
            ? $"\"{text.Replace("\"", "\"\"")}\""
            : text;
    }

    /// <summary>Prefixes formula-like text with an apostrophe (OWASP CSV injection guidance).</summary>
    internal static string NeutralizeFormula(string value) =>
        value.Length > 0 && "=+-@\t\r".Contains(value[0]) && !ExportText.TryParseNumber(value, out _)
            ? "'" + value
            : value;
}
