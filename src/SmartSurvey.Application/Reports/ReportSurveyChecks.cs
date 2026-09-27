using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Common;

namespace SmartSurvey.Application.Reports;

/// <summary>
/// Validation rules of a report definition that need the survey design: the survey exists and every
/// question / option referenced by widgets and answer filters belongs to it.
/// </summary>
internal static class ReportSurveyChecks
{
    private const string ForeignQuestion = "The selected question does not belong to the report's survey.";

    /// <summary>Adds an error for every broken reference (keys are property paths).</summary>
    public static async Task CheckAsync(IAppDbContext db, ReportDefinitionDto dto, Dictionary<string, List<string>> errors, CancellationToken ct)
    {
        if (dto.SurveyId == Guid.Empty)
        {
            return; // reported by ReportDefinitionValidator
        }

        if (!await db.Surveys.AnyAsync(s => s.Id == dto.SurveyId, ct))
        {
            errors.Add(nameof(ReportDefinitionDto.SurveyId), "The selected survey does not exist.");
            return;
        }

        var options = await db.QuestionOptions
            .AsNoTracking()
            .Where(o => o.Question!.SurveyId == dto.SurveyId)
            .Select(o => new { o.Id, o.QuestionId })
            .ToListAsync(ct);
        var questionIds = (await db.Questions
                .AsNoTracking()
                .Where(q => q.SurveyId == dto.SurveyId)
                .Select(q => q.Id)
                .ToListAsync(ct))
            .ToHashSet();
        var optionOwners = options.ToDictionary(o => o.Id, o => o.QuestionId);

        CheckWidgets(dto, questionIds, errors);
        CheckFilters(dto, questionIds, optionOwners, errors);
    }

    private static void CheckWidgets(ReportDefinitionDto dto, HashSet<Guid> questionIds, Dictionary<string, List<string>> errors)
    {
        for (var i = 0; i < dto.Widgets.Count; i++)
        {
            var widget = dto.Widgets[i];
            var path = $"{nameof(ReportDefinitionDto.Widgets)}[{i}]";
            if (widget.QuestionId is { } questionId && !questionIds.Contains(questionId))
            {
                errors.Add($"{path}.{nameof(ReportWidgetDto.QuestionId)}", ForeignQuestion);
            }

            if (widget.SecondaryQuestionId is { } secondaryId && !questionIds.Contains(secondaryId))
            {
                errors.Add($"{path}.{nameof(ReportWidgetDto.SecondaryQuestionId)}", ForeignQuestion);
            }

            if (widget.Settings.ColumnQuestionIds.Any(id => !questionIds.Contains(id)))
            {
                errors.Add($"{path}.Settings.ColumnQuestionIds", "Some of the selected columns are not questions of the report's survey.");
            }
        }
    }

    private static void CheckFilters(
        ReportDefinitionDto dto, HashSet<Guid> questionIds, Dictionary<Guid, Guid> optionOwners, Dictionary<string, List<string>> errors)
    {
        var filters = dto.Filters.AnswerFilters;
        for (var i = 0; i < filters.Count; i++)
        {
            var filter = filters[i];
            var path = $"Filters.AnswerFilters[{i}]";
            if (filter.QuestionId != Guid.Empty && !questionIds.Contains(filter.QuestionId))
            {
                errors.Add($"{path}.QuestionId", "The filter question does not belong to the report's survey.");
            }
            else if (filter.OptionId is { } optionId && optionOwners.GetValueOrDefault(optionId) != filter.QuestionId)
            {
                errors.Add($"{path}.OptionId", "The selected option does not belong to the filter question.");
            }
        }
    }
}
