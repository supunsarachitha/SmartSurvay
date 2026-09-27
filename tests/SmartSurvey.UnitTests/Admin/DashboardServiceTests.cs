using SmartSurvey.Application.Common;
using SmartSurvey.Application.Dashboard;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.Reports;
using static SmartSurvey.UnitTests.Reports.ReportTestHarness;

namespace SmartSurvey.UnitTests.Admin;

/// <summary>Dashboard figures over the seeded feedback survey (4 completed responses + 1 draft).</summary>
public sealed class DashboardServiceTests : IAsyncLifetime
{
    private ReportTestHarness _h = null!;
    private DashboardService _service = null!;

    public async Task InitializeAsync()
    {
        _h = await CreateAsync();
        _service = new DashboardService(_h.Db, _h.User, _h.Db.Time);
    }

    public async Task DisposeAsync() => await _h.DisposeAsync();

    [Fact]
    public async Task Empty_database_gives_zeros_and_a_full_zero_filled_trend()
    {
        var summary = await _service.GetSummaryAsync();

        Assert.Equal(0, summary.TotalSurveys);
        Assert.Equal(0, summary.CompletedResponses);
        Assert.Equal(0, summary.CompletionRate);
        Assert.Null(summary.AverageDurationSeconds);
        Assert.Equal(DashboardService.TrendDays, summary.ResponsesPerDay.Count);
        Assert.All(summary.ResponsesPerDay, d => Assert.Equal(0, d.Count));
        Assert.Empty(summary.RecentResponses);
    }

    [Fact]
    public async Task Summary_counts_surveys_responses_users_and_reports()
    {
        var s = await _h.SeedSurveyWithResponsesAsync();
        await _h.Responses.SeedSurveyAsync(survey => survey.Status = SurveyStatus.Draft, title: "Draft survey");
        await _h.Service.CreateAsync(Definition(s, Widget(WidgetType.SummaryStats)));

        var summary = await _service.GetSummaryAsync();

        Assert.Equal(2, summary.TotalSurveys);
        Assert.Equal(1, summary.PublishedSurveys);
        Assert.Equal(1, summary.DraftSurveys);
        Assert.Equal(4, summary.CompletedResponses);
        Assert.Equal(1, summary.InProgressResponses);
        Assert.Equal(4, summary.ResponsesLast7Days);
        Assert.Equal(0.8, summary.CompletionRate, precision: 3);
        Assert.Equal(240, summary.AverageDurationSeconds!.Value, precision: 1);
        Assert.Equal(3, summary.TotalUsers);
        Assert.Equal(1, summary.TotalReports);
    }

    [Fact]
    public async Task Trend_and_recent_responses_reflect_submission_days()
    {
        await _h.SeedSurveyWithResponsesAsync();

        var summary = await _service.GetSummaryAsync();

        var today = DateOnly.FromDateTime(_h.Now);
        Assert.Equal(today, summary.ResponsesPerDay[^1].Date);
        Assert.Equal(2, summary.ResponsesPerDay.Single(d => d.Date == today.AddDays(-1)).Count);
        Assert.Equal(4, summary.ResponsesPerDay.Sum(d => d.Count));
        Assert.Equal(4, summary.RecentResponses.Count); // drafts are not listed
        Assert.Contains(summary.RecentResponses, r => r.RespondentName == DashboardService.AnonymousName);
        Assert.Equal("Customer feedback", Assert.Single(summary.TopSurveys).Title);
    }

    [Fact]
    public async Task Only_administrators_can_see_the_dashboard()
    {
        _h.User.ActAsRespondent();

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetSummaryAsync());
    }
}
