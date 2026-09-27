using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;
using static SmartSurvey.UnitTests.Responses.ResponseTestData;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Tests of <see cref="ResponseService.StartOrResumeAsync"/> (design + draft resume).</summary>
public class StartOrResumeTests
{
    [Fact]
    public async Task Slug_lookup_ignores_case_and_surrounding_spaces()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();

        var session = await h.Service.StartOrResumeAsync($"  {s.Definition.Slug!.ToUpperInvariant()} ");

        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
        Assert.Equal(s.Definition.Id, session.Survey?.Id);
    }

    [Fact]
    public async Task Session_contains_the_complete_design_in_display_order()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();

        var survey = (await h.Service.StartOrResumeAsync(s.Definition.Slug!)).Survey;

        Assert.NotNull(survey);
        Assert.Equal("Customer feedback", survey.Title);
        Assert.Equal("Thanks!", survey.ThankYouMessage);
        Assert.Equal([s.Page1.Id, s.Page2.Id], survey.Sections.Select(x => x.Id).ToArray());
        Assert.Equal(
            [s.Enjoy.Id, s.Features.Id, s.WhyNot.Id, s.Rating.Id, s.Email.Id, s.Age.Id, s.Region.Id],
            survey.AllQuestions().Select(q => q.Id).ToArray());
        Assert.True(survey.FindQuestion(s.Features.Id)!.Options.Single(o => o.Id == s.OtherFeature.Id).AllowsFreeText);
        Assert.Equal(120, survey.FindQuestion(s.Age.Id)!.Settings.MaxValue);

        var rule = Assert.Single(survey.LogicRules);
        Assert.Equal(s.WhyNot.Id, rule.TargetQuestionId);
        Assert.Equal(s.No.Id, Assert.Single(rule.Conditions).OptionId);
    }

    [Fact]
    public async Task Latest_draft_is_resumed_with_answers_selections_and_free_text()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.InProgress, r =>
        {
            r.UpdatedAt = h.Now.AddHours(-2);
            r.Answers.Add(StoredChoice(s.Enjoy, (s.Yes, null)));
        });
        var latest = await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.InProgress, r =>
        {
            r.UpdatedAt = h.Now.AddMinutes(-10);
            r.CurrentSectionIndex = 1;
            r.Answers.Add(StoredNumber(s.Rating, 4));
            r.Answers.Add(StoredText(s.WhyNot, "Too slow"));
            r.Answers.Add(StoredChoice(s.Features, (s.Reports, null), (s.OtherFeature, "Exports")));
            r.Answers.Add(StoredChoice(s.Enjoy, (s.No, null)));
        });
        h.User.ActAsRespondent();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(latest.Id, session.DraftResponseId);
        Assert.Equal(1, session.CurrentSectionIndex);
        Assert.Equal([s.Enjoy.Id, s.Features.Id, s.WhyNot.Id, s.Rating.Id], session.Answers.Select(a => a.QuestionId).ToArray());

        var byQuestion = session.Answers.ToDictionary(a => a.QuestionId);
        Assert.True(byQuestion[s.Enjoy.Id].IsSelected(s.No.Id));
        Assert.Equal("Too slow", byQuestion[s.WhyNot.Id].Text);
        Assert.Equal(4, byQuestion[s.Rating.Id].Number);
        Assert.True(byQuestion[s.Features.Id].IsSelected(s.Reports.Id));
        Assert.Equal("Exports", byQuestion[s.Features.Id].FreeTextFor(s.OtherFeature.Id));
    }

    [Fact]
    public async Task Saved_page_index_is_clamped_to_the_existing_pages()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.InProgress, r => r.CurrentSectionIndex = 7);
        h.User.ActAsRespondent();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(1, session.CurrentSectionIndex);
    }

    [Fact]
    public async Task Guests_always_start_without_a_draft()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.InProgress,
            r => r.Answers.Add(StoredNumber(s.Rating, 2)));
        h.User.ActAsAnonymous();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.True(session.CanRespond);
        Assert.Null(session.DraftResponseId);
        Assert.Equal(0, session.CurrentSectionIndex);
        Assert.Empty(session.Answers);
    }

    [Fact]
    public async Task Drafts_of_other_users_and_other_surveys_are_not_resumed()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var otherSurvey = await h.SeedSurveyAsync(title: "Other survey");
        await h.SeedResponseAsync(s, ResponseTestHarness.OtherUserId, ResponseStatus.InProgress);
        await h.SeedResponseAsync(otherSurvey, TestCurrentUser.RespondentId, ResponseStatus.InProgress);
        h.User.ActAsRespondent();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.True(session.CanRespond);
        Assert.Null(session.DraftResponseId);
    }

    [Fact]
    public async Task Completed_responses_are_never_offered_as_drafts()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.AllowMultipleResponses = true);
        await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.Completed);
        h.User.ActAsRespondent();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.True(session.CanRespond);
        Assert.Null(session.DraftResponseId);
    }

    [Fact]
    public async Task Ineligible_session_exposes_neither_design_nor_draft()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.Status = SurveyStatus.Closed);
        await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.InProgress);
        h.User.ActAsRespondent();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.False(session.CanRespond);
        Assert.Null(session.Survey);
        Assert.Null(session.DraftResponseId);
        Assert.Empty(session.Answers);
    }
}
