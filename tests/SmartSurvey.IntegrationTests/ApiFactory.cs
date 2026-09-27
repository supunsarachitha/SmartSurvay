using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

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

    /// <summary>JSON settings matching the API (camelCase, enums as strings).</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"smartsurvey-it-{Guid.NewGuid():N}.db");
    private readonly string _keysPath = Path.Combine(Path.GetTempPath(), $"smartsurvey-it-keys-{Guid.NewGuid():N}");
    private string? _adminToken;
    private string? _userToken;

    /// <summary>Client without credentials (redirects are not followed).</summary>
    public HttpClient Anonymous() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Client authenticated as the seeded administrator (bearer token).</summary>
    public HttpClient Admin() => WithToken(_adminToken!);

    /// <summary>Client authenticated as a registered respondent without roles (bearer token).</summary>
    public HttpClient Respondent() => WithToken(_userToken!);

    /// <summary>Logs in once per test run (the auth endpoints are rate limited).</summary>
    public async Task InitializeAsync()
    {
        var client = Anonymous();
        _adminToken = await LoginAsync(client, AdminEmail, AdminPassword);

        var register = await client.PostAsJsonAsync("/api/auth/register", new { email = UserEmail, password = UserPassword });
        register.EnsureSuccessStatusCode();
        _userToken = await LoginAsync(client, UserEmail, UserPassword);
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
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_dbPath};Pooling=False");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Seed:CreateAdmin", "true");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("Seed:DemoData", "false");
        builder.UseSetting("Swagger:Enabled", "true");
        builder.UseSetting("Https:Redirect", "false");
        builder.UseSetting("DataProtection:KeysPath", _keysPath);
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
