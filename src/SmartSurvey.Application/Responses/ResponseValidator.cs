using System.Globalization;
using System.Text.RegularExpressions;
using SmartSurvey.Application.Logic;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Responses;

/// <summary>
/// Validates and sanitises survey answers. Pure functions shared by the Blazor runner (per page,
/// live feedback) and <c>ResponseService</c> (authoritative, on submission).
/// </summary>
public static partial class ResponseValidator
{
    /// <summary>Hard upper bound for text answers when the question sets no MaxLength.</summary>
    public const int MaxTextLength = 10_000;

    /// <summary>Upper bound for "Other → free text" values.</summary>
    public const int MaxFreeTextLength = 1_000;

    /// <summary>
    /// Validates every visible question of the given sections (all visible sections when
    /// <paramref name="sectionIds"/> is null). Returns errors keyed by question id; empty = valid.
    /// </summary>
    public static Dictionary<Guid, List<string>> Validate(
        SurveyDefinitionDto survey,
        IReadOnlyDictionary<Guid, AnswerInputDto> answers,
        SurveyVisibility visibility,
        IEnumerable<Guid>? sectionIds = null)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(visibility);

        var only = sectionIds?.ToHashSet();
        var errors = new Dictionary<Guid, List<string>>();

        foreach (var section in visibility.VisibleSections(survey))
        {
            if (only is not null && !only.Contains(section.Id))
            {
                continue;
            }

            foreach (var question in visibility.VisibleQuestions(section))
            {
                answers.TryGetValue(question.Id, out var answer);
                var questionErrors = ValidateQuestion(question, answer);
                if (questionErrors.Count > 0)
                {
                    errors[question.Id] = questionErrors;
                }
            }
        }

        return errors;
    }

    /// <summary>Validates one answer against its question's type, settings and required flag.</summary>
    public static List<string> ValidateQuestion(QuestionDto question, AnswerInputDto? answer)
    {
        ArgumentNullException.ThrowIfNull(question);

        var errors = new List<string>();
        var settings = question.Settings;

        if (answer is null || !answer.HasValue(question.Type))
        {
            if (question.IsRequired)
            {
                errors.Add("This question is required.");
            }

            return errors;
        }

        switch (question.Type)
        {
            case QuestionType.ShortText:
            case QuestionType.LongText:
            case QuestionType.Email:
                ValidateText(question, answer.Text!.Trim(), errors);
                break;

            case QuestionType.Number:
                ValidateNumber(answer.Number!.Value, settings.MinValue, settings.MaxValue, !settings.AllowDecimals, errors);
                break;

            case QuestionType.Rating:
                ValidateNumber(answer.Number!.Value, 1, Math.Max(1, settings.RatingMax), true, errors);
                break;

            case QuestionType.Scale:
                ValidateNumber(answer.Number!.Value, settings.ScaleMin, settings.ScaleMax, true, errors);
                break;

            case QuestionType.Date:
                var date = answer.Date!.Value;
                if (settings.MinDate.HasValue && date < settings.MinDate.Value)
                {
                    errors.Add($"The date must be on or after {settings.MinDate.Value:yyyy-MM-dd}.");
                }

                if (settings.MaxDate.HasValue && date > settings.MaxDate.Value)
                {
                    errors.Add($"The date must be on or before {settings.MaxDate.Value:yyyy-MM-dd}.");
                }

                break;

            case QuestionType.Radio:
            case QuestionType.Dropdown:
            case QuestionType.Checkbox:
                ValidateChoice(question, answer, errors);
                break;
        }

        return errors;
    }

    /// <summary>
    /// Returns a cleaned copy of an answer: keeps only the field relevant to the question type, trims
    /// text, drops unknown/duplicate options, drops free text for options that do not allow it and
    /// truncates over-long values. Returns null when nothing remains.
    /// </summary>
    public static AnswerInputDto? Sanitize(QuestionDto question, AnswerInputDto answer)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(answer);

        var clean = new AnswerInputDto { QuestionId = question.Id };
        var type = question.Type;

        if (type.IsText())
        {
            var text = answer.Text?.Trim();
            clean.Text = string.IsNullOrEmpty(text) ? null : Truncate(text, MaxTextLength);
        }
        else if (type.IsNumeric())
        {
            clean.Number = answer.Number is { } n && double.IsFinite(n) ? n : null;
        }
        else if (type.IsDate())
        {
            clean.Date = answer.Date;
        }
        else if (type.IsChoice())
        {
            var options = question.Options.ToDictionary(o => o.Id);
            var seen = new HashSet<Guid>();
            foreach (var selection in answer.Selections)
            {
                if (!options.TryGetValue(selection.OptionId, out var option) || !seen.Add(selection.OptionId))
                {
                    continue;
                }

                var freeText = option.AllowsFreeText ? selection.FreeText?.Trim() : null;
                clean.Selections.Add(new SelectionInputDto
                {
                    OptionId = option.Id,
                    FreeText = string.IsNullOrEmpty(freeText) ? null : Truncate(freeText, MaxFreeTextLength),
                });
            }

            if (!type.IsMultiSelect() && clean.Selections.Count > 1)
            {
                clean.Selections.RemoveRange(1, clean.Selections.Count - 1);
            }
        }

        return clean.HasValue(type) ? clean : null;
    }

    /// <summary>Simple, pragmatic e-mail format check (local@domain.tld, no spaces).</summary>
    public static bool IsValidEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 254 && EmailRegex().IsMatch(value);

    private static void ValidateText(QuestionDto question, string text, List<string> errors)
    {
        var settings = question.Settings;
        var max = settings.MaxLength is > 0 ? Math.Min(settings.MaxLength.Value, MaxTextLength) : MaxTextLength;
        if (text.Length > max)
        {
            errors.Add($"Please enter at most {max} characters.");
        }

        if (settings.MinLength is > 0 && text.Length < settings.MinLength.Value)
        {
            errors.Add($"Please enter at least {settings.MinLength.Value} characters.");
        }

        if (question.Type == QuestionType.Email && !IsValidEmail(text))
        {
            errors.Add("Please enter a valid e-mail address.");
        }
    }

    private static void ValidateNumber(double value, double? min, double? max, bool wholeNumber, List<string> errors)
    {
        if (!double.IsFinite(value))
        {
            errors.Add("Please enter a valid number.");
            return;
        }

        if (wholeNumber && Math.Abs(value - Math.Round(value)) > 1e-9)
        {
            errors.Add("Please enter a whole number.");
        }

        if (min.HasValue && value < min.Value)
        {
            errors.Add($"The value must be at least {min.Value.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (max.HasValue && value > max.Value)
        {
            errors.Add($"The value must be at most {max.Value.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static void ValidateChoice(QuestionDto question, AnswerInputDto answer, List<string> errors)
    {
        var options = question.Options.ToDictionary(o => o.Id);
        var selections = answer.Selections;

        if (selections.Any(s => !options.ContainsKey(s.OptionId))
            || selections.Select(s => s.OptionId).Distinct().Count() != selections.Count)
        {
            errors.Add("An invalid option was selected.");
            return;
        }

        var settings = question.Settings;
        if (!question.Type.IsMultiSelect() && selections.Count > 1)
        {
            errors.Add("Please select only one option.");
        }

        if (question.Type.IsMultiSelect())
        {
            if (settings.MinSelections is > 0 && selections.Count < settings.MinSelections.Value)
            {
                errors.Add($"Please select at least {settings.MinSelections.Value} options.");
            }

            if (settings.MaxSelections is > 0 && selections.Count > settings.MaxSelections.Value)
            {
                errors.Add($"Please select at most {settings.MaxSelections.Value} options.");
            }
        }

        foreach (var selection in selections)
        {
            var option = options[selection.OptionId];
            if (!option.AllowsFreeText)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(selection.FreeText))
            {
                errors.Add($"Please specify a value for \"{option.Text}\".");
            }
            else if (selection.FreeText.Trim().Length > MaxFreeTextLength)
            {
                errors.Add($"Please enter at most {MaxFreeTextLength} characters for \"{option.Text}\".");
            }
        }
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();
}
