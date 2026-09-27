using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.Web.Components.Account;

/// <summary>
/// Account e-mails (confirm address, reset password) as branded HTML with a plain-text part, delivered
/// through <see cref="IEmailTransport"/>. Delivery failures are logged, never shown: the account pages
/// must not reveal whether an address belongs to an account.
/// </summary>
/// <remarks>
/// Identity passes links already HTML-encoded ("do not double encode"), so they are inserted into the
/// HTML as they are and decoded for the text part; every other value is encoded here.
/// </remarks>
internal sealed class IdentityEmailSender(IEmailTransport transport, IBrandingService branding, ILogger<IdentityEmailSender> logger)
    : IEmailSender<ApplicationUser>
{
    /// <inheritdoc />
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink) =>
        SendAsync(user, email, "Confirm your e-mail address",
            "Please confirm your e-mail address to finish setting up your account.",
            "Confirm e-mail address", confirmationLink, code: null);

    /// <inheritdoc />
    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(user, email, "Reset your password",
            "We received a request to reset the password of your account. Choose a new password with the button below.",
            "Choose a new password", resetLink, code: null);

    /// <inheritdoc />
    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode) =>
        SendAsync(user, email, "Your password reset code",
            "We received a request to reset the password of your account. Use this code to choose a new password:",
            buttonText: null, encodedLink: null, code: resetCode);

    private async Task SendAsync(ApplicationUser user, string email, string subject, string intro, string? buttonText, string? encodedLink, string? code)
    {
        try
        {
            var productName = (await branding.GetAsync()).ProductName;
            await transport.SendAsync(AccountEmails.Build(email, productName, user.DisplayName, subject, intro, buttonText, encodedLink, code));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sending the account e-mail \"{Subject}\" failed.", subject);
        }
    }
}

/// <summary>Renders the account e-mails (inline-styled HTML that works in common mail clients, plus text).</summary>
internal static class AccountEmails
{
    /// <summary>Builds one account e-mail.</summary>
    /// <param name="to">Recipient.</param>
    /// <param name="productName">Product name (branding).</param>
    /// <param name="displayName">Recipient's display name, if known.</param>
    /// <param name="subject">Subject and heading.</param>
    /// <param name="intro">Explanation above the button / code.</param>
    /// <param name="buttonText">Button label (link e-mails).</param>
    /// <param name="encodedLink">HTML-encoded link (link e-mails).</param>
    /// <param name="code">One-time code (code e-mails).</param>
    public static EmailMessage Build(
        string to, string productName, string? displayName, string subject, string intro, string? buttonText, string? encodedLink, string? code)
    {
        var html = HtmlEncoder.Default;
        var greeting = string.IsNullOrWhiteSpace(displayName) ? "Hi there," : $"Hi {displayName.Trim()},";
        var link = encodedLink is null ? null : WebUtility.HtmlDecode(encodedLink);

        var action = encodedLink is not null
            ? $"""
              <p style="margin:24px 0"><a href="{encodedLink}" style="display:inline-block;background:#4f46e5;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px;font-weight:600">{html.Encode(buttonText ?? "Open")}</a></p>
              <p style="font-size:13px;line-height:1.5;color:#64748b;margin:0 0 12px">If the button doesn't work, copy this address into your browser:<br><a href="{encodedLink}" style="color:#4f46e5;word-break:break-all">{encodedLink}</a></p>
              """
            : $"""<p style="margin:20px 0;font-size:26px;font-weight:700;letter-spacing:.15em;font-family:Consolas,Menlo,monospace">{html.Encode(code ?? string.Empty)}</p>""";

        var htmlBody = $"""
            <!DOCTYPE html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{html.Encode(subject)}</title></head>
            <body style="margin:0;padding:24px;background:#f5f6fa;font-family:Inter,'Segoe UI',Helvetica,Arial,sans-serif;color:#0f172a">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td align="center">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px">
            <tr><td style="padding:28px 32px">
            <div style="font-weight:700;font-size:18px;color:#4f46e5">{html.Encode(productName)}</div>
            <h1 style="font-size:20px;line-height:1.3;margin:20px 0 12px">{html.Encode(subject)}</h1>
            <p style="font-size:15px;line-height:1.6;margin:0 0 12px">{html.Encode(greeting)}</p>
            <p style="font-size:15px;line-height:1.6;margin:0 0 12px">{html.Encode(intro)}</p>
            {action}
            <p style="font-size:13px;line-height:1.5;color:#64748b;margin:0">If you didn't ask for this, you can safely ignore this e-mail.</p>
            </td></tr></table>
            <p style="font-size:12px;color:#94a3b8;margin:16px 0 0">{html.Encode(productName)}</p>
            </td></tr></table>
            </body></html>
            """;

        var textAction = link is not null ? $"{buttonText}: {link}" : code ?? string.Empty;
        var textBody = $"{greeting}\n\n{intro}\n\n{textAction}\n\nIf you didn't ask for this, you can safely ignore this e-mail.\n\n— {productName}\n";

        return new EmailMessage(to, $"{subject} · {productName}", htmlBody, textBody, SenderName: productName);
    }
}
