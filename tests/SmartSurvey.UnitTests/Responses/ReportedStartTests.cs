using SmartSurvey.Application.Responses;
using SmartSurvey.UnitTests.TestSupport;
using static SmartSurvey.UnitTests.Responses.ResponseTestData;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>Client-reported start times give guest submissions a real completion time, within safe bounds.</summary>
public sealed class ReportedStartTests : IAsyncLifetime
{
    private ResponseTestHarness _h = null!;
    private SampleSurvey _s = null!;

    public async Task InitializeAsync()
    {
        _h = await ResponseTestHarness.CreateAsync();
        _s = await _h.SeedSurveyAsync();
        _h.User.ActAsAnonymous();
    }

    public async Task DisposeAsync() => await _h.DisposeAsync();

    [Fact]
    public async Task Guest_submission_keeps_the_reported_start()
    {
        var request = ValidRequest(_s);
        request.StartedAt = _h.Now.AddMinutes(-7);

        var result = await _h.Service.SubmitAsync(_s.Definition.Id, request);

        var stored = await _h.FindResponseAsync(result.ResponseId);
        Assert.Equal(_h.Now.AddMinutes(-7), stored!.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(7), stored.Duration);
    }

    [Fact]
    public async Task Future_start_is_replaced_by_now()
    {
        var request = ValidRequest(_s);
        request.StartedAt = _h.Now.AddHours(3);

        var result = await _h.Service.SubmitAsync(_s.Definition.Id, request);

        Assert.Equal(_h.Now, (await _h.FindResponseAsync(result.ResponseId))!.StartedAt);
    }

    [Fact]
    public async Task Implausibly_old_start_is_clamped()
    {
        var request = ValidRequest(_s);
        request.StartedAt = _h.Now.AddDays(-5);

        var result = await _h.Service.SubmitAsync(_s.Definition.Id, request);

        Assert.Equal(_h.Now - ResponseService.MaxReportedDuration, (await _h.FindResponseAsync(result.ResponseId))!.StartedAt);
    }

    [Fact]
    public async Task Without_a_reported_start_the_submission_time_is_used()
    {
        var result = await _h.Service.SubmitAsync(_s.Definition.Id, ValidRequest(_s));

        Assert.Equal(_h.Now, (await _h.FindResponseAsync(result.ResponseId))!.StartedAt);
    }

    [Fact]
    public async Task Completing_a_draft_keeps_the_draft_start()
    {
        _h.User.ActAsRespondent();
        var draftRequest = Request(Choice(_s.Enjoy, _s.Yes));
        draftRequest.StartedAt = _h.Now.AddMinutes(-20);
        var draftId = await _h.Service.SaveDraftAsync(_s.Definition.Id, draftRequest);

        var final = ValidRequest(_s, draftId);
        final.StartedAt = _h.Now.AddMinutes(-1); // ignored: the response already exists
        await _h.Service.SubmitAsync(_s.Definition.Id, final);

        Assert.Equal(_h.Now.AddMinutes(-20), (await _h.FindResponseAsync(draftId))!.StartedAt);
    }
}
