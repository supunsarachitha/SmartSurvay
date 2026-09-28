using SmartSurvey.Domain.Common;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Domain.Entities;

/// <summary>A question on a survey page.</summary>
public class Question : Entity, IWorkspaceOwned
{
    /// <inheritdoc />
    public Guid WorkspaceId { get; set; }

    /// <summary>Owning survey.</summary>
    public Guid SurveyId { get; set; }

    /// <summary>Owning survey (navigation).</summary>
    public Survey? Survey { get; set; }

    /// <summary>Section (page) the question is displayed on.</summary>
    public Guid SectionId { get; set; }

    /// <summary>Section (navigation).</summary>
    public SurveySection? Section { get; set; }

    /// <summary>Input type.</summary>
    public QuestionType Type { get; set; }

    /// <summary>The question text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Optional help text shown under the question.</summary>
    public string? Description { get; set; }

    /// <summary>Short, unique (per survey) code such as <c>Q1</c>, used as column header in exports.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Zero-based position within the section.</summary>
    public int Order { get; set; }

    /// <summary>Whether an answer is mandatory (only enforced while the question is visible).</summary>
    public bool IsRequired { get; set; }

    /// <summary>Type-specific settings (JSON column).</summary>
    public QuestionSettings Settings { get; set; } = new();

    /// <summary>Answer options (choice types only).</summary>
    public List<QuestionOption> Options { get; set; } = [];
}

/// <summary>
/// An answer option of a choice question. Setting <see cref="AllowsFreeText"/> turns the option into a
/// combined "choice + text" option such as <em>Other (please specify)</em>.
/// </summary>
public class QuestionOption : Entity, IWorkspaceOwned
{
    /// <inheritdoc />
    public Guid WorkspaceId { get; set; }

    /// <summary>Owning question.</summary>
    public Guid QuestionId { get; set; }

    /// <summary>Owning question (navigation).</summary>
    public Question? Question { get; set; }

    /// <summary>Label shown to respondents.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Optional stable code for exports/integrations (defaults to the label).</summary>
    public string? Value { get; set; }

    /// <summary>Zero-based position within the question.</summary>
    public int Order { get; set; }

    /// <summary>When selected, a free-text input is shown and its value stored with the selection.</summary>
    public bool AllowsFreeText { get; set; }

    /// <summary>Placeholder of the free-text input, e.g. "Please specify".</summary>
    public string? FreeTextPlaceholder { get; set; }
}

/// <summary>
/// Conditional logic rule: shows or hides a target question or section depending on answers to
/// earlier questions. Exactly one of <see cref="TargetQuestionId"/> / <see cref="TargetSectionId"/> is set.
/// </summary>
public class LogicRule : Entity, IWorkspaceOwned
{
    /// <inheritdoc />
    public Guid WorkspaceId { get; set; }

    /// <summary>Owning survey.</summary>
    public Guid SurveyId { get; set; }

    /// <summary>Owning survey (navigation).</summary>
    public Survey? Survey { get; set; }

    /// <summary>Question affected by the rule.</summary>
    public Guid? TargetQuestionId { get; set; }

    /// <summary>Question affected by the rule (navigation).</summary>
    public Question? TargetQuestion { get; set; }

    /// <summary>Section affected by the rule.</summary>
    public Guid? TargetSectionId { get; set; }

    /// <summary>Section affected by the rule (navigation).</summary>
    public SurveySection? TargetSection { get; set; }

    /// <summary>Show or hide.</summary>
    public LogicAction Action { get; set; } = LogicAction.Show;

    /// <summary>Combine conditions with AND (All) or OR (Any).</summary>
    public LogicMatchType MatchType { get; set; } = LogicMatchType.All;

    /// <summary>Conditions evaluated against earlier answers.</summary>
    public List<LogicCondition> Conditions { get; set; } = [];
}

/// <summary>A single condition of a <see cref="LogicRule"/>.</summary>
public class LogicCondition : Entity, IWorkspaceOwned
{
    /// <inheritdoc />
    public Guid WorkspaceId { get; set; }

    /// <summary>Owning rule.</summary>
    public Guid LogicRuleId { get; set; }

    /// <summary>Owning rule (navigation).</summary>
    public LogicRule? LogicRule { get; set; }

    /// <summary>Question whose answer is tested (must come before the rule's target).</summary>
    public Guid SourceQuestionId { get; set; }

    /// <summary>Source question (navigation).</summary>
    public Question? SourceQuestion { get; set; }

    /// <summary>Comparison operator.</summary>
    public ConditionOperator Operator { get; set; } = ConditionOperator.Equals;

    /// <summary>Option to compare with (choice questions).</summary>
    public Guid? OptionId { get; set; }

    /// <summary>Option (navigation).</summary>
    public QuestionOption? Option { get; set; }

    /// <summary>Value to compare with (text, number or ISO-8601 date).</summary>
    public string? Value { get; set; }
}
