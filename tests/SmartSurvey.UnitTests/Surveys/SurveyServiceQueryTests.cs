using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary>Listing, lookups, slug availability, answer counts and deletion.</summary>
public class SurveyServiceQueryTests
{
    private static async Task<SurveyDefinitionDto> CreateAsync(SurveyServiceHarness h, string title, string? description = null, bool template = false)
    {
        h.Db.Time.Advance(TimeSpan.FromMinutes(1)); // distinct creation times for ordering
        return await h.Service.CreateAsync(new SurveyDefinitionDto { Title = title, Description = description, IsTemplate = template });
    }

    [Fact]
    public async Task List_hides_archived_surveys_unless_requested()
    {
        await using var h = new SurveyServiceHarness();
        var active = await CreateAsync(h, "Active");
        var archived = await CreateAsync(h, "Archived");
        await h.Service.ChangeStatusAsync(archived.Id, SurveyStatus.Archived);

        var byDefault = await h.Service.ListAsync(new SurveyQuery());
        var including = await h.Service.ListAsync(new SurveyQuery { IncludeArchived = true });
        var onlyArchived = await h.Service.ListAsync(new SurveyQuery { Status = SurveyStatus.Archived });

        Assert.Equal([active.Id], byDefault.Items.Select(i => i.Id));
        Assert.Equal(2, including.TotalCount);
        Assert.Equal([archived.Id], onlyArchived.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task List_filters_by_status_and_template_flag()
    {
        await using var h = new SurveyServiceHarness();
        var (_, published) = await h.CreateSampleAsync("Live one");
        await h.Service.ChangeStatusAsync(published.Id, SurveyStatus.Published);
        var draft = await CreateAsync(h, "Draft one");
        var template = await CreateAsync(h, "Template one", template: true);

        var publishedOnly = await h.Service.ListAsync(new SurveyQuery { Status = SurveyStatus.Published });
        var templatesOnly = await h.Service.ListAsync(new SurveyQuery { IsTemplate = true });
        var nonTemplates = await h.Service.ListAsync(new SurveyQuery { IsTemplate = false });
        var drafts = await h.Service.ListAsync(new SurveyQuery { Status = SurveyStatus.Draft, IsTemplate = false });

        Assert.Equal([published.Id], publishedOnly.Items.Select(i => i.Id));
        Assert.Equal([template.Id], templatesOnly.Items.Select(i => i.Id));
        Assert.Equal(2, nonTemplates.TotalCount);
        Assert.Equal([draft.Id], drafts.Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData("PULSE", "Team pulse")]
    [InlineData("quarterly", "Team pulse")]
    [InlineData("employee-net", "Employee NPS")]
    [InlineData("  nps  ", "Employee NPS")]
    public async Task List_searches_title_description_and_slug_ignoring_case(string search, string expectedTitle)
    {
        await using var h = new SurveyServiceHarness();
        await CreateAsync(h, "Team pulse", "Quarterly check-in");
        await h.Service.CreateAsync(new SurveyDefinitionDto { Title = "Employee NPS", Slug = "employee-net-promoter" });
        await CreateAsync(h, "Unrelated");

        var result = await h.Service.ListAsync(new SurveyQuery { Search = search });

        Assert.Equal([expectedTitle], result.Items.Select(i => i.Title));
    }

    [Fact]
    public async Task List_pages_newest_activity_first()
    {
        await using var h = new SurveyServiceHarness();
        var created = new List<SurveyDefinitionDto>();
        for (var i = 1; i <= 5; i++)
        {
            created.Add(await CreateAsync(h, $"Survey {i}"));
        }

        // Editing the oldest survey moves it to the top.
        h.Db.Time.Advance(TimeSpan.FromMinutes(1));
        created[0].Title = "Survey 1 (edited)";
        await h.Service.UpdateAsync(created[0].Id, created[0]);

        var page1 = await h.Service.ListAsync(new SurveyQuery { Page = 1, PageSize = 2 });
        var page3 = await h.Service.ListAsync(new SurveyQuery { Page = 3, PageSize = 2 });

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(["Survey 1 (edited)", "Survey 5"], page1.Items.Select(i => i.Title));
        Assert.Equal(["Survey 2"], page3.Items.Select(i => i.Title));
        Assert.True(page1.HasNextPage);
        Assert.False(page3.HasNextPage);
    }

    [Fact]
    public async Task List_reports_question_and_response_counts()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();
        var firstSubmission = h.Db.UtcNow.AddHours(1);
        var lastSubmission = h.Db.UtcNow.AddHours(5);
        await h.SeedResponseAsync(saved.Id, ResponseStatus.Completed, firstSubmission, SurveyServiceHarness.Choice(sample.Enjoy.Id, sample.Yes.Id));
        await h.SeedResponseAsync(saved.Id, ResponseStatus.Completed, lastSubmission);
        await h.SeedResponseAsync(saved.Id, ResponseStatus.InProgress);
        var empty = await CreateAsync(h, "Empty");

        var rows = (await h.Service.ListAsync(new SurveyQuery())).Items;

        var row = rows.Single(r => r.Id == saved.Id);
        Assert.Equal(7, row.QuestionCount);
        Assert.Equal(2, row.CompletedResponses);
        Assert.Equal(1, row.InProgressResponses);
        Assert.Equal(lastSubmission, row.LastResponseAt);
        Assert.Equal("customer-feedback", row.Slug);
        Assert.True(row.AllowAnonymous);

        var emptyRow = rows.Single(r => r.Id == empty.Id);
        Assert.Equal(0, emptyRow.QuestionCount);
        Assert.Equal(0, emptyRow.CompletedResponses);
        Assert.Null(emptyRow.LastResponseAt);
    }

    [Fact]
    public async Task Templates_gallery_lists_active_templates_newest_first()
    {
        await using var h = new SurveyServiceHarness();
        var older = await CreateAsync(h, "Older template", template: true);
        var newer = await CreateAsync(h, "Newer template", template: true);
        var archived = await CreateAsync(h, "Archived template", template: true);
        await h.Service.ChangeStatusAsync(archived.Id, SurveyStatus.Archived);
        await CreateAsync(h, "Regular survey");

        var templates = await h.Service.ListTemplatesAsync();

        Assert.Equal([newer.Id, older.Id], templates.Select(t => t.Id));
        Assert.All(templates, t => Assert.True(t.IsTemplate));
    }

    [Fact]
    public async Task Get_of_a_missing_survey_is_not_found()
    {
        await using var h = new SurveyServiceHarness();
        var id = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<NotFoundException>(() => h.Service.GetAsync(id));

        Assert.Equal("Survey", ex.EntityName);
        Assert.Equal(id, ex.Key);
    }

    [Fact]
    public async Task Find_by_slug_ignores_case_and_returns_null_when_missing()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        var found = await h.Service.FindBySlugAsync("  Customer-FEEDBACK ");

        Assert.NotNull(found);
        Assert.Equal(saved.Id, found.Id);
        Assert.Equal(7, found.AllQuestions().Count());
        Assert.Single(found.LogicRules);
        Assert.Null(await h.Service.FindBySlugAsync("nope"));
        Assert.Null(await h.Service.FindBySlugAsync("  "));
    }

    [Fact]
    public async Task Slug_availability_ignores_case_and_can_exclude_a_survey()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();

        Assert.False(await h.Service.IsSlugAvailableAsync("customer-feedback"));
        Assert.False(await h.Service.IsSlugAvailableAsync("Customer-Feedback"));
        Assert.True(await h.Service.IsSlugAvailableAsync("customer-feedback", saved.Id));
        Assert.True(await h.Service.IsSlugAvailableAsync("something-new"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("has spaces")]
    [InlineData("trailing-")]
    public async Task Malformed_slugs_are_never_available(string slug)
    {
        await using var h = new SurveyServiceHarness();

        Assert.False(await h.Service.IsSlugAvailableAsync(slug));
    }

    [Fact]
    public async Task Count_answers_counts_stored_answers_of_a_question()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();
        await h.SeedResponseAsync(saved.Id, ResponseStatus.Completed, h.Db.UtcNow, SurveyServiceHarness.Choice(sample.Enjoy.Id, sample.Yes.Id));
        await h.SeedResponseAsync(saved.Id, ResponseStatus.InProgress, null, SurveyServiceHarness.Choice(sample.Enjoy.Id, sample.No.Id));

        Assert.Equal(2, await h.Service.CountAnswersAsync(sample.Enjoy.Id));
        Assert.Equal(0, await h.Service.CountAnswersAsync(sample.Email.Id));
        Assert.Equal(0, await h.Service.CountAnswersAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Delete_removes_the_survey_with_its_responses_and_reports()
    {
        await using var h = new SurveyServiceHarness();
        var (sample, saved) = await h.CreateSampleAsync();
        var (_, other) = await h.CreateSampleAsync("Other survey");
        await h.SeedResponseAsync(saved.Id, ResponseStatus.Completed, h.Db.UtcNow, SurveyServiceHarness.Choice(sample.Enjoy.Id, sample.Yes.Id));
        await h.Db.SeedAsync(new ReportDefinition { Name = "Report", SurveyId = saved.Id });

        await h.Service.DeleteAsync(saved.Id);

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.GetAsync(saved.Id));
        await using var ctx = h.Db.CreateContext();
        Assert.Equal(0, await ctx.Responses.CountAsync());
        Assert.Equal(0, await ctx.Answers.CountAsync());
        Assert.Equal(0, await ctx.Reports.CountAsync());
        Assert.Equal(0, await ctx.SurveySections.CountAsync(s => s.SurveyId == saved.Id));
        Assert.Equal(7, await ctx.Questions.CountAsync(q => q.SurveyId == other.Id));
    }

    [Fact]
    public async Task Delete_of_a_missing_survey_is_not_found()
    {
        await using var h = new SurveyServiceHarness();

        await Assert.ThrowsAsync<NotFoundException>(() => h.Service.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Delete_is_audited_with_the_number_of_responses()
    {
        await using var h = new SurveyServiceHarness();
        var (_, saved) = await h.CreateSampleAsync();
        await h.SeedResponseAsync(saved.Id, ResponseStatus.Completed, h.Db.UtcNow);
        await h.SeedResponseAsync(saved.Id, ResponseStatus.InProgress);

        await h.Service.DeleteAsync(saved.Id);

        var entry = h.Audit.Entries.Last();
        Assert.Equal(AuditActions.SurveyDeleted, entry.Action);
        Assert.Equal(saved.Id.ToString(), entry.EntityId);
        Assert.Equal("Deleted survey 'Customer feedback' with 2 responses.", entry.Details);
    }
}
