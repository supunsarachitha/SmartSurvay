using SmartSurvey.Application.Common;
using SmartSurvey.Application.Exports;

namespace SmartSurvey.Application.Reports;

// STUB - replaced in Phase 4C (see DEVELOPMENT_PLAN.md). Kept compiling so DI wiring is complete.
/// <summary>Report management, execution and export.</summary>
public sealed class ReportService : IReportService
{
    public Task<PagedResult<ReportSummaryDto>> ListAsync(ReportQuery query, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ReportDefinitionDto> GetAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ReportDefinitionDto> CreateAsync(ReportDefinitionDto dto, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ReportDefinitionDto> UpdateAsync(Guid id, ReportDefinitionDto dto, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ReportDefinitionDto> DuplicateAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ReportDefinitionDto> BuildDefaultAsync(Guid surveyId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ReportResult> RunAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ReportResult> PreviewAsync(ReportDefinitionDto dto, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ExportFile> ExportAsync(Guid id, ExportFormat format, CancellationToken ct = default) => throw new NotImplementedException();
}
