using SmartSurvey.Application.Responses;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Tests of <see cref="ResponseService.ListAvailableAsync"/>.</summary>
public class AvailableSurveysTests
{
    /// <summary>Guests list a workspace's surveys through its public page (slug of the default test workspace).</summary>
    private const string PublicPage = "test";

    [Fact]
    public async Task Guests_see_only_open_surveys_that_accept_anonymous_responses()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var open = await h.SeedSurveyAsync(x => x.ClosesAt = h.Now.AddDays(7), title: "Open to everyone");
        await h.SeedSurveyAsync(x => x.AllowAnonymous = false, title: "Members only");
        await h.SeedSurveyAsync(x => { x.Status = SurveyStatus.Draft; x.PublishedAt = null; }, title: "Draft");
        await h.SeedSurveyAsync(x => x.Status = SurveyStatus.Closed, title: "Closed");
        await h.SeedSurveyAsync(x => x.Status = SurveyStatus.Archived, title: "Archived");
        await h.SeedSurveyAsync(x => x.IsTemplate = true, title: "Template");
        await h.SeedSurveyAsync(x => x.OpensAt = h.Now.AddDays(1), title: "Opens tomorrow");
        await h.SeedSurveyAsync(x => x.ClosesAt = h.Now.AddMinutes(-1), title: "Expired");
        var full = await h.SeedSurveyAsync(x => x.MaxResponses = 1, title: "Full");
        await h.SeedResponseAsync(full, null, ResponseStatus.Completed);
        h.User.ActAsAnonymous();

        var list = await h.Service.ListAvailableAsync(PublicPage);

        var item = Assert.Single(list);
        Assert.Equal(open.Definition.Id, item.SurveyId);
        Assert.Equal("Open to everyone", item.Title);
        Assert.Equal("Tell us what you think.", item.Description);
        Assert.Equal(open.Definition.Slug, item.Slug);
        Assert.Equal(h.Now.AddDays(7), item.ClosesAt);
        Assert.True(item.AllowAnonymous);
        Assert.True(item.CanRespond);
        Assert.False(item.HasDraft);
        Assert.False(item.HasCompleted);
    }

    [Fact]
    public async Task Logged_in_users_also_see_members_only_surveys_with_personal_flags()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var members = await h.SeedSurveyAsync(x => x.AllowAnonymous = false, title: "Members only");
        var drafted = await h.SeedSurveyAsync(title: "Drafted");
        var done = await h.SeedSurveyAsync(title: "Done once");
        var again = await h.SeedSurveyAsync(x => x.AllowMultipleResponses = true, title: "Done but repeatable");
        await h.SeedResponseAsync(drafted, TestCurrentUser.RespondentId, ResponseStatus.InProgress);
        await h.SeedResponseAsync(done, TestCurrentUser.RespondentId, ResponseStatus.Completed);
        await h.SeedResponseAsync(again, TestCurrentUser.RespondentId, ResponseStatus.Completed);
        h.User.ActAsRespondent();

        var list = (await h.Service.ListAvailableAsync()).ToDictionary(x => x.SurveyId);

        Assert.Equal(4, list.Count);

        var membersItem = list[members.Definition.Id];
        Assert.False(membersItem.AllowAnonymous);
        Assert.True(membersItem.CanRespond);

        var draftedItem = list[drafted.Definition.Id];
        Assert.True(draftedItem.HasDraft);
        Assert.False(draftedItem.HasCompleted);
        Assert.True(draftedItem.CanRespond);

        var doneItem = list[done.Definition.Id];
        Assert.True(doneItem.HasCompleted);
        Assert.False(doneItem.CanRespond);

        var againItem = list[again.Definition.Id];
        Assert.True(againItem.HasCompleted);
        Assert.True(againItem.CanRespond);
    }

    [Fact]
    public async Task Other_peoples_responses_do_not_set_personal_flags()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync();
        await h.SeedResponseAsync(s, ResponseTestHarness.OtherUserId, ResponseStatus.InProgress);
        await h.SeedResponseAsync(s, ResponseTestHarness.OtherUserId, ResponseStatus.Completed);
        await h.SeedResponseAsync(s, null, ResponseStatus.Completed);

        h.User.ActAsRespondent();
        var asUser = Assert.Single(await h.Service.ListAvailableAsync());
        h.User.ActAsAnonymous();
        var asGuest = Assert.Single(await h.Service.ListAvailableAsync(PublicPage));

        Assert.False(asUser.HasDraft);
        Assert.False(asUser.HasCompleted);
        Assert.True(asUser.CanRespond);
        Assert.False(asGuest.HasDraft);
        Assert.False(asGuest.HasCompleted);
        Assert.True(asGuest.CanRespond);
    }

    [Fact]
    public async Task Question_count_and_estimated_minutes_are_reported()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var full = await h.SeedSurveyAsync(title: "Seven questions");
        var tiny = await h.SeedSurveyAsync(title: "One question", design: s =>
        {
            s.Page1.Questions.RemoveRange(1, 2);
            s.Page2.Questions.Clear();
            s.Definition.LogicRules.Clear();
        });
        h.User.ActAsAnonymous();

        var list = (await h.Service.ListAvailableAsync(PublicPage)).ToDictionary(x => x.SurveyId);

        Assert.Equal(7, list[full.Definition.Id].QuestionCount);
        Assert.Equal(3, list[full.Definition.Id].EstimatedMinutes); // 7 × 20 s = 140 s → 3 min
        Assert.Equal(1, list[tiny.Definition.Id].QuestionCount);
        Assert.Equal(1, list[tiny.Definition.Id].EstimatedMinutes); // never less than a minute
    }

    [Fact]
    public async Task Newest_publications_come_first()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var oldest = await h.SeedSurveyAsync(x => x.PublishedAt = h.Now.AddDays(-3), title: "Oldest");
        var newest = await h.SeedSurveyAsync(x => x.PublishedAt = h.Now.AddHours(-1), title: "Newest");
        var middle = await h.SeedSurveyAsync(x => x.PublishedAt = h.Now.AddDays(-2), title: "Middle");
        h.User.ActAsAnonymous();

        var list = await h.Service.ListAvailableAsync(PublicPage);

        Assert.Equal(
            [newest.Definition.Id, middle.Definition.Id, oldest.Definition.Id],
            list.Select(x => x.SurveyId).ToArray());
    }

    [Fact]
    public async Task Survey_disappears_from_the_list_once_its_quota_is_reached()
    {
        await using var h = await ResponseTestHarness.CreateAsync();
        var s = await h.SeedSurveyAsync(x => x.MaxResponses = 1);
        h.User.ActAsAnonymous();

        var before = await h.Service.ListAvailableAsync(PublicPage);
        await h.Service.SubmitAsync(s.Definition.Id, ResponseTestData.ValidRequest(s));
        var after = await h.Service.ListAvailableAsync(PublicPage);

        Assert.Single(before);
        Assert.Empty(after);
    }
}
