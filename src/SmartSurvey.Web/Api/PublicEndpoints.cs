using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Responses;
using SmartSurvey.Web.Infrastructure;

namespace SmartSurvey.Web.Api;

/// <summary>
/// <c>/api/v1/public</c> (respondents; anonymous allowed — eligibility is decided by the services),
/// <c>/api/v1/me</c> (the signed-in user) and <c>/api/v1/responses</c> (response detail/deletion).
/// </summary>
public static class PublicEndpoints
{
    /// <summary>Maximum stored length of the submitting browser's user agent.</summary>
    private const int MaxUserAgentLength = 512;

    /// <summary>Request header carrying the access key of a password-protected survey.</summary>
    public const string AccessKeyHeader = "X-Survey-Access-Key";

    /// <summary>Maps the public endpoints.</summary>
    public static RouteGroupBuilder MapPublicEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/surveys", (string? workspace, IResponseService responses, CancellationToken ct) => responses.ListAvailableAsync(workspace, ct))
            .WithName("ListAvailableSurveys")
            .WithSummary("Surveys the caller can take right now: their own workspace's, or with ?workspace={slug} that workspace's public surveys.");

        group.MapGet("/surveys/{slug}", (IResponseService responses, HttpContext http, string slug, CancellationToken ct) =>
                responses.StartOrResumeAsync(slug, http.Request.Headers[AccessKeyHeader].FirstOrDefault(), ct))
            .WithName("StartSurvey")
            .WithSummary("Survey session: the definition, the caller's draft (if any) and eligibility.")
            .WithDescription($"Password-protected surveys return eligibility PasswordRequired until the access key from the unlock endpoint is sent in the {AccessKeyHeader} header.");

        group.MapPost("/surveys/{slug}/unlock", (IResponseService responses, string slug, UnlockSurveyRequest request, CancellationToken ct) =>
                responses.UnlockAsync(slug, request.Password, ct))
            .RequireRateLimiting(RateLimitPolicies.Submissions)
            .WithName("UnlockSurvey")
            .WithSummary("Checks the password of a protected survey and returns an access key (send it as X-Survey-Access-Key / accessKey).");

        group.MapPost("/surveys/{surveyId:guid}/responses", (
                IResponseService responses, HttpContext http, Guid surveyId, SaveResponseRequest request, CancellationToken ct) =>
            {
                request.UserAgent = UserAgent(http);
                return responses.SubmitAsync(surveyId, request, ct);
            })
            .RequireRateLimiting(RateLimitPolicies.Submissions)
            .WithName("SubmitResponse")
            .WithSummary("Submits a response. Answers are validated on the server with the survey's conditional logic.");

        group.MapPut("/surveys/{surveyId:guid}/draft", async (
                IResponseService responses, HttpContext http, Guid surveyId, SaveResponseRequest request, CancellationToken ct) =>
            {
                request.UserAgent = UserAgent(http);
                return new DraftSavedDto(await responses.SaveDraftAsync(surveyId, request, ct));
            })
            .RequireAuthorization(AuthPolicies.ApiUser)
            .RequireRateLimiting(RateLimitPolicies.Submissions)
            .WithName("SaveDraft")
            .WithSummary("Saves unfinished answers (signed-in users) so the survey can be resumed later.");

        group.MapGet("/branding", (IBrandingService branding, CancellationToken ct) => branding.GetAsync(ct))
            .WithName("GetBranding")
            .WithSummary("Product name, tagline, icon and logo/favicon URLs.");

        return group;
    }

    /// <summary>Maps the <c>/me</c> endpoints.</summary>
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/responses", (IResponseService responses, CancellationToken ct) => responses.ListMineAsync(ct))
            .WithName("ListMyResponses")
            .WithSummary("The caller's submitted responses and drafts.");

        return group;
    }

    /// <summary>Maps the <c>/responses</c> endpoints.</summary>
    public static RouteGroupBuilder MapResponseEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{id:guid}", (IResponseService responses, Guid id, CancellationToken ct) => responses.GetAsync(id, ct))
            .WithName("GetResponse")
            .WithSummary("A response with every answer (administrators, or the respondent for their own response).");

        group.MapDelete("/{id:guid}", async (IResponseService responses, Guid id, CancellationToken ct) =>
            {
                await responses.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(AuthPolicies.ApiAdmin)
            .WithName("DeleteResponse")
            .WithSummary("Deletes a response (administrators).");

        return group;
    }

    private static string? UserAgent(HttpContext http)
    {
        var value = http.Request.Headers.UserAgent.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Length <= MaxUserAgentLength ? value : value[..MaxUserAgentLength];
    }
}
