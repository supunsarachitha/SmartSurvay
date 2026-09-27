using SmartSurvey.Application.Reports.Engine;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports;

/// <summary>
/// Builds the automatic "default report" of a survey: summary KPIs, responses over time and one
/// widget per question chosen by question type.
/// </summary>
internal static class DefaultReportBuilder
{
    /// <summary>Single-choice questions with at most this many options are shown as pie charts.</summary>
    public const int MaxPieOptions = 6;

    /// <summary>Builds the (unsaved) definition.</summary>
    public static ReportDefinitionDto Build(SurveyDefinitionDto survey)
    {
        var widgets = new List<ReportWidgetDto>
        {
            new() { Title = "Summary", Type = WidgetType.SummaryStats },
            new() { Title = "Responses over time", Type = WidgetType.LineChart, Settings = new WidgetSettings { TimeGrouping = TimeGrouping.Day } },
        };
        widgets.AddRange(survey.AllQuestions().Select(QuestionWidget));

        for (var i = 0; i < widgets.Count; i++)
        {
            widgets[i].Order = i;
        }

        return new ReportDefinitionDto
        {
            Name = ReportFormat.Truncate($"{survey.Title} – overview", ReportLimits.NameMaxLength),
            Description = "Automatically generated overview of every question.",
            SurveyId = survey.Id,
            Filters = new ReportFilterSet(),
            Widgets = widgets,
        };
    }

    /// <summary>Widget for one question (type chosen by <see cref="WidgetTypeFor"/>), titled "Q1. Question text".</summary>
    private static ReportWidgetDto QuestionWidget(QuestionDto question) => new()
    {
        Title = ReportFormat.Truncate(string.IsNullOrWhiteSpace(question.Code) ? question.Text : $"{question.Code}. {question.Text}", ReportLimits.WidgetTitleMaxLength),
        Type = WidgetTypeFor(question),
        QuestionId = question.Id,
        Settings = new WidgetSettings(),
    };

    /// <summary>
    /// Small single-choice questions → pie; other choice questions → horizontal bars; ratings and
    /// scales → bars; numbers and dates → question table; text → text responses.
    /// </summary>
    private static WidgetType WidgetTypeFor(QuestionDto question) => question.Type switch
    {
        QuestionType.Radio or QuestionType.Dropdown when question.Options.Count <= MaxPieOptions => WidgetType.PieChart,
        _ when question.Type.IsChoice() => WidgetType.HorizontalBarChart,
        QuestionType.Rating or QuestionType.Scale => WidgetType.BarChart,
        QuestionType.Number or QuestionType.Date => WidgetType.QuestionTable,
        _ => WidgetType.TextResponses,
    };
}
