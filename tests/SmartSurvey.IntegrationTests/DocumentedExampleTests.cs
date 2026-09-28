using System.Net;
using System.Net.Http.Json;
using System.Text;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using static SmartSurvey.IntegrationTests.ApiTestData;

namespace SmartSurvey.IntegrationTests;

/// <summary>The JSON examples in <c>docs/examples</c> are accepted by the API exactly as documented.</summary>
[Collection(ApiCollection.Name)]
public sealed class DocumentedExampleTests(ApiFactory factory)
{
    [Fact]
    public async Task Survey_response_and_report_examples_work_end_to_end()
    {
        var admin = factory.Admin();

        var survey = await ReadAsync<SurveyDefinitionDto>(await admin.PostAsync("/api/v1/surveys", Example("survey-definition.json")), HttpStatusCode.Created);
        Assert.Equal("customer-satisfaction", survey.Slug);
        Assert.Single(survey.LogicRules);
        await AssertStatusAsync(await admin.PostAsync($"/api/v1/surveys/{survey.Id}/status", Json("""{ "status": "Published" }""")), HttpStatusCode.OK);

        var example = await Example("submit-response.json").ReadFromJsonAsync<SaveResponseRequest>(ApiFactory.Json);
        var submitted = await ReadAsync<SubmitResponseResult>(await SubmitAnonymouslyAsync(factory.Anonymous(), survey, example!));
        var detail = await ReadAsync<ResponseDetailDto>(await admin.GetAsync($"/api/v1/responses/{submitted.ResponseId}"));
        Assert.Equal(Domain.Enums.ResponseStatus.Completed, detail.Status);

        var report = await ReadAsync<ReportDefinitionDto>(
            await admin.PostAsync("/api/v1/reports", Example("report-definition.json", ("REPLACE-WITH-SURVEY-ID", survey.Id.ToString()))),
            HttpStatusCode.Created);
        var result = await ReadAsync<ReportResult>(await admin.GetAsync($"/api/v1/reports/{report.Id}/run"));
        Assert.Equal(7, result.Widgets.Count);
        Assert.All(result.Widgets, w => Assert.Null(w.Error));
    }

    [Fact]
    public void Http_walkthrough_references_existing_example_files()
    {
        var http = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples", "smartsurvey.http"));

        foreach (var file in new[] { "survey-definition.json", "submit-response.json", "report-definition.json" })
        {
            Assert.Contains($"< ./{file}", http);
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "Examples", file)), $"{file} is missing");
        }
    }

    private static StringContent Example(string fileName, params (string Placeholder, string Value)[] replacements)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples", fileName));
        foreach (var (placeholder, value) in replacements)
        {
            json = json.Replace(placeholder, value);
        }

        return Json(json);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
}
