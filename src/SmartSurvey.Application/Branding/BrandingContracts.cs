namespace SmartSurvey.Application.Branding;

/// <summary>Current product branding (cached; safe to read on every request).</summary>
public sealed record BrandingDto
{
    /// <summary>Product name (e.g. "SmartSurvey").</summary>
    public string ProductName { get; init; } = BrandingDefaults.ProductName;

    /// <summary>Optional tagline.</summary>
    public string? Tagline { get; init; } = BrandingDefaults.Tagline;

    /// <summary>Bootstrap icon class used when no logo is uploaded (e.g. "bi-ui-checks").</summary>
    public string IconName { get; init; } = BrandingDefaults.IconName;

    /// <summary>True when a custom logo image is uploaded.</summary>
    public bool HasLogo { get; init; }

    /// <summary>MIME type of the uploaded logo.</summary>
    public string? LogoContentType { get; init; }

    /// <summary>Change counter used for cache-busting URLs.</summary>
    public int Version { get; init; }

    /// <summary>Last update (UTC); null while the defaults are in use.</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Relative URL of the logo image (null when none), versioned for long-lived caching.</summary>
    public string? LogoUrl => HasLogo ? $"branding/logo?v={Version}" : null;

    /// <summary>Relative URL of the favicon (the logo when set, otherwise the default icon).</summary>
    public string FaviconUrl => $"branding/favicon?v={Version}";
}

/// <summary>Editable branding fields.</summary>
public sealed class UpdateBrandingRequest
{
    /// <summary>Product name (required, max 80).</summary>
    public string ProductName { get; set; } = BrandingDefaults.ProductName;

    /// <summary>Tagline (max 200).</summary>
    public string? Tagline { get; set; }

    /// <summary>Bootstrap icon class, e.g. "bi-clipboard-data" (must match <c>^bi-[a-z0-9-]+$</c>).</summary>
    public string IconName { get; set; } = BrandingDefaults.IconName;
}

/// <summary>An uploaded logo ready to be served.</summary>
/// <param name="Content">Image bytes.</param>
/// <param name="ContentType">MIME type.</param>
/// <param name="Version">Branding version (for ETags).</param>
public sealed record BrandingLogo(byte[] Content, string ContentType, int Version);

/// <summary>
/// "Branding" configuration section: initial values used until an administrator saves branding
/// in the UI (useful to pre-brand a deployment through environment variables).
/// </summary>
public sealed class BrandingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Branding";

    /// <summary>Initial product name.</summary>
    public string ProductName { get; set; } = BrandingDefaults.ProductName;

    /// <summary>Initial tagline.</summary>
    public string? Tagline { get; set; } = BrandingDefaults.Tagline;

    /// <summary>Initial Bootstrap icon class.</summary>
    public string IconName { get; set; } = BrandingDefaults.IconName;
}

/// <summary>Built-in defaults and limits.</summary>
public static class BrandingDefaults
{
    /// <summary>Default product name.</summary>
    public const string ProductName = "SmartSurvey";

    /// <summary>Default tagline.</summary>
    public const string Tagline = "Smart surveys, beautiful insights.";

    /// <summary>Default brand icon.</summary>
    public const string IconName = "bi-ui-checks";

    /// <summary>Maximum product name length.</summary>
    public const int MaxProductNameLength = 80;

    /// <summary>Maximum tagline length.</summary>
    public const int MaxTaglineLength = 200;

    /// <summary>Maximum logo size in bytes (512 KB).</summary>
    public const int MaxLogoBytes = 512 * 1024;

    /// <summary>
    /// Curated Bootstrap icons offered by the icon picker (any valid <c>bi-*</c> class is accepted).
    /// </summary>
    public static readonly IReadOnlyList<string> SuggestedIcons =
    [
        "bi-ui-checks", "bi-ui-checks-grid", "bi-clipboard-data", "bi-clipboard-check", "bi-card-checklist",
        "bi-list-check", "bi-check2-square", "bi-check2-circle", "bi-patch-check", "bi-bar-chart-line",
        "bi-pie-chart", "bi-graph-up-arrow", "bi-kanban", "bi-chat-square-text", "bi-chat-heart",
        "bi-emoji-smile", "bi-hand-thumbs-up", "bi-star", "bi-stars", "bi-lightning-charge",
        "bi-lightbulb", "bi-rocket-takeoff", "bi-bullseye", "bi-compass", "bi-globe2",
        "bi-people", "bi-person-hearts", "bi-heart-pulse", "bi-mortarboard", "bi-briefcase",
        "bi-building", "bi-shop", "bi-cup-hot", "bi-tree", "bi-flower1",
        "bi-gem", "bi-shield-check", "bi-award", "bi-trophy", "bi-megaphone",
    ];
}

/// <summary>
/// Reads and updates product branding. Reads are served from an in-memory cache (refreshed every
/// minute and immediately after changes), so layouts can call <see cref="GetAsync"/> on every render.
/// Updates are admin-only and audited.
/// </summary>
public interface IBrandingService
{
    /// <summary>Current branding (defaults from configuration until customised).</summary>
    Task<BrandingDto> GetAsync(CancellationToken ct = default);

    /// <summary>Updates name, tagline and icon. Throws AppValidationException / ForbiddenException.</summary>
    Task<BrandingDto> UpdateAsync(UpdateBrandingRequest request, CancellationToken ct = default);

    /// <summary>
    /// Stores a logo image (PNG, JPEG, GIF, WebP, ICO or SVG, max 512 KB). The type is detected from
    /// the file signature, not the declared content type or extension.
    /// </summary>
    Task<BrandingDto> SetLogoAsync(byte[] content, string? fileName, CancellationToken ct = default);

    /// <summary>Removes the uploaded logo (the icon is shown again).</summary>
    Task<BrandingDto> RemoveLogoAsync(CancellationToken ct = default);

    /// <summary>Restores the configured defaults and removes the logo.</summary>
    Task<BrandingDto> ResetAsync(CancellationToken ct = default);

    /// <summary>The uploaded logo, or null.</summary>
    Task<BrandingLogo?> GetLogoAsync(CancellationToken ct = default);
}
