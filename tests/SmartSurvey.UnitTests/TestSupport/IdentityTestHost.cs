using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartSurvey.Application;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Identity;
using SmartSurvey.Infrastructure;
using SmartSurvey.Infrastructure.Persistence;

namespace SmartSurvey.UnitTests.TestSupport;

/// <summary>
/// A real service container (application + infrastructure + ASP.NET Core Identity, as registered by
/// the web host) over a temporary SQLite file, for services that depend on <see cref="UserManager{TUser}"/>.
/// The current user is a settable <see cref="TestCurrentUser"/> (admin by default).
/// </summary>
public sealed class IdentityTestHost : IAsyncDisposable
{
    private readonly string _path;

    /// <summary>Scope that owns the contexts handed out by <see cref="CreateDbContextAsync"/> (EF Core needs it alive).</summary>
    private readonly AsyncServiceScope _contextScope;

    private IdentityTestHost(string path, ServiceProvider services, TestCurrentUser user, FakeTimeProvider time)
    {
        _path = path;
        Services = services;
        _contextScope = services.CreateAsyncScope();
        User = user;
        Time = time;
    }

    public ServiceProvider Services { get; }

    public TestCurrentUser User { get; }

    public FakeTimeProvider Time { get; }

    /// <summary>Builds the container and creates the schema.</summary>
    /// <param name="settings">Overrides of the configuration (e.g. <c>Seed:*</c> keys).</param>
    public static async Task<IdentityTestHost> CreateAsync(IDictionary<string, string?>? settings = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"smartsurvey-test-{Guid.NewGuid():N}.db");
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = $"Data Source={path};Pooling=False",
            ["Database:Provider"] = "Sqlite",
            ["Seed:CreateAdmin"] = "true",
            ["Seed:AdminEmail"] = "admin@test.local",
            ["Seed:AdminPassword"] = "Admin123!",
            ["Seed:DemoData"] = "false",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var user = TestCurrentUser.Admin();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 15, 10, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton<TimeProvider>(time);
        services.AddSingleton<ICurrentUser>(user);
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var host = new IdentityTestHost(path, provider, user, time);
        await using var db = await host.CreateDbContextAsync();
        await db.Database.EnsureCreatedAsync();
        return host;
    }

    /// <summary>A new database context (caller disposes).</summary>
    public Task<AppDbContext> CreateDbContextAsync() =>
        _contextScope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();

    /// <summary>Runs <paramref name="action"/> with a service resolved from a fresh scope.</summary>
    public async Task<TResult> WithAsync<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>Runs <paramref name="action"/> with a service resolved from a fresh scope.</summary>
    public async Task WithAsync<TService>(Func<TService, Task> action)
        where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>Creates a user with the given roles (roles are created when missing).</summary>
    public Task<ApplicationUser> AddUserAsync(Guid id, string email, params string[] roles) =>
        WithAsync<IServiceProvider, ApplicationUser>(async sp =>
        {
            var roleManager = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
                }
            }

            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { Id = id, UserName = email, Email = email, CreatedAt = Time.GetUtcNow().UtcDateTime };
            var created = await users.CreateAsync(user, "Passw0rd!");
            Assert.True(created.Succeeded, string.Join(" ", created.Errors.Select(e => e.Description)));
            if (roles.Length > 0)
            {
                await users.AddToRolesAsync(user, roles);
            }

            return user;
        });

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _contextScope.DisposeAsync();
        await Services.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // A leftover temp file is harmless.
        }
    }
}
