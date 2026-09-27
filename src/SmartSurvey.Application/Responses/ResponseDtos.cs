using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Responses;

/// <summary>
/// The respondent's (editable) answer to one question. Used by the survey runner for two-way
/// binding, by the logic evaluator and validator, and as the REST submission payload.
/// Only the field matching the question type is considered.
/// </summary>
public sealed class AnswerInputDto
{
    /// <summary>Answered question.</summary>
    public Guid QuestionId { get; set; }

    /// <summary>Text answer (ShortText, LongText, Email).</summary>
    public string? Text { get; set; }

    /// <summary>Numeric answer (Number, Rating, Scale).</summary>
    public double? Number { get; set; }

    /// <summary>Date answer (Date).</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Selected options (choice types) with optional free text.</summary>
    public List<SelectionInputDto> Selections { get; set; } = [];

    /// <summary>True when the option is currently selected.</summary>
    public bool IsSelected(Guid optionId) => Selections.Any(s => s.OptionId == optionId);

    /// <summary>Free text entered for a selected option (null when not selected / none).</summary>
    public string? FreeTextFor(Guid optionId) => Selections.FirstOrDefault(s => s.OptionId == optionId)?.FreeText;

    /// <summary>Replaces the selection with a single option (radio / dropdown). Null clears it.</summary>
    public void SelectSingle(Guid? optionId)
    {
        if (optionId is null)
        {
            Selections.Clear();
            return;
        }

        if (Selections.Count == 1 && Selections[0].OptionId == optionId)
        {
            return; // keep any free text already typed
        }

        Selections.Clear();
        Selections.Add(new SelectionInputDto { OptionId = optionId.Value });
    }

    /// <summary>Adds or removes an option (checkbox).</summary>
    public void Toggle(Guid optionId, bool selected)
    {
        var existing = Selections.FirstOrDefault(s => s.OptionId == optionId);
        if (selected && existing is null)
        {
            Selections.Add(new SelectionInputDto { OptionId = optionId });
        }
        else if (!selected && existing is not null)
        {
            Selections.Remove(existing);
        }
    }

    /// <summary>Sets the free text of an already selected option.</summary>
    public void SetFreeText(Guid optionId, string? text)
    {
        var existing = Selections.FirstOrDefault(s => s.OptionId == optionId);
        if (existing is not null)
        {
            existing.FreeText = text;
        }
    }

    /// <summary>True when the answer contains a value for the given question type.</summary>
    public bool HasValue(QuestionType type) => type switch
    {
        _ when type.IsChoice() => Selections.Count > 0,
        _ when type.IsNumeric() => Number.HasValue,
        _ when type.IsDate() => Date.HasValue,
        _ => !string.IsNullOrWhiteSpace(Text),
    };

    /// <summary>Deep copy.</summary>
    public AnswerInputDto Clone() => new()
    {
        QuestionId = QuestionId,
        Text = Text,
        Number = Number,
        Date = Date,
        Selections = Selections.Select(s => new SelectionInputDto { OptionId = s.OptionId, FreeText = s.FreeText }).ToList(),
    };
}

/// <summary>A selected option.</summary>
public sealed class SelectionInputDto
{
    /// <summary>Selected option id.</summary>
    public Guid OptionId { get; set; }

    /// <summary>Free text for options that allow it.</summary>
    public string? FreeText { get; set; }
}

/// <summary>Payload for saving a draft or submitting a response.</summary>
public sealed class SaveResponseRequest
{
    /// <summary>Existing draft id (null to start a new response).</summary>
    public Guid? ResponseId { get; set; }

    /// <summary>Current page index (stored with drafts for resuming).</summary>
    public int CurrentSectionIndex { get; set; }

    /// <summary>Answers (at most one per question).</summary>
    public List<AnswerInputDto> Answers { get; set; } = [];

    /// <summary>Optional browser user agent (set by the host, truncated to 512 chars).</summary>
    public string? UserAgent { get; set; }
}

/// <summary>Result of a successful submission.</summary>
/// <param name="ResponseId">Stored response id.</param>
/// <param name="ThankYouMessage">Survey thank-you message.</param>
public sealed record SubmitResponseResult(Guid ResponseId, string? ThankYouMessage);

/// <summary>Why a respondent can or cannot answer a survey.</summary>
public enum SurveyEligibility
{
    /// <summary>May respond.</summary>
    Eligible = 0,

    /// <summary>No survey with that slug.</summary>
    NotFound = 1,

    /// <summary>Draft/archived/template.</summary>
    NotPublished = 2,

    /// <summary>Response window not started.</summary>
    NotOpenYet = 3,

    /// <summary>Closed or response window ended.</summary>
    Closed = 4,

    /// <summary>Response quota reached.</summary>
    QuotaReached = 5,

    /// <summary>Survey requires login and the user is anonymous.</summary>
    LoginRequired = 6,

    /// <summary>User already submitted and multiple responses are not allowed.</summary>
    AlreadyResponded = 7,
}

/// <summary>Everything the survey runner needs to start or resume answering.</summary>
public sealed class SurveySessionDto
{
    /// <summary>Eligibility verdict.</summary>
    public SurveyEligibility Eligibility { get; set; }

    /// <summary>Human-readable explanation when not eligible.</summary>
    public string? Message { get; set; }

    /// <summary>Survey design (null when not found / not published).</summary>
    public SurveyDefinitionDto? Survey { get; set; }

    /// <summary>Existing draft id when resuming.</summary>
    public Guid? DraftResponseId { get; set; }

    /// <summary>Page to resume at.</summary>
    public int CurrentSectionIndex { get; set; }

    /// <summary>Previously saved answers (empty for new responses).</summary>
    public List<AnswerInputDto> Answers { get; set; } = [];

    /// <summary>Convenience flag.</summary>
    public bool CanRespond => Eligibility == SurveyEligibility.Eligible;
}

/// <summary>A survey listed on the respondent's "available surveys" page.</summary>
public sealed record AvailableSurveyDto
{
    /// <summary>Survey id.</summary>
    public Guid SurveyId { get; init; }

    /// <summary>Title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>Slug for the link <c>/s/{slug}</c>.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Response window end (UTC).</summary>
    public DateTime? ClosesAt { get; init; }

    /// <summary>Number of questions.</summary>
    public int QuestionCount { get; init; }

    /// <summary>Rough completion time estimate in minutes (≈ 20 s per question, min 1).</summary>
    public int EstimatedMinutes { get; init; }

    /// <summary>Anonymous responses allowed.</summary>
    public bool AllowAnonymous { get; init; }

    /// <summary>Current user already completed it.</summary>
    public bool HasCompleted { get; init; }

    /// <summary>Current user has a saved draft.</summary>
    public bool HasDraft { get; init; }

    /// <summary>Current user may (still) respond.</summary>
    public bool CanRespond { get; init; }
}

/// <summary>One of the current user's responses.</summary>
public sealed record MyResponseDto
{
    /// <summary>Response id.</summary>
    public Guid ResponseId { get; init; }

    /// <summary>Survey id.</summary>
    public Guid SurveyId { get; init; }

    /// <summary>Survey title.</summary>
    public string SurveyTitle { get; init; } = string.Empty;

    /// <summary>Survey slug.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Draft or completed.</summary>
    public ResponseStatus Status { get; init; }

    /// <summary>Started (UTC).</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>Submitted (UTC).</summary>
    public DateTime? SubmittedAt { get; init; }

    /// <summary>Draft can still be continued (survey open).</summary>
    public bool CanContinue { get; init; }
}

/// <summary>Row in the admin responses list.</summary>
public sealed record ResponseSummaryDto
{
    /// <summary>Response id.</summary>
    public Guid Id { get; init; }

    /// <summary>Survey id.</summary>
    public Guid SurveyId { get; init; }

    /// <summary>Respondent id (null = anonymous).</summary>
    public Guid? RespondentId { get; init; }

    /// <summary>Respondent display name or "Anonymous".</summary>
    public string RespondentName { get; init; } = "Anonymous";

    /// <summary>Respondent e-mail (null = anonymous).</summary>
    public string? RespondentEmail { get; init; }

    /// <summary>Draft or completed.</summary>
    public ResponseStatus Status { get; init; }

    /// <summary>Started (UTC).</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>Submitted (UTC).</summary>
    public DateTime? SubmittedAt { get; init; }

    /// <summary>Number of answered questions.</summary>
    public int AnswerCount { get; init; }

    /// <summary>Completion time in seconds.</summary>
    public double? DurationSeconds { get; init; }
}

/// <summary>Filters for the admin responses list.</summary>
public sealed class ResponseQuery : PageRequest
{
    /// <summary>Status filter (null = all).</summary>
    public ResponseStatus? Status { get; set; }

    /// <summary>Submitted/started on or after (UTC date).</summary>
    public DateOnly? From { get; set; }

    /// <summary>Submitted/started on or before (UTC date).</summary>
    public DateOnly? To { get; set; }

    /// <summary>Search respondent e-mail / name.</summary>
    public string? Search { get; set; }
}

/// <summary>Full response with human-readable answers.</summary>
public sealed record ResponseDetailDto
{
    /// <summary>Response id.</summary>
    public Guid Id { get; init; }

    /// <summary>Survey id.</summary>
    public Guid SurveyId { get; init; }

    /// <summary>Survey title.</summary>
    public string SurveyTitle { get; init; } = string.Empty;

    /// <summary>Respondent id.</summary>
    public Guid? RespondentId { get; init; }

    /// <summary>Respondent name or "Anonymous".</summary>
    public string RespondentName { get; init; } = "Anonymous";

    /// <summary>Respondent e-mail.</summary>
    public string? RespondentEmail { get; init; }

    /// <summary>Status.</summary>
    public ResponseStatus Status { get; init; }

    /// <summary>Started (UTC).</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>Submitted (UTC).</summary>
    public DateTime? SubmittedAt { get; init; }

    /// <summary>User agent.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Answers in survey order (unanswered questions included with empty display value).</summary>
    public IReadOnlyList<AnswerDetailDto> Answers { get; init; } = [];
}

/// <summary>A human-readable answer.</summary>
public sealed record AnswerDetailDto
{
    /// <summary>Question id.</summary>
    public Guid QuestionId { get; init; }

    /// <summary>Question code.</summary>
    public string QuestionCode { get; init; } = string.Empty;

    /// <summary>Question text.</summary>
    public string QuestionText { get; init; } = string.Empty;

    /// <summary>Question type.</summary>
    public QuestionType QuestionType { get; init; }

    /// <summary>Section title.</summary>
    public string SectionTitle { get; init; } = string.Empty;

    /// <summary>Formatted answer (e.g. "Red; Other: teal"), empty when unanswered.</summary>
    public string DisplayValue { get; init; } = string.Empty;

    /// <summary>True when answered.</summary>
    public bool IsAnswered { get; init; }
}
