using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Application.Users;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Web.Api;

// Query-string parameters of the list endpoints. Every parameter is optional (nullable): minimal
// APIs treat non-nullable [AsParameters] members as required, which the service query classes
// (with their defaulted, clamped paging properties) would otherwise make mandatory.

/// <summary>Paging parameters shared by every list endpoint.</summary>
public interface IPagingParameters
{
    /// <summary>1-based page number (default 1).</summary>
    int? Page { get; }

    /// <summary>Items per page, 1–200 (default 20).</summary>
    int? PageSize { get; }
}

/// <summary><c>GET /api/v1/surveys</c> parameters.</summary>
public sealed record SurveyListParameters(
    int? Page, int? PageSize, string? Search, SurveyStatus? Status, bool? IncludeArchived, bool? IsTemplate) : IPagingParameters
{
    /// <summary>The service query.</summary>
    public SurveyQuery ToQuery() => new SurveyQuery
    {
        Search = Search,
        Status = Status,
        IncludeArchived = IncludeArchived ?? false,
        IsTemplate = IsTemplate,
    }.WithPaging(this);
}

/// <summary><c>GET /api/v1/surveys/{id}/responses</c> parameters.</summary>
public sealed record ResponseListParameters(
    int? Page, int? PageSize, ResponseStatus? Status, DateOnly? From, DateOnly? To, string? Search) : IPagingParameters
{
    /// <summary>The service query.</summary>
    public ResponseQuery ToQuery() => new ResponseQuery { Status = Status, From = From, To = To, Search = Search }.WithPaging(this);
}

/// <summary><c>GET /api/v1/reports</c> parameters.</summary>
public sealed record ReportListParameters(int? Page, int? PageSize, Guid? SurveyId, string? Search) : IPagingParameters
{
    /// <summary>The service query.</summary>
    public ReportQuery ToQuery() => new ReportQuery { SurveyId = SurveyId, Search = Search }.WithPaging(this);
}

/// <summary><c>GET /api/v1/users</c> parameters.</summary>
public sealed record UserListParameters(int? Page, int? PageSize, string? Search, string? Role) : IPagingParameters
{
    /// <summary>The service query.</summary>
    public UserQuery ToQuery() => new UserQuery { Search = Search, Role = Role }.WithPaging(this);
}

/// <summary><c>GET /api/v1/audit</c> parameters.</summary>
public sealed record AuditListParameters(
    int? Page, int? PageSize, string? Search, string? EntityType, string? EntityId, DateOnly? From, DateOnly? To) : IPagingParameters
{
    /// <summary>The service query.</summary>
    public AuditQuery ToQuery() => new AuditQuery
    {
        Search = Search,
        EntityType = EntityType,
        EntityId = EntityId,
        From = From,
        To = To,
    }.WithPaging(this);
}

/// <summary>Applies optional paging parameters to a service query.</summary>
internal static class PagingParametersExtensions
{
    /// <summary>Copies the supplied page/page size (the query clamps them); returns the query.</summary>
    public static TQuery WithPaging<TQuery>(this TQuery query, IPagingParameters paging)
        where TQuery : PageRequest
    {
        if (paging.Page is { } page)
        {
            query.Page = page;
        }

        if (paging.PageSize is { } pageSize)
        {
            query.PageSize = pageSize;
        }

        return query;
    }
}
