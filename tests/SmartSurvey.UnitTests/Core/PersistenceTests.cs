using Microsoft.EntityFrameworkCore;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;
using SmartSurvey.Infrastructure.Persistence;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Core;

/// <summary>Verifies the EF Core model: graph persistence, JSON columns, UTC handling, cascades, audit stamps.</summary>
public class PersistenceTests
{
    private static Survey NewSurvey(out Question question, out QuestionOption option)
    {
        var survey = new Survey { Title = "Model test", Slug = "model-test-" + Guid.NewGuid().ToString("N")[..8] };
        var section = new SurveySection { Survey = survey, Title = "Page 1" };
        option = new QuestionOption { Text = "Other", AllowsFreeText = true };
        question = new Question
        {
            Survey = survey, Section = section, Type = QuestionType.Checkbox, Text = "Pick", Code = "Q1",
            Settings = new QuestionSettings { MaxSelections = 2, Placeholder = "p" },
            Options = [option],
        };
        survey.Sections.Add(section);
        survey.Questions.Add(question);
        return survey;
    }

    [Fact]
    public async Task Survey_graph_round_trips_with_json_settings()
    {
        await using var db = new SqliteTestDatabase();
        var survey = NewSurvey(out var question, out _);
        await db.SeedAsync(survey);

        await using var ctx = db.CreateContext();
        var loaded = await ctx.Questions.Include(q => q.Options).SingleAsync(q => q.Id == question.Id);

        Assert.Equal(2, loaded.Settings.MaxSelections);
        Assert.Equal("p", loaded.Settings.Placeholder);
        Assert.True(Assert.Single(loaded.Options).AllowsFreeText);
    }

    [Fact]
    public async Task In_place_json_mutation_is_detected()
    {
        await using var db = new SqliteTestDatabase();
        var survey = NewSurvey(out var question, out _);
        await db.SeedAsync(survey);

        await using (var ctx = db.CreateContext())
        {
            var q = await ctx.Questions.SingleAsync(x => x.Id == question.Id);
            q.Settings.MaxSelections = 5; // mutate the owned JSON object in place
            await ctx.SaveChangesAsync();
        }

        await using var verify = db.CreateContext();
        Assert.Equal(5, (await verify.Questions.SingleAsync(x => x.Id == question.Id)).Settings.MaxSelections);
    }

    [Fact]
    public async Task Auditable_entities_are_stamped_with_utc_time_and_user()
    {
        await using var db = new SqliteTestDatabase();
        var survey = NewSurvey(out _, out _);
        await db.SeedAsync(survey);

        await using var ctx = db.CreateContext();
        var loaded = await ctx.Surveys.SingleAsync(s => s.Id == survey.Id);

        Assert.Equal(db.UtcNow, loaded.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, loaded.CreatedAt.Kind);
        Assert.Equal(TestCurrentUser.AdminId, loaded.CreatedById);
        Assert.Null(loaded.UpdatedAt);
    }

    [Fact]
    public async Task Deleting_survey_cascades_to_responses_answers_and_reports()
    {
        await using var db = new SqliteTestDatabase();
        var survey = NewSurvey(out var question, out var option);
        var response = new SurveyResponse
        {
            Survey = survey, Status = ResponseStatus.Completed, StartedAt = db.UtcNow, SubmittedAt = db.UtcNow,
            Answers = [new Answer { Question = question, Selections = [new AnswerSelection { Option = option, FreeText = "x" }] }],
        };
        var report = new ReportDefinition
        {
            Name = "R", Survey = survey,
            Widgets = [new ReportWidget { Type = WidgetType.BarChart, Question = question }],
        };
        await db.SeedAsync(survey, response, report);

        await using (var ctx = db.CreateContext())
        {
            await ctx.Surveys.Where(s => s.Id == survey.Id).ExecuteDeleteAsync();
        }

        await using var verify = db.CreateContext();
        Assert.Equal(0, await verify.Responses.CountAsync());
        Assert.Equal(0, await verify.Answers.CountAsync());
        Assert.Equal(0, await verify.AnswerSelections.CountAsync());
        Assert.Equal(0, await verify.Reports.CountAsync());
        Assert.Equal(0, await verify.ReportWidgets.CountAsync());
    }

    [Fact]
    public async Task Deleting_question_keeps_widget_but_clears_reference()
    {
        await using var db = new SqliteTestDatabase();
        var survey = NewSurvey(out var question, out _);
        var report = new ReportDefinition { Name = "R", Survey = survey, Widgets = [new ReportWidget { Type = WidgetType.PieChart, Question = question }] };
        await db.SeedAsync(survey, report);

        await using (var ctx = db.CreateContext())
        {
            await ctx.Questions.Where(q => q.Id == question.Id).ExecuteDeleteAsync();
        }

        await using var verify = db.CreateContext();
        var widget = await verify.ReportWidgets.SingleAsync();
        Assert.Null(widget.QuestionId);
    }

    [Fact]
    public async Task Slug_is_unique()
    {
        await using var db = new SqliteTestDatabase();
        await db.SeedAsync(new Survey { Title = "A", Slug = "same" });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SeedAsync(new Survey { Title = "B", Slug = "same" }));
    }

    [Fact]
    public async Task Enum_values_are_stored_as_strings()
    {
        await using var db = new SqliteTestDatabase();
        await db.SeedAsync(new Survey { Title = "A", Slug = "enum-test", Status = SurveyStatus.Published });

        await using var ctx = db.CreateContext();
        var raw = await ctx.Database.SqlQueryRaw<string>("SELECT \"Status\" AS \"Value\" FROM \"Surveys\"").SingleAsync();
        Assert.Equal("Published", raw);
    }

    [Theory]
    [InlineData("Host=db;Database=s;Username=u;Password=p", "Host=db;Database=s;Username=u;Password=p;GSS Encryption Mode=Disable")]
    [InlineData("Host=db;Password='a;b';", "Host=db;Password='a;b';GSS Encryption Mode=Disable")]
    [InlineData("Host=db;GSS Encryption Mode=Require", "Host=db;GSS Encryption Mode=Require")]
    [InlineData("Host=db;gssencryptionmode=Prefer", "Host=db;gssencryptionmode=Prefer")]
    public void Postgres_connection_string_disables_implicit_gss_encryption(string configured, string expected)
    {
        Assert.Equal(expected, PostgresConnectionString.WithDefaults(configured));
    }
}
