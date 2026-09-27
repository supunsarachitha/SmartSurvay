using System.Globalization;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Responses;

/// <summary>Outcome of an eligibility check: the verdict plus a friendly explanation.</summary>
/// <param name="Eligibility">The verdict.</param>
/// <param name="Message">User-facing explanation; null when the respondent is eligible.</param>
internal readonly record struct EligibilityVerdict(SurveyEligibility Eligibility, string? Message)
{
    /// <summary>True when the respondent may answer the survey.</summary>
    public bool IsEligible => Eligibility == SurveyEligibility.Eligible;
}

/// <summary>Response facts (gathered from the database by the caller) needed to decide eligibility.</summary>
/// <param name="CompletedResponses">Number of completed responses of the survey, all respondents.</param>
/// <param name="IsAuthenticated">Whether the current user is logged in.</param>
/// <param name="HasCompletedResponse">Whether the current user has already completed the survey.</param>
internal readonly record struct ParticipationFacts(int CompletedResponses, bool IsAuthenticated, bool HasCompletedResponse);

/// <summary>
/// Decides whether the current user may answer a survey and explains why not. Pure and
/// deterministic so the same rules drive the available-surveys list, the survey runner, draft
/// saving and submission.
/// </summary>
/// <remarks>
/// Rules are evaluated in this order (the first failing rule wins):
/// <list type="number">
/// <item>Status and schedule (<see cref="Survey.GetAvailability"/>): not published / not open yet / closed.</item>
/// <item>Quota: completed responses ≥ <see cref="Survey.MaxResponses"/>.</item>
/// <item>Login: anonymous responses not allowed and the user is a guest.</item>
/// <item>One response per user: the user already completed it and multiple responses are not allowed.</item>
/// </list>
/// </remarks>
internal static class SurveyEligibilityChecker
{
    /// <summary>Shown when no survey matches the link.</summary>
    public const string NotFoundMessage = "We couldn't find this survey. Please check the link and try again.";

    /// <summary>Shown for templates.</summary>
    public const string TemplateMessage = "This survey is a template and cannot be answered.";

    /// <summary>Shown for surveys still being designed.</summary>
    public const string DraftMessage = "This survey hasn't been published yet. Please check back later.";

    /// <summary>Shown for archived surveys.</summary>
    public const string ArchivedMessage = "This survey has been archived and no longer accepts responses.";

    /// <summary>Shown for manually closed surveys.</summary>
    public const string ClosedMessage = "This survey is closed and no longer accepts responses.";

    /// <summary>Shown when the response quota has been reached.</summary>
    public const string QuotaReachedMessage =
        "This survey has reached its maximum number of responses and is no longer accepting new ones.";

    /// <summary>Shown to guests when the survey requires an account.</summary>
    public const string LoginRequiredMessage = "Please log in to answer this survey.";

    /// <summary>Shown when the user already completed a single-response survey.</summary>
    public const string AlreadyRespondedMessage = "You have already completed this survey. Thank you for taking part!";

    /// <summary>Verdict for an unknown survey.</summary>
    public static EligibilityVerdict NotFound { get; } = new(SurveyEligibility.NotFound, NotFoundMessage);

    /// <summary>Verdict for an eligible respondent.</summary>
    public static EligibilityVerdict Eligible { get; } = new(SurveyEligibility.Eligible, null);

    /// <summary>Evaluates all eligibility rules for <paramref name="survey"/>.</summary>
    /// <param name="survey">The survey (only its own columns are used; no navigation properties).</param>
    /// <param name="utcNow">Current UTC time.</param>
    /// <param name="facts">Response facts for the survey and the current user.</param>
    public static EligibilityVerdict Check(Survey survey, DateTime utcNow, ParticipationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(survey);

        var availability = survey.GetAvailability(utcNow);
        if (availability != SurveyAvailability.Open)
        {
            return FromAvailability(survey, availability, utcNow);
        }

        if (survey.MaxResponses is { } max && facts.CompletedResponses >= max)
        {
            return new(SurveyEligibility.QuotaReached, QuotaReachedMessage);
        }

        if (!survey.AllowAnonymous && !facts.IsAuthenticated)
        {
            return new(SurveyEligibility.LoginRequired, LoginRequiredMessage);
        }

        if (facts.IsAuthenticated && !survey.AllowMultipleResponses && facts.HasCompletedResponse)
        {
            return new(SurveyEligibility.AlreadyResponded, AlreadyRespondedMessage);
        }

        return Eligible;
    }

    /// <summary>
    /// Status/schedule availability for projected survey columns. Delegates to the domain rule
    /// (<see cref="Survey.GetAvailability"/>) so there is a single source of truth.
    /// </summary>
    public static SurveyAvailability GetAvailability(
        SurveyStatus status, bool isTemplate, DateTime? opensAt, DateTime? closesAt, DateTime utcNow) =>
        new Survey { Status = status, IsTemplate = isTemplate, OpensAt = opensAt, ClosesAt = closesAt }.GetAvailability(utcNow);

    /// <summary>Formats a UTC timestamp for messages, e.g. "1 Mar 2026 10:00 UTC".</summary>
    public static string FormatUtc(DateTime utc) =>
        utc.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture) + " UTC";

    private static EligibilityVerdict FromAvailability(Survey survey, SurveyAvailability availability, DateTime utcNow) =>
        availability switch
        {
            SurveyAvailability.NotOpenYet => new(
                SurveyEligibility.NotOpenYet,
                $"This survey opens on {FormatUtc(survey.OpensAt.GetValueOrDefault())}."),
            SurveyAvailability.Closed => new(SurveyEligibility.Closed, ClosedMessageFor(survey, utcNow)),
            _ => new(SurveyEligibility.NotPublished, NotPublishedMessageFor(survey)),
        };

    private static string ClosedMessageFor(Survey survey, DateTime utcNow) =>
        // A published survey whose window has ended gets the exact closing time; a manual close does not.
        survey.Status == SurveyStatus.Published && survey.ClosesAt is { } closesAt && closesAt <= utcNow
            ? $"This survey closed on {FormatUtc(closesAt)}."
            : ClosedMessage;

    private static string NotPublishedMessageFor(Survey survey) => survey switch
    {
        { IsTemplate: true } => TemplateMessage,
        { Status: SurveyStatus.Archived } => ArchivedMessage,
        _ => DraftMessage,
    };
}
