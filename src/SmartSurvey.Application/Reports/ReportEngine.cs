namespace SmartSurvey.Application.Reports;

// STUB - replaced in Phase 4C (see DEVELOPMENT_PLAN.md). Kept compiling so DI wiring is complete.
/// <summary>Computes report results.</summary>
public sealed class ReportEngine : IReportEngine
{
    public Task<ReportResult> ExecuteAsync(ReportDefinitionDto definition, CancellationToken ct = default) => throw new NotImplementedException();
}
