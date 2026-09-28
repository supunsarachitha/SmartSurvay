using SmartSurvey.Application.Common;

namespace SmartSurvey.Application.Responses;

/// <summary>
/// Response collection use cases for respondents (start/resume, drafts, submit) and admins
/// (browse, inspect, delete). Submission re-evaluates conditional logic and validation on the
/// server — the client is never trusted.
/// </summary>
public interface IResponseService
{
    /// <summary>
    /// Published, open, non-template surveys the current user may see: all of them for logged-in
    /// users, anonymous-enabled ones for guests. Includes per-user draft/completion flags.
    /// </summary>
    Task<IReadOnlyList<AvailableSurveyDto>> ListAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Determines eligibility for <paramref name="slug"/> and returns the survey plus, for logged-in
    /// users, the latest draft to resume. Password-protected surveys need a valid
    /// <paramref name="accessKey"/> (from <see cref="UnlockAsync"/>); without one the result is
    /// <see cref="SurveyEligibility.PasswordRequired"/> and the design is withheld.
    /// </summary>
    Task<SurveySessionDto> StartOrResumeAsync(string slug, string? accessKey = null, CancellationToken ct = default);

    /// <summary>
    /// Checks the password of a protected survey and returns an access key. Throws
    /// <see cref="AppValidationException"/> (key "Password") when the password is wrong and
    /// <see cref="NotFoundException"/> for unknown surveys.
    /// </summary>
    Task<SurveyUnlockResult> UnlockAsync(string slug, string password, CancellationToken ct = default);

    /// <summary>
    /// Saves (creates or updates) the current user's draft. Logged-in users only; answers are stored
    /// without required-field validation but with type/ownership sanitising. Returns the draft id.
    /// </summary>
    Task<Guid> SaveDraftAsync(Guid surveyId, SaveResponseRequest request, CancellationToken ct = default);

    /// <summary>
    /// Validates (logic-aware) and stores a completed response. Answers to hidden questions are
    /// discarded. Throws <see cref="AppValidationException"/> keyed by question id, or
    /// <see cref="BusinessRuleException"/> when the survey no longer accepts responses.
    /// </summary>
    Task<SubmitResponseResult> SubmitAsync(Guid surveyId, SaveResponseRequest request, CancellationToken ct = default);

    /// <summary>
    /// Title and thank-you message for the completion page of <paramref name="slug"/>. Returns null
    /// for unknown surveys, templates and drafts, and for members-only surveys when the user is a guest
    /// (the design is not disclosed to people who could not answer it).
    /// </summary>
    Task<SurveyCompletionDto?> GetCompletionAsync(string slug, CancellationToken ct = default);

    /// <summary>The current user's responses and drafts, newest first.</summary>
    Task<IReadOnlyList<MyResponseDto>> ListMineAsync(CancellationToken ct = default);

    /// <summary>Paged responses of a survey (admin).</summary>
    Task<PagedResult<ResponseSummaryDto>> ListForSurveyAsync(Guid surveyId, ResponseQuery query, CancellationToken ct = default);

    /// <summary>Response detail (admin, or the respondent who owns it).</summary>
    Task<ResponseDetailDto> GetAsync(Guid responseId, CancellationToken ct = default);

    /// <summary>Deletes a response (admin).</summary>
    Task DeleteAsync(Guid responseId, CancellationToken ct = default);
}
