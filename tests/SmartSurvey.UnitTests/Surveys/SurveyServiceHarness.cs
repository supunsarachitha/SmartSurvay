using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Application.Surveys.Validation;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Surveys;

/// <summary>
/// <see cref="SurveyService"/> wired to an isolated SQLite database, the fake clock, the mutable
/// current user (admin by default) and a recording audit trail. Create one per test.
/// </summary>
internal sealed class SurveyServiceHarness : IAsyncDisposable
{
    /// <summary>Creates the database and the service.</summary>
    public SurveyServiceHarness()
    {
        Service = new SurveyService(Db, Db.CurrentUser, Db.Time, Audit, new SurveyDefinitionValidator(), NullLogger<SurveyService>.Instance);
    }

    /// <summary>The test database (also the context factory, clock and current user).</summary>
    public SqliteTestDatabase Db { get; } = new();

    /// <summary>Recorded audit entries.</summary>
    public RecordingAuditService Audit { get; } = new();

    /// <summary>The service under test.</summary>
    public SurveyService Service { get; }

    /// <summary>Creates the two-page sample survey through the service; its ids are kept by the server.</summary>
    public async Task<(SampleSurvey Sample, SurveyDefinitionDto Saved)> CreateSampleAsync(string title = "Customer feedback")
    {
        var sample = SampleSurveys.CustomerFeedback(title);
        var saved = await Service.CreateAsync(sample.Definition);
        return (sample, saved);
    }

    /// <summary>Stores a response with the given answers directly in the database.</summary>
    public Task SeedResponseAsync(Guid surveyId, ResponseStatus status, DateTime? submittedAt = null, params Answer[] answers) =>
        Db.SeedAsync(new SurveyResponse
        {
            SurveyId = surveyId,
            Status = status,
            StartedAt = Db.UtcNow,
            SubmittedAt = submittedAt,
            Answers = answers.ToList(),
        });

    /// <summary>Sets a survey's status directly (arrangement shortcut that bypasses the state machine).</summary>
    public async Task SetStatusAsync(Guid surveyId, SurveyStatus status)
    {
        await using var ctx = Db.CreateContext();
        await ctx.Surveys.Where(s => s.Id == surveyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, status));
    }

    /// <summary>Loads a survey entity without its design (to inspect lifecycle columns not exposed by the DTO).</summary>
    public async Task<Survey> LoadSurveyAsync(Guid surveyId)
    {
        await using var ctx = Db.CreateContext();
        return await ctx.Surveys.AsNoTracking().SingleAsync(s => s.Id == surveyId);
    }

    /// <summary>A choice answer selecting the given options.</summary>
    public static Answer Choice(Guid questionId, params Guid[] optionIds) => new()
    {
        QuestionId = questionId,
        Selections = optionIds.Select(id => new AnswerSelection { OptionId = id }).ToList(),
    };

    /// <summary>A text answer.</summary>
    public static Answer Text(Guid questionId, string text) => new() { QuestionId = questionId, TextValue = text };

    /// <summary>A numeric answer.</summary>
    public static Answer Number(Guid questionId, double value) => new() { QuestionId = questionId, NumberValue = value };

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Db.DisposeAsync();
}

/// <summary>Helpers for inspecting survey designs in tests.</summary>
internal static class SurveyDesignInspection
{
    /// <summary>Every item id of a design (sections, questions, options, rules, conditions).</summary>
    public static HashSet<Guid> AllItemIds(this SurveyDefinitionDto survey)
    {
        var ids = new HashSet<Guid>();
        foreach (var section in survey.Sections)
        {
            ids.Add(section.Id);
            foreach (var question in section.Questions)
            {
                ids.Add(question.Id);
                ids.UnionWith(question.Options.Select(o => o.Id));
            }
        }

        foreach (var rule in survey.LogicRules)
        {
            ids.Add(rule.Id);
            ids.UnionWith(rule.Conditions.Select(c => c.Id));
        }

        return ids;
    }

    /// <summary>Finds a question by its code.</summary>
    public static QuestionDto Question(this SurveyDefinitionDto survey, string code) =>
        survey.AllQuestions().Single(q => q.Code == code);

    /// <summary>
    /// JSON description of the design structure with every id replaced by a readable reference
    /// (question code, page title, option label). Two designs with equal fingerprints are identical
    /// apart from their ids, title, slug and lifecycle fields.
    /// </summary>
    public static string StructureFingerprint(this SurveyDefinitionDto survey)
    {
        var questions = survey.AllQuestions().ToDictionary(q => q.Id);
        var options = survey.AllQuestions().SelectMany(q => q.Options).ToDictionary(o => o.Id);

        string TargetOf(LogicRuleDto rule) => rule.TargetQuestionId is { } q
            ? "question:" + questions[q].Code
            : "page:" + survey.Sections.Single(s => s.Id == rule.TargetSectionId).Title;

        var shape = new
        {
            survey.Description,
            survey.AllowAnonymous,
            survey.AllowMultipleResponses,
            survey.ShowProgressBar,
            survey.ShowQuestionNumbers,
            survey.OpensAt,
            survey.ClosesAt,
            survey.MaxResponses,
            survey.WelcomeMessage,
            survey.ThankYouMessage,
            Sections = survey.Sections.Select(s => new
            {
                s.Title,
                s.Description,
                s.Order,
                Questions = s.Questions.Select(q => new
                {
                    q.Code, q.Type, q.Text, q.Description, q.Order, q.IsRequired, q.Settings,
                    Options = q.Options.Select(o => new { o.Text, o.Value, o.Order, o.AllowsFreeText, o.FreeTextPlaceholder }),
                }),
            }),
            Rules = survey.LogicRules
                .Select(r => new
                {
                    Target = TargetOf(r),
                    r.Action,
                    r.MatchType,
                    Conditions = r.Conditions
                        .Select(c => new
                        {
                            Source = questions[c.SourceQuestionId].Code,
                            c.Operator,
                            Option = c.OptionId is { } o ? options[o].Text : null,
                            c.Value,
                        })
                        .OrderBy(c => c.Source).ThenBy(c => c.Operator).ThenBy(c => c.Option),
                })
                .OrderBy(r => r.Target),
        };

        return JsonSerializer.Serialize(shape);
    }
}
