using SmartSurvey.Application.Common;
using SmartSurvey.Application.Responses;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>
/// <see cref="IResponseService"/> for component tests: configurable lists and session, recorded draft
/// saves and submissions (optionally failing with <see cref="SubmitError"/>); admin operations are not supported.
/// </summary>
public sealed class FakeResponseService : IResponseService
{
    public List<AvailableSurveyDto> Available { get; } = [];

    public List<MyResponseDto> Mine { get; } = [];

    /// <summary>Session returned by <see cref="StartOrResumeAsync"/> (default: not found).</summary>
    public SurveySessionDto Session { get; set; } = new() { Eligibility = SurveyEligibility.NotFound, Message = "Not found." };

    /// <summary>Completion info returned by <see cref="GetCompletionAsync"/>.</summary>
    public SurveyCompletionDto? Completion { get; set; }

    /// <summary>Slugs passed to <see cref="StartOrResumeAsync"/>.</summary>
    public List<string> StartedSlugs { get; } = [];

    /// <summary>Recorded draft saves.</summary>
    public List<SaveResponseRequest> Drafts { get; } = [];

    /// <summary>Recorded submissions.</summary>
    public List<SaveResponseRequest> Submissions { get; } = [];

    /// <summary>Thrown by <see cref="SubmitAsync"/> when set.</summary>
    public Exception? SubmitError { get; set; }

    /// <summary>Id returned for saved drafts.</summary>
    public Guid DraftId { get; } = Guid.NewGuid();

    public Task<IReadOnlyList<AvailableSurveyDto>> ListAvailableAsync(string? workspaceSlug = null, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AvailableSurveyDto>>(Available);

    public Task<IReadOnlyList<MyResponseDto>> ListMineAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<MyResponseDto>>(Mine);

    public Task<SurveySessionDto> StartOrResumeAsync(string slug, CancellationToken ct = default)
    {
        StartedSlugs.Add(slug);
        return Task.FromResult(Session);
    }

    public Task<SurveyCompletionDto?> GetCompletionAsync(string slug, CancellationToken ct = default) => Task.FromResult(Completion);

    public Task<Guid> SaveDraftAsync(Guid surveyId, SaveResponseRequest request, CancellationToken ct = default)
    {
        Drafts.Add(request);
        return Task.FromResult(DraftId);
    }

    public Task<SubmitResponseResult> SubmitAsync(Guid surveyId, SaveResponseRequest request, CancellationToken ct = default)
    {
        Submissions.Add(request);
        return SubmitError is { } error
            ? Task.FromException<SubmitResponseResult>(error)
            : Task.FromResult(new SubmitResponseResult(Guid.NewGuid(), "Thanks!"));
    }

    /// <summary>Rows returned by <see cref="ListForSurveyAsync"/>.</summary>
    public List<ResponseSummaryDto> SurveyResponses { get; } = [];

    /// <summary>Queries passed to <see cref="ListForSurveyAsync"/>.</summary>
    public List<ResponseQuery> ResponseQueries { get; } = [];

    /// <summary>Response returned by <see cref="GetAsync"/>.</summary>
    public ResponseDetailDto? Detail { get; set; }

    /// <summary>Deleted response ids.</summary>
    public List<Guid> Deleted { get; } = [];

    public Task<PagedResult<ResponseSummaryDto>> ListForSurveyAsync(Guid surveyId, ResponseQuery query, CancellationToken ct = default)
    {
        ResponseQueries.Add(query);
        var rows = SurveyResponses.Where(r => !Deleted.Contains(r.Id)).ToList();
        return Task.FromResult(new PagedResult<ResponseSummaryDto>(rows, rows.Count, query.Page, query.PageSize));
    }

    public Task<ResponseDetailDto> GetAsync(Guid responseId, CancellationToken ct = default) =>
        Detail is { } detail && detail.Id == responseId
            ? Task.FromResult(detail)
            : Task.FromException<ResponseDetailDto>(new NotFoundException("Response", responseId));

    public Task DeleteAsync(Guid responseId, CancellationToken ct = default)
    {
        Deleted.Add(responseId);
        return Task.CompletedTask;
    }
}
