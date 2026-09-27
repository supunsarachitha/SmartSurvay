using SmartSurvey.Application.Common;
using SmartSurvey.Application.Responses;

namespace SmartSurvey.Application.Surveys.Validation;

/// <summary>
/// Size and range limits of a survey design. The text limits mirror the database column sizes, so
/// a design that passes validation can always be stored. The builder UI can use the same constants
/// for <c>maxlength</c> attributes and range inputs.
/// </summary>
public static class SurveyDesignLimits
{
    /// <summary>Maximum length of the survey title.</summary>
    public const int TitleMaxLength = 200;

    /// <summary>Maximum length of the survey description.</summary>
    public const int DescriptionMaxLength = 4000;

    /// <summary>Maximum length of the welcome and thank-you messages.</summary>
    public const int MessageMaxLength = 4000;

    /// <summary>Maximum length of the share-link slug.</summary>
    public const int SlugMaxLength = SlugGenerator.MaxLength;

    /// <summary>Maximum length of a page title.</summary>
    public const int SectionTitleMaxLength = 200;

    /// <summary>Maximum length of a page description.</summary>
    public const int SectionDescriptionMaxLength = 2000;

    /// <summary>Maximum length of a question text.</summary>
    public const int QuestionTextMaxLength = 1000;

    /// <summary>Maximum length of a question help text.</summary>
    public const int QuestionDescriptionMaxLength = 2000;

    /// <summary>Maximum length of a question code.</summary>
    public const int QuestionCodeMaxLength = 50;

    /// <summary>Maximum length of an input placeholder.</summary>
    public const int PlaceholderMaxLength = 200;

    /// <summary>Maximum length of the labels under the ends of a linear scale.</summary>
    public const int ScaleLabelMaxLength = 100;

    /// <summary>Minimum number of answer options of a choice question.</summary>
    public const int MinChoiceOptions = 2;

    /// <summary>Maximum length of an option label.</summary>
    public const int OptionTextMaxLength = 500;

    /// <summary>Maximum length of an option export value.</summary>
    public const int OptionValueMaxLength = 100;

    /// <summary>Maximum length of the placeholder of an "Other → free text" input.</summary>
    public const int FreeTextPlaceholderMaxLength = 200;

    /// <summary>Largest configurable maximum length of a text answer.</summary>
    public const int TextAnswerMaxLength = ResponseValidator.MaxTextLength;

    /// <summary>Smallest number of stars of a rating question.</summary>
    public const int RatingMaxLowest = 2;

    /// <summary>Largest number of stars of a rating question.</summary>
    public const int RatingMaxHighest = 10;

    /// <summary>Lowest value a linear scale may start at.</summary>
    public const int ScaleLowest = 0;

    /// <summary>Highest value a linear scale may end at.</summary>
    public const int ScaleHighest = 100;

    /// <summary>Largest allowed distance between the ends of a linear scale.</summary>
    public const int ScaleMaxSpan = 20;

    /// <summary>Maximum length of a logic condition's comparison value.</summary>
    public const int ConditionValueMaxLength = 500;
}
