using SmartSurvey.Application.Common;

namespace SmartSurvey.Application.Audit;

// STUB - replaced in Phase 4D (see DEVELOPMENT_PLAN.md). Kept compiling so DI wiring is complete.
/// <summary>Audit trail.</summary>
public sealed class AuditService : IAuditService
{
    public Task LogAsync(string action, string entityType, string? entityId, string? details = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery query, CancellationToken ct = default) => throw new NotImplementedException();
}
