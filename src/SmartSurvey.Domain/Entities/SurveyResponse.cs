using SmartSurvey.Domain.Common;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.Domain.Entities;

/// <summary>One respondent's submission (or saved draft) for a survey.</summary>
public class SurveyResponse : Entity, IWorkspaceOwned
{
    /// <inheritdoc />
    public Guid WorkspaceId { get; set; }

    /// <summary>Survey being answered.</summary>
    public Guid SurveyId { get; set; }

    /// <summary>Survey (navigation).</summary>
    public Survey? Survey { get; set; }

    /// <summary>Logged-in respondent; null for anonymous responses.</summary>
    public Guid? RespondentId { get; set; }

    /// <summary>Respondent (navigation).</summary>
    public ApplicationUser? Respondent { get; set; }

    /// <summary>Draft or completed.</summary>
    public ResponseStatus Status { get; set; } = ResponseStatus.InProgress;

    /// <summary>UTC timestamp when answering started.</summary>
    public DateTime StartedAt { get; set; }

    /// <summary>UTC timestamp of the last draft save.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>UTC timestamp of submission (completed responses only).</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>Page index the respondent was on when the draft was saved (resume position).</summary>
    public int CurrentSectionIndex { get; set; }

    /// <summary>Browser user agent (truncated), useful for response quality analysis.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Answers, at most one per question.</summary>
    public List<Answer> Answers { get; set; } = [];

    /// <summary>Time taken to complete the survey, if submitted.</summary>
    public TimeSpan? Duration => SubmittedAt.HasValue ? SubmittedAt.Value - StartedAt : null;
}

/// <summary>
/// Answer to a single question. The column used depends on the question type:
/// text types → <see cref="TextValue"/>, numeric types → <see cref="NumberValue"/>,
/// date → <see cref="DateValue"/>, choice types → <see cref="Selections"/>.
/// </summary>
public class Answer : Entity, IWorkspaceOwned
{
    /// <inheritdoc />
    public Guid WorkspaceId { get; set; }

    /// <summary>Owning response.</summary>
    public Guid ResponseId { get; set; }

    /// <summary>Owning response (navigation).</summary>
    public SurveyResponse? Response { get; set; }

    /// <summary>Answered question.</summary>
    public Guid QuestionId { get; set; }

    /// <summary>Answered question (navigation).</summary>
    public Question? Question { get; set; }

    /// <summary>Text answer (ShortText, LongText, Email).</summary>
    public string? TextValue { get; set; }

    /// <summary>Numeric answer (Number, Rating, Scale).</summary>
    public double? NumberValue { get; set; }

    /// <summary>Date answer (Date).</summary>
    public DateOnly? DateValue { get; set; }

    /// <summary>Selected options (Radio, Dropdown: one; Checkbox: many).</summary>
    public List<AnswerSelection> Selections { get; set; } = [];
}

/// <summary>A selected option of a choice answer, optionally with free text ("Other: …").</summary>
public class AnswerSelection : Entity, IWorkspaceOwned
{
    /// <inheritdoc />
    public Guid WorkspaceId { get; set; }

    /// <summary>Owning answer.</summary>
    public Guid AnswerId { get; set; }

    /// <summary>Owning answer (navigation).</summary>
    public Answer? Answer { get; set; }

    /// <summary>Selected option.</summary>
    public Guid OptionId { get; set; }

    /// <summary>Selected option (navigation).</summary>
    public QuestionOption? Option { get; set; }

    /// <summary>Free text entered for options with <see cref="QuestionOption.AllowsFreeText"/>.</summary>
    public string? FreeText { get; set; }
}
