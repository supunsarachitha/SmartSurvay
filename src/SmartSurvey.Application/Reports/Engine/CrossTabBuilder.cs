using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>
/// Cross-tabulation of two choice questions: rows are the primary question's options, columns the
/// secondary question's options and each cell counts the responses that selected both.
/// </summary>
internal static class CrossTabBuilder
{
    /// <summary>Error shown when one of the questions is not a choice question.</summary>
    public const string NonChoiceError =
        "Cross-tabulation needs two choice questions (single choice, multiple choice or dropdown).";

    /// <summary>Fills the table (with row/column totals) and a stacked bar chart (one series per column option).</summary>
    public static async Task BuildAsync(ReportRunContext ctx, QuestionDto primary, QuestionDto secondary, WidgetResult result)
    {
        if (!primary.Type.IsChoice() || !secondary.Type.IsChoice())
        {
            result.Error = NonChoiceError;
            return;
        }

        var cells = await CountPairsAsync(ctx, primary.Id, secondary.Id);
        var rows = primary.Options.OrderBy(o => o.Order).ToList();
        var columns = secondary.Options.OrderBy(o => o.Order).ToList();
        int Cell(Guid row, Guid column) => cells.GetValueOrDefault((row, column));

        var table = new TableData
        {
            Columns = [ReportFormat.QuestionLabel(primary, 40), .. columns.Select(c => c.Text), "Total"],
            NumericColumns = Enumerable.Range(1, columns.Count + 1).ToList(),
        };
        foreach (var row in rows)
        {
            var counts = columns.Select(c => Cell(row.Id, c.Id)).ToList();
            table.Rows.Add([row.Text, .. counts.Select(ReportFormat.Count), ReportFormat.Count(counts.Sum())]);
        }

        var columnTotals = columns.Select(c => rows.Sum(r => Cell(r.Id, c.Id))).ToList();
        table.Footer = ["Total", .. columnTotals.Select(ReportFormat.Count), ReportFormat.Count(columnTotals.Sum())];

        result.Table = table;
        result.ResponseCount = columnTotals.Sum();
        result.Chart = new ChartData
        {
            Kind = ChartKind.StackedBar,
            Labels = rows.Select(r => r.Text).ToList(),
            Series = columns.Select(c => new ChartSeries
            {
                Name = c.Text,
                Values = rows.Select(r => (double)Cell(r.Id, c.Id)).ToList(),
            }).ToList(),
            ValueAxisTitle = WidgetOutput.ResponsesSeries,
        };

        if (primary.Type.IsMultiSelect() || secondary.Type.IsMultiSelect())
        {
            result.Note = "A multiple-choice question is involved, so a response can be counted in more than one cell and totals count selections.";
        }
    }

    /// <summary>
    /// Counts (primary option, secondary option) pairs per response in the database: the selections
    /// of both questions are joined on the response and grouped by the two option ids.
    /// </summary>
    private static async Task<Dictionary<(Guid Row, Guid Column), int>> CountPairsAsync(ReportRunContext ctx, Guid primaryId, Guid secondaryId)
    {
        var primary = ReportAnswerQueries.AnswersOf(ctx, primaryId)
            .SelectMany(a => a.Selections, (a, s) => new { a.ResponseId, s.OptionId });
        var secondary = ReportAnswerQueries.AnswersOf(ctx, secondaryId)
            .SelectMany(a => a.Selections, (a, s) => new { a.ResponseId, s.OptionId });

        var pairs = await primary
            .Join(secondary, p => p.ResponseId, s => s.ResponseId, (p, s) => new { Row = p.OptionId, Column = s.OptionId })
            .GroupBy(x => new { x.Row, x.Column })
            .Select(g => new { g.Key.Row, g.Key.Column, Count = g.Count() })
            .ToListAsync(ctx.CancellationToken);

        return pairs.ToDictionary(p => (p.Row, p.Column), p => p.Count);
    }
}
