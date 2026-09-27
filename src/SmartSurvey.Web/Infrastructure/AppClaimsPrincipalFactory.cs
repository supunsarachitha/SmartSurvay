using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>
/// Adds the user's display name to the sign-in claims so the UI can greet people by name without a
/// database lookup per request (falls back to the e-mail address when no name is set).
/// </summary>
public sealed class AppClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole<Guid>> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole<Guid>>(userManager, roleManager, options)
{
    /// <summary>Claim type carrying <see cref="ApplicationUser.DisplayName"/>.</summary>
    public const string DisplayNameClaim = "display_name";

    /// <inheritdoc />
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
        {
            identity.AddClaim(new Claim(DisplayNameClaim, user.DisplayName));
        }

        return identity;
    }
}
