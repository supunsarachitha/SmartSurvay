using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>A dated free-text answer.</summary>
/// <param name="Submitted">Response timestamp (submitted, else started).</param>
/// <param name="OptionId">Option the text belongs to (choice questions), otherwise null.</param>
/// <param name="Text">The text.</param>
internal sealed record DatedText(DateTime Submitted, Guid? OptionId, string Text);

/// <summary>A page of the latest free-text answers plus the total number of such answers.</summary>
/// <param name="Items">Latest answers first.</param>
/// <param name="Total">Total number of matching answers.</param>
internal sealed record TextPage(IReadOnlyList<DatedText> Items, int Total);

/// <summary>
/// Answer queries restricted to the filtered response set. Aggregations (counts, groupings) run in
/// the database; only single value columns are materialised for numeric/date statistics. All
/// queries use simple, provider-portable LINQ (PostgreSQL in production, SQLite in tests).
/// </summary>
internal static class ReportAnswerQueries
{
    /// <summary>Answers of one question within the filtered responses.</summary>
    public static IQueryable<Answer> AnswersOf(ReportRunContext ctx, Guid questionId) =>
        ctx.Db.Answers.AsNoTracking().Where(a => a.QuestionId == questionId && ctx.ResponseIds.Contains(a.ResponseId));

    /// <summary>Number of filtered responses that answered the question.</summary>
    public static Task<int> AnsweredCountAsync(ReportRunContext ctx, QuestionDto question)
    {
        var answers = AnswersOf(ctx, question.Id);
        var type = question.Type;
        answers = type switch
        {
            _ when type.IsChoice() => answers.Where(a => a.Selections.Any()),
            _ when type.IsNumeric() => answers.Where(a => a.NumberValue != null),
            _ when type.IsDate() => answers.Where(a => a.DateValue != null),
            _ => answers.Where(a => a.TextValue != null && a.TextValue != ""),
        };
        return answers.CountAsync(ctx.CancellationToken);
    }

    /// <summary>Selection count per option (GROUP BY OptionId in the database).</summary>
    public static async Task<Dictionary<Guid, int>> ChoiceCountsAsync(ReportRunContext ctx, Guid questionId)
    {
        var counts = await AnswersOf(ctx, questionId)
            .SelectMany(a => a.Selections)
            .GroupBy(s => s.OptionId)
            .Select(g => new { OptionId = g.Key, Count = g.Count() })
            .ToListAsync(ctx.CancellationToken);
        return counts.ToDictionary(c => c.OptionId, c => c.Count);
    }

    /// <summary>Numeric answers (only the NumberValue column is read).</summary>
    public static Task<List<double>> NumberValuesAsync(ReportRunContext ctx, Guid questionId) =>
        AnswersOf(ctx, questionId)
            .Where(a => a.NumberValue != null)
            .Select(a => a.NumberValue!.Value)
            .ToListAsync(ctx.CancellationToken);

    /// <summary>Date answers (only the DateValue column is read).</summary>
    public static Task<List<DateOnly>> DateValuesAsync(ReportRunContext ctx, Guid questionId) =>
        AnswersOf(ctx, questionId)
            .Where(a => a.DateValue != null)
            .Select(a => a.DateValue!.Value)
            .ToListAsync(ctx.CancellationToken);

    /// <summary>Latest non-empty text answers of a text question.</summary>
    public static async Task<TextPage> LatestTextAnswersAsync(ReportRunContext ctx, Guid questionId, int maxRows)
    {
        var answers = AnswersOf(ctx, questionId).Where(a => a.TextValue != null && a.TextValue != "");
        var total = await answers.CountAsync(ctx.CancellationToken);
        var items = await answers
            .Select(a => new { Submitted = a.Response!.SubmittedAt ?? a.Response.StartedAt, a.TextValue, a.Id })
            .OrderByDescending(x => x.Submitted)
            .ThenBy(x => x.Id)
            .Take(maxRows)
            .ToListAsync(ctx.CancellationToken);
        return new TextPage(items.Select(x => new DatedText(x.Submitted, null, x.TextValue!)).ToList(), total);
    }

    /// <summary>Latest numeric or date answers, formatted like raw data (invariant numbers, ISO dates).</summary>
    public static async Task<TextPage> LatestValueAnswersAsync(ReportRunContext ctx, QuestionDto question, int maxRows)
    {
        var isDate = question.Type.IsDate();
        var answers = isDate
            ? AnswersOf(ctx, question.Id).Where(a => a.DateValue != null)
            : AnswersOf(ctx, question.Id).Where(a => a.NumberValue != null);
        var total = await answers.CountAsync(ctx.CancellationToken);
        var items = await answers
            .Select(a => new { Submitted = a.Response!.SubmittedAt ?? a.Response.StartedAt, a.NumberValue, a.DateValue, a.Id })
            .OrderByDescending(x => x.Submitted)
            .ThenBy(x => x.Id)
            .Take(maxRows)
            .ToListAsync(ctx.CancellationToken);
        return new TextPage(
            items.Select(x => new DatedText(
                x.Submitted,
                null,
                isDate ? AnswerFormatter.FormatDate(x.DateValue!.Value) : AnswerFormatter.FormatNumber(x.NumberValue!.Value))).ToList(),
            total);
    }

    /// <summary>Latest non-empty free texts entered for the given options of a choice question.</summary>
    public static async Task<TextPage> LatestFreeTextsAsync(ReportRunContext ctx, Guid questionId, IReadOnlyCollection<Guid> optionIds, int maxRows)
    {
        if (optionIds.Count == 0)
        {
            return new TextPage([], 0);
        }

        var ids = optionIds.ToList();
        var selections = AnswersOf(ctx, questionId)
            .SelectMany(a => a.Selections, (a, s) => new { a.ResponseId, s.Id, s.OptionId, s.FreeText })
            .Where(s => ids.Contains(s.OptionId) && s.FreeText != null && s.FreeText != "");
        var total = await selections.CountAsync(ctx.CancellationToken);
        var items = await selections
            .Join(
                ctx.Db.Responses,
                s => s.ResponseId,
                r => r.Id,
                (s, r) => new { Submitted = r.SubmittedAt ?? r.StartedAt, s.Id, s.OptionId, s.FreeText })
            .OrderByDescending(x => x.Submitted)
            .ThenBy(x => x.Id)
            .Take(maxRows)
            .ToListAsync(ctx.CancellationToken);
        return new TextPage(items.Select(x => new DatedText(x.Submitted, x.OptionId, x.FreeText!.Trim())).ToList(), total);
    }
}
