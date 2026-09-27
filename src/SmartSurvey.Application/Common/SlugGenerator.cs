using System.Globalization;
using System.Text;

namespace SmartSurvey.Application.Common;

/// <summary>Creates URL-friendly slugs such as <c>customer-satisfaction-2026</c>.</summary>
public static class SlugGenerator
{
    /// <summary>Maximum slug length stored in the database.</summary>
    public const int MaxLength = 80;

    /// <summary>
    /// Converts arbitrary text into a lowercase, hyphen-separated ASCII slug. Diacritics are removed
    /// ("Café Menu" → "cafe-menu"); every other run of non-alphanumeric characters becomes a single
    /// hyphen. Returns "survey" when nothing usable remains.
    /// </summary>
    public static string Generate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "survey";
        }

        var normalized = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var lastWasHyphen = false;

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue; // strip accents
            }

            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                sb.Append(c);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && sb.Length > 0)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].Trim('-');
        }

        return slug.Length == 0 ? "survey" : slug;
    }

    /// <summary>True when <paramref name="slug"/> only contains lowercase letters, digits and single hyphens.</summary>
    public static bool IsValid(string? slug) =>
        !string.IsNullOrEmpty(slug)
        && slug.Length <= MaxLength
        && slug == Generate(slug);

    /// <summary>Appends a numeric suffix (<c>-2</c>, <c>-3</c>, …) respecting <see cref="MaxLength"/>.</summary>
    public static string WithSuffix(string slug, int suffix)
    {
        var tail = "-" + suffix.ToString(CultureInfo.InvariantCulture);
        var head = slug.Length + tail.Length > MaxLength ? slug[..(MaxLength - tail.Length)].Trim('-') : slug;
        return head + tail;
    }
}
