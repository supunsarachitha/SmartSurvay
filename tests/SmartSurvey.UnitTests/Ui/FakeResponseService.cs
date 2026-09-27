using SmartSurvey.Application.Common;
using SmartSurvey.Application.Responses;

namespace SmartSurvey.UnitTests.Ui;

/// <summary><see cref="IResponseService"/> returning configurable lists; write operations are not supported.</summary>
public sealed class FakeResponseService : IResponseService
{
    public List<AvailableSurveyDto> Available { get; } = [];

    public List<MyResponseDto> Mine { get; } = [];

    public Task<IReadOnlyList<AvailableSurveyDto>> ListAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AvailableSurveyDto>>(Available);

    public Task<IReadOnlyList<MyResponseDto>> ListMineAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MyResponseDto>>(Mine);

    public Task<SurveySessionDto> StartOrResumeAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<Guid> SaveDraftAsync(Guid surveyId, SaveResponseRequest request, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<SubmitResponseResult> SubmitAsync(Guid surveyId, SaveResponseRequest request, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<PagedResult<ResponseSummaryDto>> ListForSurveyAsync(Guid surveyId, ResponseQuery query, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<ResponseDetailDto> GetAsync(Guid responseId, CancellationToken ct = default) => throw new NotSupportedException();

    public Task DeleteAsync(Guid responseId, CancellationToken ct = default) => throw new NotSupportedException();
}
