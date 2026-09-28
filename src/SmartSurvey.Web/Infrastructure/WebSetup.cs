using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Identity;
using SmartSurvey.Web.Components.Shared;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>Authorization policy names.</summary>
public static class AuthPolicies
{
    /// <summary>Workspace admin UI pages (cookie authentication).</summary>
    public const string Admin = "Admin";

    /// <summary>System (super admin) UI pages (cookie authentication).</summary>
    public const string SuperAdmin = "SuperAdmin";

    /// <summary>Authenticated API caller (cookie or bearer token).</summary>
    public const string ApiUser = "ApiUser";

    /// <summary>Workspace admin API caller (cookie or bearer token).</summary>
    public const string ApiAdmin = "ApiAdmin";

    /// <summary>Super admin API caller (cookie or bearer token).</summary>
    public const string ApiSuperAdmin = "ApiSuperAdmin";
}

/// <summary>Authentication scheme names defined by the Web host.</summary>
public static class AuthSchemes
{
    /// <summary>Default policy scheme: bearer token when an Authorization header is present, otherwise the Identity cookie.</summary>
    public const string CookieOrBearer = "CookieOrBearer";
}

/// <summary>Rate-limiter policy names.</summary>
public static class RateLimitPolicies
{
    /// <summary>Response submissions / draft saves (per client IP).</summary>
    public const string Submissions = "submissions";

    /// <summary>Authentication endpoints (per client IP).</summary>
    public const string Auth = "auth";
}

/// <summary>"Support" configuration section (Buy Me a Coffee page, footer links).</summary>
public sealed class SupportOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Support";

    /// <summary>buymeacoffee.com user name (the page at https://buymeacoffee.com/{name}).</summary>
    public string BuyMeACoffeeUsername { get; set; } = "jkhy9gtjs";

    /// <summary>Optional source repository URL.</summary>
    public string? GitHubUrl { get; set; } = "https://github.com/supunsarachitha/SmartSurvay";

    /// <summary>Optional contact e-mail.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>Full Buy Me a Coffee URL.</summary>
    public string BuyMeACoffeeUrl => $"https://buymeacoffee.com/{Uri.EscapeDataString(BuyMeACoffeeUsername)}";
}

/// <summary>Registers Web-host services.</summary>
public static class WebSetup
{
    /// <summary>
    /// Adds the current-user plumbing, UI services, authorization policies, rate limiting and
    /// configuration options used by the Web project.
    /// </summary>
    public static IServiceCollection AddWebServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SupportOptions>(configuration.GetSection(SupportOptions.SectionName));
        services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));

        // Frame headers are set by SecurityHeadersMiddleware so /embed pages can be allowed in iframes.
        services.Configure<AntiforgeryOptions>(options => options.SuppressXFrameOptionsHeader = true);

        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, UserCircuitHandler>());

        services.AddScoped<AccountEmails>();
        services.AddScoped<CurrentWorkspace>();
        services.AddScoped<ToastService>();
        services.AddScoped<BrowserInterop>();
        services.AddScoped<SubmissionThrottle>();
        services.AddScoped<PasswordAttemptThrottle>();

        // Workspace admins need the Admin role *and* a workspace; super admins belong to no workspace.
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Admin, p => p.RequireRole(AppRoles.Admin).RequireClaim(AppClaimTypes.WorkspaceId))
            .AddPolicy(AuthPolicies.SuperAdmin, p => p.RequireRole(AppRoles.SuperAdmin))
            .AddPolicy(AuthPolicies.ApiUser, p => p
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, IdentityConstants.BearerScheme)
                .RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.ApiAdmin, p => p
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, IdentityConstants.BearerScheme)
                .RequireRole(AppRoles.Admin)
                .RequireClaim(AppClaimTypes.WorkspaceId))
            .AddPolicy(AuthPolicies.ApiSuperAdmin, p => p
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, IdentityConstants.BearerScheme)
                .RequireRole(AppRoles.SuperAdmin));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicies.Submissions, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
            // Sign-in, sign-up and join requests per IP and minute (RateLimits:AuthPerMinute, default 20).
            var authPerMinute = Math.Max(1, configuration.GetValue("RateLimits:AuthPerMinute", 20));
            options.AddPolicy(RateLimitPolicies.Auth, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
        });

        return services;
    }

    /// <summary>
    /// Makes the Identity cookie return 401/403 for API calls instead of redirecting to the login page.
    /// </summary>
    public static IServiceCollection ConfigureApiFriendlyCookies(this IServiceCollection services)
    {
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.Events.OnRedirectToLogin = context =>
            {
                if (IsApiRequest(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (IsApiRequest(context.Request))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });

        return services;
    }

    /// <summary>True for requests under <c>/api</c>.</summary>
    public static bool IsApiRequest(HttpRequest request) => request.Path.StartsWithSegments("/api");
}
