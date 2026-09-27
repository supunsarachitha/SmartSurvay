using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;
using static SmartSurvey.UnitTests.Responses.ResponseTestData;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>
/// Admin use cases of <see cref="ResponseService"/>: browsing (filters, search, paging), response
/// detail (incl. ownership rules and display formatting) and deletion.
/// </summary>
public class ResponseAdminTests
{
    private static DateTime Utc(int day, int hour, int minute, int second = 0) =>
        new(2026, 1, day, hour, minute, second, DateTimeKind.Utc);

    /// <summary>
    /// Three responses: an anonymous one completed on 10 Jan, Rita's completed just before midnight
    /// on 12 Jan and the other user's draft started on 13 Jan.
    /// </summary>
    private static async Task<BrowseData> SeedBrowseDataAsync(ResponseTestHarness h)
    {
        var s = await h.SeedSurveyAsync(x => x.AllowMultipleResponses = true);
        var anonymous = await h.SeedResponseAsync(s, null, ResponseStatus.Completed, r =>
        {
            r.StartedAt = Utc(10, 10, 0);
            r.SubmittedAt = Utc(10, 10, 5);
            r.Answers.Add(StoredNumber(s.Rating, 5));
        });
        var rita = await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.Completed, r =>
        {
            r.StartedAt = Utc(12, 23, 50);
            r.SubmittedAt = Utc(12, 23, 59, 30);
            r.Answers.Add(StoredChoice(s.Enjoy, (s.Yes, null)));
            r.Answers.Add(StoredNumber(s.Rating, 4));
            r.Answers.Add(StoredText(s.Email, "rita@example.com"));
        });
        var otherDraft = await h.SeedResponseAsync(s, ResponseTestHarness.OtherUserId, ResponseStatus.InProgress, r =>
            r.StartedAt = Utc(13, 8, 0));
        return new BrowseData(s, anonymous, rita, otherDraft);
    }

    private static Guid[] Ids(PagedResult<ResponseSummaryDto> page) => page.Items.Select(x => x.Id).ToArray();

    // ---------------------------------------------------------------- ListForSurveyAsync

    [Fact]
    public async Task Responses_are_listed_newest_first_with_names_counts_and_durations()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var data = await SeedBrowseDataAsync(h);

        var page = await h.Service.ListForSurveyAsync(data.Survey.Definition.Id, new ResponseQuery());

        Assert.Equal(3, page.TotalCount);
        Assert.Equal([data.OtherDraft.Id, data.Rita.Id, data.Anonymous.Id], Ids(page));

        var draft = page.Items[0];
        Assert.Equal(ResponseTestHarness.OtherEmail, draft.RespondentName); // no display name → e-mail
        Assert.Equal(ResponseTestHarness.OtherEmail, draft.RespondentEmail);
        Assert.Equal(ResponseStatus.InProgress, draft.Status);
        Assert.Equal(0, draft.AnswerCount);
        Assert.Null(draft.DurationSeconds);

        var rita = page.Items[1];
        Assert.Equal(ResponseTestHarness.RespondentName, rita.RespondentName);
        Assert.Equal(ResponseTestHarness.RespondentEmail, rita.RespondentEmail);
        Assert.Equal(TestCurrentUser.RespondentId, rita.RespondentId);
        Assert.Equal(data.Survey.Definition.Id, rita.SurveyId);
        Assert.Equal(3, rita.AnswerCount);
        Assert.Equal(570, rita.DurationSeconds);
        Assert.Equal(Utc(12, 23, 59, 30), rita.SubmittedAt);

        var anonymous = page.Items[2];
        Assert.Equal("Anonymous", anonymous.RespondentName);
        Assert.Null(anonymous.RespondentEmail);
        Assert.Null(anonymous.RespondentId);
        Assert.Equal(300, anonymous.DurationSeconds);
    }

    [Theory]
    [InlineData(ResponseStatus.Completed, 2)]
    [InlineData(ResponseStatus.InProgress, 1)]
    public async Task Responses_can_be_filtered_by_status(ResponseStatus status, int expected)
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var data = await SeedBrowseDataAsync(h);

        var page = await h.Service.ListForSurveyAsync(data.Survey.Definition.Id, new ResponseQuery { Status = status });

        Assert.Equal(expected, page.TotalCount);
        Assert.All(page.Items, x => Assert.Equal(status, x.Status));
    }

    [Fact]
    public async Task Date_range_is_inclusive_and_uses_submission_or_start_time()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var data = await SeedBrowseDataAsync(h);
        var surveyId = data.Survey.Definition.Id;

        var onTwelfth = await h.Service.ListForSurveyAsync(surveyId, new ResponseQuery { From = new(2026, 1, 12), To = new(2026, 1, 12) });
        var fromThirteenth = await h.Service.ListForSurveyAsync(surveyId, new ResponseQuery { From = new(2026, 1, 13) });
        var untilTenth = await h.Service.ListForSurveyAsync(surveyId, new ResponseQuery { To = new(2026, 1, 10) });
        var eleventh = await h.Service.ListForSurveyAsync(surveyId, new ResponseQuery { From = new(2026, 1, 11), To = new(2026, 1, 11) });

        Assert.Equal([data.Rita.Id], Ids(onTwelfth));          // submitted 23:59:30 still counts
        Assert.Equal([data.OtherDraft.Id], Ids(fromThirteenth)); // draft: start time is used
        Assert.Equal([data.Anonymous.Id], Ids(untilTenth));
        Assert.Empty(eleventh.Items);
    }

    [Theory]
    [InlineData("RITA", 1)]
    [InlineData("  rita  ", 1)]
    [InlineData("OTHER@TEST", 1)]
    [InlineData("test.local", 2)]
    [InlineData("nobody", 0)]
    public async Task Search_matches_email_or_display_name_ignoring_case(string search, int expected)
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var data = await SeedBrowseDataAsync(h);

        var page = await h.Service.ListForSurveyAsync(data.Survey.Definition.Id, new ResponseQuery { Search = search });

        Assert.Equal(expected, page.TotalCount);
        Assert.DoesNotContain(page.Items, x => x.RespondentId is null); // anonymous responses never match
    }

    [Fact]
    public async Task Responses_are_paged()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var submittedAt = Utc(10 + i, 9, 0);
            var response = await h.SeedResponseAsync(s, null, ResponseStatus.Completed, r =>
            {
                r.StartedAt = submittedAt.AddMinutes(-3);
                r.SubmittedAt = submittedAt;
            });
            ids.Add(response.Id);
        }

        ids.Reverse(); // newest first
        var second = await h.Service.ListForSurveyAsync(s.Definition.Id, new ResponseQuery { Page = 2, PageSize = 2 });
        var third = await h.Service.ListForSurveyAsync(s.Definition.Id, new ResponseQuery { Page = 3, PageSize = 2 });

        Assert.Equal(5, second.TotalCount);
        Assert.Equal(3, second.TotalPages);
        Assert.Equal(2, second.Page);
        Assert.Equal(2, second.PageSize);
        Assert.True(second.HasNextPage);
        Assert.Equal(ids.Skip(2).Take(2).ToArray(), Ids(second));
        Assert.Equal([ids[4]], Ids(third));
        Assert.False(third.HasNextPage);
    }

    [Fact]
    public async Task Only_responses_of_the_requested_survey_are_listed()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var data = await SeedBrowseDataAsync(h);
        var other = await h.SeedSurveyAsync(title: "Another survey");
        var foreign = await h.SeedResponseAsync(other, null, ResponseStatus.Completed);

        var page = await h.Service.ListForSurveyAsync(data.Survey.Definition.Id, new ResponseQuery());
        var otherPage = await h.Service.ListForSurveyAsync(other.Definition.Id, new ResponseQuery());

        Assert.DoesNotContain(foreign.Id, Ids(page));
        Assert.Equal([foreign.Id], Ids(otherPage));
    }

    [Fact]
    public async Task Non_admins_cannot_browse_responses()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();

        h.User.ActAsRespondent();
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.ListForSurveyAsync(s.Definition.Id, new ResponseQuery()));
        h.User.ActAsAnonymous();
        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.ListForSurveyAsync(s.Definition.Id, new ResponseQuery()));
    }

    [Fact]
    public async Task Browsing_a_missing_survey_is_not_found()
    {
        await using var h = await ResponseTestHarness.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.ListForSurveyAsync(Guid.NewGuid(), new ResponseQuery()));
    }

    [Fact]
    public async Task Inverted_date_range_is_rejected()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => h.Service.ListForSurveyAsync(
            s.Definition.Id, new ResponseQuery { From = new(2026, 1, 12), To = new(2026, 1, 11) }));

        Assert.Equal(["The end date must be on or after the start date."], ex.Errors["To"]);
    }

    // ---------------------------------------------------------------- GetAsync

    [Fact]
    public async Task Detail_lists_every_question_in_display_order_with_formatted_answers()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        QuestionDto? purchaseDate = null;
        var s = await h.SeedSurveyAsync(design: x => purchaseDate = AddDateQuestion(x));
        var response = await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.Completed, r =>
        {
            r.UserAgent = "UnitTest/1.0";
            r.Answers.Add(StoredDate(purchaseDate!, new DateOnly(2025, 12, 24)));
            r.Answers.Add(StoredChoice(s.Region, (s.OtherRegion, "Oceania")));
            r.Answers.Add(StoredNumber(s.Age, 42));
            r.Answers.Add(StoredNumber(s.Rating, 4));
            r.Answers.Add(StoredChoice(s.Features, (s.OtherFeature, "Exports"), (s.Reports, null)));
            r.Answers.Add(StoredChoice(s.Enjoy, (s.Yes, null)));
        });

        var detail = await h.Service.GetAsync(response.Id);

        Assert.Equal(response.Id, detail.Id);
        Assert.Equal(s.Definition.Id, detail.SurveyId);
        Assert.Equal("Customer feedback", detail.SurveyTitle);
        Assert.Equal(TestCurrentUser.RespondentId, detail.RespondentId);
        Assert.Equal(ResponseTestHarness.RespondentName, detail.RespondentName);
        Assert.Equal(ResponseTestHarness.RespondentEmail, detail.RespondentEmail);
        Assert.Equal(ResponseStatus.Completed, detail.Status);
        Assert.Equal(response.StartedAt, detail.StartedAt);
        Assert.Equal(response.SubmittedAt, detail.SubmittedAt);
        Assert.Equal("UnitTest/1.0", detail.UserAgent);

        Assert.Equal(["Q1", "Q2", "Q3", "Q4", "Q5", "Q6", "Q7", "Q8"], detail.Answers.Select(a => a.QuestionCode).ToArray());
        Assert.Equal(
            ["Yes", "Reports; Other: Exports", "", "4 / 5", "", "42", "Other: Oceania", "2025-12-24"],
            detail.Answers.Select(a => a.DisplayValue).ToArray());
        Assert.Equal(
            [true, true, false, true, false, true, true, true],
            detail.Answers.Select(a => a.IsAnswered).ToArray());

        var first = detail.Answers[0];
        Assert.Equal(s.Enjoy.Id, first.QuestionId);
        Assert.Equal("Did you enjoy the product?", first.QuestionText);
        Assert.Equal(QuestionType.Radio, first.QuestionType);
        Assert.Equal("Experience", first.SectionTitle);
        Assert.Equal("About you", detail.Answers[3].SectionTitle);
    }

    [Fact]
    public async Task Anonymous_response_detail_names_the_respondent_anonymous()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var response = await h.SeedResponseAsync(s, null, ResponseStatus.Completed);

        var detail = await h.Service.GetAsync(response.Id);

        Assert.Equal("Anonymous", detail.RespondentName);
        Assert.Null(detail.RespondentEmail);
        Assert.Null(detail.RespondentId);
        Assert.All(detail.Answers, a => Assert.False(a.IsAnswered));
    }

    [Fact]
    public async Task Respondents_can_view_their_own_response()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var response = await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.InProgress,
            r => r.Answers.Add(StoredNumber(s.Rating, 3)));
        h.User.ActAsRespondent();

        var detail = await h.Service.GetAsync(response.Id);

        Assert.Equal(ResponseStatus.InProgress, detail.Status);
        Assert.Equal("3 / 5", detail.Answers.Single(a => a.QuestionId == s.Rating.Id).DisplayValue);
    }

    [Fact]
    public async Task Other_users_cannot_view_a_response()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var response = await h.SeedResponseAsync(s, ResponseTestHarness.OtherUserId, ResponseStatus.Completed);
        h.User.ActAsRespondent();

        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.GetAsync(response.Id));
    }

    [Fact]
    public async Task Guests_cannot_view_anonymous_responses()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var response = await h.SeedResponseAsync(s, null, ResponseStatus.Completed);
        h.User.ActAsAnonymous();

        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.GetAsync(response.Id));
    }

    [Fact]
    public async Task Unknown_response_is_not_found()
    {
        await using var h = await ResponseTestHarness.CreateAsync();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => h.Service.GetAsync(Guid.NewGuid()));

        Assert.Equal("Response", ex.EntityName);
    }

    // ---------------------------------------------------------------- DeleteAsync

    [Fact]
    public async Task Admin_deletes_a_response_with_its_answers_and_the_deletion_is_audited()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var response = await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.Completed, r =>
        {
            r.Answers.Add(StoredChoice(s.Features, (s.Reports, null), (s.OtherFeature, "x")));
            r.Answers.Add(StoredNumber(s.Rating, 4));
        });
        var kept = await h.SeedResponseAsync(s, null, ResponseStatus.Completed, r => r.Answers.Add(StoredNumber(s.Rating, 1)));

        await h.Service.DeleteAsync(response.Id);

        Assert.Null(await h.FindResponseAsync(response.Id));
        Assert.NotNull(await h.FindResponseAsync(kept.Id));
        await using (var db = h.Db.CreateContext())
        {
            Assert.Equal(1, await db.Answers.CountAsync());
            Assert.Equal(0, await db.AnswerSelections.CountAsync());
        }

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal(AuditActions.ResponseDeleted, entry.Action);
        Assert.Equal("Response", entry.EntityType);
        Assert.Equal(response.Id.ToString(), entry.EntityId);
    }

    [Fact]
    public async Task Non_admins_cannot_delete_responses()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        var own = await h.SeedResponseAsync(s, TestCurrentUser.RespondentId, ResponseStatus.Completed);
        h.User.ActAsRespondent();

        await Assert.ThrowsAsync<ForbiddenException>(() => h.Service.DeleteAsync(own.Id));

        Assert.NotNull(await h.FindResponseAsync(own.Id));
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Deleting_an_unknown_response_is_not_found()
    {
        await using var h = await ResponseTestHarness.CreateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.DeleteAsync(Guid.NewGuid()));

        Assert.Empty(h.Audit.Entries);
    }

    private sealed record BrowseData(SampleSurvey Survey, SurveyResponse Anonymous, SurveyResponse Rita, SurveyResponse OtherDraft);
}
