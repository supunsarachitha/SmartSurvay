using Microsoft.Net.Http.Headers;
using SmartSurvey.Application.Branding;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>
/// Public endpoints serving the uploaded brand logo and the favicon. URLs carry a version query
/// (<c>?v=</c>) so responses can be cached for a long time and change instantly after an update.
/// </summary>
public static class BrandingAssets
{
    /// <summary>Maps <c>GET /branding/logo</c> and <c>GET /branding/favicon</c>.</summary>
    public static IEndpointRouteBuilder MapBrandingAssets(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/branding").AllowAnonymous().ExcludeFromDescription();

        group.MapGet("/logo", async (HttpContext http, IBrandingService branding, CancellationToken ct) =>
        {
            var logo = await branding.GetLogoAsync(ct);
            if (logo is null)
            {
                return Results.NotFound();
            }

            ApplyHeaders(http, logo);
            return Results.File(logo.Content, logo.ContentType);
        });

        group.MapGet("/favicon", async (HttpContext http, IBrandingService branding, IWebHostEnvironment env, CancellationToken ct) =>
        {
            var logo = await branding.GetLogoAsync(ct);
            if (logo is not null)
            {
                ApplyHeaders(http, logo);
                return Results.File(logo.Content, logo.ContentType);
            }

            http.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.File(env.WebRootFileProvider.GetFileInfo("favicon.png").CreateReadStream(), "image/png");
        });

        return app;
    }

    private static void ApplyHeaders(HttpContext http, BrandingLogo logo)
    {
        var headers = http.Response.Headers;
        var versioned = http.Request.Query.ContainsKey("v");
        headers.CacheControl = versioned ? "public, max-age=31536000, immutable" : "public, max-age=300";
        headers.ETag = $"\"logo-{logo.Version}\"";
        headers[HeaderNames.XContentTypeOptions] = "nosniff";

        // Uploaded SVGs are screened on upload; this CSP additionally prevents any active content
        // from running if the image is opened directly in the browser.
        headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";
    }
}
