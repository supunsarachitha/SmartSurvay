using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.Identity;
using SmartSurvey.Infrastructure.Security;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Responses;

/// <summary>
/// Per-test arrangement for <see cref="ResponseService"/> tests: an isolated SQLite database seeded
/// with three users (admin, respondent, other user), a recording audit service and the service under
/// test wired to the database's clock and current user (admin by default).
/// </summary>
internal sealed class ResponseTestHarness : IAsyncDisposable
{
    /// <summary>Id of a second respondent (no display name, so lists fall back to the e-mail).</summary>
    public static readonly Guid OtherUserId = Guid.Parse("00000000-0000-0000-0000-00000000c001");

    public const string AdminEmail = "admin@test.local";
    public const string AdminName = "Ada Admin";
    public const string RespondentEmail = "user@test.local";
    public const string RespondentName = "Rita Respondent";
    public const string OtherEmail = "other@test.local";

    private int _surveyCounter;

    private ResponseTestHarness(IBotProtection? botProtection)
    {
        Service = new ResponseService(
            Db,
            Db.CurrentUser,
            Db.Time,
            Audit,
            new SaveResponseRequestValidator(),
            new ResponseQueryValidator(),
            AccessKeys,
            botProtection ?? new DisabledBotProtection(),
            NullLogger<ResponseService>.Instance);
    }

    public SqliteTestDatabase Db { get; } = new();

    /// <summary>Access keys of password-protected surveys (ephemeral key ring).</summary>
    public DataProtectionSurveyAccessKeys AccessKeys => _accessKeys ??= new DataProtectionSurveyAccessKeys(new EphemeralDataProtectionProvider(), Db.Time);

    private DataProtectionSurveyAccessKeys? _accessKeys;

    public RecordingAuditService Audit { get; } = new();

    public ResponseService Service { get; }

    public TestCurrentUser User => Db.CurrentUser;

    public DateTime Now => Db.UtcNow;

    /// <summary>Creates the harness and seeds the users.</summary>
    public static async Task<ResponseTestHarness> CreateAsync(IBotProtection? botProtection = null)
    {
        var harness = new ResponseTestHarness(botProtection);
        await harness.Db.SeedAsync(
            NewUser(TestCurrentUser.AdminId, AdminEmail, AdminName),
            NewUser(TestCurrentUser.RespondentId, RespondentEmail, RespondentName),
            NewUser(OtherUserId, OtherEmail, displayName: null));
        return harness;
    }

    /// <summary>
    /// Seeds <see cref="SampleSurveys.CustomerFeedback"/> as a published, open survey (published a day
    /// ago, anonymous responses allowed, one response per user). Ids and slug are written back to the
    /// returned handle's <see cref="SampleSurvey.Definition"/>.
    /// </summary>
    /// <param name="configure">Adjusts the survey entity (status, schedule, quota, flags…).</param>
    /// <param name="design">Adjusts the design before it is converted to entities.</param>
    /// <param name="title">Survey title.</param>
    /// <param name="workspaceId">Workspace of the survey (default <see cref="TestWorkspaces.DefaultId"/>).</param>
    public async Task<SampleSurvey> SeedSurveyAsync(
        Action<Survey>? configure = null, Action<SampleSurvey>? design = null, string title = "Customer feedback", Guid? workspaceId = null)
    {
        var sample = SampleSurveys.CustomerFeedback(title);
        design?.Invoke(sample);

        var survey = ToEntity(sample.Definition);
        configure?.Invoke(survey);
        await Db.SeedInWorkspaceAsync(workspaceId ?? TestWorkspaces.DefaultId, survey);

        sample.Definition.Id = survey.Id;
        sample.Definition.Slug = survey.Slug;
        return sample;
    }

    /// <summary>
    /// Seeds a response (started 30 minutes ago; completed ones submitted 5 minutes later).
    /// </summary>
    public async Task<SurveyResponse> SeedResponseAsync(
        SampleSurvey survey, Guid? respondentId, ResponseStatus status, Action<SurveyResponse>? configure = null, Guid? workspaceId = null)
    {
        var startedAt = Now.AddMinutes(-30);
        var response = new SurveyResponse
        {
            SurveyId = survey.Definition.Id,
            RespondentId = respondentId,
            Status = status,
            StartedAt = startedAt,
            SubmittedAt = status == ResponseStatus.Completed ? startedAt.AddMinutes(5) : null,
        };
        configure?.Invoke(response);
        await Db.SeedInWorkspaceAsync(workspaceId ?? TestWorkspaces.DefaultId, response);
        return response;
    }

    /// <summary>Loads a response with answers and selections straight from the database.</summary>
    public async Task<SurveyResponse?> FindResponseAsync(Guid id)
    {
        await using var db = Db.CreateContext();
        return await db.Responses.AsNoTracking()
            .Include(r => r.Answers).ThenInclude(a => a.Selections)
            .SingleOrDefaultAsync(r => r.Id == id);
    }

    /// <summary>All responses of a survey (with answers and selections).</summary>
    public async Task<List<SurveyResponse>> ResponsesOfAsync(SampleSurvey survey)
    {
        await using var db = Db.CreateContext();
        return await db.Responses.AsNoTracking()
            .Include(r => r.Answers).ThenInclude(a => a.Selections)
            .Where(r => r.SurveyId == survey.Definition.Id)
            .ToListAsync();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Db.DisposeAsync();

    private static ApplicationUser NewUser(Guid id, string email, string? displayName) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        DisplayName = displayName,
    };

    /// <summary>Converts a design DTO into a survey entity graph, keeping every id.</summary>
    private Survey ToEntity(SurveyDefinitionDto definition)
    {
        var survey = new Survey
        {
            Title = definition.Title,
            Description = definition.Description,
            Slug = $"{SlugGenerator.Generate(definition.Title)}-{++_surveyCounter}",
            Status = SurveyStatus.Published,
            PublishedAt = Now.AddDays(-1),
            AllowAnonymous = definition.AllowAnonymous,
            AllowMultipleResponses = definition.AllowMultipleResponses,
            WelcomeMessage = definition.WelcomeMessage,
            ThankYouMessage = definition.ThankYouMessage,
        };

        foreach (var section in definition.Sections)
        {
            survey.Sections.Add(new SurveySection
            {
                Id = section.Id, SurveyId = survey.Id, Title = section.Title, Description = section.Description, Order = section.Order,
            });

            foreach (var q in section.Questions)
            {
                survey.Questions.Add(new Question
                {
                    Id = q.Id,
                    SurveyId = survey.Id,
                    SectionId = section.Id,
                    Type = q.Type,
                    Text = q.Text,
                    Description = q.Description,
                    Code = q.Code ?? $"Q{survey.Questions.Count + 1}",
                    Order = q.Order,
                    IsRequired = q.IsRequired,
                    Settings = q.Settings.Clone(),
                    Options = q.Options.Select(o => new QuestionOption
                    {
                        Id = o.Id, QuestionId = q.Id, Text = o.Text, Value = o.Value, Order = o.Order,
                        AllowsFreeText = o.AllowsFreeText, FreeTextPlaceholder = o.FreeTextPlaceholder,
                    }).ToList(),
                });
            }
        }

        survey.LogicRules = definition.LogicRules.Select(r => new LogicRule
        {
            Id = r.Id,
            SurveyId = survey.Id,
            TargetQuestionId = r.TargetQuestionId,
            TargetSectionId = r.TargetSectionId,
            Action = r.Action,
            MatchType = r.MatchType,
            Conditions = r.Conditions.Select(c => new LogicCondition
            {
                Id = c.Id, LogicRuleId = r.Id, SourceQuestionId = c.SourceQuestionId, Operator = c.Operator, OptionId = c.OptionId, Value = c.Value,
            }).ToList(),
        }).ToList();

        return survey;
    }
}

/// <summary>Builders for answer payloads and stored answers used by the response tests.</summary>
internal static class ResponseTestData
{
    /// <summary>Selects options (no free text).</summary>
    public static AnswerInputDto Choice(QuestionDto question, params OptionDto[] options) => new()
    {
        QuestionId = question.Id,
        Selections = options.Select(o => new SelectionInputDto { OptionId = o.Id }).ToList(),
    };

    /// <summary>Selects options with optional free text.</summary>
    public static AnswerInputDto ChoiceWithText(QuestionDto question, params (OptionDto Option, string? FreeText)[] selections) => new()
    {
        QuestionId = question.Id,
        Selections = selections.Select(s => new SelectionInputDto { OptionId = s.Option.Id, FreeText = s.FreeText }).ToList(),
    };

    public static AnswerInputDto Text(QuestionDto question, string? text) => new() { QuestionId = question.Id, Text = text };

    public static AnswerInputDto Number(QuestionDto question, double number) => new() { QuestionId = question.Id, Number = number };

    public static AnswerInputDto Date(QuestionDto question, DateOnly date) => new() { QuestionId = question.Id, Date = date };

    public static SaveResponseRequest Request(params AnswerInputDto[] answers) => new() { Answers = answers.ToList() };

    /// <summary>
    /// A complete, valid answer set for the customer feedback survey (Q1 = Yes, so the conditional
    /// "Why not?" question stays hidden).
    /// </summary>
    public static SaveResponseRequest ValidRequest(SampleSurvey s, Guid? responseId = null)
    {
        var request = Request(
            Choice(s.Enjoy, s.Yes),
            ChoiceWithText(s.Features, (s.Reports, null), (s.OtherFeature, "Exports")),
            Number(s.Rating, 4),
            Text(s.Email, "jane@example.com"),
            Number(s.Age, 42),
            ChoiceWithText(s.Region, (s.OtherRegion, "Oceania")));
        request.ResponseId = responseId;
        return request;
    }

    /// <summary>Adds a Date question ("Purchase date", Q8) at the end of page 2.</summary>
    public static QuestionDto AddDateQuestion(SampleSurvey s)
    {
        var question = new QuestionDto { Type = QuestionType.Date, Text = "Purchase date", Code = "Q8", Order = 4 };
        s.Page2.Questions.Add(question);
        return question;
    }

    public static Answer StoredText(QuestionDto question, string text) => new() { QuestionId = question.Id, TextValue = text };

    public static Answer StoredNumber(QuestionDto question, double number) => new() { QuestionId = question.Id, NumberValue = number };

    public static Answer StoredDate(QuestionDto question, DateOnly date) => new() { QuestionId = question.Id, DateValue = date };

    public static Answer StoredChoice(QuestionDto question, params (OptionDto Option, string? FreeText)[] selections) => new()
    {
        QuestionId = question.Id,
        Selections = selections.Select(s => new AnswerSelection { OptionId = s.Option.Id, FreeText = s.FreeText }).ToList(),
    };

    /// <summary>The stored answer to a question (fails when missing).</summary>
    public static Answer AnswerTo(SurveyResponse response, QuestionDto question) =>
        Assert.Single(response.Answers, a => a.QuestionId == question.Id);
}
