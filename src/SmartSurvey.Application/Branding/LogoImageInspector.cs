using System.Text;
using System.Text.RegularExpressions;

namespace SmartSurvey.Application.Branding;

/// <summary>
/// Detects the image type of an uploaded logo from its file signature ("magic bytes") and rejects
/// anything that is not a supported image. SVG files are additionally screened for active content
/// (scripts, event handlers, external references); they are also served with a restrictive
/// Content-Security-Policy by the web host as a second line of defence.
/// </summary>
public static partial class LogoImageInspector
{
    /// <summary>Returns the MIME type of a supported image, or null with a reason when rejected.</summary>
    /// <param name="content">File bytes.</param>
    /// <param name="error">Human-readable rejection reason.</param>
    public static string? DetectContentType(byte[] content, out string? error)
    {
        error = null;
        if (content.Length == 0)
        {
            error = "The file is empty.";
            return null;
        }

        if (content.Length > BrandingDefaults.MaxLogoBytes)
        {
            error = $"The logo must be {BrandingDefaults.MaxLogoBytes / 1024} KB or smaller.";
            return null;
        }

        if (StartsWith(content, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A))
        {
            return "image/png";
        }

        if (StartsWith(content, 0xFF, 0xD8, 0xFF))
        {
            return "image/jpeg";
        }

        if (StartsWith(content, (byte)'G', (byte)'I', (byte)'F', (byte)'8'))
        {
            return "image/gif";
        }

        if (content.Length > 12 && StartsWith(content, (byte)'R', (byte)'I', (byte)'F', (byte)'F')
            && content[8] == 'W' && content[9] == 'E' && content[10] == 'B' && content[11] == 'P')
        {
            return "image/webp";
        }

        if (StartsWith(content, 0x00, 0x00, 0x01, 0x00))
        {
            return "image/x-icon";
        }

        if (LooksLikeSvg(content, out var svgError))
        {
            if (svgError is not null)
            {
                error = svgError;
                return null;
            }

            return "image/svg+xml";
        }

        error = "Unsupported file type. Please upload a PNG, JPEG, GIF, WebP, ICO or SVG image.";
        return null;
    }

    private static bool StartsWith(byte[] content, params byte[] signature) =>
        content.Length >= signature.Length && content.AsSpan(0, signature.Length).SequenceEqual(signature);

    private static bool LooksLikeSvg(byte[] content, out string? error)
    {
        error = null;
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(content);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (!SvgRootRegex().IsMatch(text))
        {
            return false;
        }

        if (ActiveContentRegex().IsMatch(text))
        {
            error = "SVG logos must not contain scripts, event handlers, embedded HTML or external references.";
        }

        return true;
    }

    [GeneratedRegex(@"<svg[\s>]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SvgRootRegex();

    [GeneratedRegex(@"<\s*script|<\s*foreignObject|<\s*iframe|<\s*embed|<\s*object|\son[a-z]+\s*=|javascript:|data:text/html|xlink:href\s*=\s*[""']\s*(?!#)|href\s*=\s*[""']\s*(?!#)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ActiveContentRegex();
}
