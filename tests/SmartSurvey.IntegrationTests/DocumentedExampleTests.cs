using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    [Fact]
    public async Task Every_request_of_the_http_walkthrough_is_an_operation_of_the_api()
    {
        var http = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples", "smartsurvey.http"));
        var document = await factory.Anonymous().GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");

        // Operation templates as regexes: "/api/v1/surveys/{id}" → ^/api/v1/surveys/[^/]+$
        var operations = document.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(method => (
                Method: method.Name.ToUpperInvariant(),
                Pattern: new Regex("^" + Regex.Replace(Regex.Escape(path.Name), @"\\\{[^}]+}", "[^/]+") + "$", RegexOptions.IgnoreCase))))
            .ToList();

        var requests = Regex.Matches(http, @"^(GET|POST|PUT|DELETE) \{\{baseUrl}}(/[^?\s]*)", RegexOptions.Multiline);
        Assert.True(requests.Count > 30, $"expected the full walkthrough, found {requests.Count} requests");
        foreach (Match request in requests)
        {
            var path = Regex.Replace(request.Groups[2].Value, @"\{\{[^}]+}}", "x"); // {{surveyId}} → a segment value
            Assert.True(
                operations.Any(o => o.Method == request.Groups[1].Value && o.Pattern.IsMatch(path)),
                $"{request.Groups[1].Value} {request.Groups[2].Value} is not an API operation");
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
