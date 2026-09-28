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

        var available = await ReadAsync<List<AvailableSurveyDto>>(await anonymous.GetAsync($"/api/v1/public/surveys?workspace={ApiFactory.WorkspaceSlug}"));
        Assert.Contains(available, s => s.SurveyId == survey.Id && s.CanRespond);

        var session = await ReadAsync<SurveySessionDto>(await anonymous.GetAsync($"/api/v1/public/surveys/{survey.Slug}"));
        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
        Assert.Equal(survey.Id, session.Survey!.Id);

        var result = await ReadAsync<SubmitResponseResult>(
            await anonymous.PostAsJsonAsync($"/api/v1/public/surveys/{survey.Id}/responses", Answers(survey), ApiFactory.Json));
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

        await ProblemAsync(
            await factory.Anonymous().PostAsJsonAsync($"/api/v1/public/surveys/{survey.Id}/responses", request, ApiFactory.Json),
            HttpStatusCode.BadRequest);
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
            await factory.Anonymous().PostAsJsonAsync($"/api/v1/public/surveys/{survey.Id}/responses", Answers(survey, "Exported, \"quoted\""), ApiFactory.Json));

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

        var available = await ReadAsync<List<AvailableSurveyDto>>(await factory.Anonymous().GetAsync($"/api/v1/public/surveys?workspace={ApiFactory.WorkspaceSlug}"));
        var session = await ReadAsync<SurveySessionDto>(await factory.Anonymous().GetAsync($"/api/v1/public/surveys/{draft.Slug}"));

        Assert.DoesNotContain(available, s => s.SurveyId == draft.Id);
        Assert.NotEqual(SurveyEligibility.Eligible, session.Eligibility);
        Assert.Null(session.Survey);
    }
}
