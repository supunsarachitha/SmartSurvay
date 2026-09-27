using System.Text.RegularExpressions;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;
using SmartSurvey.Web.Components.Admin.Builder;

namespace SmartSurvey.Web.Components.Admin.Reports;

/// <summary>
/// Editing operations of the report builder on a <see cref="ReportDefinitionDto"/>: adding, moving,
/// duplicating and removing widgets, keeping their question choices compatible with the widget type
/// and the survey, answer filters, and which settings apply to which widget.
/// </summary>
public static class ReportDesign
{
    /// <summary>Widget types in the order the "add widget" menu offers them.</summary>
    public static IReadOnlyList<WidgetType> MenuOrder { get; } =
    [
        WidgetType.SummaryStats, WidgetType.BarChart, WidgetType.HorizontalBarChart, WidgetType.PieChart, WidgetType.DoughnutChart,
        WidgetType.QuestionTable, WidgetType.LineChart, WidgetType.CrossTab, WidgetType.TextResponses, WidgetType.RawResponses,
    ];

    /// <summary>One-line explanation of a widget type for the "add widget" menu.</summary>
    public static string Describe(WidgetType type) => type switch
    {
        WidgetType.SummaryStats => "Responses, completion rate and average time.",
        WidgetType.BarChart => "Answer distribution as vertical bars.",
        WidgetType.HorizontalBarChart => "Best for long option labels.",
        WidgetType.PieChart => "Share of each answer (few options).",
        WidgetType.DoughnutChart => "A pie chart with a hole.",
        WidgetType.QuestionTable => "Counts and percentages, or statistics for numbers.",
        WidgetType.LineChart => "Responses per day, week or month.",
        WidgetType.CrossTab => "Compare the answers of two choice questions.",
        WidgetType.TextResponses => "The latest written answers.",
        WidgetType.RawResponses => "One row per response, one column per question.",
        _ => string.Empty,
    };

    /// <summary>
    /// True when a widget of <paramref name="type"/> can analyse a question of <paramref name="questionType"/>:
    /// charts need non-text answers, cross-tabs need choice questions, tables and text lists take any question.
    /// </summary>
    public static bool Supports(WidgetType type, QuestionType questionType) => type switch
    {
        WidgetType.CrossTab => questionType.IsChoice(),
        _ when type.IsChart() => !questionType.IsText(),
        _ => true,
    };

    /// <summary>Questions the widget type can use, in survey order.</summary>
    public static List<PlacedQuestion> QuestionsFor(SurveyDefinitionDto survey, WidgetType type) =>
        SurveyDesign.Placed(survey).Where(p => Supports(type, p.Question.Type)).ToList();

    /// <summary>True when the survey has the questions a widget of this type needs.</summary>
    public static bool CanAdd(SurveyDefinitionDto survey, WidgetType type) =>
        !type.RequiresQuestion() || QuestionsFor(survey, type).Count >= (type.RequiresSecondaryQuestion() ? 2 : 1);

    /// <summary>Appends a widget of <paramref name="type"/> using the first suitable question(s).</summary>
    public static ReportWidgetDto AddWidget(ReportDefinitionDto report, SurveyDefinitionDto survey, WidgetType type)
    {
        var widget = new ReportWidgetDto { Type = type, Settings = new WidgetSettings() };
        AssignQuestions(widget, survey);
        report.Widgets.Add(widget);
        Renumber(report);
        return widget;
    }

    /// <summary>Changes the widget type, keeping the question when the new type supports it.</summary>
    public static void ChangeType(ReportWidgetDto widget, SurveyDefinitionDto survey, WidgetType type)
    {
        widget.Type = type;
        AssignQuestions(widget, survey);
    }

    /// <summary>Copies a widget (new id) right below the original.</summary>
    public static ReportWidgetDto Duplicate(ReportDefinitionDto report, ReportWidgetDto source)
    {
        var copy = new ReportWidgetDto
        {
            Type = source.Type,
            Title = string.IsNullOrWhiteSpace(source.Title) ? string.Empty : $"{source.Title} (copy)",
            QuestionId = source.QuestionId,
            SecondaryQuestionId = source.SecondaryQuestionId,
            Settings = source.Settings.Clone(),
        };

        report.Widgets.Insert(report.Widgets.IndexOf(source) + 1, copy);
        Renumber(report);
        return copy;
    }

    /// <summary>Moves a widget one step up (-1) or down (+1). Returns false at the edge.</summary>
    public static bool Move(ReportDefinitionDto report, ReportWidgetDto widget, int delta)
    {
        var index = report.Widgets.IndexOf(widget);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= report.Widgets.Count)
        {
            return false;
        }

        (report.Widgets[index], report.Widgets[target]) = (report.Widgets[target], report.Widgets[index]);
        Renumber(report);
        return true;
    }

    /// <summary>Removes a widget.</summary>
    public static void Remove(ReportDefinitionDto report, ReportWidgetDto widget)
    {
        report.Widgets.Remove(widget);
        Renumber(report);
    }

    /// <summary>Replaces the widgets with the minimal starting point: summary KPIs and responses over time.</summary>
    public static void StartBlank(ReportDefinitionDto report, SurveyDefinitionDto survey)
    {
        report.Widgets.Clear();
        AddWidget(report, survey, WidgetType.SummaryStats).Title = "Summary";
        AddWidget(report, survey, WidgetType.LineChart).Title = "Responses over time";
    }

    /// <summary>
    /// Points the report at another survey: answer filters are removed (they reference the old survey's
    /// questions) and every widget gets questions of the new survey.
    /// </summary>
    public static void ApplySurvey(ReportDefinitionDto report, SurveyDefinitionDto survey)
    {
        report.SurveyId = survey.Id;
        report.Filters.AnswerFilters.Clear();
        var questionIds = survey.AllQuestions().Select(q => q.Id).ToHashSet();
        foreach (var widget in report.Widgets)
        {
            if (widget.QuestionId is { } q && !questionIds.Contains(q))
            {
                widget.QuestionId = null;
            }

            if (widget.SecondaryQuestionId is { } s && !questionIds.Contains(s))
            {
                widget.SecondaryQuestionId = null;
            }

            widget.Settings.ColumnQuestionIds.RemoveAll(id => !questionIds.Contains(id));
            AssignQuestions(widget, survey);
        }
    }

    /// <summary>Title shown for a widget: its own title, else <see cref="DefaultTitle"/>.</summary>
    public static string TitleOf(ReportWidgetDto widget, SurveyDefinitionDto? survey) =>
        string.IsNullOrWhiteSpace(widget.Title) ? DefaultTitle(widget, survey) : widget.Title;

    /// <summary>Title used when the widget has none: "Q1. Question text", else the widget type.</summary>
    public static string DefaultTitle(ReportWidgetDto widget, SurveyDefinitionDto? survey) =>
        widget.QuestionId is { } id && survey?.FindQuestion(id) is { } question
            ? string.IsNullOrWhiteSpace(question.Code) ? question.Text : $"{question.Code}. {question.Text}"
            : widget.Type.DisplayName();

    /// <summary>Sets contiguous widget orders.</summary>
    public static void Renumber(ReportDefinitionDto report)
    {
        for (var i = 0; i < report.Widgets.Count; i++)
        {
            report.Widgets[i].Order = i;
        }
    }

    /// <summary>A new answer filter on <paramref name="question"/> with the first supported comparison.</summary>
    public static AnswerFilter NewFilter(QuestionDto question) => new()
    {
        QuestionId = question.Id,
        Operator = question.Type.SupportedOperators()[0],
        OptionId = question.Type.IsChoice() ? question.Options.OrderBy(o => o.Order).FirstOrDefault()?.Id : null,
    };

    /// <summary>Resets comparison, option and value after the filter's question changed.</summary>
    public static void ResetFilter(AnswerFilter filter, QuestionDto question)
    {
        var fresh = NewFilter(question);
        filter.QuestionId = question.Id;
        filter.Operator = fresh.Operator;
        filter.OptionId = fresh.OptionId;
        filter.Value = null;
    }

    /// <summary>Deep copy (the live preview works on a snapshot while the user keeps editing).</summary>
    public static ReportDefinitionDto Clone(ReportDefinitionDto report) => new()
    {
        Id = report.Id,
        Name = report.Name,
        Description = report.Description,
        SurveyId = report.SurveyId,
        Filters = report.Filters.Clone(),
        Widgets = report.Widgets.Select(w => new ReportWidgetDto
        {
            Id = w.Id,
            Title = w.Title,
            Order = w.Order,
            Type = w.Type,
            QuestionId = w.QuestionId,
            SecondaryQuestionId = w.SecondaryQuestionId,
            Settings = w.Settings.Clone(),
        }).ToList(),
        CreatedAt = report.CreatedAt,
        UpdatedAt = report.UpdatedAt,
    };

    // ---------------------------------------------------------------- which settings apply

    /// <summary>Distribution widgets (question table and question charts) on non-text questions.</summary>
    public static bool IsDistribution(WidgetType type, QuestionType? questionType) =>
        type is WidgetType.QuestionTable or WidgetType.BarChart or WidgetType.HorizontalBarChart or WidgetType.PieChart or WidgetType.DoughnutChart
        && questionType is { } q && !q.IsText();

    /// <summary>"Show percentages", sort order and top-N apply to distributions.</summary>
    public static bool UsesDistributionOptions(WidgetType type, QuestionType? questionType) => IsDistribution(type, questionType);

    /// <summary>"Include 'Other' answers" applies to distributions of choice questions with free-text options.</summary>
    public static bool UsesFreeText(WidgetType type, QuestionDto? question) =>
        question is not null && IsDistribution(type, question.Type) && question.Type.IsChoice() && question.Options.Any(o => o.AllowsFreeText);

    /// <summary>"Show data table" applies to distribution charts.</summary>
    public static bool UsesDataTable(WidgetType type, QuestionType? questionType) => type.IsChart() && IsDistribution(type, questionType);

    /// <summary>Time grouping applies to the responses-over-time chart.</summary>
    public static bool UsesTimeGrouping(WidgetType type) => type == WidgetType.LineChart;

    /// <summary>A row limit applies to answer lists (text answers, raw grid, "Other" answers).</summary>
    public static bool UsesMaxRows(WidgetType type, QuestionDto? question, WidgetSettings settings) =>
        type is WidgetType.TextResponses or WidgetType.RawResponses
        || (type == WidgetType.QuestionTable && question is not null && question.Type.IsText())
        || (UsesFreeText(type, question) && settings.IncludeFreeText);

    /// <summary>Column selection applies to the raw responses grid.</summary>
    public static bool UsesColumns(WidgetType type) => type == WidgetType.RawResponses;

    private static void AssignQuestions(ReportWidgetDto widget, SurveyDefinitionDto survey)
    {
        if (!widget.Type.RequiresQuestion())
        {
            widget.QuestionId = null;
            widget.SecondaryQuestionId = null;
            return;
        }

        var candidates = QuestionsFor(survey, widget.Type).Select(p => p.Question.Id).ToList();
        if (widget.QuestionId is not { } primary || !candidates.Contains(primary))
        {
            widget.QuestionId = candidates.Count > 0 ? candidates[0] : null;
        }

        if (!widget.Type.RequiresSecondaryQuestion())
        {
            widget.SecondaryQuestionId = null;
        }
        else if (widget.SecondaryQuestionId is not { } secondary || !candidates.Contains(secondary) || secondary == widget.QuestionId)
        {
            widget.SecondaryQuestionId = candidates.Where(id => id != widget.QuestionId).Cast<Guid?>().FirstOrDefault();
        }
    }
}

/// <summary>
/// Validation messages of a report definition (from <see cref="Application.Common.AppValidationException"/>)
/// sorted by where the builder shows them: report fields, widgets (by id), answer filters (by index).
/// </summary>
public sealed partial class ReportErrors
{
    /// <summary>No errors.</summary>
    public static ReportErrors None { get; } = new();

    /// <summary>Report-level fields: <c>Name</c>, <c>Description</c>, <c>SurveyId</c>, <c>Filters</c>.</summary>
    public Dictionary<string, List<string>> Fields { get; } = [];

    /// <summary>Messages per widget id.</summary>
    public Dictionary<Guid, List<string>> Widgets { get; } = [];

    /// <summary>Messages per answer-filter index.</summary>
    public Dictionary<int, List<string>> Filters { get; } = [];

    /// <summary>Messages that belong nowhere else.</summary>
    public List<string> General { get; } = [];

    /// <summary>Total number of messages.</summary>
    public int Count => Fields.Values.Sum(v => v.Count) + Widgets.Values.Sum(v => v.Count) + Filters.Values.Sum(v => v.Count) + General.Count;

    /// <summary>Messages of a report field.</summary>
    public IReadOnlyList<string> For(string field) => Fields.TryGetValue(field, out var list) ? list : [];

    /// <summary>Messages of a widget.</summary>
    public IReadOnlyList<string> ForWidget(Guid widgetId) => Widgets.TryGetValue(widgetId, out var list) ? list : [];

    /// <summary>Messages of an answer filter.</summary>
    public IReadOnlyList<string> ForFilter(int index) => Filters.TryGetValue(index, out var list) ? list : [];

    /// <summary>Sorts server errors (keys such as <c>Widgets[2].QuestionId</c>) for <paramref name="report"/>.</summary>
    public static ReportErrors From(IReadOnlyDictionary<string, string[]> errors, ReportDefinitionDto report)
    {
        var result = new ReportErrors();
        foreach (var (key, messages) in errors)
        {
            if (WidgetKey().Match(key) is { Success: true } w && int.TryParse(w.Groups[1].Value, out var wi) && wi < report.Widgets.Count)
            {
                Add(result.Widgets, report.Widgets[wi].Id, messages);
            }
            else if (FilterKey().Match(key) is { Success: true } f && int.TryParse(f.Groups[1].Value, out var fi))
            {
                Add(result.Filters, fi, messages);
            }
            else if (key.StartsWith("Filters", StringComparison.Ordinal))
            {
                Add(result.Fields, "Filters", messages);
            }
            else if (key is nameof(ReportDefinitionDto.Name) or nameof(ReportDefinitionDto.Description) or nameof(ReportDefinitionDto.SurveyId))
            {
                Add(result.Fields, key, messages);
            }
            else
            {
                result.General.AddRange(messages.Except(result.General));
            }
        }

        return result;
    }

    private static void Add<TKey>(Dictionary<TKey, List<string>> target, TKey key, IEnumerable<string> messages)
        where TKey : notnull
    {
        if (!target.TryGetValue(key, out var list))
        {
            target[key] = list = [];
        }

        list.AddRange(messages.Except(list));
    }

    [GeneratedRegex(@"^Widgets\[(\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex WidgetKey();

    [GeneratedRegex(@"^Filters\.AnswerFilters\[(\d+)\]", RegexOptions.CultureInvariant)]
    private static partial Regex FilterKey();
}
