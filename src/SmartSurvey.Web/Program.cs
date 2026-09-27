using Microsoft.AspNetCore.Components.Authorization;
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
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
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
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders()
    .AddApiEndpoints();

builder.Services.ConfigureApiFriendlyCookies();
builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// ----- API plumbing ---------------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddApiDocumentation();
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

var app = builder.Build();

// ----- HTTP pipeline --------------------------------------------------------------------------
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

app.UseHttpsRedirection();
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
app.MapHealthChecks("/health");

// ----- Database -------------------------------------------------------------------------------
if (app.Configuration.GetValue("Database:InitializeOnStartup", true))
{
    await app.Services.InitializeDatabaseAsync();
}

await app.RunAsync();

/// <summary>Entry point (partial so WebApplicationFactory can reference it in integration tests).</summary>
public partial class Program;
