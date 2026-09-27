using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Infrastructure.Exports;

/// <summary>
/// Exports the raw responses of a survey — one row per response, one column per question in display
/// order — as CSV, XLSX or JSON. Values are machine readable (<see cref="RawAnswerFormatter"/>).
/// </summary>
/// <remarks>
/// Responses are read in batches of <see cref="BatchSize"/> so the answer query stays small; the
/// finished file is built in memory.
/// </remarks>
public sealed class ResponseExportService(
    IAppDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider time,
    IAuditService audit,
    ILogger<ResponseExportService> logger) : IResponseExportService
{
    /// <summary>Value of the JSON envelope's <c>format</c> property.</summary>
    public const string JsonFormatName = "smartsurvey.responses";

    /// <summary>Responses loaded per answer query.</summary>
    internal const int BatchSize = 500;

    /// <summary>Maximum length of question headers.</summary>
    private const int HeaderMaxLength = 100;

    /// <summary>Fixed columns preceding the question columns.</summary>
    private static readonly string[] FixedColumns = ["Response ID", "Status", "Started (UTC)", "Submitted (UTC)", "Duration (seconds)", "Respondent"];

    /// <summary>Index of the numeric "Duration (seconds)" column.</summary>
    private const int DurationColumn = 4;

    /// <inheritdoc />
    public IReadOnlyList<ExportFormat> SupportedFormats { get; } = [ExportFormat.Csv, ExportFormat.Xlsx, ExportFormat.Json];

    /// <inheritdoc />
    public async Task<ExportFile> ExportResponsesAsync(Guid surveyId, ExportFormat format, bool includeInProgress = false, CancellationToken ct = default)
    {
        if (!currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only administrators can export responses.");
        }

        if (!SupportedFormats.Contains(format))
        {
            throw new BusinessRuleException($"Responses can be exported as CSV, XLSX or JSON, not {format.ToString().ToUpperInvariant()}.");
        }

        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await LoadSurveyAsync(db, surveyId, ct);
        var questions = survey.AllQuestions().ToList();
        var rows = await LoadRowsAsync(db, surveyId, questions, includeInProgress, ct);
        var now = time.GetUtcNow().UtcDateTime;

        var content = format switch
        {
            ExportFormat.Csv => ToCsv(questions, rows),
            ExportFormat.Xlsx => ToXlsx(survey, questions, rows, now),
            _ => ToJson(survey, questions, rows, now),
        };

        logger.LogInformation("Exported {Count} responses of survey {SurveyId} as {Format}", rows.Count, surveyId, format);
        await audit.LogAsync(
            AuditActions.ResponsesExported, "Survey", surveyId.ToString(),
            $"Exported {rows.Count} response(s) of '{survey.Title}' as {format.ToString().ToUpperInvariant()}{(includeInProgress ? " (including in-progress)" : string.Empty)}.",
            ct);

        return new ExportFile(content, format.ContentType(), ExportFormatExtensions.BuildFileName($"{survey.Title} responses", format, now));
    }

    private static async Task<SurveyDefinitionDto> LoadSurveyAsync(IAppDbContext db, Guid surveyId, CancellationToken ct)
    {
        var survey = await db.Surveys.AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.Sections)
            .Include(s => s.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(s => s.Id == surveyId, ct)
            ?? throw new NotFoundException("Survey", surveyId);
        return survey.ToDefinitionDto();
    }

    /// <summary>Loads the responses (oldest first) with their answers, formatted per question.</summary>
    private static async Task<List<ExportRow>> LoadRowsAsync(
        IAppDbContext db, Guid surveyId, List<QuestionDto> questions, bool includeInProgress, CancellationToken ct)
    {
        var query = db.Responses.AsNoTracking().Where(r => r.SurveyId == surveyId);
        if (!includeInProgress)
        {
            query = query.Where(r => r.Status == ResponseStatus.Completed);
        }

        var responses = await query
            .OrderBy(r => r.SubmittedAt ?? r.StartedAt)
            .ThenBy(r => r.Id)
            .Select(r => new { r.Id, r.Status, r.StartedAt, r.SubmittedAt, r.RespondentId, Email = r.Respondent != null ? r.Respondent.Email : null })
            .ToListAsync(ct);

        var rows = new List<ExportRow>(responses.Count);
        foreach (var batch in responses.Chunk(BatchSize))
        {
            var ids = batch.Select(r => r.Id).ToList();
            var answers = (await db.Answers.AsNoTracking()
                    .Include(a => a.Selections)
                    .Where(a => ids.Contains(a.ResponseId))
                    .ToListAsync(ct))
                .GroupBy(a => a.ResponseId)
                .ToDictionary(g => g.Key, g => g.ToDictionary(a => a.QuestionId, AnswerMapper.ToInputDto));

            foreach (var response in batch)
            {
                var responseAnswers = answers.GetValueOrDefault(response.Id);
                rows.Add(new ExportRow(
                    response.Id,
                    response.Status,
                    response.StartedAt,
                    response.SubmittedAt,
                    response.RespondentId is null ? "Anonymous" : response.Email ?? "Unknown user",
                    questions.Select(q => RawAnswerFormatter.Format(q, responseAnswers?.GetValueOrDefault(q.Id))).ToList()));
            }
        }

        return rows;
    }

    private static TableData ToTable(List<QuestionDto> questions, List<ExportRow> rows) => new()
    {
        Columns = [.. FixedColumns, .. questions.Select(Header)],
        NumericColumns =
        [
            DurationColumn,
            .. questions.Select((q, i) => (q, i)).Where(x => x.q.Type.IsNumeric()).Select(x => FixedColumns.Length + x.i),
        ],
        Rows = rows.Select(List<string> (r) =>
        [
            r.Id.ToString(),
            r.Status.ToString(),
            Timestamp(r.StartedAt),
            r.SubmittedAt is { } submitted ? Timestamp(submitted) : string.Empty,
            r.DurationSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            r.Respondent,
            .. r.Values,
        ]).ToList(),
    };

    private static byte[] ToCsv(List<QuestionDto> questions, List<ExportRow> rows) =>
        new CsvBuilder().Table(ToTable(questions, rows)).ToBytes();

    private static byte[] ToXlsx(SurveyDefinitionDto survey, List<QuestionDto> questions, List<ExportRow> rows, DateTime now)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = $"{survey.Title} – responses";

        var responses = workbook.Worksheets.Add("Responses");
        var writer = new SheetWriter(responses);
        writer.Table(ToTable(questions, rows));
        writer.FitColumns();
        responses.SheetView.FreezeRows(1);
        if (rows.Count > 0)
        {
            responses.Range(1, 1, rows.Count + 1, FixedColumns.Length + questions.Count).SetAutoFilter();
        }

        var legend = new SheetWriter(workbook.Worksheets.Add("Questions"));
        legend.Title(survey.Title);
        legend.Muted($"{rows.Count} response(s) exported on {ExportText.Timestamp(now)}");
        legend.Skip();
        legend.Table(new TableData
        {
            Columns = ["Code", "Question", "Type"],
            Rows = questions.Select(q => new List<string> { q.Code ?? string.Empty, q.Text, q.Type.DisplayName() }).ToList(),
        });
        legend.FitColumns();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] ToJson(SurveyDefinitionDto survey, List<QuestionDto> questions, List<ExportRow> rows, DateTime now)
    {
        // Answers are keyed by question code (unique per survey), falling back to the id.
        var keys = questions.Select(q => string.IsNullOrWhiteSpace(q.Code) ? q.Id.ToString() : q.Code).ToList();
        var document = new
        {
            format = JsonFormatName,
            version = 1,
            exportedAt = now,
            survey = new { id = survey.Id, title = survey.Title },
            questions = questions.Select((q, i) => new { key = keys[i], id = q.Id, code = q.Code, text = q.Text, type = q.Type }),
            responses = rows.Select(r => new
            {
                id = r.Id,
                status = r.Status,
                startedAt = r.StartedAt,
                submittedAt = r.SubmittedAt,
                durationSeconds = r.DurationSeconds,
                respondent = r.Respondent,
                answers = keys
                    .Select((key, i) => (key, value: r.Values[i]))
                    .Where(x => x.value.Length > 0)
                    .ToDictionary(x => x.key, x => x.value),
            }),
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, JsonReportExporter.Options);
    }

    private static string Header(QuestionDto question)
    {
        var text = ExportText.SingleLine(question.Text).Trim();
        var label = string.IsNullOrWhiteSpace(question.Code) ? text : $"{question.Code}. {text}";
        return label.Length <= HeaderMaxLength ? label : label[..(HeaderMaxLength - 1)] + "…";
    }

    private static string Timestamp(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>A response with its formatted answers (aligned with the question list).</summary>
    private sealed record ExportRow(Guid Id, ResponseStatus Status, DateTime StartedAt, DateTime? SubmittedAt, string Respondent, List<string> Values)
    {
        /// <summary>Whole seconds between start and submission (completed responses only).</summary>
        public long? DurationSeconds => SubmittedAt is { } submitted ? (long)Math.Max(0, (submitted - StartedAt).TotalSeconds) : null;
    }
}
