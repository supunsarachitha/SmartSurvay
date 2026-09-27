using SmartSurvey.Application.Reports;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;
using SmartSurvey.UnitTests.TestSupport;
using static SmartSurvey.UnitTests.Reports.ReportTestHarness;

namespace SmartSurvey.UnitTests.Reports;

/// <summary>Report engine calculations over the seeded feedback survey (4 completed responses + 1 draft).</summary>
public sealed class ReportEngineTests : IAsyncLifetime
{
    private ReportTestHarness _h = null!;
    private SampleSurvey _s = null!;

    public async Task InitializeAsync()
    {
        _h = await CreateAsync();
        _s = await _h.SeedSurveyWithResponsesAsync();
    }

    public async Task DisposeAsync() => await _h.DisposeAsync();

    [Fact]
    public async Task Summary_counts_filtered_responses_and_completion_rate_over_all_responses()
    {
        var widget = await RunSingle(Widget(WidgetType.SummaryStats));

        Assert.Equal("4", Stat(widget, "Responses"));
        Assert.Equal("4", Stat(widget, "Completed"));
        Assert.Equal("80.0%", Stat(widget, "Completion rate"));
        Assert.Equal(_h.Now.AddDays(-3).ToString("yyyy-MM-dd HH:mm"), Stat(widget, "First response"));
    }

    [Fact]
    public async Task Choice_chart_counts_each_option_with_a_data_table()
    {
        var widget = await RunSingle(Widget(WidgetType.PieChart, _s.Enjoy.Id));

        Assert.Equal(ChartKind.Pie, widget.Chart!.Kind);
        Assert.Equal(["Yes", "No"], widget.Chart.Labels);
        Assert.Equal([3d, 1d], widget.Chart.Series.Single().Values);
        Assert.Equal(["Option", "Count", "Percent"], widget.Table!.Columns);
        Assert.Equal(["Yes", "3", "75.0%"], widget.Table.Rows[0]);
        Assert.Equal(["Total", "4", "100.0%"], widget.Table.Footer);
    }

    [Fact]
    public async Task Chart_without_data_table_when_disabled()
    {
        var w = Widget(WidgetType.BarChart, _s.Enjoy.Id);
        w.Settings.ShowDataTable = false;

        var widget = await RunSingle(w);

        Assert.NotNull(widget.Chart);
        Assert.Null(widget.Table);
    }

    [Fact]
    public async Task Multi_select_percentages_use_respondents_and_list_other_free_texts()
    {
        var widget = await RunSingle(Widget(WidgetType.HorizontalBarChart, _s.Features.Id));

        Assert.Equal(3, widget.ResponseCount); // three respondents answered Q2
        Assert.Equal([2d, 1d, 1d], widget.Chart!.Series.Single().Values);
        Assert.Equal(["Reports", "2", "66.7%"], widget.Table!.Rows[0]);
        var freeTexts = Assert.Single(widget.ExtraTables);
        Assert.Equal(["Other", "Exports"], Assert.Single(freeTexts.Rows));
        Assert.Contains("more than one option", widget.Note);
    }

    [Fact]
    public async Task Rating_widget_reports_descriptive_statistics()
    {
        var widget = await RunSingle(Widget(WidgetType.BarChart, _s.Rating.Id));

        Assert.Equal("4", Stat(widget, "Count"));
        Assert.Equal("3.8", Stat(widget, "Mean")); // 3.75, shown with one decimal
        Assert.Equal("2", Stat(widget, "Min"));
        Assert.Equal("5", Stat(widget, "Max"));
        Assert.Equal(["1", "2", "3", "4", "5"], widget.Chart!.Labels); // zero-filled rating range
        Assert.Equal([0d, 1d, 0d, 2d, 1d], widget.Chart.Series.Single().Values);
    }

    [Fact]
    public async Task Answer_filters_restrict_every_widget()
    {
        var dto = Definition(_s, Widget(WidgetType.SummaryStats), Widget(WidgetType.QuestionTable, _s.Region.Id));
        dto.Filters.AnswerFilters.Add(new AnswerFilter { QuestionId = _s.Enjoy.Id, Operator = ConditionOperator.Equals, OptionId = _s.Yes.Id });

        var result = await _h.Engine.ExecuteAsync(dto);

        Assert.Equal(3, result.TotalResponses);
        Assert.Equal(["Europe", "2", "66.7%"], result.Widgets[1].Table!.Rows[0]);
        Assert.NotEmpty(result.FilterSummary);
    }

    [Fact]
    public async Task Include_in_progress_and_date_range_filters()
    {
        var withDrafts = Definition(_s, Widget(WidgetType.SummaryStats));
        withDrafts.Filters.IncludeInProgress = true;
        var recent = Definition(_s, Widget(WidgetType.SummaryStats));
        recent.Filters.From = DateOnly.FromDateTime(_h.Now.AddDays(-1));

        var draftsResult = await _h.Engine.ExecuteAsync(withDrafts);
        var recentResult = await _h.Engine.ExecuteAsync(recent);

        Assert.Equal(5, draftsResult.TotalResponses);
        Assert.Equal("1", Stat(draftsResult.Widgets[0], "In progress"));
        Assert.Equal(2, recentResult.TotalResponses);
    }

    [Fact]
    public async Task Cross_tab_counts_combinations_of_two_choice_questions()
    {
        var widget = await RunSingle(Widget(WidgetType.CrossTab, _s.Enjoy.Id, _s.Region.Id));

        Assert.Null(widget.Error);
        Assert.Equal(ChartKind.StackedBar, widget.Chart!.Kind);
        Assert.Equal("Did you enjoy the product? × Region", widget.Title);
        var yesRow = widget.Table!.Rows.Single(r => r[0] == "Yes");
        var europe = widget.Table.Columns.IndexOf("Europe");
        Assert.Equal("2", yesRow[europe]);
    }

    [Fact]
    public async Task Text_responses_list_the_latest_answers()
    {
        var widget = await RunSingle(Widget(WidgetType.TextResponses, _s.WhyNot.Id));

        Assert.Equal(1, widget.ResponseCount);
        Assert.Equal("Too slow", Assert.Single(widget.Table!.Rows)[1]);
    }

    [Fact]
    public async Task Raw_grid_has_one_row_per_response_newest_first()
    {
        var w = Widget(WidgetType.RawResponses);
        w.Settings.ColumnQuestionIds = [_s.Enjoy.Id, _s.Age.Id];

        var widget = await RunSingle(w);

        Assert.Equal(["Submitted", "Respondent", "Q1 · Did you enjoy the product?", "Q6 · Your age"], widget.Table!.Columns);
        Assert.Equal(4, widget.Table.Rows.Count);
        var oldest = widget.Table.Rows[^1]; // R1, submitted three days ago by the respondent
        Assert.Equal(["user@test.local", "Yes", "30"], oldest.Skip(1));
        Assert.Contains(widget.Table.Rows, r => r[1] == "Anonymous");
        Assert.Equal([3], widget.Table.NumericColumns);
    }

    [Fact]
    public async Task Line_chart_buckets_responses_by_day()
    {
        var widget = await RunSingle(Widget(WidgetType.LineChart));

        Assert.Equal(ChartKind.Line, widget.Chart!.Kind);
        Assert.Equal(4, widget.Chart.Series.Single().Values.Sum());
    }

    [Fact]
    public async Task Broken_widgets_report_errors_without_failing_the_report()
    {
        var dto = Definition(_s,
            Widget(WidgetType.BarChart, Guid.NewGuid()),
            Widget(WidgetType.PieChart, _s.Email.Id),
            Widget(WidgetType.SummaryStats));

        var result = await _h.Engine.ExecuteAsync(dto);

        Assert.Equal(ReportEngine.MissingQuestionError, result.Widgets[0].Error);
        Assert.NotNull(result.Widgets[1].Error); // charts are not available for text questions
        Assert.Null(result.Widgets[2].Error);
    }

    [Fact]
    public async Task Widget_title_defaults_to_the_question_text()
    {
        var widget = await RunSingle(Widget(WidgetType.QuestionTable, _s.Age.Id));

        Assert.Equal("Your age", widget.Title);
        Assert.Equal("Your age", widget.QuestionText);
    }

    private async Task<WidgetResult> RunSingle(ReportWidgetDto widget)
    {
        var result = await _h.Engine.ExecuteAsync(Definition(_s, widget));
        return Assert.Single(result.Widgets);
    }

    private static string Stat(WidgetResult widget, string label) => Assert.Single(widget.Stats, s => s.Label == label).Value;
}
