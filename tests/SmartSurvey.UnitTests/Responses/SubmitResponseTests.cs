using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;
using static SmartSurvey.UnitTests.Responses.ResponseTestData;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Tests of <see cref="ResponseService.SubmitAsync"/>.</summary>
public class SubmitResponseTests
{
    [Fact]
    public async Task Submission_stores_every_answer_type_in_its_column()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        QuestionDto? purchaseDate = null;
        var s = await h.SeedSurveyAsync(design: x => purchaseDate = AddDateQuestion(x));
        var date = purchaseDate!;
        h.User.ActAsRespondent();
        var request = ValidRequest(s);
        request.Answers.Add(Date(date, new DateOnly(2025, 12, 24)));
        request.UserAgent = "UnitTest/1.0";

        var result = await h.Service.SubmitAsync(s.Definition.Id, request);

        Assert.Equal("Thanks!", result.ThankYouMessage);
        var response = await h.FindResponseAsync(result.ResponseId);
        Assert.NotNull(response);
        Assert.Equal(ResponseStatus.Completed, response.Status);
        Assert.Equal(TestCurrentUser.RespondentId, response.RespondentId);
        Assert.Equal(h.Now, response.StartedAt);
        Assert.Equal(h.Now, response.SubmittedAt);
        Assert.Equal("UnitTest/1.0", response.UserAgent);
        Assert.Equal(7, response.Answers.Count);

        var rating = AnswerTo(response, s.Rating);
        Assert.Equal(4, rating.NumberValue);
        Assert.Null(rating.TextValue);
        Assert.Empty(rating.Selections);
        Assert.Equal(42, AnswerTo(response, s.Age).NumberValue);
        Assert.Equal("jane@example.com", AnswerTo(response, s.Email).TextValue);
        Assert.Equal(new DateOnly(2025, 12, 24), AnswerTo(response, date).DateValue);

        var enjoy = Assert.Single(AnswerTo(response, s.Enjoy).Selections);
        Assert.Equal(s.Yes.Id, enjoy.OptionId);
        Assert.Null(enjoy.FreeText);

        var features = AnswerTo(response, s.Features).Selections.ToDictionary(x => x.OptionId, x => x.FreeText);
        Assert.Equal(2, features.Count);
        Assert.Null(features[s.Reports.Id]);
        Assert.Equal("Exports", features[s.OtherFeature.Id]);

        var region = Assert.Single(AnswerTo(response, s.Region).Selections);
        Assert.Equal(s.OtherRegion.Id, region.OptionId);
        Assert.Equal("Oceania", region.FreeText);
    }

    [Fact]
    public async Task Answers_to_questions_hidden_by_logic_are_discarded()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();
        var request = ValidRequest(s); // Q1 = Yes → "Why not?" is hidden
        request.Answers.Add(Text(s.WhyNot, "Stale answer from an earlier branch"));

        var result = await h.Service.SubmitAsync(s.Definition.Id, request);

        var response = await h.FindResponseAsync(result.ResponseId);
        Assert.DoesNotContain(response!.Answers, a => a.QuestionId == s.WhyNot.Id);
        Assert.Equal(6, response.Answers.Count);
    }

    [Fact]
    public async Task Answers_to_questions_shown_by_logic_are_kept()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();

        var result = await h.Service.SubmitAsync(s.Definition.Id, Request(
            Choice(s.Enjoy, s.No),
            Text(s.WhyNot, "Too slow"),
            Number(s.Rating, 2)));

        var response = await h.FindResponseAsync(result.ResponseId);
        Assert.Equal("Too slow", AnswerTo(response!, s.WhyNot).TextValue);
    }

    [Fact]
    public async Task Validation_errors_are_keyed_by_question_id()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsRespondent();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.SubmitAsync(
            s.Definition.Id,
            Request(Text(s.Email, "not-an-email"), Number(s.Age, 130))));

        Assert.Equal(
            new[] { s.Enjoy.Id, s.Rating.Id, s.Email.Id, s.Age.Id }.Select(id => id.ToString()).Order().ToArray(),
            ex.Errors.Keys.Order().ToArray());
        Assert.Equal(["This question is required."], ex.Errors[s.Enjoy.Id.ToString()]);
        Assert.Equal(["This question is required."], ex.Errors[s.Rating.Id.ToString()]);
        Assert.Equal(["Please enter a valid e-mail address."], ex.Errors[s.Email.Id.ToString()]);
        Assert.Equal(["The value must be at most 120."], ex.Errors[s.Age.Id.ToString()]);

        Assert.Empty(await h.ResponsesOfAsync(s));
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Question_shown_by_logic_becomes_mandatory()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.SubmitAsync(
            s.Definition.Id,
            Request(Choice(s.Enjoy, s.No), Number(s.Rating, 3))));

        var error = Assert.Single(ex.Errors);
        Assert.Equal(s.WhyNot.Id.ToString(), error.Key);
    }

    [Fact]
    public async Task Invalid_option_is_sanitised_away_and_then_reported_as_missing()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();
        var request = ValidRequest(s);
        request.Answers[0] = new AnswerInputDto
        {
            QuestionId = s.Enjoy.Id,
            Selections = [new SelectionInputDto { OptionId = Guid.NewGuid() }],
        };

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.SubmitAsync(s.Definition.Id, request));

        Assert.Equal(["This question is required."], ex.Errors[s.Enjoy.Id.ToString()]);
    }

    [Fact]
    public async Task Submitting_completes_the_draft_and_keeps_its_start_time()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var startedAt = h.Now.AddHours(-1);
        var draft = await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.InProgress, r =>
        {
            r.StartedAt = startedAt;
            r.CurrentSectionIndex = 1;
            r.Answers.Add(StoredChoice(s.Enjoy, (s.No, null)));
            r.Answers.Add(StoredText(s.WhyNot, "Changed my mind"));
            r.Answers.Add(StoredText(s.Email, "old@example.com"));
            r.Answers.Add(StoredChoice(s.Features, (s.Logic, null), (s.OtherFeature, "Old")));
        });
        h.User.ActAsRespondent();

        var result = await h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s, draft.Id));

        Assert.Equal(draft.Id, result.ResponseId);
        var response = Assert.Single(await h.ResponsesOfAsync(s));
        Assert.Equal(ResponseStatus.Completed, response.Status);
        Assert.Equal(startedAt, response.StartedAt);
        Assert.Equal(h.Now, response.SubmittedAt);
        Assert.DoesNotContain(response.Answers, a => a.QuestionId == s.WhyNot.Id); // hidden now → removed
        Assert.Equal("jane@example.com", AnswerTo(response, s.Email).TextValue);
        Assert.Equal(s.Yes.Id, Assert.Single(AnswerTo(response, s.Enjoy).Selections).OptionId);

        var features = AnswerTo(response, s.Features).Selections.ToDictionary(x => x.OptionId, x => x.FreeText);
        Assert.Equal(2, features.Count);
        Assert.Null(features[s.Reports.Id]);
        Assert.Equal("Exports", features[s.OtherFeature.Id]);
    }

    [Fact]
    public async Task Submitting_without_an_id_completes_the_latest_draft()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var draft = await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.InProgress);
        h.User.ActAsRespondent();

        var result = await h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s));

        Assert.Equal(draft.Id, result.ResponseId);
        Assert.Equal(ResponseStatus.Completed, Assert.Single(await h.ResponsesOfAsync(s)).Status);
    }

    [Fact]
    public async Task Guests_submit_anonymously()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();

        var result = await h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s));

        var response = await h.FindResponseAsync(result.ResponseId);
        Assert.NotNull(response);
        Assert.Null(response.RespondentId);
        Assert.Equal(ResponseStatus.Completed, response.Status);
        Assert.Equal(h.Now, response.StartedAt);
    }

    [Fact]
    public async Task Draft_id_sent_by_a_guest_is_ignored()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var draft = await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.InProgress);
        h.User.ActAsAnonymous();

        var result = await h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s, draft.Id));

        Assert.NotEqual(draft.Id, result.ResponseId);
        var untouched = await h.FindResponseAsync(draft.Id);
        Assert.Equal(ResponseStatus.InProgress, untouched!.Status);
        Assert.Empty(untouched.Answers);
    }

    [Fact]
    public async Task Draft_of_another_user_cannot_be_submitted()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var foreign = await h.SeedResponseAsync(s, ResponseTestHarness.OtherUserId, ResponseStatus.InProgress);
        h.User.ActAsRespondent();

        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s, foreign.Id)));

        Assert.Equal(ResponseStatus.InProgress, (await h.FindResponseAsync(foreign.Id))!.Status);
    }

    [Fact]
    public async Task Guests_cannot_submit_to_members_only_surveys()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.AllowAnonymous = false);
        h.User.ActAsAnonymous();

        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s)));

        Assert.Equal(SurveyEligibilityChecker.LoginRequiredMessage, ex.Message);
    }

    [Fact]
    public async Task Quota_is_enforced_once_reached()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.MaxResponses = 1);
        h.User.ActAsAnonymous();

        await h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s)));
        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibilityChecker.QuotaReachedMessage, ex.Message);
        Assert.Equal(SurveyEligibility.QuotaReached, session.Eligibility);
        Assert.Single(await h.ResponsesOfAsync(s));
    }

    [Fact]
    public async Task Single_response_survey_rejects_a_second_submission_from_the_same_user()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsRespondent();

        await h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s)));

        Assert.Equal(SurveyEligibilityChecker.AlreadyRespondedMessage, ex.Message);
    }

    [Fact]
    public async Task Multiple_submissions_are_accepted_when_allowed()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.AllowMultipleResponses = true);
        h.User.ActAsRespondent();

        var first = await h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s));
        var second = await h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s));

        Assert.NotEqual(first.ResponseId, second.ResponseId);
        Assert.All(await h.ResponsesOfAsync(s), r => Assert.Equal(ResponseStatus.Completed, r.Status));
    }

    [Fact]
    public async Task Closed_survey_rejects_submissions()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.ClosesAt = h.Now.AddMinutes(-5));
        h.User.ActAsAnonymous();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s)));

        Assert.StartsWith("This survey closed on", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Survey_that_is_not_open_yet_rejects_submissions()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.OpensAt = h.Now.AddDays(2));
        h.User.ActAsAnonymous();

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s)));

        Assert.StartsWith("This survey opens on", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_survey_is_not_found()
    {
        await using var h = await ResponseTestHarness.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.SubmitAsync(Guid.NewGuid(), Request()));
    }

    [Fact]
    public async Task Duplicate_answers_use_the_last_value_and_unknown_questions_are_ignored()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();
        var request = ValidRequest(s);
        request.Answers.Insert(0, Number(s.Rating, 1));
        request.Answers.Add(Number(s.Rating, 5));
        request.Answers.Add(Text(new() { Type = QuestionType.ShortText }, "Question from another survey"));

        var result = await h.Service.SubmitAsync(s.Definition.Id, request);

        var response = await h.FindResponseAsync(result.ResponseId);
        Assert.Equal(5, AnswerTo(response!, s.Rating).NumberValue);
        Assert.Equal(6, response!.Answers.Count);
    }

    [Fact]
    public async Task Submission_is_audited()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsRespondent();

        var result = await h.Service.SubmitAsync(s.Definition.Id, ValidRequest(s));

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(AuditActions.ResponseSubmitted, entry.Action);
        Assert.Equal("Response", entry.EntityType);
        Assert.Equal(result.ResponseId.ToString(), entry.EntityId);
        Assert.Contains("Customer feedback", entry.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_payload_is_rejected()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.SubmitAsync(
            s.Definition.Id,
            new SaveResponseRequest { Answers = [null!] }));

        Assert.Contains("Answers[0]", ex.Errors.Keys);
    }
}
