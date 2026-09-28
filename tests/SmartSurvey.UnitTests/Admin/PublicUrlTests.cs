using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Identity;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Components.Account;
using SmartSurvey.Web.Infrastructure;

namespace SmartSurvey.UnitTests.Admin;

/// <summary><see cref="PublicUrls"/> and its use in account e-mails (<c>App:PublicBaseUrl</c>).</summary>
public sealed class PublicUrlTests
{
    private static PublicUrls Urls(string? baseUrl) => new(Options.Create(new AppOptions { PublicBaseUrl = baseUrl }));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("https://surveys.example.com", true)]
    [InlineData("http://localhost:8080/", true)]
    [InlineData("surveys.example.com", false)]
    [InlineData("ftp://example.com", false)]
    [InlineData("/relative", false)]
    public void Only_absolute_http_urls_are_accepted(string? value, bool valid) => Assert.Equal(valid, PublicUrls.IsValid(value));

    [Fact]
    public void Links_keep_their_path_and_query_but_get_the_public_origin()
    {
        var urls = Urls("https://surveys.example.com/ignored-path");

        Assert.Equal("https://surveys.example.com", urls.Origin);
        Assert.Equal("https://surveys.example.com/Account/ConfirmEmail?userId=1&amp;code=x",
            urls.Rewrite("http://10.0.0.5:8080/Account/ConfirmEmail?userId=1&amp;code=x")); // HTML-encoded, as Identity passes it
        Assert.Equal("https://surveys.example.com/app/s/demo", urls.Rewrite("http://internal:5000/app/s/demo"));
    }

    [Fact]
    public void Without_a_public_address_links_stay_as_they_are()
    {
        var urls = Urls(null);

        Assert.Null(urls.Origin);
        Assert.Equal("http://localhost:5000/s/demo", urls.Rewrite("http://localhost:5000/s/demo"));
    }

    [Fact]
    public void Page_links_use_the_public_origin_with_the_path_base()
    {
        var navigation = new TestNavigation("http://10.0.0.5:8080/surveys/", "http://10.0.0.5:8080/surveys/admin");

        Assert.Equal("https://surveys.example.com/surveys/s/demo", Urls("https://surveys.example.com").Absolute(navigation, "s/demo"));
        Assert.Equal("https://surveys.example.com/surveys/", Urls("https://surveys.example.com").BaseUri(navigation));
        Assert.Equal("http://10.0.0.5:8080/surveys/s/demo", Urls(null).Absolute(navigation, "s/demo"));
    }

    [Fact]
    public async Task Account_emails_carry_the_public_address()
    {
        var transport = new RecordingTransport();
        var services = new ServiceCollection().AddSingleton<IBrandingService>(new StubBrandingService()).BuildServiceProvider();
        var sender = new IdentityEmailSender(transport, services.GetRequiredService<IServiceScopeFactory>(), Urls("https://surveys.example.com"),
            NullLogger<IdentityEmailSender>.Instance);
        var user = new ApplicationUser { Email = "robin@example.com", DisplayName = "Robin" };

        await sender.SendConfirmationLinkAsync(user, "robin@example.com", "http://evil.example/Account/ConfirmEmail?code=1");
        await sender.SendPasswordResetLinkAsync(user, "robin@example.com", "http://internal:8080/Account/ResetPassword?code=2");

        Assert.All(transport.Sent, m =>
        {
            Assert.Contains("https://surveys.example.com/Account/", m.HtmlBody);
            Assert.DoesNotContain("evil.example", m.HtmlBody + m.TextBody);
            Assert.DoesNotContain("internal:8080", m.HtmlBody + m.TextBody);
        });
    }

    private sealed class RecordingTransport : IEmailTransport
    {
        public List<EmailMessage> Sent { get; } = [];

        public bool IsEnabled => true;

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation(string baseUri, string uri) => Initialize(baseUri, uri);
    }
}
