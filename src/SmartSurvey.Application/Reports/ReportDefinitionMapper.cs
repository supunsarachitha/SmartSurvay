using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports;

/// <summary>
/// Normalisation and entity ↔ DTO mapping of report definitions. Every method works on copies so
/// DTOs never share mutable settings objects with tracked entities (or with the caller).
/// </summary>
internal static class ReportDefinitionMapper
{
    /// <summary>
    /// Returns a normalised private copy of an incoming definition: strings trimmed, missing lists and
    /// settings replaced by defaults, widgets ordered and renumbered 0..n-1, empty widget ids replaced,
    /// and question references removed from widget types that do not use them.
    /// </summary>
    public static ReportDefinitionDto Normalize(ReportDefinitionDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var widgets = (dto.Widgets ?? [])
            .Where(w => w is not null)
            .Select((widget, index) => (widget, index))
            .OrderBy(x => x.widget.Order)
            .ThenBy(x => x.index)
            .Select((x, order) => NormalizeWidget(x.widget, order))
            .ToList();

        return new ReportDefinitionDto
        {
            Id = dto.Id,
            Name = (dto.Name ?? string.Empty).Trim(),
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            SurveyId = dto.SurveyId,
            Filters = NormalizeFilters(dto.Filters),
            Widgets = widgets,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
        };
    }

    /// <summary>Maps a report entity (widgets loaded) to its DTO, widgets in display order.</summary>
    public static ReportDefinitionDto ToDto(ReportDefinition report) => new()
    {
        Id = report.Id,
        Name = report.Name,
        Description = report.Description,
        SurveyId = report.SurveyId,
        Filters = report.Filters.Clone(),
        CreatedAt = report.CreatedAt,
        UpdatedAt = report.UpdatedAt,
        Widgets = report.Widgets
            .OrderBy(w => w.Order)
            .Select(w => new ReportWidgetDto
            {
                Id = w.Id,
                Title = w.Title,
                Order = w.Order,
                Type = w.Type,
                QuestionId = w.QuestionId,
                SecondaryQuestionId = w.SecondaryQuestionId,
                Settings = w.Settings.Clone(),
            })
            .ToList(),
    };

    /// <summary>Creates a new widget entity with a fresh id.</summary>
    public static ReportWidget ToNewWidget(ReportWidgetDto dto, Guid reportId)
    {
        var widget = new ReportWidget { ReportId = reportId };
        Apply(dto, widget);
        return widget;
    }

    /// <summary>Copies the editable widget fields onto an entity.</summary>
    public static void Apply(ReportWidgetDto dto, ReportWidget widget)
    {
        widget.Title = dto.Title;
        widget.Order = dto.Order;
        widget.Type = dto.Type;
        widget.QuestionId = dto.QuestionId;
        widget.SecondaryQuestionId = dto.SecondaryQuestionId;
        widget.Settings = dto.Settings.Clone();
    }

    /// <summary>Normalises one widget.</summary>
    private static ReportWidgetDto NormalizeWidget(ReportWidgetDto widget, int order)
    {
        var settings = widget.Settings ?? new WidgetSettings();
        return new ReportWidgetDto
        {
            Id = widget.Id == Guid.Empty ? Guid.NewGuid() : widget.Id,
            Title = (widget.Title ?? string.Empty).Trim(),
            Order = order,
            Type = widget.Type,
            QuestionId = widget.Type.RequiresQuestion() && widget.QuestionId != Guid.Empty ? widget.QuestionId : null,
            SecondaryQuestionId = widget.Type.RequiresSecondaryQuestion() && widget.SecondaryQuestionId != Guid.Empty ? widget.SecondaryQuestionId : null,
            Settings = new WidgetSettings
            {
                ShowPercentages = settings.ShowPercentages,
                SortOrder = settings.SortOrder,
                TopN = settings.TopN,
                TimeGrouping = settings.TimeGrouping,
                IncludeFreeText = settings.IncludeFreeText,
                ShowDataTable = settings.ShowDataTable,
                MaxRows = settings.MaxRows,
                // Column choices only matter for the raw grid.
                ColumnQuestionIds = widget.Type == WidgetType.RawResponses
                    ? (settings.ColumnQuestionIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList()
                    : [],
            },
        };
    }

    /// <summary>Normalises the filters (trimmed values, no null lists).</summary>
    private static ReportFilterSet NormalizeFilters(ReportFilterSet? filters)
    {
        var source = filters ?? new ReportFilterSet();
        return new ReportFilterSet
        {
            From = source.From,
            To = source.To,
            IncludeInProgress = source.IncludeInProgress,
            MatchType = source.MatchType,
            AnswerFilters = (source.AnswerFilters ?? [])
                .Where(f => f is not null)
                .Select(f => new AnswerFilter
                {
                    QuestionId = f.QuestionId,
                    Operator = f.Operator,
                    OptionId = f.OptionId == Guid.Empty ? null : f.OptionId,
                    Value = string.IsNullOrWhiteSpace(f.Value) ? null : f.Value.Trim(),
                })
                .ToList(),
        };
    }
}
