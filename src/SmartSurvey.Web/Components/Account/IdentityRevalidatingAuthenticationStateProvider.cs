using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.Web.Components.Account;

// Server-side AuthenticationStateProvider for interactive circuits. Every minute it checks that the user's
// workspace is still active (cached, cheap) and every 30 minutes it revalidates the security stamp; a failed
// check signs the circuit out.
internal sealed class IdentityRevalidatingAuthenticationStateProvider(
        ILoggerFactory loggerFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<IdentityOptions> options,
        IWorkspaceStatusProvider workspaces,
        TimeProvider time)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    private static readonly TimeSpan SecurityStampInterval = TimeSpan.FromMinutes(30);
    private DateTimeOffset _stampValidatedAt = time.GetUtcNow();

    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var principal = authenticationState.User;
        if (Guid.TryParse(principal.FindFirstValue(AppClaimTypes.WorkspaceId), out var workspaceId)
            && await workspaces.GetStatusAsync(workspaceId, cancellationToken) != WorkspaceStatus.Active)
        {
            return false; // the workspace was disabled while the circuit was open
        }

        var now = time.GetUtcNow();
        if (now - _stampValidatedAt < SecurityStampInterval)
        {
            return true;
        }

        // Get the user manager from a new scope to ensure it fetches fresh data
        await using var scope = scopeFactory.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var valid = await ValidateSecurityStampAsync(userManager, principal);
        _stampValidatedAt = now;
        return valid;
    }

    private async Task<bool> ValidateSecurityStampAsync(UserManager<ApplicationUser> userManager, ClaimsPrincipal principal)
    {
        var user = await userManager.GetUserAsync(principal);
        if (user is null)
        {
            return false;
        }
        else if (!userManager.SupportsUserSecurityStamp)
        {
            return true;
        }
        else
        {
            var principalStamp = principal.FindFirstValue(options.Value.ClaimsIdentity.SecurityStampClaimType);
            var userStamp = await userManager.GetSecurityStampAsync(user);
            return principalStamp == userStamp;
        }
    }
}
