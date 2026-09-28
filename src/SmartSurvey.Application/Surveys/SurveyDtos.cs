using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Surveys;

/// <summary>Row in admin survey lists.</summary>
public sealed record SurveySummaryDto
{
    /// <summary>Survey id.</summary>
    public Guid Id { get; init; }

    /// <summary>Title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Share-link slug.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Lifecycle state.</summary>
    public SurveyStatus Status { get; init; }

    /// <summary>Whether the survey is a template.</summary>
    public bool IsTemplate { get; init; }

    /// <summary>Anonymous responses allowed.</summary>
    public bool AllowAnonymous { get; init; }

    /// <summary>Number of questions.</summary>
    public int QuestionCount { get; init; }

    /// <summary>Number of completed responses.</summary>
    public int CompletedResponses { get; init; }

    /// <summary>Number of in-progress drafts.</summary>
    public int InProgressResponses { get; init; }

    /// <summary>Response window start (UTC).</summary>
    public DateTime? OpensAt { get; init; }

    /// <summary>Response window end (UTC).</summary>
    public DateTime? ClosesAt { get; init; }

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Last modification timestamp (UTC).</summary>
    public DateTime? UpdatedAt { get; init; }

    /// <summary>Latest completed response timestamp (UTC).</summary>
    public DateTime? LastResponseAt { get; init; }

    /// <summary>Respondents need a password to open the survey.</summary>
    public bool IsPasswordProtected { get; init; }
}

/// <summary>
/// Complete, editable survey design. Used by the Blazor builder (two-way binding, hence mutable
/// classes), the REST API (create/update payload) and the survey runner. New child items must be
/// given fresh GUIDs by the client (<c>Guid.NewGuid()</c>) so logic rules can reference them before
/// the first save; <see cref="Guid.Empty"/> ids are replaced by the server.
/// </summary>
public sealed class SurveyDefinitionDto
{
    /// <summary>Survey id (ignored on create; a new id is generated when empty).</summary>
    public Guid Id { get; set; }

    /// <summary>Title (required, max 200).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional description (max 4000).</summary>
    public string? Description { get; set; }

    /// <summary>Share-link slug; generated from the title when empty.</summary>
    public string? Slug { get; set; }

    /// <summary>Lifecycle state (read-only here; change it with the status operation).</summary>
    public SurveyStatus Status { get; set; } = SurveyStatus.Draft;

    /// <summary>Template flag.</summary>
    public bool IsTemplate { get; set; }

    /// <summary>Anonymous responses allowed.</summary>
    public bool AllowAnonymous { get; set; }

    /// <summary>Multiple completed responses per user allowed.</summary>
    public bool AllowMultipleResponses { get; set; }

    /// <summary>Show progress bar.</summary>
    public bool ShowProgressBar { get; set; } = true;

    /// <summary>Show question numbers.</summary>
    public bool ShowQuestionNumbers { get; set; } = true;

    /// <summary>Response window start (UTC).</summary>
    public DateTime? OpensAt { get; set; }

    /// <summary>Response window end (UTC).</summary>
    public DateTime? ClosesAt { get; set; }

    /// <summary>Completed-response quota.</summary>
    public int? MaxResponses { get; set; }

    /// <summary>Welcome message.</summary>
    public string? WelcomeMessage { get; set; }

    /// <summary>Thank-you message.</summary>
    public string? ThankYouMessage { get; set; }

    /// <summary>
    /// Respondents need a password to open the survey. When reading: whether a password is set. When saving:
    /// false removes the password; true keeps the current one or sets <see cref="AccessPassword"/>.
    /// </summary>
    public bool PasswordProtected { get; set; }

    /// <summary>
    /// New access password (write-only, 4–128 characters; never returned). Leave empty to keep the current
    /// password. Required when <see cref="PasswordProtected"/> is switched on for the first time.
    /// </summary>
    public string? AccessPassword { get; set; }

    /// <summary>Concurrency version; send back the value you loaded when updating.</summary>
    public int Version { get; set; }

    /// <summary>Creation timestamp (read-only).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Last update timestamp (read-only).</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Publication timestamp (read-only).</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>Pages in display order.</summary>
    public List<SectionDto> Sections { get; set; } = [];

    /// <summary>Conditional logic rules.</summary>
    public List<LogicRuleDto> LogicRules { get; set; } = [];

    /// <summary>All questions in display order (section order, then question order).</summary>
    public IEnumerable<QuestionDto> AllQuestions() =>
        Sections.OrderBy(s => s.Order).SelectMany(s => s.Questions.OrderBy(q => q.Order));

    /// <summary>Finds a question by id anywhere in the survey.</summary>
    public QuestionDto? FindQuestion(Guid id) =>
        Sections.SelectMany(s => s.Questions).FirstOrDefault(q => q.Id == id);

    /// <summary>Finds the section containing a question.</summary>
    public SectionDto? FindSectionOf(Guid questionId) =>
        Sections.FirstOrDefault(s => s.Questions.Any(q => q.Id == questionId));
}

/// <summary>A survey page.</summary>
public sealed class SectionDto
{
    /// <summary>Section id (client generated for new sections).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Page title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Page description.</summary>
    public string? Description { get; set; }

    /// <summary>Zero-based order.</summary>
    public int Order { get; set; }

    /// <summary>Questions in display order.</summary>
    public List<QuestionDto> Questions { get; set; } = [];
}

/// <summary>A question.</summary>
public sealed class QuestionDto
{
    /// <summary>Question id (client generated for new questions).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Input type.</summary>
    public QuestionType Type { get; set; } = QuestionType.ShortText;

    /// <summary>Question text (required, max 1000).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Help text (max 2000).</summary>
    public string? Description { get; set; }

    /// <summary>Export code (e.g. Q1); generated when empty; unique per survey.</summary>
    public string? Code { get; set; }

    /// <summary>Zero-based order within the section.</summary>
    public int Order { get; set; }

    /// <summary>Mandatory flag.</summary>
    public bool IsRequired { get; set; }

    /// <summary>Type-specific settings.</summary>
    public QuestionSettings Settings { get; set; } = new();

    /// <summary>Answer options (choice types).</summary>
    public List<OptionDto> Options { get; set; } = [];
}

/// <summary>An answer option.</summary>
public sealed class OptionDto
{
    /// <summary>Option id (client generated for new options).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Label (required, max 500).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Optional export code.</summary>
    public string? Value { get; set; }

    /// <summary>Zero-based order.</summary>
    public int Order { get; set; }

    /// <summary>Combined "choice + free text" option (e.g. "Other, please specify").</summary>
    public bool AllowsFreeText { get; set; }

    /// <summary>Placeholder of the free-text input.</summary>
    public string? FreeTextPlaceholder { get; set; }
}

/// <summary>A conditional show/hide rule.</summary>
public sealed class LogicRuleDto
{
    /// <summary>Rule id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Target question (exactly one target must be set).</summary>
    public Guid? TargetQuestionId { get; set; }

    /// <summary>Target section (exactly one target must be set).</summary>
    public Guid? TargetSectionId { get; set; }

    /// <summary>Show or hide.</summary>
    public LogicAction Action { get; set; } = LogicAction.Show;

    /// <summary>AND / OR.</summary>
    public LogicMatchType MatchType { get; set; } = LogicMatchType.All;

    /// <summary>At least one condition.</summary>
    public List<LogicConditionDto> Conditions { get; set; } = [];
}

/// <summary>A logic condition.</summary>
public sealed class LogicConditionDto
{
    /// <summary>Condition id.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Question whose answer is tested (must precede the target).</summary>
    public Guid SourceQuestionId { get; set; }

    /// <summary>Operator.</summary>
    public ConditionOperator Operator { get; set; } = ConditionOperator.Equals;

    /// <summary>Option (choice questions).</summary>
    public Guid? OptionId { get; set; }

    /// <summary>Comparison value (text / number / ISO date).</summary>
    public string? Value { get; set; }
}

/// <summary>Filters for the admin survey list.</summary>
public sealed class SurveyQuery : PageRequest
{
    /// <summary>Case-insensitive search in title/description/slug.</summary>
    public string? Search { get; set; }

    /// <summary>Filter by status (null = all except archived unless <see cref="IncludeArchived"/>).</summary>
    public SurveyStatus? Status { get; set; }

    /// <summary>Include archived surveys when <see cref="Status"/> is null.</summary>
    public bool IncludeArchived { get; set; }

    /// <summary>true = templates only, false = non-templates only, null = both.</summary>
    public bool? IsTemplate { get; set; }
}

/// <summary>Request to change a survey's lifecycle status.</summary>
/// <param name="Status">Target status.</param>
public sealed record ChangeSurveyStatusRequest(SurveyStatus Status);

/// <summary>Request to duplicate a survey (also used for "create from template").</summary>
public sealed class DuplicateSurveyRequest
{
    /// <summary>Title of the copy (default: "Copy of …").</summary>
    public string? Title { get; set; }

    /// <summary>Whether the copy is a template (default: false).</summary>
    public bool AsTemplate { get; set; }
}

/// <summary>Portable survey definition document used for JSON import/export.</summary>
public sealed class SurveyExportDocument
{
    /// <summary>Document format version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Generator name.</summary>
    public string Generator { get; set; } = "SmartSurvey";

    /// <summary>UTC export timestamp.</summary>
    public DateTime ExportedAt { get; set; }

    /// <summary>The survey design. Ids are remapped on import.</summary>
    public SurveyDefinitionDto Survey { get; set; } = new();
}
