using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Domain.Entities;
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
/// every context to a <see cref="DataScope"/>. Members of a workspace that is no longer active get no
/// context at all (defence in depth behind the sign-in and request checks).
/// </summary>
internal sealed class AppDbContextFactory(
    IDbContextFactory<AppDbContext> factory, ICurrentUser currentUser, IWorkspaceStatusProvider workspaces) : IAppDbContextFactory
{
    /// <inheritdoc />
    public async Task<IAppDbContext> CreateAsync(CancellationToken cancellationToken = default)
    {
        if (currentUser.WorkspaceId is not { } id)
        {
            return await CreateAsync(DataScope.None, cancellationToken);
        }

        if (await workspaces.GetStatusAsync(id, cancellationToken) != WorkspaceStatus.Active)
        {
            throw new ForbiddenException("Your workspace is currently unavailable.");
        }

        return await CreateAsync(DataScope.ForWorkspace(id), cancellationToken);
    }

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

/// <summary>
/// Defaults for the PostgreSQL connection string. Npgsql 9+ tries GSS (Kerberos) session encryption first, and on
/// Linux without libgssapi (the container image) the runtime then logs "Error: libgssapi_krb5.so.2: cannot open
/// shared object file". Kerberos is opt-in: a connection string that sets <c>GSS Encryption Mode</c> is kept as is.
/// </summary>
internal static class PostgresConnectionString
{
    /// <summary>The connection string with <c>GSS Encryption Mode=Disable</c> unless it names a mode itself.</summary>
    public static string WithDefaults(string connectionString)
    {
        var keys = new DbConnectionStringBuilder { ConnectionString = connectionString }.Keys.Cast<string>();
        return keys.Any(key => key.Replace(" ", "").Equals("GssEncryptionMode", StringComparison.OrdinalIgnoreCase))
            ? connectionString
            : connectionString.TrimEnd(';', ' ') + ";GSS Encryption Mode=Disable";
    }
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

    /// <summary>Create the initial super admin when none exists (requires <see cref="SuperAdminPassword"/>).</summary>
    public bool CreateSuperAdmin { get; set; } = true;

    /// <summary>Initial super admin e-mail.</summary>
    public string SuperAdminEmail { get; set; } = "superadmin@smartsurvey.local";

    /// <summary>Initial super admin password. Leave empty in production and set it via environment/secrets.</summary>
    public string? SuperAdminPassword { get; set; }

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

    /// <summary>
    /// With <see cref="DemoData"/>: also create the second demo workspace "Acme Research" (admin
    /// <see cref="DemoSecondAdminEmail"/>, password <see cref="AdminPassword"/>) to show workspace isolation.
    /// </summary>
    public bool DemoSecondWorkspace { get; set; } = true;

    /// <summary>Admin of the second demo workspace.</summary>
    public string DemoSecondAdminEmail { get; set; } = "admin@acme.local";
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
            .UseNpgsql(PostgresConnectionString.WithDefaults(connectionString), npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }
}
