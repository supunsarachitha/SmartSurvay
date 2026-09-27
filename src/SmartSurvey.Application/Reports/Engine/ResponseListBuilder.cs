using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports.Engine;

/// <summary>List widgets: the latest text answers of one question and the raw response grid.</summary>
internal static class ResponseListBuilder
{
    /// <summary>Maximum length of the question text in raw grid column headers.</summary>
    private const int HeaderTextLength = 40;

    /// <summary>
    /// Latest answers of a question, newest first: text answers, the free texts of "Other"-style
    /// options for choice questions, or formatted values for numeric/date questions.
    /// </summary>
    public static async Task BuildTextAsync(ReportRunContext ctx, QuestionDto question, WidgetSettings settings, WidgetResult result)
    {
        var type = question.Type;
        TextPage page;
        string? note = null;

        if (type.IsChoice())
        {
            var freeTextOptions = question.Options.Where(o => o.AllowsFreeText).ToList();
            page = await ReportAnswerQueries.LatestFreeTextsAsync(ctx, question.Id, freeTextOptions.Select(o => o.Id).ToList(), settings.MaxRows);
            page = LabelFreeTexts(page, freeTextOptions);
            if (freeTextOptions.Count == 0)
            {
                note = "This question has no free-text option (such as “Other, please specify”), so there are no text answers to list.";
            }
        }
        else if (type.IsText())
        {
            page = await ReportAnswerQueries.LatestTextAnswersAsync(ctx, question.Id, settings.MaxRows);
        }
        else
        {
            page = await ReportAnswerQueries.LatestValueAnswersAsync(ctx, question, settings.MaxRows);
        }

        result.ResponseCount = page.Total;
        result.Stats = [new StatItem("Answers", ReportFormat.Count(page.Total))];
        result.Table = WidgetOutput.ListTable("Submitted", "Answer", page.Items.Select(t => (ReportFormat.Timestamp(t.Submitted), t.Text)));
        result.Note = note ?? WidgetOutput.TruncationNote(page.Items.Count, page.Total, "answers");
    }

    /// <summary>
    /// Raw grid of the latest responses: Submitted, Respondent, then one column per selected question
    /// (all questions in display order when none are selected) with machine-readable values.
    /// </summary>
    public static async Task BuildRawAsync(ReportRunContext ctx, WidgetSettings settings, WidgetResult result)
    {
        var columns = SelectColumns(ctx, settings.ColumnQuestionIds);
        var page = ctx.Responses
            .OrderByDescending(r => r.Timestamp)
            .ThenBy(r => r.Id)
            .Take(settings.MaxRows)
            .ToList();

        var answers = await LoadAnswersAsync(ctx, page.Select(r => r.Id).ToList(), columns.Select(q => q.Id).ToList());
        var emails = await LoadEmailsAsync(ctx, page);

        var table = new TableData
        {
            Columns = ["Submitted", "Respondent", .. columns.Select(q => ReportFormat.QuestionLabel(q, HeaderTextLength))],
            NumericColumns = columns
                .Select((q, i) => (q, i))
                .Where(x => x.q.Type.IsNumeric())
                .Select(x => x.i + 2)
                .ToList(),
        };

        foreach (var response in page)
        {
            var responseAnswers = answers.GetValueOrDefault(response.Id);
            table.Rows.Add(
            [
                FormatSubmitted(response),
                response.RespondentId is { } id ? emails.GetValueOrDefault(id) ?? "Unknown user" : "Anonymous",
                .. columns.Select(q => RawAnswerFormatter.Format(q, responseAnswers?.GetValueOrDefault(q.Id))),
            ]);
        }

        result.ResponseCount = ctx.Responses.Count;
        result.Table = table;
        result.Note = WidgetOutput.TruncationNote(page.Count, ctx.Responses.Count, "responses");
    }

    /// <summary>
    /// Adds the option label ("Other: text") when a question has several free-text options, so the
    /// single Answer column stays unambiguous.
    /// </summary>
    private static TextPage LabelFreeTexts(TextPage page, IReadOnlyList<OptionDto> freeTextOptions)
    {
        if (freeTextOptions.Count <= 1)
        {
            return page;
        }

        var labels = freeTextOptions.ToDictionary(o => o.Id, o => o.Text);
        var items = page.Items
            .Select(t => t with { Text = AnswerFormatter.FormatOption(labels.GetValueOrDefault(t.OptionId ?? Guid.Empty, string.Empty), t.Text) })
            .ToList();
        return page with { Items = items };
    }

    /// <summary>Selected columns in display order; unknown (deleted) question ids are skipped.</summary>
    private static List<QuestionDto> SelectColumns(ReportRunContext ctx, IReadOnlyCollection<Guid>? selected)
    {
        if (selected is null || selected.Count == 0)
        {
            return ctx.Questions.ToList();
        }

        var wanted = selected.ToHashSet();
        return ctx.Questions.Where(q => wanted.Contains(q.Id)).ToList();
    }

    /// <summary>Answers of the page's responses for the selected questions, keyed by response and question.</summary>
    private static async Task<Dictionary<Guid, Dictionary<Guid, AnswerInputDto>>> LoadAnswersAsync(
        ReportRunContext ctx, List<Guid> responseIds, List<Guid> questionIds)
    {
        if (responseIds.Count == 0 || questionIds.Count == 0)
        {
            return [];
        }

        var answers = await ctx.Db.Answers
            .AsNoTracking()
            .Include(a => a.Selections)
            .Where(a => responseIds.Contains(a.ResponseId) && questionIds.Contains(a.QuestionId))
            .ToListAsync(ctx.CancellationToken);

        return answers
            .GroupBy(a => a.ResponseId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(a => a.QuestionId, AnswerMapper.ToInputDto));
    }

    /// <summary>E-mail addresses of the page's logged-in respondents.</summary>
    private static async Task<Dictionary<Guid, string?>> LoadEmailsAsync(ReportRunContext ctx, IEnumerable<ResponseRow> page)
    {
        var respondentIds = page.Where(r => r.RespondentId.HasValue).Select(r => r.RespondentId!.Value).Distinct().ToList();
        if (respondentIds.Count == 0)
        {
            return [];
        }

        var users = await ctx.Db.Users
            .AsNoTracking()
            .Where(u => respondentIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ctx.CancellationToken);
        return users.ToDictionary(u => u.Id, u => u.Email);
    }

    /// <summary>Submission timestamp; drafts show their start time marked "(in progress)".</summary>
    private static string FormatSubmitted(ResponseRow response) =>
        response.Status == ResponseStatus.Completed && response.SubmittedAt is { } submitted
            ? ReportFormat.Timestamp(submitted)
            : $"{ReportFormat.Timestamp(response.StartedAt)} (in progress)";
}
