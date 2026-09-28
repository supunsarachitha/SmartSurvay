using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Common;

namespace SmartSurvey.Infrastructure.Persistence;

/// <summary>
/// Stamps <see cref="AuditableEntity"/> creation/modification metadata on every SaveChanges.
/// Registered as a scoped service so it sees the current user of the request / circuit.
/// </summary>
public sealed class AuditableEntityInterceptor(TimeProvider timeProvider, ICurrentUser currentUser) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var userId = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAt == default)
                    {
                        entry.Entity.CreatedAt = now;
                    }

                    entry.Entity.CreatedById ??= userId;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedById = userId;
                    break;
            }
        }
    }
}

/// <summary>
/// Adapts EF Core's <see cref="IDbContextFactory{TContext}"/> to the Application abstraction and binds
/// every context to a <see cref="DataScope"/>.
/// </summary>
internal sealed class AppDbContextFactory(IDbContextFactory<AppDbContext> factory, ICurrentUser currentUser) : IAppDbContextFactory
{
    /// <inheritdoc />
    public Task<IAppDbContext> CreateAsync(CancellationToken cancellationToken = default) =>
        CreateAsync(currentUser.WorkspaceId is { } id ? DataScope.ForWorkspace(id) : DataScope.None, cancellationToken);

    /// <inheritdoc />
    public Task<IAppDbContext> CreateForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        CreateAsync(DataScope.ForWorkspace(workspaceId), cancellationToken);

    /// <inheritdoc />
    public Task<IAppDbContext> CreateSystemAsync(CancellationToken cancellationToken = default) =>
        CreateAsync(DataScope.System, cancellationToken);

    private async Task<IAppDbContext> CreateAsync(DataScope scope, CancellationToken cancellationToken)
    {
        var db = await factory.CreateDbContextAsync(cancellationToken);
        return db.UseScope(scope);
    }
}

/// <summary>Supported database providers.</summary>
public enum DatabaseProvider
{
    /// <summary>PostgreSQL via Npgsql (default; schema managed by EF Core migrations).</summary>
    PostgreSQL = 0,

    /// <summary>SQLite (demos and tests; schema created with EnsureCreated, no migrations).</summary>
    Sqlite = 1,
}

/// <summary>"Database" configuration section.</summary>
public sealed class DatabaseOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Database";

    /// <summary>Provider to use.</summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.PostgreSQL;

    /// <summary>Apply pending migrations at start-up (PostgreSQL only).</summary>
    public bool ApplyMigrationsOnStartup { get; set; } = true;
}

/// <summary>"Seed" configuration section.</summary>
public sealed class SeedOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Seed";

    /// <summary>Create the initial admin account when no admin exists (requires <see cref="AdminPassword"/>).</summary>
    public bool CreateAdmin { get; set; } = true;

    /// <summary>Initial admin e-mail.</summary>
    public string AdminEmail { get; set; } = "admin@smartsurvey.local";

    /// <summary>Name of the workspace created for the initial admin and the demo data.</summary>
    public string WorkspaceName { get; set; } = "Default workspace";

    /// <summary>Slug (address) of that workspace; an existing workspace with this slug is reused.</summary>
    public string WorkspaceSlug { get; set; } = "default";

    /// <summary>Initial admin password. Leave empty in production and set it via environment/secrets.</summary>
    public string? AdminPassword { get; set; }

    /// <summary>Create demo user, example surveys, responses and a report (development/demo only).</summary>
    public bool DemoData { get; set; }

    /// <summary>Demo respondent e-mail.</summary>
    public string DemoUserEmail { get; set; } = "user@smartsurvey.local";

    /// <summary>Demo respondent password.</summary>
    public string DemoUserPassword { get; set; } = "User123!";
}

/// <summary>
/// Used by <c>dotnet ef</c> to create the context at design time (migrations are PostgreSQL-specific).
/// Override the connection with the <c>SMARTSURVEY_MIGRATIONS_CONNECTION</c> environment variable.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc />
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SMARTSURVEY_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=smartsurvey;Username=smartsurvey;Password=smartsurvey";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
