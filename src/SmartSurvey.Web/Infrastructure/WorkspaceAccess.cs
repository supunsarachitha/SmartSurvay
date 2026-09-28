using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>
/// Sign-in manager that refuses members of workspaces that are disabled or waiting for approval
/// (covers the login page, the Identity API and two-factor sign-in, which all call
/// <see cref="CanSignInAsync"/>).
/// </summary>
public sealed class AppSignInManager(
    UserManager<ApplicationUser> userManager,
    IHttpContextAccessor contextAccessor,
    IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
    IOptions<IdentityOptions> optionsAccessor,
    ILogger<SignInManager<ApplicationUser>> logger,
    IAuthenticationSchemeProvider schemes,
    IUserConfirmation<ApplicationUser> confirmation,
    IWorkspaceStatusProvider workspaces)
    : SignInManager<ApplicationUser>(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
{
    /// <inheritdoc />
    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        if (!await base.CanSignInAsync(user))
        {
            return false;
        }

        return user.WorkspaceId is not { } workspaceId || await workspaces.GetStatusAsync(workspaceId) == WorkspaceStatus.Active;
    }
}

/// <summary>
/// Ends access for members of workspaces that were disabled after they signed in: API calls and Blazor
/// connections get 403, pages sign the member out and show <c>/workspace-unavailable</c>. Uses the
/// cached workspace status, so it costs no database round trip on most requests.
/// </summary>
public sealed class WorkspaceAccessMiddleware(RequestDelegate next)
{
    /// <summary>Page explaining why the workspace cannot be used.</summary>
    public const string UnavailablePath = "/workspace-unavailable";

    /// <summary>Middleware entry point.</summary>
    public async Task InvokeAsync(HttpContext context, IWorkspaceStatusProvider workspaces)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && Guid.TryParse(context.User.FindFirstValue(AppClaimTypes.WorkspaceId), out var workspaceId)
            && await workspaces.GetStatusAsync(workspaceId, context.RequestAborted) is var status and not WorkspaceStatus.Active
            && !context.Request.Path.StartsWithSegments(UnavailablePath))
        {
            var reason = status == WorkspaceStatus.PendingApproval ? "pending" : "disabled";
            if (WebSetup.IsApiRequest(context.Request) || context.Request.Path.StartsWithSegments("/_blazor"))
            {
                await Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Workspace unavailable",
                    detail: reason == "pending"
                        ? "Your workspace is waiting for approval by a site administrator."
                        : "Your workspace has been disabled by a site administrator.").ExecuteAsync(context);
                return;
            }

            await context.SignOutAsync(IdentityConstants.ApplicationScheme);
            context.Response.Redirect($"{UnavailablePath}?reason={reason}");
            return;
        }

        await next(context);
    }
}
