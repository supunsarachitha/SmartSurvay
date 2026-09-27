namespace SmartSurvey.Domain.Enums;

/// <summary>Classification helpers for <see cref="QuestionType"/> used across UI, validation and reporting.</summary>
public static class QuestionTypeExtensions
{
    /// <summary>True for question types that have answer options (radio, checkbox, dropdown).</summary>
    public static bool IsChoice(this QuestionType type) =>
        type is QuestionType.Radio or QuestionType.Checkbox or QuestionType.Dropdown;

    /// <summary>True when more than one option may be selected.</summary>
    public static bool IsMultiSelect(this QuestionType type) => type == QuestionType.Checkbox;

    /// <summary>True for types whose answer is stored in <c>Answer.NumberValue</c>.</summary>
    public static bool IsNumeric(this QuestionType type) =>
        type is QuestionType.Number or QuestionType.Rating or QuestionType.Scale;

    /// <summary>True for types whose answer is stored in <c>Answer.TextValue</c>.</summary>
    public static bool IsText(this QuestionType type) =>
        type is QuestionType.ShortText or QuestionType.LongText or QuestionType.Email;

    /// <summary>True for types whose answer is stored in <c>Answer.DateValue</c>.</summary>
    public static bool IsDate(this QuestionType type) => type == QuestionType.Date;

    /// <summary>Human-friendly display name (used in the builder and exports).</summary>
    public static string DisplayName(this QuestionType type) => type switch
    {
        QuestionType.ShortText => "Short text",
        QuestionType.LongText => "Long text (paragraph)",
        QuestionType.Radio => "Single choice (radio)",
        QuestionType.Checkbox => "Multiple choice (checkbox)",
        QuestionType.Dropdown => "Dropdown",
        QuestionType.Number => "Number",
        QuestionType.Email => "E-mail",
        QuestionType.Date => "Date",
        QuestionType.Rating => "Star rating",
        QuestionType.Scale => "Linear scale / NPS",
        _ => type.ToString(),
    };

    /// <summary>Human-friendly operator label used in logic and filter editors.</summary>
    public static string DisplayName(this ConditionOperator op) => op switch
    {
        ConditionOperator.Equals => "is / equals",
        ConditionOperator.NotEquals => "is not / not equals",
        ConditionOperator.Contains => "contains / includes",
        ConditionOperator.NotContains => "does not contain / exclude",
        ConditionOperator.GreaterThan => "greater than",
        ConditionOperator.GreaterThanOrEqual => "greater than or equal",
        ConditionOperator.LessThan => "less than",
        ConditionOperator.LessThanOrEqual => "less than or equal",
        ConditionOperator.IsAnswered => "is answered",
        ConditionOperator.IsNotAnswered => "is not answered",
        _ => op.ToString(),
    };

    /// <summary>True when the operator needs no comparison value.</summary>
    public static bool IsUnary(this ConditionOperator op) =>
        op is ConditionOperator.IsAnswered or ConditionOperator.IsNotAnswered;

    /// <summary>Operators that make sense for a given source question type (drives editor drop-downs).</summary>
    public static IReadOnlyList<ConditionOperator> SupportedOperators(this QuestionType type)
    {
        if (type.IsChoice())
        {
            return type.IsMultiSelect()
                ? [ConditionOperator.Contains, ConditionOperator.NotContains, ConditionOperator.IsAnswered, ConditionOperator.IsNotAnswered]
                : [ConditionOperator.Equals, ConditionOperator.NotEquals, ConditionOperator.IsAnswered, ConditionOperator.IsNotAnswered];
        }

        if (type.IsNumeric() || type.IsDate())
        {
            return
            [
                ConditionOperator.Equals, ConditionOperator.NotEquals,
                ConditionOperator.GreaterThan, ConditionOperator.GreaterThanOrEqual,
                ConditionOperator.LessThan, ConditionOperator.LessThanOrEqual,
                ConditionOperator.IsAnswered, ConditionOperator.IsNotAnswered,
            ];
        }

        return
        [
            ConditionOperator.Equals, ConditionOperator.NotEquals,
            ConditionOperator.Contains, ConditionOperator.NotContains,
            ConditionOperator.IsAnswered, ConditionOperator.IsNotAnswered,
        ];
    }

    /// <summary>Display name for widget types.</summary>
    public static string DisplayName(this WidgetType type) => type switch
    {
        WidgetType.SummaryStats => "Summary statistics (KPIs)",
        WidgetType.QuestionTable => "Question table",
        WidgetType.BarChart => "Bar chart",
        WidgetType.HorizontalBarChart => "Horizontal bar chart",
        WidgetType.PieChart => "Pie chart",
        WidgetType.DoughnutChart => "Doughnut chart",
        WidgetType.LineChart => "Responses over time (line)",
        WidgetType.CrossTab => "Cross-tabulation",
        WidgetType.TextResponses => "Text responses",
        WidgetType.RawResponses => "Raw responses table",
        _ => type.ToString(),
    };

    /// <summary>True when the widget needs a primary question.</summary>
    public static bool RequiresQuestion(this WidgetType type) =>
        type is not (WidgetType.SummaryStats or WidgetType.LineChart or WidgetType.RawResponses);

    /// <summary>True when the widget needs a secondary question (cross-tab).</summary>
    public static bool RequiresSecondaryQuestion(this WidgetType type) => type == WidgetType.CrossTab;

    /// <summary>True for chart-type widgets.</summary>
    public static bool IsChart(this WidgetType type) =>
        type is WidgetType.BarChart or WidgetType.HorizontalBarChart or WidgetType.PieChart
            or WidgetType.DoughnutChart or WidgetType.LineChart;
}
