using SmartSurvey.Domain.Common;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Domain.Entities;

/// <summary>
/// A saved, dynamically built report over one survey's responses: global filters plus an ordered
/// list of widgets (tables and charts). Reports are computed on demand, never stored as results.
/// </summary>
public class ReportDefinition : AuditableEntity
{
    /// <summary>Report name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Optional description printed under the title.</summary>
    public string? Description { get; set; }

    /// <summary>Survey whose responses are analysed.</summary>
    public Guid SurveyId { get; set; }

    /// <summary>Survey (navigation).</summary>
    public Survey? Survey { get; set; }

    /// <summary>Response filters (JSON column).</summary>
    public ReportFilterSet Filters { get; set; } = new();

    /// <summary>Widgets ordered by <see cref="ReportWidget.Order"/>.</summary>
    public List<ReportWidget> Widgets { get; set; } = [];
}

/// <summary>A table or chart inside a report.</summary>
public class ReportWidget : Entity
{
    /// <summary>Owning report.</summary>
    public Guid ReportId { get; set; }

    /// <summary>Owning report (navigation).</summary>
    public ReportDefinition? Report { get; set; }

    /// <summary>Widget heading (defaults to the question text when empty).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Zero-based position in the report.</summary>
    public int Order { get; set; }

    /// <summary>Widget kind.</summary>
    public WidgetType Type { get; set; }

    /// <summary>Primary question (not needed for SummaryStats / LineChart / RawResponses).</summary>
    public Guid? QuestionId { get; set; }

    /// <summary>Primary question (navigation).</summary>
    public Question? Question { get; set; }

    /// <summary>Secondary question (CrossTab columns).</summary>
    public Guid? SecondaryQuestionId { get; set; }

    /// <summary>Secondary question (navigation).</summary>
    public Question? SecondaryQuestion { get; set; }

    /// <summary>Display options (JSON column).</summary>
    public WidgetSettings Settings { get; set; } = new();
}

/// <summary>Append-only audit trail of significant user actions.</summary>
public class AuditLogEntry : Entity
{
    /// <summary>UTC timestamp.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Acting user (null for anonymous/system).</summary>
    public Guid? UserId { get; set; }

    /// <summary>Acting user's name at the time of the action.</summary>
    public string? UserName { get; set; }

    /// <summary>Action code, e.g. <c>survey.published</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Entity type, e.g. <c>Survey</c>.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Affected entity id.</summary>
    public string? EntityId { get; set; }

    /// <summary>Human-readable details.</summary>
    public string? Details { get; set; }
}
