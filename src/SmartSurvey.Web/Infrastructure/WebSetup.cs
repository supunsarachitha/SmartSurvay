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
    /// <summary>Admin UI pages (cookie authentication).</summary>
    public const string Admin = "Admin";

    /// <summary>Authenticated API caller (cookie or bearer token).</summary>
    public const string ApiUser = "ApiUser";

    /// <summary>Admin API caller (cookie or bearer token).</summary>
    public const string ApiAdmin = "ApiAdmin";
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

    /// <summary>buymeacoffee.com user name.</summary>
    public string BuyMeACoffeeUsername { get; set; } = "smartsurvey";

    /// <summary>Optional source repository URL.</summary>
    public string? GitHubUrl { get; set; }

    /// <summary>Optional contact e-mail.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>Full Buy Me a Coffee URL.</summary>
    public string BuyMeACoffeeUrl => $"https://www.buymeacoffee.com/{Uri.EscapeDataString(BuyMeACoffeeUsername)}";
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

        // Frame headers are set by FrameOptionsMiddleware so /embed pages can be allowed in iframes.
        services.Configure<AntiforgeryOptions>(options => options.SuppressXFrameOptionsHeader = true);

        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, UserCircuitHandler>());

        services.AddScoped<ToastService>();
        services.AddScoped<BrowserInterop>();

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Admin, p => p.RequireRole(AppRoles.Admin))
            .AddPolicy(AuthPolicies.ApiUser, p => p
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, IdentityConstants.BearerScheme)
                .RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.ApiAdmin, p => p
                .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme, IdentityConstants.BearerScheme)
                .RequireRole(AppRoles.Admin));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(RateLimitPolicies.Submissions, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1) }));
            options.AddPolicy(RateLimitPolicies.Auth, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
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
