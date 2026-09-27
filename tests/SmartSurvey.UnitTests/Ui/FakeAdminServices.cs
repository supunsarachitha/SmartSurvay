using SmartSurvey.Application.Common;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.UnitTests.Ui;

/// <summary><see cref="ISurveyService"/> serving configurable surveys (read operations only).</summary>
public sealed class FakeSurveyService : ISurveyService
{
    public List<SurveyDefinitionDto> Surveys { get; } = [];

    public Task<PagedResult<SurveySummaryDto>> ListAsync(SurveyQuery query, CancellationToken ct = default)
    {
        var items = Surveys.Select(s => new SurveySummaryDto
        {
            Id = s.Id, Title = s.Title, Slug = s.Slug ?? string.Empty, Status = s.Status, QuestionCount = s.AllQuestions().Count(),
        }).ToList();
        return Task.FromResult(new PagedResult<SurveySummaryDto>(items, items.Count, query.Page, query.PageSize));
    }

    public Task<SurveyDefinitionDto> GetAsync(Guid id, CancellationToken ct = default) =>
        Surveys.FirstOrDefault(s => s.Id == id) is { } survey
            ? Task.FromResult(survey)
            : Task.FromException<SurveyDefinitionDto>(new NotFoundException("Survey", id));

    public Task<IReadOnlyList<SurveySummaryDto>> ListTemplatesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto?> FindBySlugAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto> CreateAsync(SurveyDefinitionDto dto, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto> UpdateAsync(Guid id, SurveyDefinitionDto dto, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto> ChangeStatusAsync(Guid id, SurveyStatus status, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto> DuplicateAsync(Guid id, DuplicateSurveyRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyExportDocument> ExportDefinitionAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto> ImportDefinitionAsync(SurveyExportDocument document, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> IsSlugAvailableAsync(string slug, Guid? excludeSurveyId = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> CountAnswersAsync(Guid questionId, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>
/// <see cref="IReportService"/> recording what the UI asks for. Previews echo one empty widget result per
/// widget; <see cref="SaveError"/> makes create/update fail.
/// </summary>
public sealed class FakeReportService : IReportService
{
    public List<ReportDefinitionDto> Saved { get; } = [];

    public List<ReportDefinitionDto> Previews { get; } = [];

    public List<(Guid Id, ExportFormat Format)> Exports { get; } = [];

    public ReportDefinitionDto? Existing { get; set; }

    public ReportResult RunResult { get; set; } = new() { ReportName = "Saved report", SurveyTitle = "Customer feedback", TotalResponses = 3 };

    public ReportDefinitionDto? DefaultReport { get; set; }

    public Exception? SaveError { get; set; }

    public Task<PagedResult<ReportSummaryDto>> ListAsync(ReportQuery query, CancellationToken ct = default) =>
        Task.FromResult(PagedResult<ReportSummaryDto>.Empty(query.Page, query.PageSize));

    public Task<ReportDefinitionDto> GetAsync(Guid id, CancellationToken ct = default) =>
        Existing is { } report && report.Id == id ? Task.FromResult(report) : Task.FromException<ReportDefinitionDto>(new NotFoundException("Report", id));

    public Task<ReportDefinitionDto> CreateAsync(ReportDefinitionDto dto, CancellationToken ct = default) => SaveAsync(dto, Guid.NewGuid());

    public Task<ReportDefinitionDto> UpdateAsync(Guid id, ReportDefinitionDto dto, CancellationToken ct = default) => SaveAsync(dto, id);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

    public Task<ReportDefinitionDto> DuplicateAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<ReportDefinitionDto> BuildDefaultAsync(Guid surveyId, CancellationToken ct = default) =>
        Task.FromResult(DefaultReport ?? throw new NotSupportedException());

    public Task<ReportResult> RunAsync(Guid id, CancellationToken ct = default) => Task.FromResult(RunResult);

    public Task<ReportResult> PreviewAsync(ReportDefinitionDto dto, CancellationToken ct = default)
    {
        Previews.Add(dto);
        return Task.FromResult(new ReportResult
        {
            ReportName = dto.Name,
            TotalResponses = 7,
            Widgets = dto.Widgets.Select(w => new WidgetResult { WidgetId = w.Id, Title = $"Preview of {w.Type}", Type = w.Type }).ToList(),
        });
    }

    public Task<ExportFile> ExportAsync(Guid id, ExportFormat format, CancellationToken ct = default)
    {
        Exports.Add((id, format));
        return Task.FromResult(new ExportFile([1, 2, 3], format.ContentType(), $"report.{format.FileExtension()}"));
    }

    private Task<ReportDefinitionDto> SaveAsync(ReportDefinitionDto dto, Guid id)
    {
        if (SaveError is { } error)
        {
            return Task.FromException<ReportDefinitionDto>(error);
        }

        dto.Id = id;
        Saved.Add(dto);
        return Task.FromResult(dto);
    }
}
