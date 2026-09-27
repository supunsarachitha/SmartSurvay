using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Components.Admin.Reports;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>Tests of <see cref="ReportDesign"/> (report builder operations) and <see cref="ReportErrors"/>.</summary>
public sealed class ReportDesignTests
{
    private readonly SampleSurvey _s = SampleSurveys.CustomerFeedback();
    private readonly ReportDefinitionDto _report = new() { Name = "R" };

    public ReportDesignTests()
    {
        _s.Definition.Id = Guid.NewGuid();
        _report.SurveyId = _s.Definition.Id;
    }

    [Theory]
    [InlineData(WidgetType.PieChart, QuestionType.LongText, false)]
    [InlineData(WidgetType.BarChart, QuestionType.Rating, true)]
    [InlineData(WidgetType.HorizontalBarChart, QuestionType.Date, true)]
    [InlineData(WidgetType.CrossTab, QuestionType.Rating, false)]
    [InlineData(WidgetType.CrossTab, QuestionType.Checkbox, true)]
    [InlineData(WidgetType.QuestionTable, QuestionType.ShortText, true)]
    [InlineData(WidgetType.TextResponses, QuestionType.Number, true)]
    public void Widget_types_support_suitable_questions(WidgetType widget, QuestionType question, bool expected) =>
        Assert.Equal(expected, ReportDesign.Supports(widget, question));

    [Fact]
    public void New_widgets_use_the_first_suitable_questions()
    {
        var pie = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.PieChart);
        var text = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.TextResponses);
        var crossTab = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.CrossTab);
        var summary = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.SummaryStats);

        Assert.Equal(_s.Enjoy.Id, pie.QuestionId);
        Assert.Equal(_s.Enjoy.Id, text.QuestionId);
        Assert.Equal((_s.Enjoy.Id, _s.Features.Id), (crossTab.QuestionId!.Value, crossTab.SecondaryQuestionId!.Value));
        Assert.Null(summary.QuestionId);
        Assert.Equal([0, 1, 2, 3], _report.Widgets.Select(w => w.Order));
    }

    [Fact]
    public void Changing_the_type_keeps_compatible_questions_and_replaces_the_others()
    {
        var widget = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.TextResponses);
        widget.QuestionId = _s.WhyNot.Id;

        ReportDesign.ChangeType(widget, _s.Definition, WidgetType.QuestionTable);
        Assert.Equal(_s.WhyNot.Id, widget.QuestionId); // tables take text questions

        ReportDesign.ChangeType(widget, _s.Definition, WidgetType.PieChart);
        Assert.Equal(_s.Enjoy.Id, widget.QuestionId); // charts don't

        widget.QuestionId = _s.Region.Id;
        ReportDesign.ChangeType(widget, _s.Definition, WidgetType.CrossTab);
        Assert.Equal((_s.Region.Id, _s.Enjoy.Id), (widget.QuestionId!.Value, widget.SecondaryQuestionId!.Value));

        ReportDesign.ChangeType(widget, _s.Definition, WidgetType.LineChart);
        Assert.Null(widget.QuestionId);
        Assert.Null(widget.SecondaryQuestionId);
    }

    [Fact]
    public void Widgets_needing_missing_question_kinds_cannot_be_added()
    {
        var textOnly = new SurveyDefinitionDto
        {
            Sections = [new SectionDto { Questions = [new QuestionDto { Type = QuestionType.LongText, Text = "Comments" }] }],
        };
        var oneChoice = new SurveyDefinitionDto
        {
            Sections = [new SectionDto { Questions = [new QuestionDto { Type = QuestionType.Radio, Text = "Pick" }] }],
        };

        Assert.False(ReportDesign.CanAdd(textOnly, WidgetType.PieChart));
        Assert.True(ReportDesign.CanAdd(textOnly, WidgetType.TextResponses));
        Assert.True(ReportDesign.CanAdd(textOnly, WidgetType.SummaryStats));
        Assert.False(ReportDesign.CanAdd(oneChoice, WidgetType.CrossTab));
        Assert.True(ReportDesign.CanAdd(_s.Definition, WidgetType.CrossTab));
    }

    [Fact]
    public void Widgets_can_be_moved_duplicated_and_removed()
    {
        var a = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.SummaryStats);
        var b = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.BarChart);
        b.Title = "Enjoyment";
        b.Settings.TopN = 3;

        Assert.False(ReportDesign.Move(_report, a, -1));
        Assert.True(ReportDesign.Move(_report, b, -1));
        Assert.Equal([b, a], _report.Widgets);

        var copy = ReportDesign.Duplicate(_report, b);
        Assert.Equal([b, copy, a], _report.Widgets);
        Assert.NotEqual(b.Id, copy.Id);
        Assert.Equal("Enjoyment (copy)", copy.Title);
        Assert.Equal(3, copy.Settings.TopN);
        Assert.NotSame(b.Settings, copy.Settings);

        ReportDesign.Remove(_report, b);
        Assert.Equal([copy, a], _report.Widgets);
        Assert.Equal([0, 1], _report.Widgets.Select(w => w.Order));
    }

    [Fact]
    public void Start_blank_adds_summary_and_trend()
    {
        ReportDesign.AddWidget(_report, _s.Definition, WidgetType.PieChart);

        ReportDesign.StartBlank(_report, _s.Definition);

        Assert.Equal([WidgetType.SummaryStats, WidgetType.LineChart], _report.Widgets.Select(w => w.Type));
    }

    [Fact]
    public void Switching_survey_drops_filters_and_rebinds_widgets()
    {
        var other = SampleSurveys.CustomerFeedback("Other survey");
        other.Definition.Id = Guid.NewGuid();
        var pie = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.PieChart);
        var raw = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.RawResponses);
        raw.Settings.ColumnQuestionIds.Add(_s.Age.Id);
        _report.Filters.AnswerFilters.Add(ReportDesign.NewFilter(_s.Enjoy));

        ReportDesign.ApplySurvey(_report, other.Definition);

        Assert.Equal(other.Definition.Id, _report.SurveyId);
        Assert.Empty(_report.Filters.AnswerFilters);
        Assert.Equal(other.Enjoy.Id, pie.QuestionId);
        Assert.Empty(raw.Settings.ColumnQuestionIds);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var widget = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.RawResponses);
        widget.Settings.ColumnQuestionIds.Add(_s.Age.Id);
        _report.Filters.AnswerFilters.Add(ReportDesign.NewFilter(_s.Enjoy));

        var copy = ReportDesign.Clone(_report);
        copy.Widgets[0].Settings.ColumnQuestionIds.Clear();
        copy.Filters.AnswerFilters[0].OptionId = null;
        copy.Widgets.Clear();

        Assert.Single(_report.Widgets);
        Assert.Single(widget.Settings.ColumnQuestionIds);
        Assert.Equal(_s.Yes.Id, _report.Filters.AnswerFilters[0].OptionId);
    }

    [Fact]
    public void Titles_fall_back_to_the_question_and_then_the_widget_type()
    {
        var pie = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.PieChart);
        var summary = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.SummaryStats);

        Assert.Equal("Q1. Did you enjoy the product?", ReportDesign.TitleOf(pie, _s.Definition));
        Assert.Equal("Summary statistics (KPIs)", ReportDesign.TitleOf(summary, _s.Definition));
        pie.Title = "Enjoyment";
        Assert.Equal("Enjoyment", ReportDesign.TitleOf(pie, _s.Definition));
        Assert.Equal("Q1. Did you enjoy the product?", ReportDesign.DefaultTitle(pie, _s.Definition));
    }

    [Fact]
    public void Filters_start_with_the_first_comparison_and_reset_when_the_question_changes()
    {
        var filter = ReportDesign.NewFilter(_s.Enjoy);
        Assert.Equal((ConditionOperator.Equals, _s.Yes.Id), (filter.Operator, filter.OptionId!.Value));

        filter.Value = "leftover";
        ReportDesign.ResetFilter(filter, _s.Age);

        Assert.Equal(_s.Age.Id, filter.QuestionId);
        Assert.Equal(_s.Age.Type.SupportedOperators()[0], filter.Operator);
        Assert.Null(filter.OptionId);
        Assert.Null(filter.Value);
    }

    [Fact]
    public void Settings_are_offered_only_where_they_apply()
    {
        Assert.True(ReportDesign.UsesDistributionOptions(WidgetType.PieChart, QuestionType.Rating));
        Assert.False(ReportDesign.UsesDistributionOptions(WidgetType.QuestionTable, QuestionType.LongText));
        Assert.True(ReportDesign.UsesFreeText(WidgetType.BarChart, _s.Features)); // has an "Other" option
        Assert.False(ReportDesign.UsesFreeText(WidgetType.BarChart, _s.Enjoy));
        Assert.True(ReportDesign.UsesDataTable(WidgetType.PieChart, QuestionType.Radio));
        Assert.False(ReportDesign.UsesDataTable(WidgetType.QuestionTable, QuestionType.Radio));
        Assert.True(ReportDesign.UsesTimeGrouping(WidgetType.LineChart));
        Assert.True(ReportDesign.UsesMaxRows(WidgetType.QuestionTable, _s.WhyNot, new WidgetSettings()));
        Assert.True(ReportDesign.UsesMaxRows(WidgetType.BarChart, _s.Features, new WidgetSettings { IncludeFreeText = true }));
        Assert.False(ReportDesign.UsesMaxRows(WidgetType.BarChart, _s.Features, new WidgetSettings { IncludeFreeText = false }));
        Assert.True(ReportDesign.UsesColumns(WidgetType.RawResponses));
        Assert.False(ReportDesign.UsesColumns(WidgetType.TextResponses));
    }

    [Fact]
    public void Server_errors_are_sorted_by_field_widget_and_filter()
    {
        var first = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.PieChart);
        var second = ReportDesign.AddWidget(_report, _s.Definition, WidgetType.RawResponses);

        var errors = ReportErrors.From(new Dictionary<string, string[]>
        {
            ["Name"] = ["Please enter a name for the report."],
            ["Widgets[1].Settings.MaxRows"] = ["Too many rows."],
            ["Widgets[1].QuestionId"] = ["Bad question."],
            ["Widgets[7].QuestionId"] = ["Out of range."],
            ["Filters.AnswerFilters[0].OptionId"] = ["Bad option."],
            ["Filters.To"] = ["The start date must be on or before the end date."],
            ["Widgets"] = ["Too many widgets."],
        }, _report);

        Assert.Equal(["Please enter a name for the report."], errors.For("Name"));
        Assert.Empty(errors.ForWidget(first.Id));
        Assert.Equal(["Too many rows.", "Bad question."], errors.ForWidget(second.Id));
        Assert.Equal(["Bad option."], errors.ForFilter(0));
        Assert.Equal(["The start date must be on or before the end date."], errors.For("Filters"));
        Assert.Equal(["Out of range.", "Too many widgets."], errors.General);
        Assert.Equal(7, errors.Count);
    }
}
