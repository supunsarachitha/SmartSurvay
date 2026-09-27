using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;

namespace SmartSurvey.Infrastructure.Exports;

// STUB - replaced in Phase 4C (see DEVELOPMENT_PLAN.md). Kept compiling so DI wiring is complete.
/// <summary>Exports reports as Txt.</summary>
public sealed class TxtReportExporter : IReportExporter
{
    public ExportFormat Format => ExportFormat.Txt;
    public Task<byte[]> ExportAsync(ReportResult report, CancellationToken ct = default) => throw new NotImplementedException();
}
