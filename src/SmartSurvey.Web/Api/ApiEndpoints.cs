using SmartSurvey.Web.Infrastructure;

namespace SmartSurvey.Web.Api;

/// <summary>Maps all REST API endpoint groups under <c>/api/v1</c>.</summary>
public static class ApiEndpoints
{
    /// <summary>Route prefix of the versioned API.</summary>
    public const string Prefix = "/api/v1";

    /// <summary>
    /// Maps the endpoint groups. Workspace admin groups require <see cref="AuthPolicies.ApiAdmin"/>, the
    /// system group and branding <see cref="AuthPolicies.ApiSuperAdmin"/>; the public group allows
    /// anonymous access (eligibility is decided by the services); "me" and "workspace" require a user.
    /// </summary>
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(Prefix).AddEndpointFilter<ApiErrorFilter>();

        api.MapGroup("/surveys").WithTags("Surveys").RequireAuthorization(AuthPolicies.ApiAdmin).MapSurveyEndpoints();
        api.MapGroup("/responses").WithTags("Responses").RequireAuthorization(AuthPolicies.ApiUser).MapResponseEndpoints();
        api.MapGroup("/reports").WithTags("Reports").RequireAuthorization(AuthPolicies.ApiAdmin).MapReportEndpoints();
        api.MapGroup("/public").WithTags("Public (respondents)").MapPublicEndpoints().MapPublicWorkspaceEndpoints();
        api.MapGroup("/me").WithTags("Me").RequireAuthorization(AuthPolicies.ApiUser).MapMeEndpoints();
        api.MapGroup("/dashboard").WithTags("Dashboard").RequireAuthorization(AuthPolicies.ApiAdmin).MapDashboardEndpoints();
        api.MapGroup("/users").WithTags("Users").RequireAuthorization(AuthPolicies.ApiAdmin).MapUserEndpoints();
        api.MapGroup("/audit").WithTags("Audit").RequireAuthorization(AuthPolicies.ApiAdmin).MapAuditEndpoints();
        api.MapGroup("/branding").WithTags("Branding").RequireAuthorization(AuthPolicies.ApiSuperAdmin).MapBrandingEndpoints();
        api.MapGroup("/workspace").WithTags("Workspace").RequireAuthorization(AuthPolicies.ApiUser).MapWorkspaceEndpoints();
        api.MapGroup("/system").WithTags("System (super admins)").RequireAuthorization(AuthPolicies.ApiSuperAdmin).MapSystemEndpoints();

        return app;
    }
}
