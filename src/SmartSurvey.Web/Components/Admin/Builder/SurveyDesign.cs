using System.Globalization;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Web.Components.Admin.Builder;

/// <summary>A question with its position in the survey.</summary>
/// <param name="Question">The question.</param>
/// <param name="Section">Its section (page).</param>
/// <param name="SectionIndex">Zero-based page index.</param>
/// <param name="Number">One-based number across the whole survey.</param>
public sealed record PlacedQuestion(QuestionDto Question, SectionDto Section, int SectionIndex, int Number);

/// <summary>
/// Editing operations of the survey builder on a <see cref="SurveyDefinitionDto"/>: adding, moving,
/// duplicating and deleting pages/questions/options while keeping orders contiguous and conditional
/// logic consistent (rules and conditions that reference deleted items are removed).
/// </summary>
public static class SurveyDesign
{
    /// <summary>Default number of options for a new choice question.</summary>
    private const int DefaultOptionCount = 2;

    /// <summary>Every question in display order with its position.</summary>
    public static List<PlacedQuestion> Placed(SurveyDefinitionDto survey)
    {
        var result = new List<PlacedQuestion>();
        for (var s = 0; s < survey.Sections.Count; s++)
        {
            foreach (var question in survey.Sections[s].Questions)
            {
                result.Add(new PlacedQuestion(question, survey.Sections[s], s, result.Count + 1));
            }
        }

        return result;
    }

    /// <summary>Questions a condition of a rule targeting <paramref name="questionId"/> may use (strictly earlier ones).</summary>
    public static List<PlacedQuestion> QuestionsBefore(SurveyDefinitionDto survey, Guid questionId) =>
        Placed(survey).TakeWhile(p => p.Question.Id != questionId).ToList();

    /// <summary>Questions a condition of a rule targeting a page may use (questions on earlier pages).</summary>
    public static List<PlacedQuestion> QuestionsBeforeSection(SurveyDefinitionDto survey, Guid sectionId)
    {
        var index = survey.Sections.FindIndex(s => s.Id == sectionId);
        return Placed(survey).Where(p => p.SectionIndex < index).ToList();
    }

    /// <summary>A new blank question of the given type with sensible defaults.</summary>
    public static QuestionDto NewQuestion(QuestionType type)
    {
        var question = new QuestionDto { Type = type, Text = string.Empty };
        ApplyTypeDefaults(question);
        return question;
    }

    /// <summary>
    /// Adjusts a question after its type changed: choice types get default options when they have
    /// none; rating/scale settings get defaults.
    /// </summary>
    public static void ApplyTypeDefaults(QuestionDto question)
    {
        if (question.Type.IsChoice() && question.Options.Count == 0)
        {
            for (var i = 1; i <= DefaultOptionCount; i++)
            {
                question.Options.Add(new OptionDto { Text = $"Option {i}", Order = i - 1 });
            }
        }

        if (question.Type == QuestionType.Rating && question.Settings.RatingMax < 2)
        {
            question.Settings.RatingMax = 5;
        }

        if (question.Type == QuestionType.Scale && question.Settings.ScaleMax <= question.Settings.ScaleMin)
        {
            question.Settings.ScaleMin = 0;
            question.Settings.ScaleMax = 10;
        }
    }

    /// <summary>Adds a question at the end of a page (or after <paramref name="after"/>).</summary>
    public static QuestionDto AddQuestion(SectionDto section, QuestionType type, QuestionDto? after = null)
    {
        var question = NewQuestion(type);
        var index = after is null ? section.Questions.Count : section.Questions.IndexOf(after) + 1;
        section.Questions.Insert(index, question);
        Renumber(section);
        return question;
    }

    /// <summary>Copies a question (new ids, "(copy)" suffix) right below the original.</summary>
    public static QuestionDto DuplicateQuestion(SectionDto section, QuestionDto source)
    {
        var copy = new QuestionDto
        {
            Type = source.Type,
            Text = string.IsNullOrWhiteSpace(source.Text) ? string.Empty : $"{source.Text} (copy)",
            Description = source.Description,
            IsRequired = source.IsRequired,
            Settings = source.Settings.Clone(),
            Options = source.Options.Select(o => new OptionDto
            {
                Text = o.Text,
                Value = o.Value,
                Order = o.Order,
                AllowsFreeText = o.AllowsFreeText,
                FreeTextPlaceholder = o.FreeTextPlaceholder,
            }).ToList(),
        };

        section.Questions.Insert(section.Questions.IndexOf(source) + 1, copy);
        Renumber(section);
        return copy;
    }

    /// <summary>
    /// Moves a question one step up/down; at the edge of a page it moves to the neighbouring page.
    /// Returns false when it cannot move further.
    /// </summary>
    public static bool MoveQuestion(SurveyDefinitionDto survey, QuestionDto question, int delta)
    {
        var sectionIndex = survey.Sections.FindIndex(s => s.Questions.Contains(question));
        if (sectionIndex < 0)
        {
            return false;
        }

        var section = survey.Sections[sectionIndex];
        var index = section.Questions.IndexOf(question);
        var target = index + delta;

        if (target >= 0 && target < section.Questions.Count)
        {
            (section.Questions[index], section.Questions[target]) = (section.Questions[target], section.Questions[index]);
            Renumber(section);
            return true;
        }

        var neighbourIndex = sectionIndex + Math.Sign(delta);
        if (neighbourIndex < 0 || neighbourIndex >= survey.Sections.Count)
        {
            return false;
        }

        var neighbour = survey.Sections[neighbourIndex];
        section.Questions.RemoveAt(index);
        if (delta < 0)
        {
            neighbour.Questions.Add(question);
        }
        else
        {
            neighbour.Questions.Insert(0, question);
        }

        Renumber(section);
        Renumber(neighbour);
        return true;
    }

    /// <summary>Deletes a question and every logic rule/condition that references it.</summary>
    public static void DeleteQuestion(SurveyDefinitionDto survey, QuestionDto question)
    {
        foreach (var section in survey.Sections.Where(s => s.Questions.Remove(question)))
        {
            Renumber(section);
        }

        RemoveReferences(survey, [question.Id], []);
    }

    /// <summary>Adds a page after <paramref name="after"/> (or at the end) with one blank question.</summary>
    public static SectionDto AddSection(SurveyDefinitionDto survey, SectionDto? after = null)
    {
        var section = new SectionDto { Title = $"Page {survey.Sections.Count + 1}" };
        section.Questions.Add(NewQuestion(QuestionType.ShortText));
        var index = after is null ? survey.Sections.Count : survey.Sections.IndexOf(after) + 1;
        survey.Sections.Insert(index, section);
        Renumber(survey);
        return section;
    }

    /// <summary>Moves a page one step up/down.</summary>
    public static bool MoveSection(SurveyDefinitionDto survey, SectionDto section, int delta)
    {
        var index = survey.Sections.IndexOf(section);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= survey.Sections.Count)
        {
            return false;
        }

        (survey.Sections[index], survey.Sections[target]) = (survey.Sections[target], survey.Sections[index]);
        Renumber(survey);
        return true;
    }

    /// <summary>Deletes a page with its questions and every logic rule/condition referencing them.</summary>
    public static void DeleteSection(SurveyDefinitionDto survey, SectionDto section)
    {
        survey.Sections.Remove(section);
        Renumber(survey);
        RemoveReferences(survey, section.Questions.Select(q => q.Id).ToHashSet(), [section.Id]);
    }

    /// <summary>Adds an option ("Option n") to a choice question.</summary>
    public static OptionDto AddOption(QuestionDto question, bool allowsFreeText = false)
    {
        var option = new OptionDto
        {
            Text = allowsFreeText ? "Other" : $"Option {question.Options.Count + 1}",
            AllowsFreeText = allowsFreeText,
            FreeTextPlaceholder = allowsFreeText ? "Please specify" : null,
        };

        // Keep "Other"-style options last.
        var firstFreeText = question.Options.FindIndex(o => o.AllowsFreeText);
        question.Options.Insert(allowsFreeText || firstFreeText < 0 ? question.Options.Count : firstFreeText, option);
        Renumber(question);
        return option;
    }

    /// <summary>Removes an option and the conditions that compare with it.</summary>
    public static void DeleteOption(SurveyDefinitionDto survey, QuestionDto question, OptionDto option)
    {
        question.Options.Remove(option);
        Renumber(question);
        foreach (var rule in survey.LogicRules)
        {
            rule.Conditions.RemoveAll(c => c.OptionId == option.Id);
        }

        survey.LogicRules.RemoveAll(r => r.Conditions.Count == 0);
    }

    /// <summary>Moves an option one step up/down.</summary>
    public static void MoveOption(QuestionDto question, OptionDto option, int delta)
    {
        var index = question.Options.IndexOf(option);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= question.Options.Count)
        {
            return;
        }

        (question.Options[index], question.Options[target]) = (question.Options[target], question.Options[index]);
        Renumber(question);
    }

    /// <summary>Rules whose target is the question.</summary>
    public static List<LogicRuleDto> RulesFor(SurveyDefinitionDto survey, QuestionDto question) =>
        survey.LogicRules.Where(r => r.TargetQuestionId == question.Id).ToList();

    /// <summary>Rules whose target is the page.</summary>
    public static List<LogicRuleDto> RulesFor(SurveyDefinitionDto survey, SectionDto section) =>
        survey.LogicRules.Where(r => r.TargetSectionId == section.Id).ToList();

    /// <summary>Human-readable operator for a question type, e.g. "is", "contains", "≥".</summary>
    public static string OperatorLabel(QuestionType type, ConditionOperator op) => op switch
    {
        ConditionOperator.IsAnswered => "is answered",
        ConditionOperator.IsNotAnswered => "is not answered",
        _ when type.IsChoice() => op switch
        {
            ConditionOperator.Equals => "is",
            ConditionOperator.NotEquals => "is not",
            ConditionOperator.Contains => "includes",
            ConditionOperator.NotContains => "does not include",
            _ => op.ToString(),
        },
        _ when type.IsNumeric() || type.IsDate() => op switch
        {
            ConditionOperator.Equals => "=",
            ConditionOperator.NotEquals => "≠",
            ConditionOperator.GreaterThan => type.IsDate() ? "is after" : ">",
            ConditionOperator.GreaterThanOrEqual => type.IsDate() ? "is on or after" : "≥",
            ConditionOperator.LessThan => type.IsDate() ? "is before" : "<",
            ConditionOperator.LessThanOrEqual => type.IsDate() ? "is on or before" : "≤",
            _ => op.ToString(),
        },
        _ => op switch
        {
            ConditionOperator.Equals => "equals",
            ConditionOperator.NotEquals => "does not equal",
            ConditionOperator.Contains => "contains",
            ConditionOperator.NotContains => "does not contain",
            _ => op.ToString(),
        },
    };

    /// <summary>True when the condition compares with an option of a choice question.</summary>
    public static bool NeedsOption(QuestionType type, ConditionOperator op) =>
        type.IsChoice() && op is not (ConditionOperator.IsAnswered or ConditionOperator.IsNotAnswered);

    /// <summary>True when the condition compares with a typed value (text, number or date).</summary>
    public static bool NeedsValue(QuestionType type, ConditionOperator op) =>
        !type.IsChoice() && op is not (ConditionOperator.IsAnswered or ConditionOperator.IsNotAnswered);

    /// <summary>A new condition on <paramref name="source"/> with the first supported operator.</summary>
    public static LogicConditionDto NewCondition(QuestionDto source) => new()
    {
        SourceQuestionId = source.Id,
        Operator = source.Type.SupportedOperators()[0],
        OptionId = source.Type.IsChoice() ? source.Options.FirstOrDefault()?.Id : null,
    };

    /// <summary>Resets operator/option/value after the condition's source question changed.</summary>
    public static void ResetCondition(LogicConditionDto condition, QuestionDto source)
    {
        var fresh = NewCondition(source);
        condition.Operator = fresh.Operator;
        condition.OptionId = fresh.OptionId;
        condition.Value = null;
    }

    /// <summary>Formats a number for a condition value (invariant culture).</summary>
    public static string FormatNumber(double value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Sets contiguous orders for pages and their questions.</summary>
    public static void Renumber(SurveyDefinitionDto survey)
    {
        for (var i = 0; i < survey.Sections.Count; i++)
        {
            survey.Sections[i].Order = i;
            Renumber(survey.Sections[i]);
        }
    }

    private static void Renumber(SectionDto section)
    {
        for (var i = 0; i < section.Questions.Count; i++)
        {
            section.Questions[i].Order = i;
        }
    }

    private static void Renumber(QuestionDto question)
    {
        for (var i = 0; i < question.Options.Count; i++)
        {
            question.Options[i].Order = i;
        }
    }

    /// <summary>Removes rules targeting the ids and conditions using the questions; drops rules left without conditions.</summary>
    private static void RemoveReferences(SurveyDefinitionDto survey, ICollection<Guid> questionIds, ICollection<Guid> sectionIds)
    {
        survey.LogicRules.RemoveAll(r =>
            (r.TargetQuestionId is { } q && questionIds.Contains(q)) || (r.TargetSectionId is { } s && sectionIds.Contains(s)));
        foreach (var rule in survey.LogicRules)
        {
            rule.Conditions.RemoveAll(c => questionIds.Contains(c.SourceQuestionId));
        }

        survey.LogicRules.RemoveAll(r => r.Conditions.Count == 0);
    }
}
