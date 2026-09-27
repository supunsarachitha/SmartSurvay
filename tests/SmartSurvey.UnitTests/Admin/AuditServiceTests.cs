using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Admin;

public sealed class AuditServiceTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _db = new();
    private AuditService _service = null!;

    public Task InitializeAsync()
    {
        _service = new AuditService(_db, _db.CurrentUser, _db.Time, NullLogger<AuditService>.Instance);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Log_records_the_current_user_and_time()
    {
        await _service.LogAsync(AuditActions.SurveyCreated, "Survey", "42", "Created survey 'Pulse'.");

        await using var db = _db.CreateContext();
        var entry = await db.AuditLogs.SingleAsync();
        Assert.Equal(AuditActions.SurveyCreated, entry.Action);
        Assert.Equal("Survey", entry.EntityType);
        Assert.Equal("42", entry.EntityId);
        Assert.Equal(TestCurrentUser.AdminId, entry.UserId);
        Assert.Equal("admin@test.local", entry.UserName);
        Assert.Equal(_db.UtcNow, entry.Timestamp);
    }

    [Fact]
    public async Task Log_truncates_over_long_values_instead_of_failing()
    {
        await _service.LogAsync(new string('a', 500), "Survey", new string('1', 300), new string('d', 5000));

        await using var db = _db.CreateContext();
        var entry = await db.AuditLogs.SingleAsync();
        Assert.Equal(AuditService.MaxCodeLength, entry.Action.Length);
        Assert.Equal(AuditService.MaxCodeLength, entry.EntityId!.Length);
        Assert.Equal(AuditService.MaxDetailsLength, entry.Details!.Length);
    }

    [Fact]
    public async Task Anonymous_actions_are_logged_without_a_user()
    {
        _db.CurrentUser.ActAsAnonymous();

        await _service.LogAsync(AuditActions.ResponseSubmitted, "Response", "1");

        await using var db = _db.CreateContext();
        var entry = await db.AuditLogs.SingleAsync();
        Assert.Null(entry.UserId);
        Assert.Null(entry.UserName);
    }

    [Fact]
    public async Task List_returns_newest_first_with_paging()
    {
        for (var i = 1; i <= 5; i++)
        {
            await _service.LogAsync(AuditActions.SurveyUpdated, "Survey", i.ToString());
            _db.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var page = await _service.ListAsync(new AuditQuery { PageSize = 2, Page = 2 });

        Assert.Equal(5, page.TotalCount);
        Assert.Equal(["3", "2"], page.Items.Select(e => e.EntityId));
    }

    [Fact]
    public async Task List_filters_by_search_entity_and_date_range()
    {
        await _service.LogAsync(AuditActions.SurveyCreated, "Survey", "s1", "Created survey 'Pulse'.");
        _db.Time.Advance(TimeSpan.FromDays(2));
        await _service.LogAsync(AuditActions.ReportCreated, "Report", "r1", "Created report 'Weekly'.");
        await _service.LogAsync(AuditActions.ReportDeleted, "Report", "r2");

        var bySearch = await _service.ListAsync(new AuditQuery { Search = "PULSE" });
        var byEntity = await _service.ListAsync(new AuditQuery { EntityType = "Report", EntityId = "r2" });
        var recent = await _service.ListAsync(new AuditQuery { From = DateOnly.FromDateTime(_db.UtcNow) });
        var old = await _service.ListAsync(new AuditQuery { To = DateOnly.FromDateTime(_db.UtcNow.AddDays(-1)) });

        Assert.Equal("s1", Assert.Single(bySearch.Items).EntityId);
        Assert.Equal(AuditActions.ReportDeleted, Assert.Single(byEntity.Items).Action);
        Assert.Equal(2, recent.TotalCount);
        Assert.Equal("s1", Assert.Single(old.Items).EntityId);
    }

    [Fact]
    public async Task List_rejects_an_inverted_date_range() =>
        await Assert.ThrowsAsync<AppValidationException>(() => _service.ListAsync(new AuditQuery
        {
            From = new DateOnly(2026, 2, 1),
            To = new DateOnly(2026, 1, 1),
        }));

    [Fact]
    public async Task Only_administrators_can_read_the_log()
    {
        _db.CurrentUser.ActAsRespondent();

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.ListAsync(new AuditQuery()));
    }
}
