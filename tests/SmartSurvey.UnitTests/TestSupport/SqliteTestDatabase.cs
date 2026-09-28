using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Infrastructure.Persistence;
using SmartSurvey.Infrastructure.Persistence.Encryption;

namespace SmartSurvey.UnitTests.TestSupport;

/// <summary>
/// Isolated in-memory SQLite database with the real <see cref="AppDbContext"/> model, the
/// auditable-entity interceptor, a controllable clock and current user. Implements
/// <see cref="IAppDbContextFactory"/> (with the same data scopes as production) so application
/// services can be tested end-to-end against a relational provider. Contains two workspaces,
/// <see cref="TestWorkspaces.DefaultId"/> (the current user's) and <see cref="TestWorkspaces.OtherId"/>.
/// Create one per test (it is cheap) and dispose it.
/// </summary>
/// <example>
/// <code>
/// await using var db = new SqliteTestDatabase();
/// var service = new SurveyService(db, db.CurrentUser, db.Time, new RecordingAuditService(), …);
/// </code>
/// </example>
public sealed class SqliteTestDatabase : IAppDbContextFactory, IAsyncDisposable, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    /// <summary>Opens the connection and creates the schema.</summary>
    /// <param name="fieldProtector">Encrypts the sensitive answer columns like production does (null = plain text).</param>
    public SqliteTestDatabase(IFieldProtector? fieldProtector = null)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var builder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditableEntityInterceptor(Time, CurrentUser))
            .EnableSensitiveDataLogging();
        if (fieldProtector is not null)
        {
            builder.UseFieldEncryption(fieldProtector);
        }

        _options = builder.Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
        db.Workspaces.AddRange(
            new Workspace { Id = TestWorkspaces.DefaultId, Name = "Test workspace", Slug = "test" },
            new Workspace { Id = TestWorkspaces.OtherId, Name = "Other workspace", Slug = "other" });
        db.SaveChanges();
    }

    /// <summary>Controllable clock, starts at 2026-01-15 10:00 UTC.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));

    /// <summary>Mutable current user (admin by default). Change its properties to simulate other users.</summary>
    public TestCurrentUser CurrentUser { get; } = TestCurrentUser.Admin();

    /// <summary>Current UTC time of <see cref="Time"/>.</summary>
    public DateTime UtcNow => Time.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Creates a new context on the shared connection (caller disposes). Unfiltered by default so
    /// assertions see every workspace; pass a scope to test isolation.
    /// </summary>
    public AppDbContext CreateContext(DataScope? scope = null) => new AppDbContext(_options).UseScope(scope ?? DataScope.System);

    /// <inheritdoc />
    public Task<IAppDbContext> CreateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IAppDbContext>(CreateContext(CurrentUser.WorkspaceId is { } id ? DataScope.ForWorkspace(id) : DataScope.None));

    /// <inheritdoc />
    public Task<IAppDbContext> CreateForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IAppDbContext>(CreateContext(DataScope.ForWorkspace(workspaceId)));

    /// <inheritdoc />
    public Task<IAppDbContext> CreateSystemAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IAppDbContext>(CreateContext(DataScope.System));

    /// <summary>Adds entities to <see cref="TestWorkspaces.DefaultId"/> in a separate context (test arrangement helper).</summary>
    public Task SeedAsync(params object[] entities) => SeedInWorkspaceAsync(TestWorkspaces.DefaultId, entities);

    /// <summary>Adds entities to the given workspace in a separate context (test arrangement helper).</summary>
    public async Task SeedInWorkspaceAsync(Guid workspaceId, params object[] entities)
    {
        await using var db = CreateContext(DataScope.ForWorkspace(workspaceId));
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();
}
