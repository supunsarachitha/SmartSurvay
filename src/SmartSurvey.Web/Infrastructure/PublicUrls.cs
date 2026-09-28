using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>"App" configuration section.</summary>
public sealed class AppOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "App";

    /// <summary>
    /// Public address of the site (scheme and host, e.g. <c>https://surveys.example.com</c>) used in every link the
    /// app hands out: account e-mails, survey share links, QR codes, embed snippets and join links. Empty = the
    /// scheme and host of the current request.
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>
/// Builds the absolute links the app hands out. With <see cref="AppOptions.PublicBaseUrl"/> they always use that
/// origin — independent of the request's <c>Host</c> header or an internal proxy address; otherwise the request's.
/// The path base of the app is kept.
/// </summary>
public sealed partial class PublicUrls(IOptions<AppOptions> options)
{
    /// <summary>The configured public origin (<c>scheme://host[:port]</c>), or null when not configured.</summary>
    public string? Origin { get; } = NormalizeOrigin(options.Value.PublicBaseUrl);

    /// <summary>True for an empty value or an absolute http(s) URL.</summary>
    public static bool IsValid(string? url) =>
        string.IsNullOrWhiteSpace(url)
        || (Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    /// <summary>Absolute link to a page of the app (<paramref name="relative"/> to its base, e.g. <c>s/my-survey</c>).</summary>
    public string Absolute(NavigationManager navigation, string relative) => Rewrite(navigation.ToAbsoluteUri(relative).AbsoluteUri);

    /// <summary>Base address of the app (with path base and trailing slash).</summary>
    public string BaseUri(NavigationManager navigation) => Rewrite(navigation.BaseUri);

    /// <summary>
    /// Replaces scheme, host and port of an absolute link — which may be HTML-encoded, as Identity passes e-mail
    /// links — with the public origin. Returns the link unchanged when no public address is configured.
    /// </summary>
    public string Rewrite(string link) => Origin is null ? link : OriginPattern().Replace(link, Origin, 1);

    private static string? NormalizeOrigin(string? url) =>
        string.IsNullOrWhiteSpace(url) || !IsValid(url) ? null : new Uri(url.Trim()).GetLeftPart(UriPartial.Authority);

    [GeneratedRegex("^https?://[^/?#]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OriginPattern();
}
