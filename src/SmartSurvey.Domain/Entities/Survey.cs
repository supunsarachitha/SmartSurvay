using SmartSurvey.Domain.Common;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Domain.Entities;

/// <summary>
/// Aggregate root of the survey design: settings, sections (pages), questions, options and
/// conditional logic. Responses and reports reference a survey but are separate aggregates.
/// </summary>
public class Survey : AuditableEntity
{
    /// <summary>Title shown to respondents.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional introduction (plain text, line breaks preserved).</summary>
    public string? Description { get; set; }

    /// <summary>URL-friendly unique identifier used in share links: <c>/s/{slug}</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Lifecycle state.</summary>
    public SurveyStatus Status { get; set; } = SurveyStatus.Draft;

    /// <summary>Templates appear in the "create from template" gallery and are never answered directly.</summary>
    public bool IsTemplate { get; set; }

    /// <summary>When true, anyone with the link may respond without logging in.</summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>When false, a logged-in user can submit only one completed response.</summary>
    public bool AllowMultipleResponses { get; set; }

    /// <summary>Show a progress bar in multi-page surveys.</summary>
    public bool ShowProgressBar { get; set; } = true;

    /// <summary>Prefix questions with running numbers.</summary>
    public bool ShowQuestionNumbers { get; set; } = true;

    /// <summary>Optional UTC start of the response window.</summary>
    public DateTime? OpensAt { get; set; }

    /// <summary>Optional UTC end of the response window.</summary>
    public DateTime? ClosesAt { get; set; }

    /// <summary>Optional quota of completed responses; the survey stops accepting responses when reached.</summary>
    public int? MaxResponses { get; set; }

    /// <summary>Optional message on the first page.</summary>
    public string? WelcomeMessage { get; set; }

    /// <summary>Message shown after submission.</summary>
    public string? ThankYouMessage { get; set; }

    /// <summary>
    /// Salted PBKDF2 hash of the access password (null = no password). Respondents must enter the password
    /// before they can open, save or submit the survey. The password itself is never stored.
    /// </summary>
    public string? AccessPasswordHash { get; set; }

    /// <summary>UTC timestamp of the (latest) publication.</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>UTC timestamp when the survey was closed.</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>Optimistic-concurrency version, incremented on every design change.</summary>
    public int Version { get; set; }

    /// <summary>Pages of the survey, ordered by <see cref="SurveySection.Order"/>.</summary>
    public List<SurveySection> Sections { get; set; } = [];

    /// <summary>All questions of the survey (each also belongs to a section).</summary>
    public List<Question> Questions { get; set; } = [];

    /// <summary>Conditional show/hide rules.</summary>
    public List<LogicRule> LogicRules { get; set; } = [];

    /// <summary>Responses collected for this survey.</summary>
    public List<SurveyResponse> Responses { get; set; } = [];

    /// <summary>
    /// Evaluates status and schedule (quota is checked separately because it needs a database count).
    /// </summary>
    /// <param name="utcNow">Current UTC time.</param>
    public SurveyAvailability GetAvailability(DateTime utcNow)
    {
        if (IsTemplate || Status != SurveyStatus.Published)
        {
            return Status == SurveyStatus.Closed ? SurveyAvailability.Closed : SurveyAvailability.NotPublished;
        }

        if (OpensAt.HasValue && utcNow < OpensAt.Value)
        {
            return SurveyAvailability.NotOpenYet;
        }

        if (ClosesAt.HasValue && utcNow >= ClosesAt.Value)
        {
            return SurveyAvailability.Closed;
        }

        return SurveyAvailability.Open;
    }
}

/// <summary>Result of <see cref="Survey.GetAvailability"/>.</summary>
public enum SurveyAvailability
{
    /// <summary>Accepting responses.</summary>
    Open = 0,

    /// <summary>Draft, archived or template.</summary>
    NotPublished = 1,

    /// <summary>Published but the response window has not started.</summary>
    NotOpenYet = 2,

    /// <summary>Closed manually or the response window has ended.</summary>
    Closed = 3,
}

/// <summary>A page of a survey. Sections can be shown/hidden by logic rules.</summary>
public class SurveySection : Entity
{
    /// <summary>Owning survey.</summary>
    public Guid SurveyId { get; set; }

    /// <summary>Owning survey (navigation).</summary>
    public Survey? Survey { get; set; }

    /// <summary>Page title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional page introduction.</summary>
    public string? Description { get; set; }

    /// <summary>Zero-based position within the survey.</summary>
    public int Order { get; set; }

    /// <summary>Questions on this page ordered by <see cref="Question.Order"/>.</summary>
    public List<Question> Questions { get; set; } = [];
}
