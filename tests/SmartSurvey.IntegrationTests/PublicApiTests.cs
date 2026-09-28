using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Web.Api;
using static SmartSurvey.IntegrationTests.ApiTestData;

namespace SmartSurvey.IntegrationTests;

/// <summary>Respondent flows: discover, start, submit, drafts, "my responses" and admin access to the responses.</summary>
[Collection(ApiCollection.Name)]
public sealed class PublicApiTests(ApiFactory factory)
{
    private readonly HttpClient _admin = factory.Admin();

    [Fact]
    public async Task Anonymous_respondent_discovers_starts_and_submits_a_survey()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Anonymous flow survey");
        var anonymous = factory.Anonymous();

        var available = await ReadAsync<List<AvailableSurveyDto>>(await anonymous.GetAsync("/api/v1/public/surveys"));
        Assert.Contains(available, s => s.SurveyId == survey.Id && s.CanRespond);

        var session = await ReadAsync<SurveySessionDto>(await anonymous.GetAsync($"/api/v1/public/surveys/{survey.Slug}"));
        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
        Assert.Equal(survey.Id, session.Survey!.Id);

        var result = await ReadAsync<SubmitResponseResult>(
            await SubmitAnonymouslyAsync(anonymous, survey, Answers(survey)));
        Assert.NotEqual(Guid.Empty, result.ResponseId);

        var detail = await ReadAsync<ResponseDetailDto>(await _admin.GetAsync($"/api/v1/responses/{result.ResponseId}"));
        Assert.Equal(ResponseStatus.Completed, detail.Status);
    }

    [Fact]
    public async Task Missing_required_answer_is_rejected()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Required answer survey");
        var request = Answers(survey);
        request.Answers.RemoveAt(0);

        await ProblemAsync(await SubmitAnonymouslyAsync(factory.Anonymous(), survey, request), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Draft_endpoints_need_a_signed_in_user() =>
        await AssertStatusAsync(
            await factory.Anonymous().PutAsJsonAsync($"/api/v1/public/surveys/{Guid.NewGuid()}/draft", new SaveResponseRequest(), ApiFactory.Json),
            HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Signed_in_user_saves_a_draft_then_submits_and_sees_it_in_my_responses()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Draft flow survey");
        var user = factory.Respondent();

        var draft = await ReadAsync<DraftSavedDto>(
            await user.PutAsJsonAsync($"/api/v1/public/surveys/{survey.Id}/draft", Answers(survey, "Half done"), ApiFactory.Json));
        var resumed = await ReadAsync<SurveySessionDto>(await user.GetAsync($"/api/v1/public/surveys/{survey.Slug}"));
        Assert.Equal(draft.ResponseId, resumed.DraftResponseId);

        var final = Answers(survey, "Done");
        final.ResponseId = draft.ResponseId;
        var submitted = await ReadAsync<SubmitResponseResult>(
            await user.PostAsJsonAsync($"/api/v1/public/surveys/{survey.Id}/responses", final, ApiFactory.Json));
        Assert.Equal(draft.ResponseId, submitted.ResponseId);

        var mine = await ReadAsync<List<MyResponseDto>>(await user.GetAsync("/api/v1/me/responses"));
        var entry = Assert.Single(mine, r => r.SurveyId == survey.Id);
        Assert.Equal(ResponseStatus.Completed, entry.Status);

        // The respondent may read their own response but not delete it.
        await AssertStatusAsync(await user.GetAsync($"/api/v1/responses/{submitted.ResponseId}"), HttpStatusCode.OK);
        await AssertStatusAsync(await user.DeleteAsync($"/api/v1/responses/{submitted.ResponseId}"), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_lists_exports_and_deletes_responses()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Export survey");
        var submitted = await ReadAsync<SubmitResponseResult>(
            await SubmitAnonymouslyAsync(factory.Anonymous(), survey, Answers(survey, "Exported, \"quoted\"")));

        var page = await ReadAsync<PagedResult<ResponseSummaryDto>>(await _admin.GetAsync($"/api/v1/surveys/{survey.Id}/responses"));
        Assert.Equal(submitted.ResponseId, Assert.Single(page.Items).Id);

        var csv = await _admin.GetAsync($"/api/v1/surveys/{survey.Id}/responses/export?format=csv");
        await AssertStatusAsync(csv, HttpStatusCode.OK);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".csv", csv.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Contains("\"Exported, \"\"quoted\"\"\"", await csv.Content.ReadAsStringAsync());

        var xlsx = await _admin.GetAsync($"/api/v1/surveys/{survey.Id}/responses/export?format=xlsx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType?.MediaType);

        var badFormat = await ProblemAsync(await _admin.GetAsync($"/api/v1/surveys/{survey.Id}/responses/export?format=pdf"), HttpStatusCode.BadRequest);
        Assert.True(badFormat.GetProperty("errors").TryGetProperty("format", out _));

        await AssertStatusAsync(await _admin.DeleteAsync($"/api/v1/responses/{submitted.ResponseId}"), HttpStatusCode.NoContent);
        await AssertStatusAsync(await _admin.GetAsync($"/api/v1/responses/{submitted.ResponseId}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Draft_surveys_are_not_available_to_respondents()
    {
        var draft = await ReadAsync<Application.Surveys.SurveyDefinitionDto>(
            await _admin.PostAsJsonAsync("/api/v1/surveys", Survey("Unpublished survey"), ApiFactory.Json), HttpStatusCode.Created);

        var available = await ReadAsync<List<AvailableSurveyDto>>(await factory.Anonymous().GetAsync("/api/v1/public/surveys"));
        var session = await ReadAsync<SurveySessionDto>(await factory.Anonymous().GetAsync($"/api/v1/public/surveys/{draft.Slug}"));

        Assert.DoesNotContain(available, s => s.SurveyId == draft.Id);
        Assert.NotEqual(SurveyEligibility.Eligible, session.Eligibility);
        Assert.Null(session.Survey);
    }

    [Fact]
    public async Task Password_protected_survey_needs_the_access_key_from_unlock()
    {
        var design = Survey("Protected API survey");
        design.PasswordProtected = true;
        design.AccessPassword = "api-pass-123";
        var created = await ReadAsync<Application.Surveys.SurveyDefinitionDto>(
            await _admin.PostAsJsonAsync("/api/v1/surveys", design, ApiFactory.Json), HttpStatusCode.Created);
        var survey = await ReadAsync<Application.Surveys.SurveyDefinitionDto>(
            await _admin.PostAsJsonAsync($"/api/v1/surveys/{created.Id}/status", new { status = "Published" }, ApiFactory.Json));
        Assert.True(survey.PasswordProtected);
        Assert.Null(survey.AccessPassword);
        var anonymous = factory.Anonymous();

        var locked = await ReadAsync<SurveySessionDto>(await anonymous.GetAsync($"/api/v1/public/surveys/{survey.Slug}"));
        Assert.Equal(SurveyEligibility.PasswordRequired, locked.Eligibility);
        Assert.Null(locked.Survey);

        await ProblemAsync(await anonymous.PostAsJsonAsync($"/api/v1/public/surveys/{survey.Slug}/unlock", new { password = "wrong" }, ApiFactory.Json),
            HttpStatusCode.BadRequest);
        await ProblemAsync(await anonymous.PostAsJsonAsync($"/api/v1/public/surveys/{survey.Id}/responses", Answers(survey), ApiFactory.Json),
            HttpStatusCode.Forbidden);

        var unlock = await ReadAsync<SurveyUnlockResult>(
            await anonymous.PostAsJsonAsync($"/api/v1/public/surveys/{survey.Slug}/unlock", new { password = "api-pass-123" }, ApiFactory.Json));
        using var withKey = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/public/surveys/{survey.Slug}");
        withKey.Headers.Add("X-Survey-Access-Key", unlock.AccessKey);
        var open = await ReadAsync<SurveySessionDto>(await anonymous.SendAsync(withKey));
        Assert.Equal(SurveyEligibility.Eligible, open.Eligibility);

        await ReadAsync<SubmitResponseResult>(await SubmitAnonymouslyAsync(anonymous, survey, Answers(survey), unlock.AccessKey));

        var listed = await ReadAsync<List<AvailableSurveyDto>>(await anonymous.GetAsync("/api/v1/public/surveys"));
        Assert.DoesNotContain(listed, s => s.SurveyId == survey.Id);
    }

    [Fact]
    public async Task Anonymous_submissions_must_pass_the_bot_checks()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Bot protected survey");
        var anonymous = factory.Anonymous();

        var session = await ReadAsync<SurveySessionDto>(await anonymous.GetAsync($"/api/v1/public/surveys/{survey.Slug}"));
        Assert.Equal("SHA-256", session.Challenge!.Algorithm);

        // No proof of work → rejected as automated.
        await ProblemAsync(await anonymous.PostAsJsonAsync($"/api/v1/public/surveys/{survey.Id}/responses", Answers(survey), ApiFactory.Json),
            HttpStatusCode.UnprocessableEntity);

        // Honeypot filled in → rejected even with a valid solution.
        var trapped = Answers(survey);
        trapped.Challenge = Solve(session.Challenge);
        trapped.Website = "http://spam.example";
        await ProblemAsync(await anonymous.PostAsJsonAsync($"/api/v1/public/surveys/{survey.Id}/responses", trapped, ApiFactory.Json),
            HttpStatusCode.UnprocessableEntity);

        // A valid solution works once; replaying it is refused.
        var request = Answers(survey);
        request.Challenge = Solve(session.Challenge);
        await ReadAsync<SubmitResponseResult>(await anonymous.PostAsJsonAsync($"/api/v1/public/surveys/{survey.Id}/responses", request, ApiFactory.Json));
        await ProblemAsync(await anonymous.PostAsJsonAsync($"/api/v1/public/surveys/{survey.Id}/responses", request, ApiFactory.Json),
            HttpStatusCode.UnprocessableEntity);

        // Signed-in respondents are not challenged.
        var mine = await ReadAsync<SurveySessionDto>(await factory.Respondent().GetAsync($"/api/v1/public/surveys/{survey.Slug}"));
        Assert.Null(mine.Challenge);
    }

    [Fact]
    public async Task Typed_answers_are_encrypted_in_the_database_but_readable_through_the_app()
    {
        var survey = await CreatePublishedSurveyAsync(_admin, "Encrypted answers survey");
        const string comment = "Only the admins should read this 4f9c";
        var result = await ReadAsync<SubmitResponseResult>(await SubmitAnonymouslyAsync(factory.Anonymous(), survey, Answers(survey, comment)));

        var detail = await ReadAsync<ResponseDetailDto>(await _admin.GetAsync($"/api/v1/responses/{result.ResponseId}"));
        Assert.Contains(detail.Answers, a => a.DisplayValue == comment);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartSurvey.Infrastructure.Persistence.AppDbContext>();
        var raw = await db.Database.SqlQueryRaw<string>(
            "SELECT \"TextValue\" AS \"Value\" FROM \"Answers\" WHERE \"ResponseId\" = {0} AND \"TextValue\" IS NOT NULL", result.ResponseId).SingleAsync();
        Assert.StartsWith("enc:v1:", raw);
        Assert.DoesNotContain("admins", raw);
    }
}
