using Microsoft.Extensions.Logging.Abstractions;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Reports.Charts;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Infrastructure.Exports;
using SmartSurvey.UnitTests.Responses;
using SmartSurvey.UnitTests.TestSupport;
using static SmartSurvey.UnitTests.Responses.ResponseTestData;

namespace SmartSurvey.UnitTests.Reports;

/// <summary>
/// Per-test arrangement for report tests: the response harness (SQLite, users, survey seeding) plus
/// the real report engine, chart renderer, all exporters and <see cref="ReportService"/> (admin by
/// default, product name "Acme Surveys").
/// </summary>
internal sealed class ReportTestHarness : IAsyncDisposable
{
    public const string ProductName = "Acme Surveys";

    private ReportTestHarness(ResponseTestHarness responses, IEnumerable<IReportExporter>? exporters)
    {
        Responses = responses;
        Engine = new ReportEngine(Db, Db.Time, NullLogger<ReportEngine>.Instance);
        Exporters = exporters?.ToList() ??
        [
            new PdfReportExporter(Charts),
            new CsvReportExporter(),
            new TxtReportExporter(),
            new XlsxReportExporter(),
            new JsonReportExporter(),
        ];
        Service = new ReportService(
            Db, Db.CurrentUser, Db.Time, Audit, Engine, new StubBrandingService(ProductName), Exporters,
            new ReportDefinitionValidator(), NullLogger<ReportService>.Instance);
    }

    public ResponseTestHarness Responses { get; }

    public SqliteTestDatabase Db => Responses.Db;

    public RecordingAuditService Audit => Responses.Audit;

    public TestCurrentUser User => Db.CurrentUser;

    public SvgChartRenderer Charts { get; } = new();

    public ReportEngine Engine { get; }

    public List<IReportExporter> Exporters { get; }

    public ReportService Service { get; }

    public DateTime Now => Db.UtcNow;

    /// <summary>Creates the harness (users seeded); pass <paramref name="exporters"/> to replace the default set.</summary>
    public static async Task<ReportTestHarness> CreateAsync(IEnumerable<IReportExporter>? exporters = null) =>
        new(await ResponseTestHarness.CreateAsync(), exporters);

    /// <summary>
    /// Seeds the customer feedback survey with four completed responses and one draft:
    /// <list type="table">
    /// <item>R1 (respondent, 3 days ago): Q1 Yes, Q2 Reports + Other "Exports", Q4 5, Q6 30, Q7 Europe</item>
    /// <item>R2 (anonymous, 2 days ago): Q1 Yes, Q2 Logic, Q4 4, Q6 40, Q7 Asia</item>
    /// <item>R3 (other user, 1 day ago): Q1 No, Q3 "Too slow", Q4 2, Q6 50, Q7 Other "Oceania"</item>
    /// <item>R4 (anonymous, 1 day ago): Q1 Yes, Q2 Reports, Q4 4, Q7 Europe</item>
    /// <item>D1 (anonymous draft, started today): Q1 No</item>
    /// </list>
    /// </summary>
    public async Task<SampleSurvey> SeedSurveyWithResponsesAsync(string title = "Customer feedback")
    {
        var s = await Responses.SeedSurveyAsync(title: title);

        await Completed(s, TestCurrentUser.RespondentId, daysAgo: 3,
            StoredChoice(s.Enjoy, (s.Yes, null)),
            StoredChoice(s.Features, (s.Reports, null), (s.OtherFeature, "Exports")),
            StoredNumber(s.Rating, 5),
            StoredNumber(s.Age, 30),
            StoredChoice(s.Region, (s.Europe, null)));
        await Completed(s, null, daysAgo: 2,
            StoredChoice(s.Enjoy, (s.Yes, null)),
            StoredChoice(s.Features, (s.Logic, null)),
            StoredNumber(s.Rating, 4),
            StoredNumber(s.Age, 40),
            StoredChoice(s.Region, (s.Asia, null)));
        await Completed(s, ResponseTestHarness.OtherUserId, daysAgo: 1,
            StoredChoice(s.Enjoy, (s.No, null)),
            StoredText(s.WhyNot, "Too slow"),
            StoredNumber(s.Rating, 2),
            StoredNumber(s.Age, 50),
            StoredChoice(s.Region, (s.OtherRegion, "Oceania")));
        await Completed(s, null, daysAgo: 1,
            StoredChoice(s.Enjoy, (s.Yes, null)),
            StoredChoice(s.Features, (s.Reports, null)),
            StoredNumber(s.Rating, 4),
            StoredChoice(s.Region, (s.Europe, null)));
        await Responses.SeedResponseAsync(s, null, ResponseStatus.InProgress, r =>
        {
            r.StartedAt = Now.AddHours(-1);
            r.Answers = [StoredChoice(s.Enjoy, (s.No, null))];
        });

        return s;
    }

    /// <summary>A report definition over the survey with the given widgets.</summary>
    public static ReportDefinitionDto Definition(SampleSurvey s, params ReportWidgetDto[] widgets) => new()
    {
        Name = "Feedback report",
        SurveyId = s.Definition.Id,
        Widgets = widgets.Select((w, i) =>
        {
            w.Order = i;
            return w;
        }).ToList(),
    };

    /// <summary>A widget of the given type.</summary>
    public static ReportWidgetDto Widget(WidgetType type, Guid? questionId = null, Guid? secondaryId = null, string title = "") => new()
    {
        Type = type,
        QuestionId = questionId,
        SecondaryQuestionId = secondaryId,
        Title = title,
    };

    /// <inheritdoc />
    public ValueTask DisposeAsync() => Responses.DisposeAsync();

    private Task<SurveyResponse> Completed(SampleSurvey s, Guid? respondentId, int daysAgo, params Answer[] answers) =>
        Responses.SeedResponseAsync(s, respondentId, ResponseStatus.Completed, r =>
        {
            r.StartedAt = Now.AddDays(-daysAgo).AddMinutes(-4);
            r.SubmittedAt = Now.AddDays(-daysAgo);
            r.Answers = answers.ToList();
        });
}
