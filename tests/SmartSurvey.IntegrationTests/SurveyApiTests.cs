using System.Net;
using System.Net.Http.Json;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Web.Api;
using static SmartSurvey.IntegrationTests.ApiTestData;

namespace SmartSurvey.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class SurveyApiTests(ApiFactory factory)
{
    private readonly HttpClient _admin = factory.Admin();

    [Fact]
    public async Task Survey_lifecycle_create_get_update_publish_duplicate_delete()
    {
        var create = await _admin.PostAsJsonAsync("/api/v1/surveys", Survey("Lifecycle survey"), ApiFactory.Json);
        var created = await ReadAsync<SurveyDefinitionDto>(create, HttpStatusCode.Created);
        Assert.Equal($"/api/v1/surveys/{created.Id}", create.Headers.Location?.OriginalString);
        Assert.Equal(SurveyStatus.Draft, created.Status);

        var loaded = await ReadAsync<SurveyDefinitionDto>(await _admin.GetAsync($"/api/v1/surveys/{created.Id}"));
        loaded.Title = "Lifecycle survey (edited)";
        var updated = await ReadAsync<SurveyDefinitionDto>(await _admin.PutAsJsonAsync($"/api/v1/surveys/{created.Id}", loaded, ApiFactory.Json));
        Assert.Equal("Lifecycle survey (edited)", updated.Title);

        var published = await ReadAsync<SurveyDefinitionDto>(
            await _admin.PostAsJsonAsync($"/api/v1/surveys/{created.Id}/status", new { status = "Published" }, ApiFactory.Json));
        Assert.Equal(SurveyStatus.Published, published.Status);

        var copy = await ReadAsync<SurveyDefinitionDto>(
            await _admin.PostAsJsonAsync($"/api/v1/surveys/{created.Id}/duplicate", new { title = "Copied survey" }, ApiFactory.Json),
            HttpStatusCode.Created);
        Assert.Equal("Copied survey", copy.Title);
        Assert.Equal(SurveyStatus.Draft, copy.Status);

        await AssertStatusAsync(await _admin.DeleteAsync($"/api/v1/surveys/{copy.Id}"), HttpStatusCode.NoContent);
        await AssertStatusAsync(await _admin.GetAsync($"/api/v1/surveys/{copy.Id}"), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Stale_version_returns_409()
    {
        var created = await ReadAsync<SurveyDefinitionDto>(
            await _admin.PostAsJsonAsync("/api/v1/surveys", Survey("Concurrency survey"), ApiFactory.Json), HttpStatusCode.Created);
        var first = await ReadAsync<SurveyDefinitionDto>(await _admin.GetAsync($"/api/v1/surveys/{created.Id}"));
        var second = await ReadAsync<SurveyDefinitionDto>(await _admin.GetAsync($"/api/v1/surveys/{created.Id}"));

        first.Title = "Saved first";
        await AssertStatusAsync(await _admin.PutAsJsonAsync($"/api/v1/surveys/{created.Id}", first, ApiFactory.Json), HttpStatusCode.OK);
        second.Title = "Saved second";

        await ProblemAsync(await _admin.PutAsJsonAsync($"/api/v1/surveys/{created.Id}", second, ApiFactory.Json), HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_filters_by_search_and_status()
    {
        var published = await CreatePublishedSurveyAsync(_admin, "Searchable zebra survey");

        var page = await ReadAsync<PagedResult<SurveySummaryDto>>(await _admin.GetAsync("/api/v1/surveys?search=zebra&status=Published&pageSize=5"));

        var item = Assert.Single(page.Items);
        Assert.Equal(published.Id, item.Id);
        Assert.Equal(5, page.PageSize);
    }

    [Fact]
    public async Task Definition_export_and_import_round_trip()
    {
        var created = await ReadAsync<SurveyDefinitionDto>(
            await _admin.PostAsJsonAsync("/api/v1/surveys", Survey("Portable survey"), ApiFactory.Json), HttpStatusCode.Created);

        var document = await ReadAsync<SurveyExportDocument>(await _admin.GetAsync($"/api/v1/surveys/{created.Id}/definition"));
        var imported = await ReadAsync<SurveyDefinitionDto>(
            await _admin.PostAsJsonAsync("/api/v1/surveys/import", document, ApiFactory.Json), HttpStatusCode.Created);

        Assert.NotEqual(created.Id, imported.Id);
        Assert.Equal(2, imported.Sections.Single().Questions.Count);
        Assert.NotEqual(created.Slug, imported.Slug);
    }

    [Fact]
    public async Task Slug_availability_check()
    {
        var created = await ReadAsync<SurveyDefinitionDto>(
            await _admin.PostAsJsonAsync("/api/v1/surveys", Survey("Slug survey"), ApiFactory.Json), HttpStatusCode.Created);

        var taken = await _admin.GetFromJsonAsync<SlugAvailabilityDto>($"/api/v1/surveys/slug-available?slug={created.Slug}", ApiFactory.Json);
        var own = await _admin.GetFromJsonAsync<SlugAvailabilityDto>($"/api/v1/surveys/slug-available?slug={created.Slug}&excludeId={created.Id}", ApiFactory.Json);

        Assert.False(taken!.Available);
        Assert.True(own!.Available);
    }
}
