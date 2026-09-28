using SmartSurvey.Application.Common;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>The respondent flow of password-protected surveys through <see cref="ResponseService"/>.</summary>
public class PasswordProtectedSurveyTests
{
    private const string Password = "let-me-in";

    private static Task<SampleSurvey> SeedProtectedAsync(ResponseTestHarness h, Action<Domain.Entities.Survey>? configure = null) =>
        h.SeedSurveyAsync(s =>
        {
            s.AccessPasswordHash = SurveyPasswordHasher.Hash(Password);
            configure?.Invoke(s);
        });

    private static SaveResponseRequest Answers(SampleSurvey s, string? accessKey) => new()
    {
        AccessKey = accessKey,
        Answers =
        [
            new AnswerInputDto { QuestionId = s.Enjoy.Id, Selections = [new SelectionInputDto { OptionId = s.Yes.Id }] },
            new AnswerInputDto { QuestionId = s.Rating.Id, Number = 5 },
        ],
    };

    [Fact]
    public async Task Without_a_key_the_design_is_withheld_and_a_password_is_requested()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await SeedProtectedAsync(h);
        h.User.ActAsAnonymous();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.PasswordRequired, session.Eligibility);
        Assert.Null(session.Survey);
        Assert.Equal("Customer feedback", session.SurveyTitle);
        Assert.False(session.CanRespond);
    }

    [Fact]
    public async Task The_right_password_gives_a_key_that_opens_and_submits_the_survey()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await SeedProtectedAsync(h);
        h.User.ActAsAnonymous();

        var unlock = await h.Service.UnlockAsync($" {s.Definition.Slug!.ToUpperInvariant()} ", Password);
        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!, unlock.AccessKey);
        var result = await h.Service.SubmitAsync(s.Definition.Id, Answers(s, unlock.AccessKey));

        Assert.Equal(h.Now.Add(h.AccessKeys.Lifetime), unlock.ExpiresAt);
        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
        Assert.True(session.Survey!.PasswordProtected);
        Assert.NotEqual(Guid.Empty, result.ResponseId);
    }

    [Fact]
    public async Task A_wrong_password_is_rejected()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await SeedProtectedAsync(h);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.UnlockAsync(s.Definition.Slug!, "guess"));

        Assert.Equal(["Password"], ex.Errors.Keys);
        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.UnlockAsync("no-such-survey", Password));
    }

    [Fact]
    public async Task Submissions_and_drafts_without_a_valid_key_are_refused()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await SeedProtectedAsync(h);

        h.User.ActAsAnonymous();
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.SubmitAsync(s.Definition.Id, Answers(s, null)));
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.SubmitAsync(s.Definition.Id, Answers(s, "forged-key")));

        h.User.ActAsRespondent();
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.SaveDraftAsync(s.Definition.Id, Answers(s, null)));
        var key = (await h.Service.UnlockAsync(s.Definition.Slug!, Password)).AccessKey;
        Assert.NotEqual(Guid.Empty, await h.Service.SaveDraftAsync(s.Definition.Id, Answers(s, key)));
        Assert.DoesNotContain(await h.ResponsesOfAsync(s), r => r.Status == ResponseStatus.Completed);
    }

    [Fact]
    public async Task Other_reasons_win_over_the_password_prompt()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await SeedProtectedAsync(h, survey => survey.Status = SurveyStatus.Closed);

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.Closed, session.Eligibility);
    }

    [Fact]
    public async Task Protected_surveys_are_not_listed_and_their_thank_you_page_is_generic()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var open = await h.SeedSurveyAsync(title: "Open survey");
        var protectedSurvey = await SeedProtectedAsync(h);
        h.User.ActAsRespondent();

        var available = await h.Service.ListAvailableAsync();

        Assert.Equal([open.Definition.Id], available.Select(a => a.SurveyId));
        Assert.Null(await h.Service.GetCompletionAsync(protectedSurvey.Definition.Slug!));
    }

    [Fact]
    public async Task Unlocking_an_unprotected_survey_is_harmless()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();

        var result = await h.Service.UnlockAsync(s.Definition.Slug!, "anything");

        Assert.Equal(string.Empty, result.AccessKey);
    }
}
