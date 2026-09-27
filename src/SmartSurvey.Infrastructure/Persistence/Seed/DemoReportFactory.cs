using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;
using C = SmartSurvey.Infrastructure.Persistence.Seed.CustomerSurveyCatalog;

namespace SmartSurvey.Infrastructure.Persistence.Seed;

/// <summary>Builds the sample report of the demo data.</summary>
internal static class DemoReportFactory
{
    /// <summary>Name of the sample report.</summary>
    public const string CustomerOverviewName = "Customer Satisfaction Overview";

    /// <summary>
    /// "Customer Satisfaction Overview": KPIs, responses per day, satisfaction pie, feature usage, NPS
    /// distribution, satisfaction by region (cross-tab) and the improvement suggestions.
    /// </summary>
    /// <param name="customer">The demo customer survey (questions must be present).</param>
    /// <param name="utcNow">Seeding time (UTC).</param>
    /// <param name="createdById">Administrator recorded as creator.</param>
    public static ReportDefinition CustomerOverview(Survey customer, DateTime utcNow, Guid? createdById)
    {
        var report = new ReportDefinition
        {
            Name = CustomerOverviewName,
            Description = "How satisfied are our customers, which features do they use and what should we improve? Completed responses only.",
            SurveyId = customer.Id,
            Filters = new ReportFilterSet(),
            CreatedAt = utcNow.AddDays(-7),
            CreatedById = createdById,
        };

        var satisfaction = customer.QuestionByCode(C.Satisfaction);

        AddWidget(report, WidgetType.SummaryStats, "Key figures");
        AddWidget(report, WidgetType.LineChart, "Responses per day", configure: s => s.TimeGrouping = TimeGrouping.Day);
        AddWidget(report, WidgetType.PieChart, "Overall satisfaction", satisfaction);
        AddWidget(report, WidgetType.HorizontalBarChart, "Features in use", customer.QuestionByCode(C.Features),
            configure: s => s.SortOrder = WidgetSortOrder.CountDescending);
        AddWidget(report, WidgetType.BarChart, "Likelihood to recommend (0–10)", customer.QuestionByCode(C.Recommend));
        AddWidget(report, WidgetType.CrossTab, "Satisfaction by region", satisfaction, customer.QuestionByCode(C.Region));
        AddWidget(report, WidgetType.TextResponses, "What could we improve?", customer.QuestionByCode(C.Improvement),
            configure: s => s.MaxRows = 50);

        return report;
    }

    private static void AddWidget(
        ReportDefinition report,
        WidgetType type,
        string title,
        Question? question = null,
        Question? secondaryQuestion = null,
        Action<WidgetSettings>? configure = null)
    {
        var widget = new ReportWidget
        {
            ReportId = report.Id,
            Title = title,
            Order = report.Widgets.Count,
            Type = type,
            QuestionId = question?.Id,
            SecondaryQuestionId = secondaryQuestion?.Id,
        };
        configure?.Invoke(widget.Settings);
        report.Widgets.Add(widget);
    }
}
