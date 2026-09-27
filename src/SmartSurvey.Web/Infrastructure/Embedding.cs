using Microsoft.Extensions.Options;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>
/// "Embedding" configuration section: whether surveys may be embedded in iframes on other sites
/// (<c>/embed/s/{slug}</c>, snippet on the share page) and which sites may do so.
/// </summary>
public sealed class EmbeddingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Embedding";

    /// <summary>URL prefix of the pages that may be framed.</summary>
    public const string PathPrefix = "/embed";

    /// <summary>Allow <c>/embed</c> pages to be shown in iframes on other sites (default: true).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Origins allowed to embed surveys, e.g. <c>https://www.example.com</c>. Empty = any site.
    /// </summary>
    public List<string> AllowedOrigins { get; set; } = [];

    /// <summary>The CSP <c>frame-ancestors</c> sources for embeddable pages.</summary>
    public string FrameAncestors =>
        AllowedOrigins.Count == 0
            ? "'self' *"
            : string.Join(' ', AllowedOrigins.Where(IsValidOrigin).Prepend("'self'"));

    /// <summary>True for an absolute http(s) origin without path, query or whitespace (safe in a header).</summary>
    public static bool IsValidOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https"
        && uri.PathAndQuery == "/"
        && !origin.Any(char.IsWhiteSpace);
}

/// <summary>
/// Security headers for every response: <c>X-Content-Type-Options: nosniff</c>, a
/// <c>strict-origin-when-cross-origin</c> referrer policy and clickjacking protection — responses may only be
/// framed by this site, except <c>/embed</c> pages when embedding is enabled. Replaces the antiforgery
/// system's <c>X-Frame-Options</c> header (suppressed in <see cref="WebSetup.AddWebServices"/>), which
/// would otherwise block all embedding.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptions<EmbeddingOptions> options)
{
    /// <summary>Middleware entry point.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        if (options.Value.Enabled && context.Request.Path.StartsWithSegments(EmbeddingOptions.PathPrefix))
        {
            headers.ContentSecurityPolicy = $"frame-ancestors {options.Value.FrameAncestors}";
        }
        else
        {
            headers.XFrameOptions = "SAMEORIGIN";
            headers.ContentSecurityPolicy = "frame-ancestors 'self'";
        }

        return next(context);
    }
}
