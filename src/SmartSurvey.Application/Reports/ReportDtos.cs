using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports;

/// <summary>Row in the report list.</summary>
public sealed record ReportSummaryDto
{
    /// <summary>Report id.</summary>
    public Guid Id { get; init; }

    /// <summary>Name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>Survey id.</summary>
    public Guid SurveyId { get; init; }

    /// <summary>Survey title.</summary>
    public string SurveyTitle { get; init; } = string.Empty;

    /// <summary>Number of widgets.</summary>
    public int WidgetCount { get; init; }

    /// <summary>Created (UTC).</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Updated (UTC).</summary>
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>Editable report definition (builder binding + REST payload).</summary>
public sealed class ReportDefinitionDto
{
    /// <summary>Report id (empty on create).</summary>
    public Guid Id { get; set; }

    /// <summary>Name (required, max 200).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description (max 2000).</summary>
    public string? Description { get; set; }

    /// <summary>Survey analysed by the report.</summary>
    public Guid SurveyId { get; set; }

    /// <summary>Global response filters.</summary>
    public ReportFilterSet Filters { get; set; } = new();

    /// <summary>Widgets in display order.</summary>
    public List<ReportWidgetDto> Widgets { get; set; } = [];

    /// <summary>Created (read-only).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Updated (read-only).</summary>
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>A widget definition.</summary>
public sealed class ReportWidgetDto
{
    /// <summary>Widget id (client generated).</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Title (defaults to question text when empty).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Zero-based order.</summary>
    public int Order { get; set; }

    /// <summary>Widget type.</summary>
    public WidgetType Type { get; set; } = WidgetType.BarChart;

    /// <summary>Primary question.</summary>
    public Guid? QuestionId { get; set; }

    /// <summary>Secondary question (CrossTab).</summary>
    public Guid? SecondaryQuestionId { get; set; }

    /// <summary>Display options.</summary>
    public WidgetSettings Settings { get; set; } = new();
}

/// <summary>Filters for the report list.</summary>
public sealed class ReportQuery : PageRequest
{
    /// <summary>Only reports of this survey.</summary>
    public Guid? SurveyId { get; set; }

    /// <summary>Search in name/description.</summary>
    public string? Search { get; set; }
}
