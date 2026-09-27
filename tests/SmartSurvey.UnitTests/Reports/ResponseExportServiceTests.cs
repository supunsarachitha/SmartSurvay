using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Exports;
using SmartSurvey.Infrastructure.Exports;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Reports;

public sealed class ResponseExportServiceTests : IAsyncLifetime
{
    private ReportTestHarness _h = null!;
    private SampleSurvey _s = null!;
    private ResponseExportService _service = null!;

    public async Task InitializeAsync()
    {
        _h = await ReportTestHarness.CreateAsync();
        _s = await _h.SeedSurveyWithResponsesAsync();
        _service = new ResponseExportService(_h.Db, _h.User, _h.Db.Time, _h.Audit, NullLogger<ResponseExportService>.Instance);
    }

    public async Task DisposeAsync() => await _h.DisposeAsync();

    [Fact]
    public async Task Csv_has_fixed_columns_then_one_column_per_question_and_one_row_per_completed_response()
    {
        var file = await _service.ExportResponsesAsync(_s.Definition.Id, ExportFormat.Csv);

        var lines = Encoding.UTF8.GetString(file.Content).TrimStart('﻿').TrimEnd().Split("\r\n");
        Assert.Equal(
            "Response ID,Status,Started (UTC),Submitted (UTC),Duration (seconds),Respondent,"
            + "Q1. Did you enjoy the product?,Q2. Which features do you use?,Q3. Why not?,Q4. Rate us,Q5. Your e-mail,Q6. Your age,Q7. Region",
            lines[0]);
        Assert.Equal(5, lines.Length); // header + four completed responses, oldest first
        Assert.EndsWith(",Completed," + Stamp(_h.Now.AddDays(-3).AddMinutes(-4)) + "," + Stamp(_h.Now.AddDays(-3))
            + ",240,user@test.local,Yes,Reports; Other: Exports,,5,,30,Europe", lines[1]);
        Assert.Contains(",Anonymous,", lines[2]);
        Assert.Contains(lines, l => l.EndsWith(",No,,Too slow,2,,50,Other: Oceania"));
        Assert.Equal("text/csv", file.ContentType);
        Assert.Equal($"customer-feedback-responses-{_h.Now:yyyyMMdd-HHmm}.csv", file.FileName);
    }

    [Fact]
    public async Task In_progress_responses_are_included_on_request()
    {
        var file = await _service.ExportResponsesAsync(_s.Definition.Id, ExportFormat.Csv, includeInProgress: true);

        var lines = Encoding.UTF8.GetString(file.Content).TrimEnd().Split("\r\n");
        Assert.Equal(6, lines.Length);
        Assert.Contains(lines, l => l.Contains(",InProgress,") && l.Contains(",,,Anonymous,No,"));
    }

    [Fact]
    public async Task Xlsx_has_a_filterable_responses_sheet_with_numbers_and_a_question_legend()
    {
        var file = await _service.ExportResponsesAsync(_s.Definition.Id, ExportFormat.Xlsx);

        using var workbook = new XLWorkbook(new MemoryStream(file.Content));
        var sheet = workbook.Worksheet("Responses");
        Assert.Equal("Response ID", sheet.Cell(1, 1).GetString());
        Assert.Equal(5, sheet.LastRowUsed()!.RowNumber());
        Assert.True(sheet.AutoFilter.IsEnabled);
        Assert.Equal(XLDataType.Number, sheet.Cell(2, 5).DataType); // duration
        Assert.Equal(5, sheet.Cell(2, 10).GetDouble());               // Q4 rating as a number
        Assert.Contains(workbook.Worksheet("Questions").CellsUsed(), c => c.GetString() == "Rate us");
    }

    [Fact]
    public async Task Json_keys_answers_by_question_code_and_omits_unanswered_questions()
    {
        var file = await _service.ExportResponsesAsync(_s.Definition.Id, ExportFormat.Json);

        using var json = JsonDocument.Parse(file.Content);
        var root = json.RootElement;
        Assert.Equal(ResponseExportService.JsonFormatName, root.GetProperty("format").GetString());
        Assert.Equal(7, root.GetProperty("questions").GetArrayLength());
        var first = root.GetProperty("responses")[0];
        Assert.Equal("Completed", first.GetProperty("status").GetString());
        Assert.Equal(240, first.GetProperty("durationSeconds").GetInt64());
        var answers = first.GetProperty("answers");
        Assert.Equal("Reports; Other: Exports", answers.GetProperty("Q2").GetString());
        Assert.False(answers.TryGetProperty("Q3", out _));
    }

    [Fact]
    public async Task Export_is_audited()
    {
        await _service.ExportResponsesAsync(_s.Definition.Id, ExportFormat.Csv);

        var entry = Assert.Single(_h.Audit.Entries, e => e.Action == AuditActions.ResponsesExported);
        Assert.Equal(_s.Definition.Id.ToString(), entry.EntityId);
        Assert.Contains("4 response(s)", entry.Details);
    }

    [Theory]
    [InlineData(ExportFormat.Pdf)]
    [InlineData(ExportFormat.Txt)]
    public async Task Unsupported_formats_are_rejected(ExportFormat format) =>
        await Assert.ThrowsAsync<BusinessRuleException>(() => _service.ExportResponsesAsync(_s.Definition.Id, format));

    [Fact]
    public async Task Unknown_survey_throws_not_found() =>
        await Assert.ThrowsAsync<NotFoundException>(() => _service.ExportResponsesAsync(Guid.NewGuid(), ExportFormat.Csv));

    [Fact]
    public async Task Only_administrators_can_export()
    {
        _h.User.ActAsRespondent();

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.ExportResponsesAsync(_s.Definition.Id, ExportFormat.Csv));
    }

    private static string Stamp(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm:ss");
}
