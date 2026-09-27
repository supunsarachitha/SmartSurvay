namespace SmartSurvey.Application.Dashboard;

// STUB - replaced in Phase 4D (see DEVELOPMENT_PLAN.md). Kept compiling so DI wiring is complete.
/// <summary>Dashboard KPIs.</summary>
public sealed class DashboardService : IDashboardService
{
    public Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default) => throw new NotImplementedException();
}
