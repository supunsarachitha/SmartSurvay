using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSurvey.Domain.Entities;

namespace SmartSurvey.Infrastructure.Persistence.Encryption;

/// <summary>
/// Encrypts values that were stored before field encryption was enabled (run at start-up; idempotent). Rows are
/// found with plain SQL — values without the <c>enc:v1:</c> prefix — and re-saved through the encrypting converters
/// in batches.
/// </summary>
public sealed class FieldEncryptionMigrator(AppDbContext db, IOptions<FieldEncryptionOptions> options, ILogger<FieldEncryptionMigrator> logger)
{
    private const int BatchSize = 500;
    private const string Pattern = DataProtectionFieldProtector.Prefix + "%";

    /// <summary>Encrypts every remaining plain-text value; returns the number of rows updated.</summary>
    public async Task<int> EncryptExistingAsync(CancellationToken ct = default)
    {
        if (!options.Value.Enabled)
        {
            return 0;
        }

        var total = await EncryptAsync<Answer>("Answers", "TextValue", a => a.TextValue, ct)
            + await EncryptAsync<AnswerSelection>("AnswerSelections", "FreeText", s => s.FreeText, ct)
            + await EncryptAsync<SurveyResponse>("Responses", "UserAgent", r => r.UserAgent, ct);
        if (total > 0)
        {
            logger.LogInformation("Encrypted {Count} existing answer values that were stored before encryption was enabled.", total);
        }

        return total;
    }

    private async Task<int> EncryptAsync<TEntity>(
        string table, string column, System.Linq.Expressions.Expression<Func<TEntity, string?>> property, CancellationToken ct)
        where TEntity : Domain.Common.Entity
    {
        var updated = 0;
        while (true)
        {
            // Table and column names are constants of this class (never user input).
#pragma warning disable EF1002
            var ids = await db.Database
                .SqlQueryRaw<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL AND \"{column}\" NOT LIKE {{0}}", Pattern)
                .Take(BatchSize)
                .ToListAsync(ct);
#pragma warning restore EF1002
            if (ids.Count == 0)
            {
                return updated;
            }

            var rows = await db.Set<TEntity>().Where(e => ids.Contains(e.Id)).ToListAsync(ct);
            foreach (var row in rows)
            {
                db.Entry(row).Property(property).IsModified = true; // re-written through the encrypting converter
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            updated += rows.Count;
        }
    }
}
