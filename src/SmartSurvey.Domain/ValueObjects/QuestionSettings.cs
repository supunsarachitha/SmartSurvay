namespace SmartSurvey.Domain.ValueObjects;

/// <summary>
/// Type-specific configuration of a question. Persisted as a single JSON column so new settings
/// can be added without schema migrations. Only the properties relevant to the question's
/// <see cref="Enums.QuestionType"/> are used; the rest are ignored.
/// </summary>
public sealed class QuestionSettings
{
    /// <summary>Placeholder text for text / number / e-mail inputs.</summary>
    public string? Placeholder { get; set; }

    /// <summary>Minimum number of characters (text types).</summary>
    public int? MinLength { get; set; }

    /// <summary>Maximum number of characters (text types).</summary>
    public int? MaxLength { get; set; }

    /// <summary>Minimum allowed value (Number).</summary>
    public double? MinValue { get; set; }

    /// <summary>Maximum allowed value (Number).</summary>
    public double? MaxValue { get; set; }

    /// <summary>Whether decimals are accepted (Number). When false only whole numbers are valid.</summary>
    public bool AllowDecimals { get; set; } = true;

    /// <summary>Minimum number of selected options (Checkbox).</summary>
    public int? MinSelections { get; set; }

    /// <summary>Maximum number of selected options (Checkbox).</summary>
    public int? MaxSelections { get; set; }

    /// <summary>Number of stars (Rating). Valid answers are 1..RatingMax.</summary>
    public int RatingMax { get; set; } = 5;

    /// <summary>Lowest value of a linear scale (Scale). 0 for NPS.</summary>
    public int ScaleMin { get; set; }

    /// <summary>Highest value of a linear scale (Scale). 10 for NPS.</summary>
    public int ScaleMax { get; set; } = 10;

    /// <summary>Label shown under the lowest scale value, e.g. "Not at all likely".</summary>
    public string? ScaleMinLabel { get; set; }

    /// <summary>Label shown under the highest scale value, e.g. "Extremely likely".</summary>
    public string? ScaleMaxLabel { get; set; }

    /// <summary>Earliest allowed date (Date).</summary>
    public DateOnly? MinDate { get; set; }

    /// <summary>Latest allowed date (Date).</summary>
    public DateOnly? MaxDate { get; set; }

    /// <summary>Shuffle option order for each respondent (choice types). "Free text" options stay last.</summary>
    public bool RandomizeOptions { get; set; }

    /// <summary>Creates an independent copy (settings are mutable and shared between DTOs/entities otherwise).</summary>
    public QuestionSettings Clone() => (QuestionSettings)MemberwiseClone();
}
