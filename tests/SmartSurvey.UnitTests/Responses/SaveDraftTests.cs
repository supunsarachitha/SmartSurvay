using SmartSurvey.Application.Common;
using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;
using static SmartSurvey.UnitTests.Responses.ResponseTestData;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Tests of <see cref="ResponseService.SaveDraftAsync"/>.</summary>
public class SaveDraftTests
{
    [Fact]
    public async Task Guests_cannot_save_drafts()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(
            () => h.Service.SaveDraftAsync(s.Definition.Id, Request(Number(s.Rating, 3))));

        Assert.Equal("Please log in to save your progress.", ex.Message);
        Assert.Empty(await h.ResponsesOfAsync(s));
    }

    [Fact]
    public async Task First_save_creates_a_draft_with_sanitised_answers()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsRespondent();
        var request = Request(
            Choice(s.Enjoy, s.Yes, s.No),                                           // radio: first valid option wins
            Text(s.Email, "  jane@example.com  "),                                   // trimmed
            ChoiceWithText(s.Features, (s.Reports, "not allowed"), (s.OtherFeature, " Exports ")),
            new AnswerInputDto { QuestionId = Guid.NewGuid(), Text = "unknown question" }, // ignored
            new AnswerInputDto { QuestionId = s.Age.Id, Text = "42" });              // wrong field → empty → dropped
        request.UserAgent = "Mozilla/5.0";

        var id = await h.Service.SaveDraftAsync(s.Definition.Id, request);

        var draft = await h.FindResponseAsync(id);
        Assert.NotNull(draft);
        Assert.Equal(ResponseStatus.InProgress, draft.Status);
        Assert.Equal(TestCurrentUser.RespondentId, draft.RespondentId);
        Assert.Equal(h.Now, draft.StartedAt);
        Assert.Equal(h.Now, draft.UpdatedAt);
        Assert.Null(draft.SubmittedAt);
        Assert.Equal("Mozilla/5.0", draft.UserAgent);
        Assert.Equal(3, draft.Answers.Count);

        Assert.Equal(s.Yes.Id, Assert.Single(AnswerTo(draft, s.Enjoy).Selections).OptionId);
        Assert.Equal("jane@example.com", AnswerTo(draft, s.Email).TextValue);
        var features = AnswerTo(draft, s.Features).Selections.ToDictionary(x => x.OptionId);
        Assert.Null(features[s.Reports.Id].FreeText);
        Assert.Equal("Exports", features[s.OtherFeature.Id].FreeText);

        Assert.Empty(h.Audit.Entries); // drafts are not audited
    }

    [Fact]
    public async Task Drafts_skip_required_and_format_validation()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsRespondent();

        var emptyId = await h.Service.SaveDraftAsync(s.Definition.Id, Request());
        var invalidId = await h.Service.SaveDraftAsync(
            s.Definition.Id,
            Request(Text(s.Email, "not-an-email"), Number(s.Age, 130)));

        Assert.Equal(emptyId, invalidId); // the second save continues the same draft
        var draft = await h.FindResponseAsync(invalidId);
        Assert.Equal("not-an-email", AnswerTo(draft!, s.Email).TextValue);
        Assert.Equal(130, AnswerTo(draft!, s.Age).NumberValue);
    }

    [Fact]
    public async Task Saving_again_replaces_answers_in_place()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsRespondent();
        var startedAt = h.Now;
        var id = await h.Service.SaveDraftAsync(s.Definition.Id, Request(
            Choice(s.Enjoy, s.Yes),
            ChoiceWithText(s.Features, (s.Reports, null), (s.OtherFeature, "First")),
            Text(s.Email, "old@example.com")));
        h.Db.Time.Advance(TimeSpan.FromMinutes(5));

        var second = Request(
            Choice(s.Enjoy, s.No),
            ChoiceWithText(s.Features, (s.Logic, null), (s.OtherFeature, "Second")),
            Number(s.Rating, 4));
        second.ResponseId = id;
        var secondId = await h.Service.SaveDraftAsync(s.Definition.Id, second);

        Assert.Equal(id, secondId);
        var draft = Assert.Single(await h.ResponsesOfAsync(s));
        Assert.Equal(startedAt, draft.StartedAt);
        Assert.Equal(h.Now, draft.UpdatedAt);
        Assert.Equal(3, draft.Answers.Count);
        Assert.DoesNotContain(draft.Answers, a => a.QuestionId == s.Email.Id); // omitted answer removed
        Assert.Equal(s.No.Id, Assert.Single(AnswerTo(draft, s.Enjoy).Selections).OptionId);
        Assert.Equal(4, AnswerTo(draft, s.Rating).NumberValue);

        var features = AnswerTo(draft, s.Features).Selections.ToDictionary(x => x.OptionId);
        Assert.Equal(2, features.Count);
        Assert.Contains(s.Logic.Id, features.Keys);
        Assert.DoesNotContain(s.Reports.Id, features.Keys); // deselected option removed
        Assert.Equal("Second", features[s.OtherFeature.Id].FreeText); // kept option updated in place
    }

    [Fact]
    public async Task Saving_without_an_id_continues_the_latest_draft()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var existing = await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.InProgress,
            r => r.Answers.Add(StoredNumber(s.Rating, 1)));
        h.User.ActAsRespondent();

        var id = await h.Service.SaveDraftAsync(s.Definition.Id, Request(Number(s.Rating, 5)));

        Assert.Equal(existing.Id, id);
        var draft = Assert.Single(await h.ResponsesOfAsync(s));
        Assert.Equal(existing.StartedAt, draft.StartedAt);
        Assert.Equal(5, AnswerTo(draft, s.Rating).NumberValue);
    }

    [Theory]
    [InlineData(-3, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(42, 1)]
    public async Task Page_index_is_clamped_to_the_survey_pages(int requested, int stored)
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsRespondent();
        var request = Request();
        request.CurrentSectionIndex = requested;

        var id = await h.Service.SaveDraftAsync(s.Definition.Id, request);

        Assert.Equal(stored, (await h.FindResponseAsync(id))!.CurrentSectionIndex);
    }

    [Fact]
    public async Task User_agent_is_truncated_and_kept_when_not_sent_again()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsRespondent();
        var first = Request();
        first.UserAgent = new string('a', 600);

        var id = await h.Service.SaveDraftAsync(s.Definition.Id, first);
        await h.Service.SaveDraftAsync(s.Definition.Id, Request());

        var draft = await h.FindResponseAsync(id);
        Assert.Equal(new string('a', ResponseService.MaxUserAgentLength), draft!.UserAgent);
    }

    [Fact]
    public async Task Draft_of_another_user_cannot_be_modified()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var foreign = await h.SeedResponseAsync(s, ResponseTestHarness.OtherUserId, ResponseStatus.InProgress,
            r => r.Answers.Add(StoredNumber(s.Rating, 1)));
        h.User.ActAsRespondent();
        var request = Request(Number(s.Rating, 5));
        request.ResponseId = foreign.Id;

        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.SaveDraftAsync(s.Definition.Id, request));

        Assert.Equal(1, AnswerTo((await h.FindResponseAsync(foreign.Id))!, s.Rating).NumberValue);
    }

    [Fact]
    public async Task Unknown_draft_id_is_not_found()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsRespondent();
        var request = Request();
        request.ResponseId = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.SaveDraftAsync(s.Definition.Id, request));
    }

    [Fact]
    public async Task Draft_of_another_survey_is_not_found()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var other = await h.SeedSurveyAsync(title: "Other survey");
        var draftElsewhere = await h.SeedResponseAsync(other, TestCurrentUser.RespondentId, ResponseStatus.InProgress);
        h.User.ActAsRespondent();
        var request = Request();
        request.ResponseId = draftElsewhere.Id;

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.SaveDraftAsync(s.Definition.Id, request));
    }

    [Fact]
    public async Task Submitted_response_can_no_longer_be_changed()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.AllowMultipleResponses = true);
        var submitted = await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.Completed);
        h.User.ActAsRespondent();
        var request = Request();
        request.ResponseId = submitted.Id;

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.SaveDraftAsync(s.Definition.Id, request));

        Assert.Contains("already been submitted", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_survey_is_not_found()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        h.User.ActAsRespondent();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => h.Service.SaveDraftAsync(Guid.NewGuid(), Request()));

        Assert.Equal("Survey", ex.EntityName);
    }

    [Fact]
    public async Task Closed_survey_rejects_drafts_with_a_friendly_message()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.Status = SurveyStatus.Closed);
        h.User.ActAsRespondent();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.SaveDraftAsync(s.Definition.Id, Request()));

        Assert.Equal(SurveyEligibilityChecker.ClosedMessage, ex.Message);
    }

    [Fact]
    public async Task User_who_already_completed_cannot_start_another_draft()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.Completed);
        h.User.ActAsRespondent();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.SaveDraftAsync(s.Definition.Id, Request()));

        Assert.Equal(SurveyEligibilityChecker.AlreadyRespondedMessage, ex.Message);
    }

    [Fact]
    public async Task Malformed_payload_is_rejected()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsRespondent();

        var ex = await Assert.ThrowsAsync<AppValidationException>(
            () => h.Service.SaveDraftAsync(s.Definition.Id, new SaveResponseRequest { Answers = null! }));

        Assert.Contains("Answers", ex.Errors.Keys);
        Assert.Empty(await h.ResponsesOfAsync(s));
    }
}
