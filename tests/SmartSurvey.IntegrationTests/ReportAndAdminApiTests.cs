using System.Net;
using System.Net.Http.Json;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Dashboard;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Users;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.Identity;
using static SmartSurvey.IntegrationTests.ApiTestData;

namespace SmartSurvey.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ReportAndAdminApiTests(ApiFactory factory)
{
    private readonly HttpClient _admin = factory.Admin();

    [Fact]
    public async Task Default_report_is_generated_saved_run_and_exported()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Reported survey");
        await ReadAsync<SubmitResponseResult>(
            await factory.Anonymous().PostAsJsonAsync($"/api/v1/public/surveys/{survey.Id}/responses", Answers(survey), ApiFactory.Json));

        var suggestion = await ReadAsync<ReportDefinitionDto>(await _admin.PostAsync($"/api/v1/reports/default/{survey.Id}", null));
        Assert.Equal(Guid.Empty, suggestion.Id);
        var saved = await ReadAsync<ReportDefinitionDto>(await _admin.PostAsync($"/api/v1/reports/default/{survey.Id}?save=true", null), HttpStatusCode.Created);

        var result = await ReadAsync<ReportResult>(await _admin.GetAsync($"/api/v1/reports/{saved.Id}/run"));
        Assert.Equal(1, result.TotalResponses);
        var pie = Assert.Single(result.Widgets, w => w.Type == WidgetType.PieChart);
        Assert.Equal(["Yes", "No"], pie.Chart!.Labels);

        foreach (var (format, contentType) in new[]
                 {
                     ("pdf", "application/pdf"), ("csv", "text/csv"), ("txt", "text/plain"),
                     ("xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"), ("json", "application/json"),
                 })
        {
            var file = await _admin.GetAsync($"/api/v1/reports/{saved.Id}/export?format={format}");
            await AssertStatusAsync(file, HttpStatusCode.OK);
            Assert.Equal(contentType, file.Content.Headers.ContentType?.MediaType);
            Assert.EndsWith($".{format}", file.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        }

        await ProblemAsync(await _admin.GetAsync($"/api/v1/reports/{saved.Id}/export?format=docx"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Report_crud_and_preview()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Report CRUD survey");
        var definition = new ReportDefinitionDto
        {
            Name = "Custom report",
            SurveyId = survey.Id,
            Widgets = [new ReportWidgetDto { Type = WidgetType.SummaryStats }],
        };

        var preview = await ReadAsync<ReportResult>(await _admin.PostAsJsonAsync("/api/v1/reports/preview", definition, ApiFactory.Json));
        Assert.Null(preview.ReportId);

        var created = await ReadAsync<ReportDefinitionDto>(await _admin.PostAsJsonAsync("/api/v1/reports", definition, ApiFactory.Json), HttpStatusCode.Created);
        created.Name = "Custom report (renamed)";
        var updated = await ReadAsync<ReportDefinitionDto>(await _admin.PutAsJsonAsync($"/api/v1/reports/{created.Id}", created, ApiFactory.Json));
        Assert.Equal("Custom report (renamed)", updated.Name);

        var copy = await ReadAsync<ReportDefinitionDto>(await _admin.PostAsync($"/api/v1/reports/{created.Id}/duplicate", null), HttpStatusCode.Created);
        var list = await ReadAsync<PagedResult<ReportSummaryDto>>(await _admin.GetAsync($"/api/v1/reports?surveyId={survey.Id}"));
        Assert.Equal(2, list.TotalCount);

        await AssertStatusAsync(await _admin.DeleteAsync($"/api/v1/reports/{copy.Id}"), HttpStatusCode.NoContent);
        await AssertStatusAsync(await _admin.GetAsync($"/api/v1/reports/{copy.Id}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dashboard_and_audit_log_reflect_activity()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Audited survey");

        var dashboard = await ReadAsync<DashboardSummaryDto>(await _admin.GetAsync("/api/v1/dashboard"));
        var audit = await ReadAsync<PagedResult<AuditLogDto>>(await _admin.GetAsync($"/api/v1/audit?entityType=Survey&entityId={survey.Id}"));

        Assert.True(dashboard.TotalSurveys >= 1);
        Assert.Equal(30, dashboard.ResponsesPerDay.Count);
        Assert.Contains(audit.Items, e => e.Action == AuditActions.SurveyCreated && e.UserName == ApiFactory.AdminEmail);
        Assert.Contains(audit.Items, e => e.Action == AuditActions.SurveyStatusChanged);
    }

    [Fact]
    public async Task User_administration_create_roles_lock_unlock_delete()
    {
        var created = await ReadAsync<UserDto>(await _admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest
        {
            Email = "managed@it.local",
            DisplayName = "Managed User",
            Password = "Managed123!",
            Roles = [AppRoles.User],
        }, ApiFactory.Json), HttpStatusCode.Created);

        var promoted = await ReadAsync<UserDto>(await _admin.PutAsJsonAsync($"/api/v1/users/{created.Id}/roles", new { roles = new[] { "Admin", "User" } }));
        Assert.Contains(AppRoles.Admin, promoted.Roles);

        Assert.True((await ReadAsync<UserDto>(await _admin.PostAsync($"/api/v1/users/{created.Id}/lock", null))).IsLockedOut);
        Assert.False((await ReadAsync<UserDto>(await _admin.PostAsync($"/api/v1/users/{created.Id}/unlock", null))).IsLockedOut);

        var search = await ReadAsync<PagedResult<UserDto>>(await _admin.GetAsync("/api/v1/users?search=managed@"));
        Assert.Equal(created.Id, Assert.Single(search.Items).Id);

        await ProblemAsync(await _admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest
        {
            Email = "managed@it.local",
            Password = "Managed123!",
            Roles = [AppRoles.User],
        }, ApiFactory.Json), HttpStatusCode.Conflict);

        await AssertStatusAsync(await _admin.DeleteAsync($"/api/v1/users/{created.Id}"), HttpStatusCode.NoContent);
        await AssertStatusAsync(await _admin.GetAsync($"/api/v1/users/{created.Id}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admin_sets_a_new_password_that_works_for_sign_in()
    {
        var created = await ReadAsync<UserDto>(await _admin.PostAsJsonAsync("/api/v1/users", new CreateUserRequest
        {
            Email = "reset@it.local",
            Password = "Original123!",
            Roles = [AppRoles.User],
        }, ApiFactory.Json), HttpStatusCode.Created);

        var weak = await ProblemAsync(
            await _admin.PostAsJsonAsync($"/api/v1/users/{created.Id}/password", new SetUserPasswordRequest { Password = "weak" }, ApiFactory.Json),
            HttpStatusCode.BadRequest);
        Assert.True(weak.GetProperty("errors").TryGetProperty("Password", out _));

        await ReadAsync<UserDto>(await _admin.PostAsJsonAsync(
            $"/api/v1/users/{created.Id}/password", new SetUserPasswordRequest { Password = "Replaced123!" }, ApiFactory.Json));

        var anonymous = factory.Anonymous();
        await AssertStatusAsync(await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "reset@it.local", password = "Replaced123!" }), HttpStatusCode.OK);
        await AssertStatusAsync(await anonymous.PostAsJsonAsync("/api/auth/login", new { email = "reset@it.local", password = "Original123!" }), HttpStatusCode.Unauthorized);
        await AssertStatusAsync(
            await factory.Respondent().PostAsJsonAsync($"/api/v1/users/{created.Id}/password", new SetUserPasswordRequest { Password = "Hijack123!" }, ApiFactory.Json),
            HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Branding_is_public_to_read_and_admin_only_to_change()
    {
        try
        {
            var updated = await ReadAsync<BrandingDto>(await _admin.PutAsJsonAsync("/api/v1/branding", new UpdateBrandingRequest
            {
                ProductName = "Pulse Check",
                Tagline = "Listen better",
                IconName = "bi-heart-pulse",
            }, ApiFactory.Json));
            Assert.Equal("Pulse Check", updated.ProductName);

            var publicView = await ReadAsync<BrandingDto>(await factory.Anonymous().GetAsync("/api/v1/public/branding"));
            Assert.Equal("Pulse Check", publicView.ProductName);
            Assert.Equal("bi-heart-pulse", publicView.IconName);

            // A PNG signature is required; random bytes are rejected.
            await ProblemAsync(await _admin.PostAsJsonAsync("/api/v1/branding/logo", new { fileName = "logo.png", content = Convert.ToBase64String([1, 2, 3, 4]) }),
                HttpStatusCode.BadRequest);

            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
            var withLogo = await ReadAsync<BrandingDto>(await _admin.PostAsJsonAsync("/api/v1/branding/logo", new { fileName = "logo.png", content = Convert.ToBase64String(png) }));
            Assert.True(withLogo.HasLogo);

            var logo = await factory.Anonymous().GetAsync("/" + withLogo.LogoUrl);
            await AssertStatusAsync(logo, HttpStatusCode.OK);
            Assert.Equal("image/png", logo.Content.Headers.ContentType?.MediaType);

            Assert.False((await ReadAsync<BrandingDto>(await _admin.DeleteAsync("/api/v1/branding/logo"))).HasLogo);
        }
        finally
        {
            await AssertStatusAsync(await _admin.PostAsync("/api/v1/branding/reset", null), HttpStatusCode.OK);
        }
    }
}
