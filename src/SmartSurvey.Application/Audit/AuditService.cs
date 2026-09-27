using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Entities;

namespace SmartSurvey.Application.Audit;

/// <summary>
/// Append-only audit trail. Writing is best-effort (an audit failure must never break the business
/// operation that triggered it); reading is restricted to administrators.
/// </summary>
public sealed class AuditService(
    IAppDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<AuditService> logger) : IAuditService
{
    /// <summary>Maximum length of the action code, entity type and entity id (matches the column sizes).</summary>
    public const int MaxCodeLength = 100;

    /// <summary>Maximum length of the stored user name.</summary>
    public const int MaxUserNameLength = 256;

    /// <summary>Maximum length of the details text.</summary>
    public const int MaxDetailsLength = 4000;

    private static readonly AuditQueryValidator QueryValidator = new();

    /// <inheritdoc />
    public async Task LogAsync(string action, string entityType, string? entityId, string? details = null, CancellationToken ct = default)
    {
        try
        {
            // Values are truncated to the column sizes so an over-long detail text can never make
            // the insert fail (PostgreSQL rejects values longer than varchar(n)).
            var entry = new AuditLogEntry
            {
                Timestamp = time.GetUtcNow().UtcDateTime,
                UserId = currentUser.UserId,
                UserName = TruncateOrNull(currentUser.UserName, MaxUserNameLength),
                Action = Truncate(action, MaxCodeLength),
                EntityType = Truncate(entityType, MaxCodeLength),
                EntityId = TruncateOrNull(entityId, MaxCodeLength),
                Details = TruncateOrNull(details, MaxDetailsLength),
            };

            await using var db = await dbFactory.CreateAsync(ct);
            db.AuditLogs.Add(entry);
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancellation requested by the caller is not a failure of the audit trail.
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not write audit entry {Action} for {EntityType} {EntityId}", action, entityType, entityId);
        }
    }

    /// <inheritdoc />
    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only administrators can view the audit log.");
        }

        ThrowIfInvalid(QueryValidator.Validate(query));

        await using var db = await dbFactory.CreateAsync(ct);
        var entries = ApplyFilters(db.AuditLogs.AsNoTracking(), query);

        var total = await entries.CountAsync(ct);
        var items = await entries
            .OrderByDescending(e => e.Timestamp)
            .ThenByDescending(e => e.Id) // stable paging when timestamps are equal
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(e => new AuditLogDto
            {
                Id = e.Id,
                Timestamp = e.Timestamp,
                UserId = e.UserId,
                UserName = e.UserName,
                Action = e.Action,
                EntityType = e.EntityType,
                EntityId = e.EntityId,
                Details = e.Details,
            })
            .ToListAsync(ct);

        return new PagedResult<AuditLogDto>(items, total, query.Page, query.PageSize);
    }

    /// <summary>Applies the (already validated) filters using provider-portable expressions only.</summary>
    private static IQueryable<AuditLogEntry> ApplyFilters(IQueryable<AuditLogEntry> entries, AuditQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ToLower() on both sides gives a case-insensitive search on SQLite and PostgreSQL alike.
            var term = query.Search.Trim().ToLowerInvariant();
            entries = entries.Where(e =>
                e.Action.ToLower().Contains(term)
                || e.EntityType.ToLower().Contains(term)
                || (e.UserName != null && e.UserName.ToLower().Contains(term))
                || (e.Details != null && e.Details.ToLower().Contains(term)));
        }

        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType.Trim();
            entries = entries.Where(e => e.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            var entityId = query.EntityId.Trim();
            entries = entries.Where(e => e.EntityId == entityId);
        }

        // Date filters are inclusive whole UTC days: [From 00:00, To + 1 day 00:00).
        if (query.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entries = entries.Where(e => e.Timestamp >= start);
        }

        if (query.To is { } to)
        {
            var endExclusive = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            entries = entries.Where(e => e.Timestamp < endExclusive);
        }

        return entries;
    }

    /// <summary>Converts FluentValidation failures into an <see cref="AppValidationException"/> keyed by property.</summary>
    private static void ThrowIfInvalid(ValidationResult result)
    {
        if (!result.IsValid)
        {
            throw new AppValidationException(result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()));
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? TruncateOrNull(string? value, int maxLength)
    {
        var truncated = Truncate(value, maxLength);
        return truncated.Length == 0 ? null : truncated;
    }
}
