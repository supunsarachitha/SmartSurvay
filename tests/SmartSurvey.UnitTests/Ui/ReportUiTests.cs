using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Components.Admin.Reports;
using SmartSurvey.Web.Components.Pages.Admin.Reports;
using SmartSurvey.Web.Components.Shared;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>bUnit tests of the reports UI: viewer components, widget editor, builder and viewer pages.</summary>
public sealed class ReportUiTests : UiTestBase
{
    private readonly SampleSurvey _s = SampleSurveys.CustomerFeedback();
    private readonly FakeReportService _reports = new();
    private readonly FakeSurveyService _surveys = new();

    public ReportUiTests()
    {
        ReportBuilder.PreviewDelay = TimeSpan.Zero;
        _s.Definition.Id = Guid.NewGuid();
        _surveys.Surveys.Add(_s.Definition);
        Services.AddSingleton<IReportService>(_reports);
        Services.AddSingleton<ISurveyService>(_surveys);
        Services.AddScoped<ToastService>();
        SignInAsAdmin();
    }

    private string CurrentUri => Services.GetRequiredService<FakeNavigationManager>().Uri;

    private static ReportResult SampleResult() => new()
    {
        ReportName = "Q3 feedback",
        SurveyTitle = "Customer feedback",
        TotalResponses = 12,
        GeneratedAt = new DateTime(2026, 9, 27, 10, 0, 0, DateTimeKind.Utc),
        FilterSummary = ["Completed responses only"],
        Widgets =
        [
            new WidgetResult
            {
                WidgetId = Guid.NewGuid(), Title = "Summary", Type = WidgetType.SummaryStats, ResponseCount = 12,
                Stats = [new StatItem("Responses", "12"), new StatItem("Completion rate", "80 %")],
            },
            new WidgetResult
            {
                WidgetId = Guid.NewGuid(), Title = "Did you enjoy it?", Type = WidgetType.PieChart, ResponseCount = 10,
                Chart = new ChartData { Kind = ChartKind.Pie, Labels = ["Yes", "No"], Series = [new ChartSeries { Name = "Responses", Values = [7, 3] }] },
                Table = new TableData { Columns = ["Answer", "Count"], Rows = [["Yes", "7"], ["No", "3"]], NumericColumns = [1] },
                Note = "2 respondents skipped this question.",
            },
            new WidgetResult { WidgetId = Guid.NewGuid(), Title = "Broken", Type = WidgetType.BarChart, Error = "The question used by this widget no longer exists." },
        ],
    };

    [Fact]
    public void Report_view_renders_kpis_charts_tables_notes_and_errors()
    {
        var cut = RenderComponent<ReportView>(p => p.Add(x => x.Result, SampleResult()));

        Assert.Contains("12 responses", cut.Markup);
        Assert.Contains("Completed responses only", cut.Find(".alert-light").TextContent);
        Assert.Equal(["Responses", "Completion rate"], cut.FindAll(".kpi-label").Select(k => k.TextContent));
        Assert.NotNull(cut.Find(".chart-container svg"));
        Assert.Contains("text-end", cut.FindAll(".report-table td")[1].ClassName); // numeric column
        Assert.Contains("2 respondents skipped", cut.Markup);
        Assert.Contains("no longer exists", cut.Find(".alert-warning").TextContent);

        var cards = cut.FindAll(".widget-card");
        Assert.Contains("widget-wide", cards[0].ClassName); // KPIs span the row
        Assert.DoesNotContain("widget-wide", cards[1].ClassName); // pie charts share it
    }

    [Fact]
    public void Report_view_explains_empty_reports()
    {
        var cut = RenderComponent<ReportView>(p => p.Add(x => x.Result, new ReportResult { SurveyTitle = "S" }));

        Assert.Contains("No widgets yet", cut.Markup);
    }

    [Fact]
    public void Widget_editor_offers_the_settings_of_its_type()
    {
        var report = new ReportDefinitionDto();
        var pie = ReportDesign.AddWidget(report, _s.Definition, WidgetType.PieChart);

        var cut = RenderComponent<WidgetEditor>(p => p.Add(x => x.Widget, pie).Add(x => x.Survey, _s.Definition).Add(x => x.Active, true));

        Assert.Contains("Q1. Did you enjoy the product?", cut.Find(".qe-header").TextContent);
        Assert.Contains("Show percentages", cut.Markup);
        Assert.Contains("Show data table", cut.Markup);
        Assert.DoesNotContain("“Other” answers", cut.Markup); // Q1 has no free-text option
        Assert.DoesNotContain("Maximum rows", cut.Markup);

        cut.Find($"#wq-{pie.Id}").Change(_s.Features.Id.ToString());
        Assert.Contains("“Other” answers", cut.Markup);

        cut.Find($"#wt-{pie.Id}").Change(WidgetType.RawResponses.ToString());
        Assert.Equal(WidgetType.RawResponses, pie.Type);
        Assert.Null(pie.QuestionId);
        Assert.Contains("Maximum rows", cut.Markup);
        Assert.Equal(7, cut.FindAll(".column-picker input").Count);
    }

    [Fact]
    public void Cross_tab_editor_asks_for_two_different_questions()
    {
        var report = new ReportDefinitionDto();
        var widget = ReportDesign.AddWidget(report, _s.Definition, WidgetType.CrossTab);

        var cut = RenderComponent<WidgetEditor>(p => p.Add(x => x.Widget, widget).Add(x => x.Survey, _s.Definition).Add(x => x.Active, true));
        cut.Find($"#wq-{widget.Id}").Change(_s.Features.Id.ToString());

        Assert.Equal(_s.Features.Id, widget.QuestionId);
        Assert.NotEqual(widget.QuestionId, widget.SecondaryQuestionId);
        Assert.DoesNotContain($"value=\"{_s.Features.Id}\"", cut.Find($"#wq2-{widget.Id}").InnerHtml);
    }

    [Fact]
    public void New_report_for_a_survey_starts_small_previews_and_is_created()
    {
        NavigateTo($"admin/reports/new?surveyId={_s.Definition.Id}");
        var cut = RenderComponent<ReportBuilder>();

        Assert.Equal("Customer feedback report", cut.Find("#r-name").GetAttribute("value"));
        Assert.Contains("Recommended overview", cut.Markup);

        cut.FindAll("button").First(b => b.TextContent.Contains("Start small")).Click();

        Assert.Equal(2, cut.FindAll(".question-editor").Count);
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll(".report-preview .widget-card").Count));
        Assert.Contains("7 responses", cut.Find(".report-preview").TextContent);

        cut.FindAll("button").First(b => b.TextContent.Contains("Create report")).Click();

        var saved = Assert.Single(_reports.Saved);
        Assert.Equal(_s.Definition.Id, saved.SurveyId);
        Assert.Equal([WidgetType.SummaryStats, WidgetType.LineChart], saved.Widgets.Select(w => w.Type));
        Assert.EndsWith($"/admin/reports/{saved.Id}/edit", CurrentUri);
    }

    [Fact]
    public void Recommended_overview_uses_the_default_report()
    {
        _reports.DefaultReport = new ReportDefinitionDto
        {
            Description = "Automatically generated overview of every question.",
            Widgets = [new ReportWidgetDto { Type = WidgetType.SummaryStats }, new ReportWidgetDto { Type = WidgetType.PieChart, QuestionId = _s.Enjoy.Id }],
        };
        NavigateTo($"admin/reports/new?surveyId={_s.Definition.Id}");
        var cut = RenderComponent<ReportBuilder>();

        cut.FindAll("button").First(b => b.TextContent.Contains("Recommended overview")).Click();

        Assert.Equal(2, cut.FindAll(".question-editor").Count);
        Assert.Equal("Automatically generated overview of every question.", cut.Find("#r-desc").GetAttribute("value") ?? cut.Find("#r-desc").TextContent);
    }

    [Fact]
    public void Validation_errors_are_shown_next_to_their_fields_and_widgets()
    {
        NavigateTo($"admin/reports/new?surveyId={_s.Definition.Id}");
        var cut = RenderComponent<ReportBuilder>();
        cut.FindAll("button").First(b => b.TextContent.Contains("Start small")).Click();
        _reports.SaveError = new AppValidationException(new Dictionary<string, string[]>
        {
            ["Name"] = ["Please enter a name for the report."],
            ["Widgets[1].Settings.MaxRows"] = ["The maximum number of rows must be between 1 and 1000."],
        });

        cut.FindAll("button").First(b => b.TextContent.Contains("Create report")).Click();

        Assert.Contains("Please enter a name for the report.", cut.Markup);
        Assert.Contains("is-invalid", cut.Find("#r-name").ClassName);
        var failing = cut.FindAll(".question-editor")[1];
        Assert.Contains("active", failing.ClassName); // opened to show the problem
        Assert.Contains("between 1 and 1000", failing.TextContent);
        Assert.Empty(_reports.Saved);
    }

    [Fact]
    public void Existing_report_is_loaded_and_saved()
    {
        var existing = new ReportDefinitionDto { Id = Guid.NewGuid(), Name = "Q3 feedback", SurveyId = _s.Definition.Id };
        ReportDesign.AddWidget(existing, _s.Definition, WidgetType.PieChart);
        _reports.Existing = existing;

        var cut = RenderComponent<ReportBuilder>(p => p.Add(x => x.Id, existing.Id));

        Assert.Contains("Customer feedback", cut.Find(".form-control-plaintext").TextContent);
        var save = cut.FindAll("button").First(b => b.TextContent.Contains("Saved"));
        Assert.True(save.HasAttribute("disabled"));

        cut.Find("#r-name").Input("Q3 feedback (final)");
        cut.FindAll("button").First(b => b.TextContent.Contains("Save changes")).Click();

        Assert.Equal("Q3 feedback (final)", Assert.Single(_reports.Saved).Name);
    }

    [Fact]
    public void Viewer_shows_the_report_and_exports_it()
    {
        var id = Guid.NewGuid();
        _reports.RunResult = SampleResult();

        var cut = RenderComponent<ReportViewer>(p => p.Add(x => x.Id, id));

        Assert.Contains("Q3 feedback", cut.Find("h1").TextContent);
        Assert.Equal(3, cut.FindAll(".widget-card").Count);

        cut.FindAll(".dropdown-item").First(i => i.TextContent.Contains("PDF")).Click();

        Assert.Equal((id, ExportFormat.Pdf), Assert.Single(_reports.Exports));
        Assert.Contains(JSInterop.Invocations, i => i.Identifier == "SmartSurvey.downloadFile");
    }
}
