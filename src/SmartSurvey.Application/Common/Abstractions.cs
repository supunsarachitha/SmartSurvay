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

    /// <summary>Identity users (read access for display names / respondent info).</summary>
    DbSet<ApplicationUser> Users { get; }

    /// <summary>Persists pending changes.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Creates short-lived <see cref="IAppDbContext"/> instances. Services create one context per
/// operation (<c>await using var db = await factory.CreateAsync(ct);</c>) which is the recommended
/// pattern for Blazor Server where DI scopes live as long as the user's circuit.
/// </summary>
public interface IAppDbContextFactory
{
    /// <summary>Creates a new context; the caller owns and must dispose it.</summary>
    Task<IAppDbContext> CreateAsync(CancellationToken cancellationToken = default);
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

    /// <summary>True when the user is in the <see cref="AppRoles.Admin"/> role.</summary>
    bool IsAdmin { get; }

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

/// <summary>
/// Access keys for password-protected surveys: after a respondent enters the correct password they receive a
/// signed, time-limited key that proves it for that survey — until it expires or the password changes. The key
/// accompanies every later request (opening, saving and submitting), so the password isn't re-checked each time.
/// </summary>
public interface ISurveyAccessKeys
{
    /// <summary>How long an access key stays valid.</summary>
    TimeSpan Lifetime { get; }

    /// <summary>Issues a key for <paramref name="surveyId"/> bound to its current password hash.</summary>
    string Issue(Guid surveyId, string passwordHash);

    /// <summary>True when <paramref name="key"/> was issued for this survey and password and hasn't expired.</summary>
    bool IsValid(string? key, Guid surveyId, string passwordHash);
}
