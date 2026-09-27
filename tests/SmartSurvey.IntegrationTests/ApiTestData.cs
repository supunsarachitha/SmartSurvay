using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.IntegrationTests;

/// <summary>Request builders and HTTP helpers for the API tests.</summary>
internal static class ApiTestData
{
    /// <summary>
    /// A small survey: "Did you like it?" (radio Yes/No, required) and "Comments" (long text).
    /// Anonymous responses allowed.
    /// </summary>
    public static SurveyDefinitionDto Survey(string title) => new()
    {
        Title = title,
        Description = "Integration test survey",
        AllowAnonymous = true,
        Sections =
        [
            new SectionDto
            {
                Title = "Page 1",
                Questions =
                [
                    new QuestionDto
                    {
                        Type = QuestionType.Radio,
                        Text = "Did you like it?",
                        IsRequired = true,
                        Options = [new OptionDto { Text = "Yes", Order = 0 }, new OptionDto { Text = "No", Order = 1 }],
                    },
                    new QuestionDto { Type = QuestionType.LongText, Text = "Comments", Order = 1 },
                ],
            },
        ],
    };

    /// <summary>Creates and publishes a survey as admin; returns the saved definition.</summary>
    public static async Task<SurveyDefinitionDto> CreatePublishedSurveyAsync(HttpClient admin, string title)
    {
        var created = await ReadAsync<SurveyDefinitionDto>(await admin.PostAsJsonAsync("/api/v1/surveys", Survey(title), ApiFactory.Json), HttpStatusCode.Created);
        return await ReadAsync<SurveyDefinitionDto>(
            await admin.PostAsJsonAsync($"/api/v1/surveys/{created.Id}/status", new { status = "Published" }, ApiFactory.Json));
    }

    /// <summary>A complete answer set for <see cref="Survey"/> ("Yes" + comment).</summary>
    public static SaveResponseRequest Answers(SurveyDefinitionDto survey, string comment = "Great")
    {
        var questions = survey.Sections.SelectMany(s => s.Questions).ToList();
        return new SaveResponseRequest
        {
            Answers =
            [
                new AnswerInputDto { QuestionId = questions[0].Id, Selections = [new SelectionInputDto { OptionId = questions[0].Options[0].Id }] },
                new AnswerInputDto { QuestionId = questions[1].Id, Text = comment },
            ],
        };
    }

    /// <summary>Asserts the status code and deserialises the body.</summary>
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        await AssertStatusAsync(response, expected);
        return (await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json))!;
    }

    /// <summary>Asserts the status code (the body is included in the failure message).</summary>
    public static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"Expected {(int)expected} {expected} but got {(int)response.StatusCode} {response.StatusCode}: {body}");
        }
    }

    /// <summary>Asserts an RFC 7807 problem response with the given status and returns its JSON.</summary>
    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        await AssertStatusAsync(response, expected);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((int)expected, problem.GetProperty("status").GetInt32());
        return problem;
    }
}
