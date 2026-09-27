using SmartSurvey.Application.Common;
using SmartSurvey.Application.Exports;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Web.Api;

/// <summary>Body of <c>POST /api/v1/surveys/{id}/status</c>.</summary>
/// <param name="Status">Target status (<c>Draft</c>, <c>Published</c>, <c>Closed</c>, <c>Archived</c>).</param>
public sealed record ChangeSurveyStatusRequest(SurveyStatus Status);

/// <summary>Result of <c>GET /api/v1/surveys/slug-available</c>.</summary>
/// <param name="Slug">The checked slug.</param>
/// <param name="Available">True when no other survey uses it.</param>
public sealed record SlugAvailabilityDto(string Slug, bool Available);

/// <summary>Result of saving a draft.</summary>
/// <param name="ResponseId">Id of the draft response (send it back with the next save or the submission).</param>
public sealed record DraftSavedDto(Guid ResponseId);

/// <summary>Body of <c>POST /api/v1/branding/logo</c>.</summary>
/// <remarks>
/// JSON with base64 content rather than a multipart form: JSON requests cannot be sent cross-site
/// without a CORS preflight, so the endpoint is safe for cookie-authenticated callers without an
/// antiforgery token (bearer clients are unaffected either way).
/// </remarks>
public sealed class UploadLogoRequest
{
    /// <summary>Original file name (used for messages only; the type is detected from the content).</summary>
    public string? FileName { get; set; }

    /// <summary>Image bytes, base64-encoded in JSON (PNG, JPEG, GIF, WebP, ICO or SVG; max 512 KB).</summary>
    public byte[] Content { get; set; } = [];
}

/// <summary>Helpers shared by the endpoint groups.</summary>
internal static class ApiHelpers
{
    /// <summary>Parses the <c>format</c> query value or throws a 400 validation error listing the allowed values.</summary>
    public static ExportFormat ParseFormat(string? format, IReadOnlyCollection<ExportFormat> allowed)
    {
        if (ExportFormatExtensions.TryParse(format, out var parsed) && allowed.Contains(parsed))
        {
            return parsed;
        }

        var names = string.Join(", ", allowed.Select(f => f.FileExtension()));
        throw new AppValidationException("format", $"Choose one of the supported formats: {names}.");
    }

    /// <summary>A file download result.</summary>
    public static IResult File(ExportFile file) => TypedResults.File(file.Content, file.ContentType, file.FileName);
}
