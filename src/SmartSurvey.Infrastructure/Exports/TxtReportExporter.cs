using System.Text;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;

namespace SmartSurvey.Infrastructure.Exports;

/// <summary>
/// Exports a report as plain text (UTF-8) with ASCII tables — readable in any editor, e-mail body
/// or terminal. Long cells are shortened; numeric columns are right-aligned.
/// </summary>
public sealed class TxtReportExporter : IReportExporter
{
    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.Txt;

    /// <inheritdoc />
    public Task<byte[]> ExportAsync(ReportResult report, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        var text = new StringBuilder();
        Heading(text, report.ReportName, '=');
        var header = WidgetTables.HeaderLines(report).Skip(1).ToList(); // the name is the heading
        var labelWidth = header.Count == 0 ? 0 : header.Max(h => h.Label.Length);
        foreach (var (label, value) in header)
        {
            text.Append((label + ":").PadRight(labelWidth + 2)).AppendLine(ExportText.SingleLine(value));
        }

        foreach (var widget in report.Widgets)
        {
            ct.ThrowIfCancellationRequested();
            text.AppendLine();
            Heading(text, widget.Title, '-');
            if (!string.IsNullOrWhiteSpace(widget.QuestionText) && widget.QuestionText != widget.Title)
            {
                text.AppendLine(ExportText.SingleLine(widget.QuestionText)).AppendLine();
            }

            if (widget.Error is not null)
            {
                text.AppendLine($"Error: {widget.Error}");
                continue;
            }

            foreach (var table in WidgetTables.For(widget))
            {
                TextTable.Render(text, table);
                text.AppendLine();
            }

            if (widget.Note is not null)
            {
                text.AppendLine($"Note: {widget.Note}");
            }
        }

        text.AppendLine().AppendLine(ExportText.GeneratedBy(report));
        return Task.FromResult(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text.ToString()));
    }

    private static void Heading(StringBuilder text, string title, char underline)
    {
        var line = ExportText.SingleLine(title);
        text.AppendLine(line).AppendLine(new string(underline, Math.Max(3, line.Length)));
    }
}

/// <summary>Renders <see cref="TableData"/> as an ASCII grid.</summary>
internal static class TextTable
{
    /// <summary>Cells longer than this are shortened with "...".</summary>
    public const int MaxCellWidth = 60;

    /// <summary>Appends the table (caption, header, rows, optional footer).</summary>
    public static void Render(StringBuilder text, TableData table)
    {
        var columnCount = Math.Max(table.Columns.Count, table.Rows.Select(r => r.Count).DefaultIfEmpty(0).Max());
        if (columnCount == 0)
        {
            return;
        }

        var header = Normalize(table.Columns, columnCount);
        var rows = table.Rows.Select(r => Normalize(r, columnCount)).ToList();
        var footer = table.Footer is null ? null : Normalize(table.Footer, columnCount);

        var widths = new int[columnCount];
        foreach (var row in rows.Prepend(header).Concat(footer is null ? [] : [footer]))
        {
            for (var c = 0; c < columnCount; c++)
            {
                widths[c] = Math.Max(widths[c], row[c].Length);
            }
        }

        var numeric = table.NumericColumns.ToHashSet();
        var separator = "+" + string.Join("+", widths.Select(w => new string('-', w + 2))) + "+";

        if (!string.IsNullOrWhiteSpace(table.Title))
        {
            text.AppendLine(ExportText.SingleLine(table.Title));
        }

        text.AppendLine(separator);
        AppendRow(text, header, widths, numeric: []);
        text.AppendLine(separator);
        foreach (var row in rows)
        {
            AppendRow(text, row, widths, numeric);
        }

        if (footer is not null)
        {
            text.AppendLine(separator);
            AppendRow(text, footer, widths, numeric);
        }

        text.AppendLine(separator);
    }

    private static void AppendRow(StringBuilder text, string[] cells, int[] widths, HashSet<int> numeric)
    {
        text.Append('|');
        for (var c = 0; c < cells.Length; c++)
        {
            var cell = numeric.Contains(c) ? cells[c].PadLeft(widths[c]) : cells[c].PadRight(widths[c]);
            text.Append(' ').Append(cell).Append(" |");
        }

        text.AppendLine();
    }

    private static string[] Normalize(IReadOnlyList<string> cells, int columnCount)
    {
        var result = new string[columnCount];
        for (var c = 0; c < columnCount; c++)
        {
            var value = ExportText.SingleLine(c < cells.Count ? cells[c] : string.Empty);
            result[c] = value.Length > MaxCellWidth ? value[..(MaxCellWidth - 3)] + "..." : value;
        }

        return result;
    }
}
