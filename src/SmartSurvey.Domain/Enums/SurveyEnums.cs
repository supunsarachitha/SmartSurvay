namespace SmartSurvey.Domain.Enums;

/// <summary>Lifecycle state of a survey.</summary>
public enum SurveyStatus
{
    /// <summary>Being designed; not visible to respondents.</summary>
    Draft = 0,

    /// <summary>Live; accepting responses (subject to schedule and quota).</summary>
    Published = 1,

    /// <summary>No longer accepting responses; results remain available.</summary>
    Closed = 2,

    /// <summary>Hidden from the default admin lists; read-only.</summary>
    Archived = 3,
}

/// <summary>
/// Supported question types. "Combined" types (e.g. <em>Other → free text</em>) are modelled by
/// flagging a choice option with <c>QuestionOption.AllowsFreeText</c> rather than as a separate type.
/// </summary>
public enum QuestionType
{
    /// <summary>Single-line text input.</summary>
    ShortText = 0,

    /// <summary>Multi-line text area.</summary>
    LongText = 1,

    /// <summary>Single choice rendered as radio buttons.</summary>
    Radio = 2,

    /// <summary>Multiple choice rendered as check boxes.</summary>
    Checkbox = 3,

    /// <summary>Single choice rendered as a drop-down list.</summary>
    Dropdown = 4,

    /// <summary>Numeric input (integer or decimal, see settings).</summary>
    Number = 5,

    /// <summary>E-mail address input with format validation.</summary>
    Email = 6,

    /// <summary>Calendar date input.</summary>
    Date = 7,

    /// <summary>Star rating from 1 to <c>QuestionSettings.RatingMax</c>.</summary>
    Rating = 8,

    /// <summary>Linear scale from <c>ScaleMin</c> to <c>ScaleMax</c> (e.g. NPS 0–10).</summary>
    Scale = 9,
}

/// <summary>What a logic rule does to its target when its conditions match.</summary>
public enum LogicAction
{
    /// <summary>Target is hidden by default and shown only when the rule matches.</summary>
    Show = 0,

    /// <summary>Target is shown by default and hidden when the rule matches.</summary>
    Hide = 1,
}

/// <summary>How the conditions of a rule (or report filters) are combined.</summary>
public enum LogicMatchType
{
    /// <summary>All conditions must match (logical AND).</summary>
    All = 0,

    /// <summary>At least one condition must match (logical OR).</summary>
    Any = 1,
}

/// <summary>Comparison operators used by logic conditions and report answer filters.</summary>
public enum ConditionOperator
{
    /// <summary>Text equals (case-insensitive) / number equals / date equals / option selected.</summary>
    Equals = 0,

    /// <summary>Negation of <see cref="Equals"/>.</summary>
    NotEquals = 1,

    /// <summary>Text contains (case-insensitive) / option selected (for choice questions).</summary>
    Contains = 2,

    /// <summary>Negation of <see cref="Contains"/>.</summary>
    NotContains = 3,

    /// <summary>Number or date greater than the value.</summary>
    GreaterThan = 4,

    /// <summary>Number or date greater than or equal to the value.</summary>
    GreaterThanOrEqual = 5,

    /// <summary>Number or date less than the value.</summary>
    LessThan = 6,

    /// <summary>Number or date less than or equal to the value.</summary>
    LessThanOrEqual = 7,

    /// <summary>The question has any answer.</summary>
    IsAnswered = 8,

    /// <summary>The question has no answer.</summary>
    IsNotAnswered = 9,
}

/// <summary>State of a single response.</summary>
public enum ResponseStatus
{
    /// <summary>Draft saved by a logged-in respondent (save &amp; resume).</summary>
    InProgress = 0,

    /// <summary>Submitted and validated.</summary>
    Completed = 1,
}

/// <summary>Kinds of widgets a report can contain.</summary>
public enum WidgetType
{
    /// <summary>KPI tiles: total/completed responses, completion rate, average duration.</summary>
    SummaryStats = 0,

    /// <summary>Distribution table for one question (counts/percentages or numeric statistics).</summary>
    QuestionTable = 1,

    /// <summary>Vertical bar chart of a question's distribution.</summary>
    BarChart = 2,

    /// <summary>Horizontal bar chart of a question's distribution.</summary>
    HorizontalBarChart = 3,

    /// <summary>Pie chart of a question's distribution.</summary>
    PieChart = 4,

    /// <summary>Doughnut chart of a question's distribution.</summary>
    DoughnutChart = 5,

    /// <summary>Line chart of responses over time (day/week/month).</summary>
    LineChart = 6,

    /// <summary>Cross-tabulation of two choice questions.</summary>
    CrossTab = 7,

    /// <summary>List of free-text answers for a question.</summary>
    TextResponses = 8,

    /// <summary>Raw response grid (one row per response, one column per question).</summary>
    RawResponses = 9,
}

/// <summary>Ordering of categories in distribution widgets.</summary>
public enum WidgetSortOrder
{
    /// <summary>Question option order as designed.</summary>
    Default = 0,

    /// <summary>Most frequent first.</summary>
    CountDescending = 1,

    /// <summary>Least frequent first.</summary>
    CountAscending = 2,

    /// <summary>Alphabetical by label.</summary>
    LabelAscending = 3,
}

/// <summary>Bucket size for time-series widgets.</summary>
public enum TimeGrouping
{
    /// <summary>One bucket per calendar day (UTC).</summary>
    Day = 0,

    /// <summary>One bucket per ISO week (starting Monday, UTC).</summary>
    Week = 1,

    /// <summary>One bucket per calendar month (UTC).</summary>
    Month = 2,
}
