using SmartSurvey.Domain.Common;

namespace SmartSurvey.Domain.Entities;

/// <summary>
/// Product branding customised by administrators: product name, tagline and brand icon — either a
/// built-in Bootstrap icon or an uploaded logo image (also used as the favicon). Stored as a single
/// row with the well-known id <see cref="SingletonId"/>.
/// </summary>
public class BrandingSettings : AuditableEntity
{
    /// <summary>Id of the one and only branding row.</summary>
    public static readonly Guid SingletonId = Guid.Parse("00000000-0000-0000-0000-00000000b4a7");

    /// <summary>Creates the settings row with the singleton id.</summary>
    public BrandingSettings()
    {
        Id = SingletonId;
    }

    /// <summary>Product name shown in the navigation bar, page titles, footer and exports.</summary>
    public string ProductName { get; set; } = "SmartSurvey";

    /// <summary>Short tagline shown on the landing page and in the footer.</summary>
    public string? Tagline { get; set; }

    /// <summary>Bootstrap icon class (e.g. <c>bi-ui-checks</c>) used when no logo is uploaded.</summary>
    public string IconName { get; set; } = "bi-ui-checks";

    /// <summary>Uploaded logo image (PNG, JPEG, GIF, WebP, ICO or SVG), null when not set.</summary>
    public byte[]? LogoContent { get; set; }

    /// <summary>MIME type of <see cref="LogoContent"/>.</summary>
    public string? LogoContentType { get; set; }

    /// <summary>Incremented on every change; used for cache-busting logo/favicon URLs.</summary>
    public int Version { get; set; }
}
