using SmartSurvey.Web.Infrastructure;

namespace SmartSurvey.Web.Api;

/// <summary>Maps all REST API endpoint groups under <c>/api/v1</c>.</summary>
public static class ApiEndpoints
{
    /// <summary>Route prefix of the versioned API.</summary>
    public const string Prefix = "/api/v1";

    /// <summary>
    /// Maps the endpoint groups. Admin groups require <see cref="AuthPolicies.ApiAdmin"/>; the public
    /// group allows anonymous access (eligibility is decided by the services); "me" requires a user.
    /// </summary>
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup(Prefix).AddEndpointFilter<ApiErrorFilter>();

        api.MapGroup("/surveys").WithTags("Surveys").RequireAuthorization(AuthPolicies.ApiAdmin).MapSurveyEndpoints();
        api.MapGroup("/responses").WithTags("Responses").RequireAuthorization(AuthPolicies.ApiUser).MapResponseEndpoints();
        api.MapGroup("/reports").WithTags("Reports").RequireAuthorization(AuthPolicies.ApiAdmin).MapReportEndpoints();
        api.MapGroup("/public").WithTags("Public (respondents)").MapPublicEndpoints();
        api.MapGroup("/me").WithTags("Me").RequireAuthorization(AuthPolicies.ApiUser).MapMeEndpoints();
        api.MapGroup("/dashboard").WithTags("Dashboard").RequireAuthorization(AuthPolicies.ApiAdmin).MapDashboardEndpoints();
        api.MapGroup("/users").WithTags("Users").RequireAuthorization(AuthPolicies.ApiAdmin).MapUserEndpoints();
        api.MapGroup("/audit").WithTags("Audit").RequireAuthorization(AuthPolicies.ApiAdmin).MapAuditEndpoints();
        api.MapGroup("/branding").WithTags("Branding").RequireAuthorization(AuthPolicies.ApiSuperAdmin).MapBrandingEndpoints();

        return app;
    }
}
