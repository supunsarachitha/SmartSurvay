using Microsoft.EntityFrameworkCore;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;
using SmartSurvey.Infrastructure.Persistence;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Core;

/// <summary>
/// Workspace isolation in the data layer: <see cref="DataScope"/> query filters, the save guard in
/// <see cref="AppDbContext"/> and the scopes handed out by the context factory.
/// </summary>
public class WorkspaceIsolationTests
{
    private static readonly Guid Mine = TestWorkspaces.DefaultId;
    private static readonly Guid Theirs = TestWorkspaces.OtherId;

    private static Survey NewSurvey(string title)
    {
        var survey = new Survey { Title = title, Slug = "iso-" + Guid.NewGuid().ToString("N")[..10], Status = SurveyStatus.Published };
        var section = new SurveySection { Survey = survey, Title = "Page 1" };
        var question = new Question
        {
            Survey = survey, Section = section, Type = QuestionType.Radio, Text = "Pick one", Code = "Q1",
            Settings = new QuestionSettings(), Options = [new QuestionOption { Text = "Yes" }, new QuestionOption { Text = "No" }],
        };
        survey.Sections.Add(section);
        survey.Questions.Add(question);
        survey.Responses.Add(new SurveyResponse
        {
            Status = ResponseStatus.Completed,
            StartedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Answers = [new Answer { Question = question, TextValue = "secret of " + title }],
        });
        return survey;
    }

    /// <summary>One survey (with a response, a report and an audit entry) in each workspace.</summary>
    private static async Task<(Survey Mine, Survey Theirs)> SeedBothAsync(SqliteTestDatabase db)
    {
        var mine = NewSurvey("Mine");
        var theirs = NewSurvey("Theirs");
        await db.SeedInWorkspaceAsync(Mine, mine, new ReportDefinition { Name = "My report", Survey = mine },
            new AuditLogEntry { Action = "test.mine", EntityType = "Test", Timestamp = db.UtcNow });
        await db.SeedInWorkspaceAsync(Theirs, theirs, new ReportDefinition { Name = "Their report", Survey = theirs },
            new AuditLogEntry { Action = "test.theirs", EntityType = "Test", Timestamp = db.UtcNow });
        return (mine, theirs);
    }

    [Fact]
    public async Task Inserts_in_a_workspace_scope_stamp_the_whole_graph()
    {
        await using var db = new SqliteTestDatabase();
        var (mine, _) = await SeedBothAsync(db);

        await using var ctx = db.CreateContext();
        Assert.Equal(Mine, (await ctx.Surveys.SingleAsync(s => s.Id == mine.Id)).WorkspaceId);
        Assert.All(await ctx.SurveySections.Where(s => s.SurveyId == mine.Id).ToListAsync(), s => Assert.Equal(Mine, s.WorkspaceId));
        Assert.All(await ctx.Questions.Where(q => q.SurveyId == mine.Id).ToListAsync(), q => Assert.Equal(Mine, q.WorkspaceId));
        Assert.All(await ctx.QuestionOptions.Where(o => o.Question!.SurveyId == mine.Id).ToListAsync(), o => Assert.Equal(Mine, o.WorkspaceId));
        Assert.All(await ctx.Responses.Where(r => r.SurveyId == mine.Id).ToListAsync(), r => Assert.Equal(Mine, r.WorkspaceId));
        Assert.All(await ctx.Answers.Where(a => a.Response!.SurveyId == mine.Id).ToListAsync(), a => Assert.Equal(Mine, a.WorkspaceId));
        Assert.Equal(Mine, (await ctx.Reports.SingleAsync(r => r.SurveyId == mine.Id)).WorkspaceId);
        Assert.Equal(Mine, (await ctx.AuditLogs.SingleAsync(a => a.Action == "test.mine")).WorkspaceId);
    }

    [Fact]
    public async Task Workspace_scope_sees_only_its_own_rows_in_every_table()
    {
        await using var db = new SqliteTestDatabase();
        var (mine, theirs) = await SeedBothAsync(db);

        await using var ctx = db.CreateContext(DataScope.ForWorkspace(Mine));

        Assert.Equal([mine.Id], await ctx.Surveys.Select(s => s.Id).ToListAsync());
        Assert.Null(await ctx.Surveys.FirstOrDefaultAsync(s => s.Id == theirs.Id)); // direct id lookups too
        Assert.All(await ctx.SurveySections.ToListAsync(), s => Assert.Equal(mine.Id, s.SurveyId));
        Assert.All(await ctx.Questions.ToListAsync(), q => Assert.Equal(mine.Id, q.SurveyId));
        Assert.Equal(2, await ctx.QuestionOptions.CountAsync());
        Assert.Equal([mine.Id], await ctx.Responses.Select(r => r.SurveyId).ToListAsync());
        Assert.Equal(["secret of Mine"], await ctx.Answers.Select(a => a.TextValue).ToListAsync());
        Assert.Equal(["My report"], await ctx.Reports.Select(r => r.Name).ToListAsync());
        Assert.Equal(["test.mine"], await ctx.AuditLogs.Select(a => a.Action).ToListAsync());

        // Navigations and includes are filtered as well.
        var loaded = await ctx.Surveys.Include(s => s.Responses).ThenInclude(r => r.Answers).SingleAsync();
        Assert.Equal("secret of Mine", loaded.Responses.Single().Answers.Single().TextValue);
    }

    [Fact]
    public async Task None_scope_sees_and_deletes_nothing()
    {
        await using var db = new SqliteTestDatabase();
        await SeedBothAsync(db);

        await using (var ctx = db.CreateContext(DataScope.None))
        {
            Assert.Equal(0, await ctx.Surveys.CountAsync());
            Assert.Equal(0, await ctx.Answers.CountAsync());
            Assert.Equal(0, await ctx.Reports.CountAsync());
            Assert.Equal(0, await ctx.AuditLogs.CountAsync());
            Assert.Equal(0, await ctx.Surveys.ExecuteDeleteAsync());
        }

        await using var system = db.CreateContext(DataScope.System);
        Assert.Equal(2, await system.Surveys.CountAsync());
    }

    [Fact]
    public async Task Bulk_deletes_in_a_workspace_scope_never_touch_other_workspaces()
    {
        await using var db = new SqliteTestDatabase();
        var (_, theirs) = await SeedBothAsync(db);

        await using (var ctx = db.CreateContext(DataScope.ForWorkspace(Mine)))
        {
            Assert.Equal(1, await ctx.Surveys.ExecuteDeleteAsync());
        }

        await using var system = db.CreateContext();
        Assert.Equal([theirs.Id], await system.Surveys.Select(s => s.Id).ToListAsync());
    }

    [Fact]
    public async Task System_scope_sees_every_workspace()
    {
        await using var db = new SqliteTestDatabase();
        await SeedBothAsync(db);

        await using var ctx = db.CreateContext(DataScope.System);

        Assert.Equal(2, await ctx.Surveys.CountAsync());
        Assert.Equal(2, await ctx.Answers.CountAsync());
        Assert.Equal(2, await ctx.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Writing_data_of_another_workspace_is_rejected()
    {
        await using var db = new SqliteTestDatabase();
        var (_, theirs) = await SeedBothAsync(db);

        await using var ctx = db.CreateContext(DataScope.ForWorkspace(Mine));

        // Insert explicitly marked with another workspace.
        ctx.Surveys.Add(new Survey { Title = "Smuggled", Slug = "smuggled", WorkspaceId = Theirs });
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        ctx.ChangeTracker.Clear();

        // Update/delete of a row the scope cannot see (attached by id).
        var foreign = new Survey { Id = theirs.Id, Title = "Hacked", Slug = theirs.Slug, WorkspaceId = Theirs };
        ctx.Surveys.Update(foreign);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        ctx.ChangeTracker.Clear();

        ctx.Surveys.Remove(foreign);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());

        await using var system = db.CreateContext();
        Assert.Equal("Theirs", (await system.Surveys.SingleAsync(s => s.Id == theirs.Id)).Title);
    }

    [Fact]
    public async Task The_workspace_of_existing_rows_cannot_be_changed_even_by_system_code()
    {
        await using var db = new SqliteTestDatabase();
        var (mine, _) = await SeedBothAsync(db);

        await using var ctx = db.CreateContext(DataScope.System);
        var survey = await ctx.Surveys.SingleAsync(s => s.Id == mine.Id);
        survey.WorkspaceId = Theirs;

        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task System_scope_inserts_must_name_their_workspace()
    {
        await using var db = new SqliteTestDatabase();
        await using var ctx = db.CreateContext(DataScope.System);

        ctx.Surveys.Add(new Survey { Title = "Unowned", Slug = "unowned" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        ctx.ChangeTracker.Clear();

        ctx.Surveys.Add(new Survey { Title = "Owned", Slug = "owned", WorkspaceId = Theirs });
        await ctx.SaveChangesAsync();
        Assert.Equal(Theirs, (await ctx.Surveys.SingleAsync(s => s.Slug == "owned")).WorkspaceId);
    }

    [Fact]
    public async Task None_scope_can_only_write_system_audit_events()
    {
        await using var db = new SqliteTestDatabase();
        await using var ctx = db.CreateContext(DataScope.None);

        ctx.Surveys.Add(new Survey { Title = "Nowhere", Slug = "nowhere" });
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        ctx.ChangeTracker.Clear();

        ctx.AuditLogs.Add(new AuditLogEntry { Action = "workspace.disabled", EntityType = "Workspace", Timestamp = db.UtcNow, WorkspaceId = Mine });
        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
        ctx.ChangeTracker.Clear();

        ctx.AuditLogs.Add(new AuditLogEntry { Action = "workspace.disabled", EntityType = "Workspace", Timestamp = db.UtcNow });
        await ctx.SaveChangesAsync();

        await using var system = db.CreateContext();
        Assert.Null((await system.AuditLogs.SingleAsync()).WorkspaceId);
    }

    [Fact]
    public async Task Factory_scopes_follow_the_current_user()
    {
        await using var db = new SqliteTestDatabase();
        await SeedBothAsync(db);

        async Task<List<string>> VisibleTitlesAsync()
        {
            await using var ctx = await db.CreateAsync();
            return await ctx.Surveys.Select(s => s.Title).ToListAsync();
        }

        db.CurrentUser.ActAsAdmin();
        Assert.Equal(["Mine"], await VisibleTitlesAsync());

        db.CurrentUser.ActAsRespondent().InWorkspace(Theirs);
        Assert.Equal(["Theirs"], await VisibleTitlesAsync());

        db.CurrentUser.ActAsSuperAdmin();
        Assert.Empty(await VisibleTitlesAsync());

        db.CurrentUser.ActAsAnonymous();
        Assert.Empty(await VisibleTitlesAsync());
    }

    [Fact]
    public void Scope_for_an_empty_workspace_id_is_refused() =>
        Assert.Throws<ArgumentException>(() => DataScope.ForWorkspace(Guid.Empty));
}
