using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Reports.Engine;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.ValueObjects;

namespace SmartSurvey.Application.Reports;

/// <summary>
/// Computes a <see cref="ReportResult"/> from a report definition: loads the survey design, applies
/// the report filters to its responses and computes every widget in display order.
/// </summary>
/// <remarks>
/// <para>The engine performs no authorization (callers such as <see cref="ReportService"/> do) and
/// never modifies data. It uses one database context for the whole run; counts and groupings are
/// aggregated in the database, only lightweight columns are materialised.</para>
/// <para>A widget that cannot be computed (for example because its question was deleted) gets an
/// <see cref="WidgetResult.Error"/> message instead of failing the whole report.</para>
/// </remarks>
public sealed class ReportEngine(IAppDbContextFactory dbFactory, TimeProvider time, ILogger<ReportEngine> logger) : IReportEngine
{
    /// <summary>Error of widgets whose (primary or secondary) question no longer exists.</summary>
    public const string MissingQuestionError = "The question used by this widget no longer exists.";

    /// <summary>Error of widgets that failed unexpectedly (details are logged).</summary>
    public const string UnexpectedWidgetError = "This widget could not be computed. Please try again or edit the widget.";

    /// <summary>Upper bound for <see cref="WidgetSettings.MaxRows"/>.</summary>
    public const int MaxRowsLimit = 1000;

    /// <inheritdoc />
    public async Task<ReportResult> ExecuteAsync(ReportDefinitionDto definition, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await LoadSurveyAsync(db, definition.SurveyId, ct);
        var filters = definition.Filters ?? new ReportFilterSet();
        var selection = await ReportResponseSet.LoadAsync(db, survey, filters, ct);
        var context = new ReportRunContext(db, survey, filters, selection.Rows, selection.Ids, ct);

        var result = new ReportResult
        {
            ReportId = definition.Id == Guid.Empty ? null : definition.Id,
            ReportName = string.IsNullOrWhiteSpace(definition.Name) ? "Untitled report" : definition.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(definition.Description) ? null : definition.Description.Trim(),
            SurveyId = survey.Id,
            SurveyTitle = survey.Title,
            GeneratedAt = time.GetUtcNow().UtcDateTime,
            TotalResponses = selection.Rows.Count,
            FilterSummary = ReportFilterSummary.Describe(filters, selection),
        };

        foreach (var widget in (definition.Widgets ?? []).OrderBy(w => w.Order))
        {
            result.Widgets.Add(await BuildWidgetSafelyAsync(context, widget));
        }

        return result;
    }

    /// <summary>Loads the survey design (sections, questions, options) as a DTO.</summary>
    private static async Task<SurveyDefinitionDto> LoadSurveyAsync(IAppDbContext db, Guid surveyId, CancellationToken ct)
    {
        var survey = await db.Surveys
            .AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.Sections)
            .Include(s => s.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(s => s.Id == surveyId, ct)
            ?? throw new NotFoundException("Survey", surveyId);
        return survey.ToDefinitionDto();
    }

    /// <summary>Builds one widget; unexpected failures are logged and reported on the widget only.</summary>
    private async Task<WidgetResult> BuildWidgetSafelyAsync(ReportRunContext context, ReportWidgetDto widget)
    {
        try
        {
            return await BuildWidgetAsync(context, widget);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Report widget {WidgetId} ({WidgetType}) of survey {SurveyId} could not be computed.", widget.Id, widget.Type, context.Survey.Id);
            return new WidgetResult
            {
                WidgetId = widget.Id,
                Type = widget.Type,
                Title = WidgetTitle(widget, context.FindQuestion(widget.QuestionId), context.FindQuestion(widget.SecondaryQuestionId)),
                Error = UnexpectedWidgetError,
            };
        }
    }

    /// <summary>Validates the widget's questions and dispatches to the builder of its type.</summary>
    private static async Task<WidgetResult> BuildWidgetAsync(ReportRunContext context, ReportWidgetDto widget)
    {
        var question = context.FindQuestion(widget.QuestionId);
        var secondary = context.FindQuestion(widget.SecondaryQuestionId);
        var settings = EffectiveSettings(widget.Settings);
        var result = new WidgetResult
        {
            WidgetId = widget.Id,
            Type = widget.Type,
            Title = WidgetTitle(widget, question, secondary),
            QuestionText = question?.Text,
        };

        if ((widget.Type.RequiresQuestion() && question is null) || (widget.Type.RequiresSecondaryQuestion() && secondary is null))
        {
            result.Error = MissingQuestionError;
            return result;
        }

        switch (widget.Type)
        {
            case WidgetType.SummaryStats:
                await SummaryStatsBuilder.BuildAsync(context, result);
                break;
            case WidgetType.LineChart:
                TimeSeriesBuilder.Build(context, settings, result);
                break;
            case WidgetType.CrossTab:
                await CrossTabBuilder.BuildAsync(context, question!, secondary!, result);
                break;
            case WidgetType.TextResponses:
                await ResponseListBuilder.BuildTextAsync(context, question!, settings, result);
                break;
            case WidgetType.RawResponses:
                await ResponseListBuilder.BuildRawAsync(context, settings, result);
                break;
            case WidgetType.QuestionTable or WidgetType.BarChart or WidgetType.HorizontalBarChart
                or WidgetType.PieChart or WidgetType.DoughnutChart:
                await QuestionWidgetBuilder.BuildAsync(context, question!, settings, result);
                break;
            default:
                result.Error = "This widget type is not supported.";
                break;
        }

        return result;
    }

    /// <summary>
    /// Widget title: the configured title, else the question text (both questions for cross-tabs),
    /// else the widget type's display name.
    /// </summary>
    private static string WidgetTitle(ReportWidgetDto widget, QuestionDto? question, QuestionDto? secondary)
    {
        if (!string.IsNullOrWhiteSpace(widget.Title))
        {
            return widget.Title.Trim();
        }

        if (widget.Type == WidgetType.CrossTab && question is not null && secondary is not null)
        {
            return $"{question.Text} × {secondary.Text}";
        }

        return question?.Text ?? widget.Type.DisplayName();
    }

    /// <summary>A private copy of the settings with defaults for missing values and MaxRows clamped to 1..1000.</summary>
    private static WidgetSettings EffectiveSettings(WidgetSettings? settings)
    {
        if (settings is null)
        {
            return new WidgetSettings();
        }

        // Copied field by field: a JSON payload may carry a null ColumnQuestionIds list.
        return new WidgetSettings
        {
            ShowPercentages = settings.ShowPercentages,
            SortOrder = settings.SortOrder,
            TopN = settings.TopN,
            TimeGrouping = settings.TimeGrouping,
            IncludeFreeText = settings.IncludeFreeText,
            ShowDataTable = settings.ShowDataTable,
            MaxRows = Math.Clamp(settings.MaxRows, 1, MaxRowsLimit),
            ColumnQuestionIds = settings.ColumnQuestionIds is null ? [] : [.. settings.ColumnQuestionIds],
        };
    }
}
