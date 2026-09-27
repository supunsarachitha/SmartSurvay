using SmartSurvey.Application.Common;

namespace SmartSurvey.Application.Responses;

// STUB - replaced in Phase 4B (see DEVELOPMENT_PLAN.md). Kept compiling so DI wiring is complete.
/// <summary>Response collection use cases.</summary>
public sealed class ResponseService : IResponseService
{
    public Task<IReadOnlyList<AvailableSurveyDto>> ListAvailableAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SurveySessionDto> StartOrResumeAsync(string slug, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Guid> SaveDraftAsync(Guid surveyId, SaveResponseRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<SubmitResponseResult> SubmitAsync(Guid surveyId, SaveResponseRequest request, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<MyResponseDto>> ListMineAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<PagedResult<ResponseSummaryDto>> ListForSurveyAsync(Guid surveyId, ResponseQuery query, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ResponseDetailDto> GetAsync(Guid responseId, CancellationToken ct = default) => throw new NotImplementedException();
    public Task DeleteAsync(Guid responseId, CancellationToken ct = default) => throw new NotImplementedException();
}
