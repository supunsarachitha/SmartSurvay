using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SmartSurvey.Application.Common;
using SmartSurvey.Infrastructure.Persistence;

namespace SmartSurvey.UnitTests.TestSupport;

/// <summary>
/// Isolated in-memory SQLite database with the real <see cref="AppDbContext"/> model, the
/// auditable-entity interceptor, a controllable clock and current user. Implements
/// <see cref="IAppDbContextFactory"/> so application services can be tested end-to-end against a
/// relational provider. Create one per test (it is cheap) and dispose it.
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
    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new AuditableEntityInterceptor(Time, CurrentUser))
            .EnableSensitiveDataLogging()
            .Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    /// <summary>Controllable clock, starts at 2026-01-15 10:00 UTC.</summary>
    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));

    /// <summary>Mutable current user (admin by default). Change its properties to simulate other users.</summary>
    public TestCurrentUser CurrentUser { get; } = TestCurrentUser.Admin();

    /// <summary>Current UTC time of <see cref="Time"/>.</summary>
    public DateTime UtcNow => Time.GetUtcNow().UtcDateTime;

    /// <summary>Creates a new context on the shared connection (caller disposes).</summary>
    public AppDbContext CreateContext() => new(_options);

    /// <inheritdoc />
    public Task<IAppDbContext> CreateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IAppDbContext>(CreateContext());

    /// <summary>Adds entities in a separate context and saves them (test arrangement helper).</summary>
    public async Task SeedAsync(params object[] entities)
    {
        await using var db = CreateContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    /// <inheritdoc />
    public void Dispose() => _connection.Dispose();
}
