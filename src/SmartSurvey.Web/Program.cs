using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using SmartSurvey.Application;
using SmartSurvey.Domain.Identity;
using SmartSurvey.Infrastructure;
using SmartSurvey.Infrastructure.Persistence;
using SmartSurvey.Web.Api;
using SmartSurvey.Web.Components;
using SmartSurvey.Web.Components.Account;
using SmartSurvey.Web.Infrastructure;

// ---------------------------------------------------------------------------------------------
// SmartSurvey composition root.
//   UI:  Blazor Web App — static SSR by default, InteractiveServer on pages that need it.
//   API: Minimal APIs under /api/v1 (cookie or bearer auth) + Identity API under /api/auth.
//   All business logic lives in the Application layer and is shared by the UI and the API.
// ---------------------------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);

// ----- Blazor ---------------------------------------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

// ----- Application layers ---------------------------------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddWebServices(builder.Configuration);

// ----- Authentication & Identity --------------------------------------------------------------
// The default scheme is a policy scheme: requests carrying "Authorization: Bearer …" are authenticated
// with Identity bearer tokens (REST clients), everything else with the Identity cookie (browser/UI).
// HttpContext.User — and therefore ICurrentUser — reflects whichever credential was presented.
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = AuthSchemes.CookieOrBearer;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddPolicyScheme(AuthSchemes.CookieOrBearer, displayName: null, options => // no display name: keeps it off the external-login list
        options.ForwardDefaultSelector = context =>
            context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? IdentityConstants.BearerScheme
                : IdentityConstants.ApplicationScheme)
    .AddBearerToken(IdentityConstants.BearerScheme)
    .AddIdentityCookies();

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = builder.Configuration.GetValue("Identity:RequireConfirmedAccount", false);
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddApiEndpoints();

builder.Services.ConfigureApiFriendlyCookies();
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// ----- Hosting concerns -----------------------------------------------------------------------
// Persist data-protection keys (auth cookies, bearer tokens, antiforgery) when a path is configured,
// e.g. a Docker volume, so logins survive restarts and scale-out instances share keys.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("SmartSurvey");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

// Behind a reverse proxy (nginx, Traefik, Azure App Service…) honour X-Forwarded-For/Proto.
var behindProxy = builder.Configuration.GetValue("ReverseProxy:Enabled", false);
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

// ----- API plumbing ---------------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddApiDocumentation();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

var app = builder.Build();

// ----- HTTP pipeline --------------------------------------------------------------------------
if (behindProxy)
{
    app.UseForwardedHeaders();
}

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// API errors are always returned as ProblemDetails (also in Development); UI 404s show a friendly page.
app.UseWhen(ctx => WebSetup.IsApiRequest(ctx.Request), api => api.UseExceptionHandler());
app.UseWhen(ctx => !WebSetup.IsApiRequest(ctx.Request), ui => ui.UseStatusCodePagesWithReExecute("/not-found"));

// Containers usually terminate TLS at the proxy; set Https:Redirect=false there.
if (app.Configuration.GetValue("Https:Redirect", true))
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseAntiforgery();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "SmartSurvey API v1");
        options.DocumentTitle = "SmartSurvey API";
    });
}

// ----- Endpoints ------------------------------------------------------------------------------
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

app.MapGroup("/api/auth")
    .WithTags("Auth")
    .RequireRateLimiting(RateLimitPolicies.Auth)
    .MapIdentityApi<ApplicationUser>();

app.MapApiEndpoints();
app.MapBrandingAssets();
app.MapHealthChecks("/health");

// ----- Database -------------------------------------------------------------------------------
if (app.Configuration.GetValue("Database:InitializeOnStartup", true))
{
    await app.Services.InitializeDatabaseAsync();
}

await app.RunAsync();

/// <summary>Entry point (partial so WebApplicationFactory can reference it in integration tests).</summary>
public partial class Program;
