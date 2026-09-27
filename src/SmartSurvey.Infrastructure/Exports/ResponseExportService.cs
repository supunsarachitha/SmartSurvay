using SmartSurvey.Application.Exports;

namespace SmartSurvey.Infrastructure.Exports;

// STUB - replaced in Phase 4C (see DEVELOPMENT_PLAN.md). Kept compiling so DI wiring is complete.
/// <summary>Raw response export (CSV/XLSX/JSON).</summary>
public sealed class ResponseExportService : IResponseExportService
{
    public IReadOnlyList<ExportFormat> SupportedFormats { get; } = [ExportFormat.Csv, ExportFormat.Xlsx, ExportFormat.Json];
    public Task<ExportFile> ExportResponsesAsync(Guid surveyId, ExportFormat format, bool includeInProgress = false, CancellationToken ct = default) => throw new NotImplementedException();
}
