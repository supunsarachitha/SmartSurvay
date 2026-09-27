using System.Net;
using static SmartSurvey.IntegrationTests.ApiTestData;

namespace SmartSurvey.IntegrationTests;

/// <summary>
/// Respondent pages served by the real host: prerendered survey runner, thank-you page, embedded
/// variants and the frame (clickjacking) headers that decide where pages may be shown in iframes.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class RespondentPageTests(ApiFactory factory)
{
    private readonly HttpClient _admin = factory.Admin();

    [Fact]
    public async Task Survey_page_is_prerendered_with_its_questions()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Prerendered runner survey");

        var response = await factory.Anonymous().GetAsync($"/s/{survey.Slug}");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Prerendered runner survey", html);
        Assert.Contains("Did you like it?", html);
        Assert.Contains("<!--Blazor:", html); // interactive component marker
        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task Unknown_survey_page_explains_that_it_was_not_found()
    {
        var html = await factory.Anonymous().GetStringAsync("/s/no-such-survey");

        Assert.Contains("Survey not found", html);
    }

    [Fact]
    public async Task Thank_you_page_shows_the_survey_title()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Thank you page survey");

        var html = await factory.Anonymous().GetStringAsync($"/s/{survey.Slug}/thank-you");

        Assert.Contains("Thank you!", html);
        Assert.Contains("Thank you page survey", html);
    }

    [Fact]
    public async Task Embedded_pages_may_be_framed_by_other_sites()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Embedded survey");
        var client = factory.Anonymous();

        foreach (var path in new[] { $"/embed/s/{survey.Slug}", $"/embed/s/{survey.Slug}/thank-you" })
        {
            var response = await client.GetAsync(path);
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Embedded survey", html);
            Assert.Contains("Powered by", html);
            Assert.False(response.Headers.Contains("X-Frame-Options"), path);
            Assert.Equal("frame-ancestors 'self' *", response.Headers.GetValues("Content-Security-Policy").Single());
        }
    }

    [Fact]
    public async Task Other_pages_may_only_be_framed_by_this_site()
    {
        var response = await factory.Anonymous().GetAsync("/Account/Login");

        Assert.Equal("SAMEORIGIN", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("frame-ancestors 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
    }
}
