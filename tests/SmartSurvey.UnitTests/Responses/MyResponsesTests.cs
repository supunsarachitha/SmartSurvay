using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Tests of <see cref="ResponseService.ListMineAsync"/>.</summary>
public class MyResponsesTests
{
    [Fact]
    public async Task Guests_get_an_empty_list()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        await h.SeedResponseAsync(s, null, ResponseStatus.Completed);
        h.User.ActAsAnonymous();

        Assert.Empty(await h.Service.ListMineAsync());
    }

    [Fact]
    public async Task Own_responses_are_listed_newest_first_with_a_continue_flag()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var open = await h.SeedSurveyAsync(x => x.AllowMultipleResponses = true, title: "Open survey");
        var closed = await h.SeedSurveyAsync(title: "Closed survey");
        var completed = await h.SeedResponseAsync(open, TestCurrentUser.RespondentId, ResponseStatus.Completed, r =>
        {
            r.StartedAt = h.Now.AddDays(-1).AddMinutes(-10);
            r.SubmittedAt = h.Now.AddDays(-1);
        });
        var openDraft = await h.SeedResponseAsync(open, TestCurrentUser.RespondentId, ResponseStatus.InProgress, r =>
        {
            r.StartedAt = h.Now.AddDays(-3);
            r.UpdatedAt = h.Now.AddHours(-1); // saved recently → newest
        });
        var closedDraft = await h.SeedResponseAsync(closed, TestCurrentUser.RespondentId, ResponseStatus.InProgress, r =>
            r.StartedAt = h.Now.AddDays(-2));
        await h.SeedResponseAsync(open, ResponseTestHarness.OtherUserId, ResponseStatus.InProgress);

        // Close the second survey after the draft was started.
        await using (var db = h.Db.CreateContext())
        {
            var survey = await db.Surveys.FindAsync(closed.Definition.Id);
            survey!.Status = SurveyStatus.Closed;
            await db.SaveChangesAsync();
        }

        h.User.ActAsRespondent();

        var mine = await h.Service.ListMineAsync();

        Assert.Equal([openDraft.Id, completed.Id, closedDraft.Id], mine.Select(x => x.ResponseId).ToArray());

        Assert.True(mine[0].CanContinue);
        Assert.Equal(ResponseStatus.InProgress, mine[0].Status);
        Assert.Equal("Open survey", mine[0].SurveyTitle);
        Assert.Equal(open.Definition.Slug, mine[0].Slug);
        Assert.Equal(open.Definition.Id, mine[0].SurveyId);

        Assert.False(mine[1].CanContinue); // completed responses cannot be continued
        Assert.Equal(completed.SubmittedAt, mine[1].SubmittedAt);
        Assert.Equal(completed.StartedAt, mine[1].StartedAt);

        Assert.False(mine[2].CanContinue); // survey closed in the meantime
        Assert.Equal("Closed survey", mine[2].SurveyTitle);
    }
}
