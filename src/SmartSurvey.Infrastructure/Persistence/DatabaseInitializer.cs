using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSurvey.Infrastructure.Persistence.Seed;

namespace SmartSurvey.Infrastructure.Persistence;

/// <summary>Creates/migrates the database and runs seeding at application start-up.</summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// PostgreSQL: applies pending migrations (when enabled). SQLite: <c>EnsureCreated</c>.
    /// Then seeds roles, the initial admin and optional demo data (idempotent).
    /// </summary>
    public static async Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        var db = provider.GetRequiredService<AppDbContext>();

        if (options.Provider == DatabaseProvider.Sqlite)
        {
            logger.LogInformation("Ensuring SQLite database is created");
            await db.Database.EnsureCreatedAsync(ct);
        }
        else if (options.ApplyMigrationsOnStartup)
        {
            logger.LogInformation("Applying PostgreSQL migrations");
            await db.Database.MigrateAsync(ct);
        }

        await provider.GetRequiredService<DbSeeder>().SeedAsync(ct);
    }
}
