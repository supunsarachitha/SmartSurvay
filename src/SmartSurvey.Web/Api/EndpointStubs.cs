namespace SmartSurvey.Web.Api;

// STUB - replaced in Phase 5 by one file per group (SurveyEndpoints.cs, ResponseEndpoints.cs,
// ReportEndpoints.cs, PublicEndpoints.cs, MeEndpoints.cs, AdminEndpoints.cs). Delete this file then.

/// <summary>Temporary endpoint group stubs.</summary>
public static class EndpointStubs
{
    /// <summary>/api/v1/surveys</summary>
    public static RouteGroupBuilder MapSurveyEndpoints(this RouteGroupBuilder group) => group;

    /// <summary>/api/v1/responses</summary>
    public static RouteGroupBuilder MapResponseEndpoints(this RouteGroupBuilder group) => group;

    /// <summary>/api/v1/reports</summary>
    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder group) => group;

    /// <summary>/api/v1/public</summary>
    public static RouteGroupBuilder MapPublicEndpoints(this RouteGroupBuilder group) => group;

    /// <summary>/api/v1/me</summary>
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder group) => group;

    /// <summary>/api/v1/dashboard</summary>
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder group) => group;

    /// <summary>/api/v1/users</summary>
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group) => group;

    /// <summary>/api/v1/audit</summary>
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder group) => group;
}
