using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Tests of <see cref="ResponseService.GetCompletionAsync"/> (thank-you page data).</summary>
public class CompletionTests
{
    [Fact]
    public async Task Returns_title_and_thank_you_message_by_slug()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();

        var completion = await h.Service.GetCompletionAsync($" {s.Definition.Slug!.ToUpperInvariant()} ");

        Assert.NotNull(completion);
        Assert.Equal(s.Definition.Id, completion.SurveyId);
        Assert.Equal("Customer feedback", completion.Title);
        Assert.Equal(s.Definition.Slug, completion.Slug);
        Assert.Equal("Thanks!", completion.ThankYouMessage);
        Assert.False(completion.CanRespondAgain); // one response per user
    }

    [Fact]
    public async Task Answering_again_is_offered_when_multiple_responses_are_allowed()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(survey => survey.AllowMultipleResponses = true);
        h.User.ActAsRespondent();
        await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.Completed);

        var completion = await h.Service.GetCompletionAsync(s.Definition.Slug!);

        Assert.True(completion?.CanRespondAgain);
    }

    [Fact]
    public async Task Answering_again_is_not_offered_once_the_survey_closed()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(survey =>
        {
            survey.AllowMultipleResponses = true;
            survey.MaxResponses = 1;
        });
        await h.SeedResponseAsync(s, null, ResponseStatus.Completed);
        h.User.ActAsAnonymous();

        var completion = await h.Service.GetCompletionAsync(s.Definition.Slug!);

        Assert.NotNull(completion);
        Assert.False(completion.CanRespondAgain);
    }

    [Theory]
    [InlineData(SurveyStatus.Closed, true)]
    [InlineData(SurveyStatus.Archived, true)]
    [InlineData(SurveyStatus.Draft, false)]
    public async Task Drafts_are_never_disclosed(SurveyStatus status, bool expected)
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(survey => survey.Status = status);

        var completion = await h.Service.GetCompletionAsync(s.Definition.Slug!);

        Assert.Equal(expected, completion is not null);
    }

    [Fact]
    public async Task Templates_and_unknown_slugs_return_null()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var template = await h.SeedSurveyAsync(survey => survey.IsTemplate = true);

        Assert.Null(await h.Service.GetCompletionAsync(template.Definition.Slug!));
        Assert.Null(await h.Service.GetCompletionAsync("does-not-exist"));
        Assert.Null(await h.Service.GetCompletionAsync("  "));
    }

    [Fact]
    public async Task Members_only_surveys_are_hidden_from_guests()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(survey => survey.AllowAnonymous = false);

        h.User.ActAsAnonymous();
        Assert.Null(await h.Service.GetCompletionAsync(s.Definition.Slug!));

        h.User.ActAsRespondent();
        Assert.NotNull(await h.Service.GetCompletionAsync(s.Definition.Slug!));
    }
}
