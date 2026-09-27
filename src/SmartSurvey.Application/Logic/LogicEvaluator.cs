using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Logic;

/// <summary>Which sections and questions are visible for a given set of answers.</summary>
public sealed class SurveyVisibility
{
    private readonly HashSet<Guid> _sections;
    private readonly HashSet<Guid> _questions;

    internal SurveyVisibility(HashSet<Guid> sections, HashSet<Guid> questions)
    {
        _sections = sections;
        _questions = questions;
    }

    /// <summary>Ids of visible sections.</summary>
    public IReadOnlySet<Guid> VisibleSectionIds => _sections;

    /// <summary>Ids of visible questions (a question in a hidden section is never visible).</summary>
    public IReadOnlySet<Guid> VisibleQuestionIds => _questions;

    /// <summary>True when the section is visible.</summary>
    public bool IsSectionVisible(Guid sectionId) => _sections.Contains(sectionId);

    /// <summary>True when the question is visible.</summary>
    public bool IsQuestionVisible(Guid questionId) => _questions.Contains(questionId);

    /// <summary>Visible sections of <paramref name="survey"/> in display order (the runner's pages).</summary>
    public IReadOnlyList<SectionDto> VisibleSections(SurveyDefinitionDto survey) =>
        survey.Sections.OrderBy(s => s.Order).Where(s => _sections.Contains(s.Id)).ToList();

    /// <summary>Visible questions of a section in display order.</summary>
    public IReadOnlyList<QuestionDto> VisibleQuestions(SectionDto section) =>
        section.Questions.OrderBy(q => q.Order).Where(q => _questions.Contains(q.Id)).ToList();
}

/// <summary>
/// Evaluates a survey's show/hide rules. Pure and deterministic: the Blazor runner calls it after
/// every answer change for live branching, and <c>ResponseService</c> calls it again on submission.
/// </summary>
/// <remarks>
/// Algorithm: walk sections and questions in display order. For each target:
/// <list type="number">
/// <item>No rules (or only rules without conditions) → visible.</item>
/// <item>If it has <c>Show</c> rules → visible only when at least one Show rule matches.</item>
/// <item>If any <c>Hide</c> rule matches → hidden (Hide wins over Show).</item>
/// </list>
/// A condition only "sees" the answer of a source question that has already been evaluated as
/// visible; answers to hidden questions count as unanswered. This makes chained logic behave
/// intuitively (hiding Q2 also hides Q3 that depends on Q2) and ignores forward references.
/// </remarks>
public static class LogicEvaluator
{
    /// <summary>Computes visibility for the given answers.</summary>
    /// <param name="survey">Survey design.</param>
    /// <param name="answers">Answers keyed by question id (may be empty).</param>
    public static SurveyVisibility Evaluate(SurveyDefinitionDto survey, IReadOnlyDictionary<Guid, AnswerInputDto> answers)
    {
        ArgumentNullException.ThrowIfNull(survey);
        ArgumentNullException.ThrowIfNull(answers);

        var questionRules = survey.LogicRules
            .Where(r => r.TargetQuestionId.HasValue && r.Conditions.Count > 0)
            .ToLookup(r => r.TargetQuestionId!.Value);
        var sectionRules = survey.LogicRules
            .Where(r => r.TargetSectionId.HasValue && r.Conditions.Count > 0)
            .ToLookup(r => r.TargetSectionId!.Value);
        var questionsById = survey.Sections.SelectMany(s => s.Questions)
            .GroupBy(q => q.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var visibleSections = new HashSet<Guid>();
        var visibleQuestions = new HashSet<Guid>();

        bool ConditionMatches(LogicConditionDto condition)
        {
            if (!questionsById.TryGetValue(condition.SourceQuestionId, out var source))
            {
                return false; // dangling reference (source deleted) never matches
            }

            var answer = visibleQuestions.Contains(source.Id) && answers.TryGetValue(source.Id, out var a) ? a : null;
            return ConditionMatcher.Matches(source.Type, condition.Operator, condition.OptionId, condition.Value, answer);
        }

        bool RuleMatches(LogicRuleDto rule) => rule.MatchType == LogicMatchType.Any
            ? rule.Conditions.Any(ConditionMatches)
            : rule.Conditions.All(ConditionMatches);

        bool IsVisible(IEnumerable<LogicRuleDto> rules)
        {
            var list = rules as IList<LogicRuleDto> ?? rules.ToList();
            if (list.Count == 0)
            {
                return true;
            }

            var showRules = list.Where(r => r.Action == LogicAction.Show).ToList();
            var visible = showRules.Count == 0 || showRules.Any(RuleMatches);
            if (visible && list.Where(r => r.Action == LogicAction.Hide).Any(RuleMatches))
            {
                visible = false;
            }

            return visible;
        }

        foreach (var section in survey.Sections.OrderBy(s => s.Order))
        {
            if (!IsVisible(sectionRules[section.Id].ToList()))
            {
                continue;
            }

            visibleSections.Add(section.Id);

            foreach (var question in section.Questions.OrderBy(q => q.Order))
            {
                if (IsVisible(questionRules[question.Id].ToList()))
                {
                    visibleQuestions.Add(question.Id);
                }
            }
        }

        return new SurveyVisibility(visibleSections, visibleQuestions);
    }

    /// <summary>Convenience overload taking an answer list.</summary>
    public static SurveyVisibility Evaluate(SurveyDefinitionDto survey, IEnumerable<AnswerInputDto> answers) =>
        Evaluate(survey, ToDictionary(answers));

    /// <summary>Indexes answers by question id (last one wins for duplicates).</summary>
    public static Dictionary<Guid, AnswerInputDto> ToDictionary(IEnumerable<AnswerInputDto> answers)
    {
        var dict = new Dictionary<Guid, AnswerInputDto>();
        foreach (var answer in answers)
        {
            dict[answer.QuestionId] = answer;
        }

        return dict;
    }
}
