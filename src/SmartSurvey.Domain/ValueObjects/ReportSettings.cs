using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Domain.ValueObjects;

/// <summary>Display / computation options of a report widget. Persisted as JSON.</summary>
public sealed class WidgetSettings
{
    /// <summary>Show percentage columns / labels next to counts.</summary>
    public bool ShowPercentages { get; set; } = true;

    /// <summary>Category ordering for distribution widgets.</summary>
    public WidgetSortOrder SortOrder { get; set; } = WidgetSortOrder.Default;

    /// <summary>Only keep the N largest categories (null = all).</summary>
    public int? TopN { get; set; }

    /// <summary>Bucket size for <see cref="WidgetType.LineChart"/>.</summary>
    public TimeGrouping TimeGrouping { get; set; } = TimeGrouping.Day;

    /// <summary>For choice questions, also list the free-text answers given to "Other"-style options.</summary>
    public bool IncludeFreeText { get; set; } = true;

    /// <summary>For chart widgets, also render the underlying data table.</summary>
    public bool ShowDataTable { get; set; } = true;

    /// <summary>Maximum rows for text / raw widgets (keeps reports and PDFs bounded).</summary>
    public int MaxRows { get; set; } = 100;

    /// <summary>Questions to include as columns in <see cref="WidgetType.RawResponses"/> (empty = all).</summary>
    public List<Guid> ColumnQuestionIds { get; set; } = [];

    /// <summary>Creates a deep copy.</summary>
    public WidgetSettings Clone()
    {
        var copy = (WidgetSettings)MemberwiseClone();
        copy.ColumnQuestionIds = [.. ColumnQuestionIds];
        return copy;
    }
}

/// <summary>
/// Filters applied to the response set before any widget is computed. Persisted as JSON on the report.
/// </summary>
public sealed class ReportFilterSet
{
    /// <summary>Only responses submitted (or started, for drafts) on/after this date (UTC, inclusive).</summary>
    public DateOnly? From { get; set; }

    /// <summary>Only responses submitted (or started, for drafts) on/before this date (UTC, inclusive).</summary>
    public DateOnly? To { get; set; }

    /// <summary>Include draft (in-progress) responses. Default: completed only.</summary>
    public bool IncludeInProgress { get; set; }

    /// <summary>How <see cref="AnswerFilters"/> are combined.</summary>
    public LogicMatchType MatchType { get; set; } = LogicMatchType.All;

    /// <summary>Answer-based filters, e.g. "only respondents who chose <em>Germany</em> for Country".</summary>
    public List<AnswerFilter> AnswerFilters { get; set; } = [];

    /// <summary>Creates a deep copy.</summary>
    public ReportFilterSet Clone()
    {
        var copy = (ReportFilterSet)MemberwiseClone();
        copy.AnswerFilters = AnswerFilters.Select(f => f.Clone()).ToList();
        return copy;
    }
}

/// <summary>A single answer-based report filter (same semantics as a logic condition).</summary>
public sealed class AnswerFilter
{
    /// <summary>Question whose answer is tested.</summary>
    public Guid QuestionId { get; set; }

    /// <summary>Comparison operator.</summary>
    public ConditionOperator Operator { get; set; } = ConditionOperator.Equals;

    /// <summary>Option to compare with (choice questions).</summary>
    public Guid? OptionId { get; set; }

    /// <summary>Value to compare with (text / number / ISO date questions).</summary>
    public string? Value { get; set; }

    /// <summary>Creates a copy.</summary>
    public AnswerFilter Clone() => (AnswerFilter)MemberwiseClone();
}
