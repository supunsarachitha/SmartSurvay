using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MailKit.Security;
using SmartSurvey.Application.Common;
using SmartSurvey.Infrastructure.Email;
using SmartSurvey.Web.Components.Account;

namespace SmartSurvey.UnitTests.Admin;

/// <summary>Account e-mail rendering and the SMTP transport's configuration handling.</summary>
public sealed class EmailTests
{
    private const string EncodedLink = "https://surveys.example.com/Account/ResetPassword?code=abc&amp;userId=42";

    [Fact]
    public void Link_emails_are_branded_and_keep_the_link_usable_in_both_parts()
    {
        var message = AccountEmails.Build("rita@example.com", "Acme <Surveys>", "Rita", "Reset your password",
            "We received a request.", "Choose a new password", EncodedLink, code: null);

        Assert.Equal("rita@example.com", message.To);
        Assert.Equal("Reset your password · Acme <Surveys>", message.Subject);
        Assert.Contains("Acme &lt;Surveys&gt;", message.HtmlBody); // encoded in HTML
        Assert.Contains($"href=\"{EncodedLink}\"", message.HtmlBody); // not double-encoded
        Assert.Contains("Hi Rita,", message.HtmlBody);
        Assert.Contains("code=abc&userId=42", message.TextBody); // decoded for plain text
        Assert.DoesNotContain("&amp;", message.TextBody);
    }

    [Fact]
    public void Code_emails_show_the_code_and_fall_back_to_a_generic_greeting()
    {
        var message = AccountEmails.Build("x@example.com", "Acme", null, "Your password reset code", "Use this code:", null, null, "<123456>");

        Assert.Contains("&lt;123456&gt;", message.HtmlBody);
        Assert.Contains("Hi there,", message.TextBody);
        Assert.Contains("<123456>", message.TextBody);
        Assert.DoesNotContain("href=", message.HtmlBody);
    }

    [Fact]
    public async Task Without_a_host_nothing_is_sent_and_nothing_throws()
    {
        var transport = new SmtpEmailTransport(Options.Create(new EmailOptions()), new TestEnvironment(), NullLogger<SmtpEmailTransport>.Instance);

        Assert.False(transport.IsEnabled);
        await transport.SendAsync(new EmailMessage("a@example.com", "Hi", "<p>Hi</p>", "Hi"));
    }

    [Fact]
    public void Configured_host_enables_delivery()
    {
        var options = new EmailOptions { Smtp = { Host = "smtp.example.com" } };

        Assert.True(new SmtpEmailTransport(Options.Create(options), new TestEnvironment(), NullLogger<SmtpEmailTransport>.Instance).IsEnabled);
    }

    [Fact]
    public void Mime_message_has_sender_recipient_and_both_bodies()
    {
        using var mime = SmtpEmailTransport.BuildMessage(
            new EmailMessage("rita@example.com", "Subject", "<p>Html</p>", "Text"),
            new EmailOptions { FromAddress = "no-reply@example.com", FromName = "Acme" });

        Assert.Equal("\"Acme\" <no-reply@example.com>", mime.From.ToString());
        Assert.Equal("rita@example.com", mime.To.ToString());
        Assert.Equal("<p>Html</p>", mime.HtmlBody);
        Assert.Equal("Text", mime.TextBody);
    }

    [Fact]
    public void Sender_name_falls_back_to_the_message_sender_name()
    {
        var message = new EmailMessage("rita@example.com", "Subject", "<p>Html</p>", "Text", SenderName: "Acme Surveys");

        using var fallback = SmtpEmailTransport.BuildMessage(message, new EmailOptions { FromAddress = "no-reply@example.com" });
        using var configured = SmtpEmailTransport.BuildMessage(message, new EmailOptions { FromAddress = "no-reply@example.com", FromName = "Support" });

        Assert.Equal("\"Acme Surveys\" <no-reply@example.com>", fallback.From.ToString());
        Assert.Equal("\"Support\" <no-reply@example.com>", configured.From.ToString());
        Assert.Equal("Acme", AccountEmails.Build("a@example.com", "Acme", null, "S", "I", null, null, "1").SenderName);
    }

    [Theory]
    [InlineData(SmtpSecurity.Auto, 587, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(SmtpSecurity.Auto, 465, SecureSocketOptions.SslOnConnect)]
    [InlineData(SmtpSecurity.None, 25, SecureSocketOptions.None)]
    [InlineData(SmtpSecurity.StartTls, 587, SecureSocketOptions.StartTls)]
    [InlineData(SmtpSecurity.SslOnConnect, 2465, SecureSocketOptions.SslOnConnect)]
    public void Security_modes_map_to_mailkit_options(SmtpSecurity security, int port, SecureSocketOptions expected) =>
        Assert.Equal(expected, SmtpEmailTransport.MapSecurity(security, port));

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "SmartSurvey";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
