using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Surveys;

// STUB - replaced in Phase 4A (see DEVELOPMENT_PLAN.md). Kept compiling so DI wiring is complete.
/// <summary>Survey design use cases.</summary>
public sealed class SurveyService : ISurveyService
{
    public Task<PagedResult<SurveySummaryDto>> ListAsync(SurveyQuery query, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<SurveySummaryDto>> ListTemplatesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SurveyDefinitionDto> GetAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SurveyDefinitionDto?> FindBySlugAsync(string slug, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SurveyDefinitionDto> CreateAsync(SurveyDefinitionDto dto, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SurveyDefinitionDto> UpdateAsync(Guid id, SurveyDefinitionDto dto, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SurveyDefinitionDto> ChangeStatusAsync(Guid id, SurveyStatus status, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SurveyDefinitionDto> DuplicateAsync(Guid id, DuplicateSurveyRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SurveyExportDocument> ExportDefinitionAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SurveyDefinitionDto> ImportDefinitionAsync(SurveyExportDocument document, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> IsSlugAvailableAsync(string slug, Guid? excludeSurveyId = null, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> CountAnswersAsync(Guid questionId, CancellationToken ct = default) => throw new NotImplementedException();
}
