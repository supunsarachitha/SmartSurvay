using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SmartSurvey.Application.Common;

namespace SmartSurvey.Infrastructure.Email;

/// <summary>"Email" configuration section: sender identity and SMTP server.</summary>
public sealed class EmailOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Email";

    /// <summary>Sender address (must be allowed by the SMTP server).</summary>
    public string FromAddress { get; set; } = "no-reply@smartsurvey.local";

    /// <summary>Sender display name; empty = the message's sender name (the product name for account e-mails).</summary>
    public string? FromName { get; set; }

    /// <summary>SMTP server; e-mail delivery is disabled while <see cref="SmtpOptions.Host"/> is empty.</summary>
    public SmtpOptions Smtp { get; set; } = new();
}

/// <summary>SMTP server settings.</summary>
public sealed class SmtpOptions
{
    /// <summary>Server host name; empty disables delivery (messages are logged instead).</summary>
    public string? Host { get; set; }

    /// <summary>Port (587 = submission with STARTTLS, 465 = implicit TLS, 25 = plain relay).</summary>
    public int Port { get; set; } = 587;

    /// <summary>Transport security.</summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.Auto;

    /// <summary>Login name (empty = no authentication).</summary>
    public string? UserName { get; set; }

    /// <summary>Password (use an environment variable or secret store, never appsettings in source control).</summary>
    public string? Password { get; set; }

    /// <summary>Connection/command timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>SMTP transport security.</summary>
public enum SmtpSecurity
{
    /// <summary>Implicit TLS on port 465, otherwise STARTTLS when the server offers it.</summary>
    Auto = 0,

    /// <summary>No encryption (local relays and development mail catchers only).</summary>
    None = 1,

    /// <summary>Upgrade the connection with STARTTLS (required).</summary>
    StartTls = 2,

    /// <summary>TLS from the first byte (usually port 465).</summary>
    SslOnConnect = 3,
}

/// <summary>
/// Sends e-mail through an SMTP server with MailKit (one connection per message — account e-mails are
/// rare). Without a configured host nothing is sent: a warning is logged, and in Development the whole
/// message (including confirmation/reset links) is logged so the flows can be tried locally.
/// </summary>
public sealed class SmtpEmailTransport(
    IOptions<EmailOptions> options,
    IHostEnvironment environment,
    ILogger<SmtpEmailTransport> logger) : IEmailTransport
{
    private EmailOptions Options => options.Value;

    /// <inheritdoc />
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Options.Smtp.Host);

    /// <inheritdoc />
    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!IsEnabled)
        {
            if (environment.IsDevelopment())
            {
                logger.LogWarning(
                    "E-mail delivery is not configured (Email:Smtp:Host). Message to {Recipient} not sent: {Subject}{NewLine}{Body}",
                    message.To, message.Subject, Environment.NewLine, message.TextBody);
            }
            else
            {
                logger.LogWarning("E-mail delivery is not configured (Email:Smtp:Host); an e-mail \"{Subject}\" was not sent.", message.Subject);
            }

            return;
        }

        var smtp = Options.Smtp;
        using var mime = BuildMessage(message, Options);
        using var client = new SmtpClient { Timeout = Math.Max(1, smtp.TimeoutSeconds) * 1000 };
        await client.ConnectAsync(smtp.Host!, smtp.Port, MapSecurity(smtp.Security, smtp.Port), ct);
        if (!string.IsNullOrWhiteSpace(smtp.UserName))
        {
            await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? string.Empty, ct);
        }

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
        logger.LogInformation("E-mail \"{Subject}\" sent via {Host}.", message.Subject, smtp.Host);
    }

    /// <summary>Builds the MIME message: HTML with a plain-text alternative.</summary>
    internal static MimeMessage BuildMessage(EmailMessage message, EmailOptions options)
    {
        var mime = new MimeMessage();
        var senderName = string.IsNullOrWhiteSpace(options.FromName) ? message.SenderName ?? string.Empty : options.FromName;
        mime.From.Add(new MailboxAddress(senderName, options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();
        return mime;
    }

    /// <summary>Maps the configured security mode to MailKit's socket options.</summary>
    internal static SecureSocketOptions MapSecurity(SmtpSecurity security, int port) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable,
    };
}
