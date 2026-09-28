using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>
/// Scoped holder of the current <see cref="ClaimsPrincipal"/>. Filled by
/// <see cref="CurrentUserMiddleware"/> for HTTP requests (API, SSR pages, prerendering) and by
/// <see cref="UserCircuitHandler"/> for interactive Blazor circuits, where <c>HttpContext</c> must
/// not be used.
/// </summary>
public sealed class CurrentUser : ICurrentUser
{
    private ClaimsPrincipal _principal = new(new ClaimsIdentity());

    /// <summary>Replaces the principal.</summary>
    public void SetPrincipal(ClaimsPrincipal principal) => _principal = principal;

    /// <inheritdoc />
    public Guid? UserId =>
        Guid.TryParse(_principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <inheritdoc />
    public string? UserName => IsAuthenticated ? _principal.Identity?.Name : null;

    /// <inheritdoc />
    public bool IsAuthenticated => _principal.Identity?.IsAuthenticated == true;

    /// <inheritdoc />
    public Guid? WorkspaceId =>
        IsAuthenticated && Guid.TryParse(_principal.FindFirstValue(AppClaimTypes.WorkspaceId), out var id) ? id : null;

    /// <inheritdoc />
    public bool IsAdmin => IsInRole(AppRoles.Admin) && WorkspaceId.HasValue;

    /// <inheritdoc />
    public bool IsSuperAdmin => IsInRole(AppRoles.SuperAdmin);

    /// <inheritdoc />
    public bool IsInRole(string role) => IsAuthenticated && _principal.IsInRole(role);
}

/// <summary>Copies <c>HttpContext.User</c> into the scoped <see cref="CurrentUser"/> after authentication.</summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    /// <summary>Middleware entry point.</summary>
    public Task InvokeAsync(HttpContext context, CurrentUser currentUser)
    {
        currentUser.SetPrincipal(context.User);
        return next(context);
    }
}

/// <summary>
/// Keeps <see cref="CurrentUser"/> in sync with the circuit's authentication state
/// (pattern from the ASP.NET Core docs: "circuit handler to capture users for custom services").
/// </summary>
internal sealed class UserCircuitHandler(AuthenticationStateProvider authenticationStateProvider, CurrentUser currentUser)
    : CircuitHandler, IDisposable
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationChanged;
        await RefreshAsync();
    }

    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken) => await RefreshAsync();

    public void Dispose() => authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationChanged;

    private async Task RefreshAsync()
    {
        var state = await authenticationStateProvider.GetAuthenticationStateAsync();
        currentUser.SetPrincipal(state.User);
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> task) => _ = UpdateAsync(task);

    private async Task UpdateAsync(Task<AuthenticationState> task)
    {
        try
        {
            var state = await task;
            currentUser.SetPrincipal(state.User);
        }
        catch
        {
            currentUser.SetPrincipal(new ClaimsPrincipal(new ClaimsIdentity()));
        }
    }
}
