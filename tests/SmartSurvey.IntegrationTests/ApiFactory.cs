using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.IntegrationTests;

/// <summary>
/// The real application (Program.cs pipeline) on a temporary SQLite database, seeded with an
/// administrator. One instance is shared by every test class (see <see cref="ApiCollection"/>), so
/// tests create their own data and never assume an empty database.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@it.local";
    public const string AdminPassword = "Admin123!";
    public const string UserEmail = "respondent@it.local";
    public const string UserPassword = "Respondent123!";
    public const string SuperAdminEmail = "super@it.local";
    public const string SuperAdminPassword = "Super123!";

    /// <summary>Slug of the workspace the seeder creates for the administrator.</summary>
    public const string WorkspaceSlug = "default";

    /// <summary>JSON settings matching the API (camelCase, enums as strings).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"smartsurvey-it-{Guid.NewGuid():N}.db");
    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), $"smartsurvey-it-keys-{Guid.NewGuid():N}");
    private string? _adminToken;
    private string? _userToken;
    private string? _superAdminToken;

    /// <summary>Log entries written by the application (for assertions about logging).</summary>
    public CapturedLogs Logs { get; } = new();

    /// <summary>Client without credentials (redirects are not followed).</summary>
    public HttpClient Anonymous() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Client authenticated as the seeded administrator (bearer token).</summary>
    public HttpClient Admin() => WithToken(_adminToken!);

    /// <summary>Client authenticated as a member (User role) of the administrator's workspace (bearer token).</summary>
    public HttpClient Respondent() => WithToken(_userToken!);

    /// <summary>Client authenticated as the seeded super admin (bearer token).</summary>
    public HttpClient SuperAdmin() => WithToken(_superAdminToken!);

    /// <summary>Logs in once per test run (the auth endpoints are rate limited).</summary>
    public async Task InitializeAsync()
    {
        var client = Anonymous();
        _adminToken = await LoginAsync(client, AdminEmail, AdminPassword);

        await CreateMemberAsync(UserEmail, UserPassword);
        _userToken = await LoginAsync(client, UserEmail, UserPassword);
        _superAdminToken = await LoginAsync(client, SuperAdminEmail, SuperAdminPassword);
    }

    /// <inheritdoc />
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        SqliteConnection.ClearAllPools();
        TryDelete(() => File.Delete(_dbPath));
        TryDelete(() => Directory.Delete(_keysPath, recursive: true));
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));

        // Same DI checks as Development: catches scoped services resolved from the root provider (e.g. by
        // endpoint builders at start-up) and registrations that cannot be constructed.
        builder.UseDefaultServiceProvider(options =>
        {
            options.ValidateScopes = true;
            options.ValidateOnBuild = true;
        });
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_dbPath};Pooling=False");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Seed:CreateAdmin", "true");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Seed:SuperAdminEmail", SuperAdminEmail);
        builder.UseSetting("Seed:SuperAdminPassword", SuperAdminPassword);
        builder.UseSetting("Seed:DemoData", "false");
        builder.UseSetting("Swagger:Enabled", "true");
        builder.UseSetting("Https:Redirect", "false");
        builder.UseSetting("DataProtection:KeysPath", _keysPath);
        // Real bot protection, tuned for speed: tiny proof of work, no minimum answering time.
        builder.UseSetting("BotProtection:Difficulty", "2000");
        builder.UseSetting("BotProtection:MinimumSeconds", "0");
    }

    /// <summary>Creates a member (User role) of the administrator's workspace directly through Identity.</summary>
    public async Task<ApplicationUser> CreateMemberAsync(string email, string password)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByEmailAsync(AdminEmail) ?? throw new InvalidOperationException("The seeded admin is missing.");
        var member = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, WorkspaceId = admin.WorkspaceId };
        var result = await users.CreateAsync(member, password);
        if (result.Succeeded)
        {
            result = await users.AddToRoleAsync(member, AppRoles.User);
        }

        return result.Succeeded ? member : throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }

    private HttpClient WithToken(string token)
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (IOException)
        {
            // Leftover temp files are harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Shares one <see cref="ApiFactory"/> between all API test classes.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>Logger provider that records every entry of Warning level and above.</summary>
public sealed class CapturedLogs : ILoggerProvider
{
    private readonly List<(string Category, LogLevel Level, string Message)> _entries = [];

    /// <summary>Recorded entries (a snapshot).</summary>
    public IReadOnlyList<(string Category, LogLevel Level, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return _entries.ToList();
            }
        }
    }

    /// <summary>Forgets everything recorded so far.</summary>
    public void Clear()
    {
        lock (_entries)
        {
            _entries.Clear();
        }
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class Logger(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            lock (owner._entries)
            {
                owner._entries.Add((category, logLevel, formatter(state, exception)));
            }
        }
    }
}
