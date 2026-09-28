using Microsoft.EntityFrameworkCore;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.Application.Common;

/// <summary>
/// Persistence abstraction used by application services. Implemented by the EF Core
/// <c>AppDbContext</c> in the Infrastructure layer. Exposing <see cref="DbSet{TEntity}"/> keeps
/// LINQ-to-SQL composition available to services without leaking the concrete provider.
/// </summary>
public interface IAppDbContext : IDisposable, IAsyncDisposable
{
    /// <summary>Workspaces (not filtered by the data scope; filter explicitly).</summary>
    DbSet<Workspace> Workspaces { get; }

    /// <summary>System settings (single row).</summary>
    DbSet<PlatformSettings> PlatformSettings { get; }

    /// <summary>Surveys.</summary>
    DbSet<Survey> Surveys { get; }

    /// <summary>Survey sections (pages).</summary>
    DbSet<SurveySection> SurveySections { get; }

    /// <summary>Questions.</summary>
    DbSet<Question> Questions { get; }

    /// <summary>Question options.</summary>
    DbSet<QuestionOption> QuestionOptions { get; }

    /// <summary>Logic rules.</summary>
    DbSet<LogicRule> LogicRules { get; }

    /// <summary>Logic conditions.</summary>
    DbSet<LogicCondition> LogicConditions { get; }

    /// <summary>Responses.</summary>
    DbSet<SurveyResponse> Responses { get; }

    /// <summary>Answers.</summary>
    DbSet<Answer> Answers { get; }

    /// <summary>Answer selections.</summary>
    DbSet<AnswerSelection> AnswerSelections { get; }

    /// <summary>Report definitions.</summary>
    DbSet<ReportDefinition> Reports { get; }

    /// <summary>Report widgets.</summary>
    DbSet<ReportWidget> ReportWidgets { get; }

    /// <summary>Audit log entries.</summary>
    DbSet<AuditLogEntry> AuditLogs { get; }

    /// <summary>Product branding (single row).</summary>
    DbSet<BrandingSettings> BrandingSettings { get; }

    /// <summary>Identity users (read access for display names / respondent info). Not filtered by the data scope.</summary>
    DbSet<ApplicationUser> Users { get; }

    /// <summary>Persists pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates short-lived <see cref="IAppDbContext"/> instances. Services create one context per
/// operation (<c>await using var db = await factory.CreateAsync(ct);</c>) which is the recommended
/// pattern for Blazor Server where DI scopes live as long as the user's circuit.
/// </summary>
/// <remarks>
/// Every context is bound to a data scope: it only returns rows of one workspace (or none), and
/// inserts are stamped with that workspace. Workspaces never see each other's data.
/// </remarks>
public interface IAppDbContextFactory
{
    /// <summary>
    /// Creates a context scoped to the current user's workspace. Anonymous visitors and super admins
    /// have no workspace and get a context that sees no workspace data. The caller disposes it.
    /// </summary>
    Task<IAppDbContext> CreateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a context scoped to the given workspace — for flows that act on behalf of a workspace
    /// without being signed in to it (answering a survey from a share link).
    /// </summary>
    Task<IAppDbContext> CreateForWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an unfiltered context. Only for system operations that must span workspaces
    /// (system-wide slug check, resolving the workspace of a share link, super admin workspace
    /// administration, seeding). Inserts must set <c>WorkspaceId</c> explicitly.
    /// </summary>
    Task<IAppDbContext> CreateSystemAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about the user executing the current operation. Populated per HTTP request by
/// middleware and per Blazor circuit by a circuit handler (see the Web project).
/// </summary>
public interface ICurrentUser
{
    /// <summary>User id, or null when anonymous.</summary>
    Guid? UserId { get; }

    /// <summary>User name / e-mail, or null when anonymous.</summary>
    string? UserName { get; }

    /// <summary>True when a user is logged in.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Workspace of the signed-in user; null for anonymous visitors and super admins.</summary>
    Guid? WorkspaceId { get; }

    /// <summary>True for an admin of the current workspace (<see cref="AppRoles.Admin"/> role and a workspace).</summary>
    bool IsAdmin { get; }

    /// <summary>True for a super admin (<see cref="AppRoles.SuperAdmin"/> role).</summary>
    bool IsSuperAdmin { get; }

    /// <summary>Role membership check.</summary>
    bool IsInRole(string role);
}

/// <summary>An e-mail ready to be delivered.</summary>
/// <param name="To">Recipient address.</param>
/// <param name="Subject">Subject line.</param>
/// <param name="HtmlBody">HTML body.</param>
/// <param name="TextBody">Plain-text alternative (shown by clients that don't render HTML).</param>
/// <param name="SenderName">Sender display name used when none is configured (e.g. the product name).</param>
public sealed record EmailMessage(string To, string Subject, string HtmlBody, string TextBody, string? SenderName = null);

/// <summary>
/// Delivers e-mails (account confirmation, password reset). Implemented with SMTP in the
/// Infrastructure layer; when no server is configured messages are only logged.
/// </summary>
public interface IEmailTransport
{
    /// <summary>True when messages actually leave the application (an SMTP server is configured).</summary>
    bool IsEnabled { get; }

    /// <summary>Sends the message. Throws when the server rejects it or cannot be reached.</summary>
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
