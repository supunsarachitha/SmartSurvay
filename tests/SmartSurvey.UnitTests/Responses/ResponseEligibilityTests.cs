using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Eligibility matrix as seen through <see cref="ResponseService.StartOrResumeAsync"/>.</summary>
public class ResponseEligibilityTests
{
    [Fact]
    public async Task Published_open_survey_is_eligible_and_returns_the_design()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        h.User.ActAsAnonymous();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
        Assert.True(session.CanRespond);
        Assert.Null(session.Message);
        Assert.NotNull(session.Survey);
        Assert.Equal(s.Definition.Id, session.Survey.Id);
    }

    [Theory]
    [InlineData("does-not-exist")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Unknown_or_blank_slug_is_not_found(string slug)
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        await h.SeedSurveyAsync();

        var session = await h.Service.StartOrResumeAsync(slug);

        Assert.Equal(SurveyEligibility.NotFound, session.Eligibility);
        Assert.Equal(SurveyEligibilityChecker.NotFoundMessage, session.Message);
        Assert.Null(session.Survey);
    }

    [Theory]
    [InlineData(SurveyStatus.Draft, SurveyEligibilityChecker.DraftMessage)]
    [InlineData(SurveyStatus.Archived, SurveyEligibilityChecker.ArchivedMessage)]
    public async Task Unpublished_survey_cannot_be_answered(SurveyStatus status, string expectedMessage)
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.Status = status);

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.NotPublished, session.Eligibility);
        Assert.Equal(expectedMessage, session.Message);
        Assert.Null(session.Survey);
        Assert.False(session.CanRespond);
    }

    [Fact]
    public async Task Template_cannot_be_answered()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.IsTemplate = true);

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.NotPublished, session.Eligibility);
        Assert.Equal(SurveyEligibilityChecker.TemplateMessage, session.Message);
    }

    [Fact]
    public async Task Manually_closed_survey_is_closed()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.Status = SurveyStatus.Closed);

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.Closed, session.Eligibility);
        Assert.Equal(SurveyEligibilityChecker.ClosedMessage, session.Message);
        Assert.Null(session.Survey);
    }

    [Fact]
    public async Task Survey_past_its_closing_time_reports_when_it_closed()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.ClosesAt = new DateTime(2026, 1, 14, 18, 30, 0, DateTimeKind.Utc));

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.Closed, session.Eligibility);
        Assert.Equal("This survey closed on 14 Jan 2026 18:30 UTC.", session.Message);
    }

    [Fact]
    public async Task Survey_not_open_yet_reports_the_opening_time()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.OpensAt = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc));

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.NotOpenYet, session.Eligibility);
        Assert.Equal("This survey opens on 1 Mar 2026 10:00 UTC.", session.Message);
        Assert.Null(session.Survey);
    }

    [Fact]
    public async Task Survey_becomes_available_once_its_window_opens()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.OpensAt = h.Now.AddHours(1));

        var before = await h.Service.StartOrResumeAsync(s.Definition.Slug!);
        h.Db.Time.Advance(TimeSpan.FromHours(1));
        var after = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.NotOpenYet, before.Eligibility);
        Assert.Equal(SurveyEligibility.Eligible, after.Eligibility);
    }

    [Fact]
    public async Task Reached_quota_blocks_new_respondents()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.MaxResponses = 2);
        await h.SeedResponseAsync(s, null, ResponseStatus.Completed);
        await h.SeedResponseAsync(s, ResponseTestHarness.OtherUserId, ResponseStatus.Completed);
        h.User.ActAsRespondent();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.QuotaReached, session.Eligibility);
        Assert.Equal(SurveyEligibilityChecker.QuotaReachedMessage, session.Message);
        Assert.Null(session.Survey);
    }

    [Fact]
    public async Task Drafts_do_not_count_towards_the_quota()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.MaxResponses = 2);
        await h.SeedResponseAsync(s, null, ResponseStatus.Completed);
        await h.SeedResponseAsync(s, ResponseTestHarness.OtherUserId, ResponseStatus.InProgress);
        await h.SeedResponseAsync(s, TestCurrentUser.AdminId, ResponseStatus.InProgress);
        h.User.ActAsAnonymous();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
    }

    [Fact]
    public async Task Guest_must_log_in_when_anonymous_responses_are_disabled()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.AllowAnonymous = false);
        h.User.ActAsAnonymous();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.LoginRequired, session.Eligibility);
        Assert.Equal(SurveyEligibilityChecker.LoginRequiredMessage, session.Message);
        Assert.Null(session.Survey);
    }

    [Fact]
    public async Task Logged_in_user_may_answer_a_members_only_survey()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.AllowAnonymous = false);
        h.User.ActAsRespondent();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
    }

    [Fact]
    public async Task User_who_completed_a_single_response_survey_cannot_respond_again()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.Completed);
        h.User.ActAsRespondent();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.AlreadyResponded, session.Eligibility);
        Assert.Equal(SurveyEligibilityChecker.AlreadyRespondedMessage, session.Message);
        Assert.Null(session.Survey);
    }

    [Fact]
    public async Task User_may_respond_again_when_multiple_responses_are_allowed()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.AllowMultipleResponses = true);
        await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.Completed);
        h.User.ActAsRespondent();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
    }

    [Fact]
    public async Task Completions_by_other_people_do_not_block_the_user()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        await h.SeedResponseAsync(s, ResponseTestHarness.OtherUserId, ResponseStatus.Completed);
        await h.SeedResponseAsync(s, null, ResponseStatus.Completed);
        h.User.ActAsRespondent();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
    }

    [Fact]
    public async Task Guests_are_never_treated_as_having_already_responded()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        await h.SeedResponseAsync(s, null, ResponseStatus.Completed);
        h.User.ActAsAnonymous();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
    }

    [Fact]
    public async Task Schedule_is_checked_before_quota_and_login()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x =>
        {
            x.Status = SurveyStatus.Closed;
            x.MaxResponses = 1;
            x.AllowAnonymous = false;
        });
        await h.SeedResponseAsync(s, null, ResponseStatus.Completed);
        h.User.ActAsAnonymous();

        var session = await h.Service.StartOrResumeAsync(s.Definition.Slug!);

        Assert.Equal(SurveyEligibility.Closed, session.Eligibility);
    }
}
