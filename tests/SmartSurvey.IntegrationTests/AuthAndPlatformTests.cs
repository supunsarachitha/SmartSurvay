using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static SmartSurvey.IntegrationTests.ApiTestData;

namespace SmartSurvey.IntegrationTests;

/// <summary>Authentication, authorization, error format and platform endpoints.</summary>
[Collection(ApiCollection.Name)]
public sealed class AuthAndPlatformTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_check_is_public() =>
        await AssertStatusAsync(await factory.Anonymous().GetAsync("/health"), HttpStatusCode.OK);

    [Theory]
    [InlineData("/api/v1/surveys")]
    [InlineData("/api/v1/reports")]
    [InlineData("/api/v1/dashboard")]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/audit")]
    [InlineData("/api/v1/me/responses")]
    public async Task Api_returns_401_instead_of_a_login_redirect_for_anonymous_callers(string path)
    {
        var response = await factory.Anonymous().GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("/api/v1/surveys")]
    [InlineData("/api/v1/reports")]
    [InlineData("/api/v1/dashboard")]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/audit")]
    public async Task Admin_endpoints_return_403_for_respondents(string path) =>
        await AssertStatusAsync(await factory.Respondent().GetAsync(path), HttpStatusCode.Forbidden);

    [Fact]
    public async Task Admin_branding_endpoints_return_403_for_respondents() =>
        await AssertStatusAsync(await factory.Respondent().PostAsync("/api/v1/branding/reset", null), HttpStatusCode.Forbidden);

    [Fact]
    public async Task Login_with_a_wrong_password_fails() =>
        await AssertStatusAsync(
            await factory.Anonymous().PostAsJsonAsync("/api/auth/login", new { email = ApiFactory.AdminEmail, password = "wrong-password" }),
            HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Unknown_resources_return_problem_details()
    {
        var problem = await ProblemAsync(await factory.Admin().GetAsync($"/api/v1/surveys/{Guid.NewGuid()}"), HttpStatusCode.NotFound);

        Assert.Equal("Not found", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Validation_errors_return_400_with_field_errors()
    {
        var invalid = Survey(title: "");

        var problem = await ProblemAsync(await factory.Admin().PostAsJsonAsync("/api/v1/surveys", invalid, ApiFactory.Json), HttpStatusCode.BadRequest);

        Assert.True(problem.GetProperty("errors").EnumerateObject().Any(), "expected field errors");
    }

    [Fact]
    public async Task OpenApi_document_describes_every_endpoint_group()
    {
        var document = await factory.Anonymous().GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        var paths = document.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/v1/surveys", paths);
        Assert.Contains("/api/v1/surveys/{id}/responses/export", paths);
        Assert.Contains("/api/v1/public/surveys/{slug}", paths);
        Assert.Contains("/api/v1/reports/{id}/export", paths);
        Assert.Contains("/api/v1/branding/logo", paths);
        Assert.Contains("/api/v1/users/{id}/roles", paths);
        Assert.Contains("/api/auth/login", paths);
    }

    [Fact]
    public async Task Expected_api_errors_are_not_logged_as_server_errors()
    {
        var admin = factory.Admin();
        factory.Logs.Clear();

        await ApiTestData.AssertStatusAsync(await admin.GetAsync($"/api/v1/surveys/{Guid.NewGuid()}"), System.Net.HttpStatusCode.NotFound);
        await ApiTestData.AssertStatusAsync(
            await admin.PostAsJsonAsync("/api/v1/surveys", new { title = "" }, ApiFactory.Json), System.Net.HttpStatusCode.BadRequest);

        Assert.DoesNotContain(factory.Logs.Entries, e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Error);
    }
}
