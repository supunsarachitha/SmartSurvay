using SmartSurvey.Application.Logic;
using SmartSurvey.Domain.Enums;
using L = SmartSurvey.Application.Surveys.Validation.SurveyDesignLimits;

namespace SmartSurvey.Application.Surveys.Validation;

/// <summary>
/// Validates conditional logic against the survey layout:
/// <list type="bullet">
/// <item>a rule targets exactly one existing question or page and has at least one condition;</item>
/// <item>every condition uses an existing question that is answered <em>before</em> the target
/// (an earlier question for question targets, a question on an earlier page for page targets) —
/// the runner evaluates logic in display order, so forward references could never match;</item>
/// <item>the operator suits the source question's type and the comparison operand is usable
/// (an option of that question for choice questions, a parseable number/date otherwise).</item>
/// </list>
/// </summary>
internal static class SurveyLogicChecks
{
    /// <summary>Reports every logic problem of <paramref name="survey"/>.</summary>
    /// <param name="survey">Design to check.</param>
    /// <param name="addFailure">Receives (property path, message) for each violation.</param>
    public static void Check(SurveyDefinitionDto survey, Action<string, string> addFailure)
    {
        var layout = SurveyLayout.From(survey);
        var rules = survey.LogicRules.OrEmpty();

        for (var r = 0; r < rules.Count; r++)
        {
            if (rules[r] is not null)
            {
                CheckRule(rules[r], $"LogicRules[{r}]", layout, addFailure);
            }
        }
    }

    private static void CheckRule(LogicRuleDto rule, string path, SurveyLayout layout, Action<string, string> addFailure)
    {
        if (!Enum.IsDefined(rule.Action))
        {
            addFailure($"{path}.Action", "Please choose whether the rule shows or hides its target.");
        }

        if (!Enum.IsDefined(rule.MatchType))
        {
            addFailure($"{path}.MatchType", "Please choose whether all or any of the conditions must match.");
        }

        var target = ResolveTarget(rule, path, layout, addFailure);

        var conditions = rule.Conditions.OrEmpty();
        if (conditions.Count == 0)
        {
            addFailure($"{path}.Conditions", "Add at least one condition to the rule.");
            return;
        }

        for (var c = 0; c < conditions.Count; c++)
        {
            if (conditions[c] is not null)
            {
                CheckCondition(conditions[c], target, $"{path}.Conditions[{c}]", layout, addFailure);
            }
        }
    }

    /// <summary>Resolves the rule's target; returns null (after reporting why) when it is unusable.</summary>
    private static RuleTarget? ResolveTarget(LogicRuleDto rule, string path, SurveyLayout layout, Action<string, string> addFailure)
    {
        if (rule.TargetQuestionId is null && rule.TargetSectionId is null)
        {
            addFailure(path, "Choose what the rule shows or hides: a question or a page.");
            return null;
        }

        if (rule.TargetQuestionId is not null && rule.TargetSectionId is not null)
        {
            addFailure(path, "A rule can show or hide either a question or a page, not both.");
            return null;
        }

        if (rule.TargetQuestionId is { } questionId)
        {
            if (layout.Questions.TryGetValue(questionId, out var question))
            {
                return new RuleTarget(question, null);
            }

            addFailure($"{path}.TargetQuestionId", "The question this rule shows or hides does not exist (it may have been deleted).");
            return null;
        }

        if (layout.Sections.TryGetValue(rule.TargetSectionId!.Value, out var section))
        {
            return new RuleTarget(null, section);
        }

        addFailure($"{path}.TargetSectionId", "The page this rule shows or hides does not exist (it may have been deleted).");
        return null;
    }

    private static void CheckCondition(
        LogicConditionDto condition, RuleTarget? target, string path, SurveyLayout layout, Action<string, string> addFailure)
    {
        if (!layout.Questions.TryGetValue(condition.SourceQuestionId, out var source))
        {
            addFailure($"{path}.SourceQuestionId", "The question used in this condition does not exist (it may have been deleted).");
            return;
        }

        if (target is not null)
        {
            CheckOrdering(source, target, $"{path}.SourceQuestionId", addFailure);
        }

        var type = source.Question.Type;
        if (!Enum.IsDefined(condition.Operator))
        {
            addFailure($"{path}.Operator", "Please choose a valid comparison.");
            return;
        }

        if (!type.SupportedOperators().Contains(condition.Operator))
        {
            addFailure($"{path}.Operator", $"The comparison '{condition.Operator.DisplayName()}' cannot be used with {type.DisplayName()} questions.");
            return;
        }

        if (!condition.Operator.IsUnary())
        {
            CheckOperand(condition, source, path, addFailure);
        }
    }

    /// <summary>A condition may only look at answers given before the target is reached.</summary>
    private static void CheckOrdering(PlacedQuestion source, RuleTarget target, string path, Action<string, string> addFailure)
    {
        if (target.Question is { } targetQuestion)
        {
            if (source.Question.Id == targetQuestion.Question.Id)
            {
                addFailure(path, "A question cannot show or hide itself. Base the condition on an earlier question.");
            }
            else if (source.Position > targetQuestion.Position)
            {
                addFailure(path, $"Conditions can only use questions that come before the question they show or hide. Move {source.Label} above {targetQuestion.Label} or choose another question.");
            }

            return;
        }

        var targetSection = target.Section!;
        if (source.SectionPosition == targetSection.Position)
        {
            addFailure(path, $"A page cannot be shown or hidden based on its own questions. Choose a question from a page before {targetSection.Label}.");
        }
        else if (source.SectionPosition > targetSection.Position)
        {
            addFailure(path, $"Conditions of a page rule can only use questions from earlier pages. {source.Label} comes after {targetSection.Label}.");
        }
    }

    /// <summary>Checks the option (choice questions) or the comparison value (all other types).</summary>
    private static void CheckOperand(LogicConditionDto condition, PlacedQuestion source, string path, Action<string, string> addFailure)
    {
        var question = source.Question;
        if (question.Type.IsChoice())
        {
            if (condition.OptionId is not { } optionId)
            {
                addFailure($"{path}.OptionId", $"Choose which answer option of {source.Label} the condition checks.");
            }
            else if (!question.Options.OrEmpty().Any(o => o is not null && o.Id == optionId))
            {
                addFailure($"{path}.OptionId", $"The selected option does not belong to {source.Label}. Choose one of its answer options.");
            }

            return;
        }

        var value = condition.Value;
        if (value is { Length: > L.ConditionValueMaxLength })
        {
            addFailure($"{path}.Value", $"The comparison value can be at most {L.ConditionValueMaxLength} characters long.");
        }
        else if (question.Type.IsNumeric() && !ConditionMatcher.TryParseNumber(value, out _))
        {
            addFailure($"{path}.Value", "Enter a number to compare with (for example 42 or 3.5).");
        }
        else if (question.Type.IsDate() && !ConditionMatcher.TryParseDate(value, out _))
        {
            addFailure($"{path}.Value", "Enter a date to compare with in the format yyyy-MM-dd.");
        }
        else if (string.IsNullOrWhiteSpace(value))
        {
            addFailure($"{path}.Value", "Enter a value to compare with.");
        }
    }

    /// <summary>What a rule shows or hides: exactly one of the two is set.</summary>
    private sealed record RuleTarget(PlacedQuestion? Question, PlacedSection? Section);
}

/// <summary>A page with its zero-based display position.</summary>
/// <param name="Section">The page.</param>
/// <param name="Position">Display position among all pages.</param>
internal sealed record PlacedSection(SectionDto Section, int Position)
{
    /// <summary>Human-readable reference used in messages, e.g. "page 2".</summary>
    public string Label => $"page {Position + 1}";
}

/// <summary>A question with its display position.</summary>
/// <param name="Question">The question.</param>
/// <param name="Position">Zero-based position across the whole survey.</param>
/// <param name="SectionPosition">Zero-based position of the page containing the question.</param>
internal sealed record PlacedQuestion(QuestionDto Question, int Position, int SectionPosition)
{
    /// <summary>Human-readable reference used in messages: the code, or "question N" when there is none.</summary>
    public string Label => string.IsNullOrWhiteSpace(Question.Code) ? $"question {Position + 1}" : Question.Code.Trim();
}

/// <summary>
/// Display positions of all pages and questions, computed exactly like the runner does (pages by
/// <c>Order</c>, then questions by <c>Order</c>). Duplicate ids keep their first occurrence; the
/// uniqueness check reports the duplicates separately.
/// </summary>
internal sealed class SurveyLayout
{
    private SurveyLayout()
    {
    }

    /// <summary>Pages by id.</summary>
    public Dictionary<Guid, PlacedSection> Sections { get; } = [];

    /// <summary>Questions by id.</summary>
    public Dictionary<Guid, PlacedQuestion> Questions { get; } = [];

    /// <summary>Builds the layout of a design.</summary>
    public static SurveyLayout From(SurveyDefinitionDto survey)
    {
        var layout = new SurveyLayout();
        var position = 0;
        var sections = survey.Sections.OrEmpty().Where(s => s is not null).OrderBy(s => s.Order).ToList();

        for (var s = 0; s < sections.Count; s++)
        {
            layout.Sections.TryAdd(sections[s].Id, new PlacedSection(sections[s], s));

            foreach (var question in sections[s].Questions.OrEmpty().Where(q => q is not null).OrderBy(q => q.Order))
            {
                layout.Questions.TryAdd(question.Id, new PlacedQuestion(question, position++, s));
            }
        }

        return layout;
    }
}
