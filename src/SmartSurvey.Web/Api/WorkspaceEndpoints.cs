using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Users;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Web.Infrastructure;

namespace SmartSurvey.Web.Api;

/// <summary>
/// <c>/api/v1/workspace</c> (the caller's workspace), <c>/api/v1/system</c> (super admins) and the public
/// workspace endpoints (<c>/api/v1/public/workspaces</c>, <c>/api/v1/public/settings</c>).
/// </summary>
public static class WorkspaceEndpoints
{
    /// <summary>Maps the caller's workspace endpoints (members read, admins change).</summary>
    public static RouteGroupBuilder MapWorkspaceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (IWorkspaceService workspaces, CancellationToken ct) => workspaces.GetCurrentAsync(ct))
            .WithName("GetMyWorkspace")
            .WithSummary("The caller's workspace and its settings.");

        group.MapPut("/", (IWorkspaceService workspaces, UpdateWorkspaceSettingsRequest request, CancellationToken ct) =>
                workspaces.UpdateSettingsAsync(request, ct))
            .RequireAuthorization(AuthPolicies.ApiAdmin)
            .WithName("UpdateMyWorkspace")
            .WithSummary("Changes the workspace settings: name, description, contact, join link, public survey page (workspace admins).");

        return group;
    }

    /// <summary>Maps the super admin endpoints: overview, workspaces, accounts, settings and the system audit log.</summary>
    public static RouteGroupBuilder MapSystemEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/overview", (IPlatformWorkspaceService platform, CancellationToken ct) => platform.GetOverviewAsync(ct))
            .WithName("GetSystemOverview")
            .WithSummary("System-wide figures, newest workspaces and workspaces waiting for approval.");

        // ----- workspaces
        group.MapGet("/workspaces", (IPlatformWorkspaceService platform, [AsParameters] WorkspaceListParameters query, CancellationToken ct) =>
                platform.ListAsync(query.ToQuery(), ct))
            .WithName("ListWorkspaces")
            .WithSummary("Workspaces with member, survey and response counts (search, status, paging).");

        group.MapPost("/workspaces", async (IPlatformWorkspaceService platform, CreateWorkspaceRequest request, CancellationToken ct) =>
            {
                var created = await platform.CreateAsync(request, ct);
                return TypedResults.Created($"{ApiEndpoints.Prefix}/system/workspaces/{created.Id}", created);
            })
            .WithName("CreateWorkspace")
            .WithSummary("Creates an active workspace together with its first admin account.");

        group.MapGet("/workspaces/{id:guid}", (IPlatformWorkspaceService platform, Guid id, CancellationToken ct) => platform.GetAsync(id, ct))
            .WithName("GetWorkspace")
            .WithSummary("One workspace with its figures and admin addresses.");

        group.MapPut("/workspaces/{id:guid}", (IPlatformWorkspaceService platform, Guid id, UpdateWorkspaceRequest request, CancellationToken ct) =>
                platform.UpdateAsync(id, request, ct))
            .WithName("UpdateWorkspace")
            .WithSummary("Changes a workspace's name, address, description and contact.");

        group.MapPost("/workspaces/{id:guid}/enable", (IPlatformWorkspaceService platform, Guid id, CancellationToken ct) => platform.EnableAsync(id, ct))
            .WithName("EnableWorkspace")
            .WithSummary("Activates a disabled (or pending) workspace.");

        group.MapPost("/workspaces/{id:guid}/approve", (IPlatformWorkspaceService platform, Guid id, CancellationToken ct) => platform.ApproveAsync(id, ct))
            .WithName("ApproveWorkspace")
            .WithSummary("Activates a workspace that waits for approval.");

        group.MapPost("/workspaces/{id:guid}/disable", (IPlatformWorkspaceService platform, Guid id, DisableWorkspaceRequest? request, CancellationToken ct) =>
                platform.DisableAsync(id, request?.Reason, ct))
            .WithName("DisableWorkspace")
            .WithSummary("Disables a workspace: members are signed out and cannot sign in, its survey links stop working. Data is kept.");

        group.MapDelete("/workspaces/{id:guid}", async (IPlatformWorkspaceService platform, Guid id, string? confirmName, CancellationToken ct) =>
            {
                await platform.DeleteAsync(id, confirmName ?? string.Empty, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteWorkspace")
            .WithSummary("Permanently deletes a disabled workspace with all its data and accounts (?confirmName= must repeat its name).");

        // ----- accounts (every workspace + super admins)
        group.MapGet("/accounts", (IUserAdminService users, [AsParameters] AccountListParameters query, CancellationToken ct) =>
                users.ListAsync(query.ToQuery(), ct))
            .WithName("ListAccounts")
            .WithSummary("All accounts with their workspace (search, role, workspace, paging).");

        group.MapPost("/accounts", async (IUserAdminService users, CreateUserRequest request, CancellationToken ct) =>
            {
                var created = await users.CreateAsync(request, ct);
                return TypedResults.Created($"{ApiEndpoints.Prefix}/system/accounts/{created.Id}", created);
            })
            .WithName("CreateAccount")
            .WithSummary("Creates a super admin (no workspaceId) or a member of the given workspace.");

        group.MapGet("/accounts/{id:guid}", (IUserAdminService users, Guid id, CancellationToken ct) => users.GetAsync(id, ct))
            .WithName("GetAccount")
            .WithSummary("Returns an account.");

        group.MapPut("/accounts/{id:guid}/roles", (IUserAdminService users, Guid id, SetUserRolesRequest request, CancellationToken ct) =>
                users.SetRolesAsync(id, request.Roles, ct))
            .WithName("SetAccountRoles")
            .WithSummary("Replaces the roles of an account (Admin/User for members, SuperAdmin for super admins).");

        group.MapPost("/accounts/{id:guid}/password", (IUserAdminService users, Guid id, SetUserPasswordRequest request, CancellationToken ct) =>
                users.SetPasswordAsync(id, request.Password, ct))
            .WithName("SetAccountPassword")
            .WithSummary("Sets a new password and signs the account out everywhere.");

        group.MapPost("/accounts/{id:guid}/lock", (IUserAdminService users, Guid id, CancellationToken ct) => users.LockAsync(id, ct))
            .WithName("LockAccount")
            .WithSummary("Locks an account until unlocked.");

        group.MapPost("/accounts/{id:guid}/unlock", (IUserAdminService users, Guid id, CancellationToken ct) => users.UnlockAsync(id, ct))
            .WithName("UnlockAccount")
            .WithSummary("Unlocks an account.");

        group.MapDelete("/accounts/{id:guid}", async (IUserAdminService users, Guid id, CancellationToken ct) =>
            {
                await users.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteAccount")
            .WithSummary("Deletes an account (its responses are kept as anonymous).");

        // ----- settings and audit
        group.MapGet("/settings", (IPlatformSettingsService settings, CancellationToken ct) => settings.GetAsync(ct))
            .WithName("GetSystemSettings")
            .WithSummary("System settings.");

        group.MapPut("/settings", (IPlatformSettingsService settings, UpdatePlatformSettingsRequest request, CancellationToken ct) =>
                settings.UpdateAsync(request, ct))
            .WithName("UpdateSystemSettings")
            .WithSummary("Workspace sign-up on/off, approval of new workspaces, support address.");

        group.MapGet("/audit", (IAuditService audit, [AsParameters] AuditListParameters query, CancellationToken ct) => audit.ListSystemAsync(query.ToQuery(), ct))
            .WithName("ListSystemAuditLog")
            .WithSummary("System events (super admin actions, sign-ups), newest first.");

        return group;
    }

    /// <summary>Maps the anonymous workspace endpoints into the public group.</summary>
    public static RouteGroupBuilder MapPublicWorkspaceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/settings", async (IPlatformSettingsService settings, CancellationToken ct) =>
            {
                var current = await settings.GetAsync(ct);
                return new { current.AllowWorkspaceSignup, current.RequireWorkspaceApproval, current.SupportEmail };
            })
            .WithName("GetPublicSettings")
            .WithSummary("Whether new workspaces can be created (and need approval), and the support address.");

        group.MapGet("/workspaces/{slug}", async (IWorkspaceService workspaces, string slug, CancellationToken ct) =>
                await workspaces.FindPublicAsync(slug, ct) ?? throw new NotFoundException("Workspace", slug))
            .WithName("GetPublicWorkspace")
            .WithSummary("Name and join options of an active workspace (its public surveys: GET /api/v1/public/surveys?workspace={slug}).");

        group.MapPost("/workspaces", async (
                IWorkspaceSignupService signup, AccountEmails emails, HttpContext http, WorkspaceSignupRequest request, CancellationToken ct) =>
            {
                var result = await signup.SignUpAsync(request, ct);
                await emails.SendConfirmationAsync(result.UserId, AccountEmails.BaseUri(http.Request));
                return TypedResults.Created($"{ApiEndpoints.Prefix}/public/workspaces/{result.WorkspaceSlug}", result);
            })
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("SignUpWorkspace")
            .WithSummary("Creates a new workspace and its first admin (when sign-up is enabled). Sign in with POST /api/auth/login.");

        group.MapPost("/workspaces/{slug}/register", async (
                IWorkspaceSignupService signup, AccountEmails emails, HttpContext http, string slug, JoinWorkspaceRequest request, CancellationToken ct) =>
            {
                var result = await signup.JoinAsync(slug, request, ct);
                await emails.SendConfirmationAsync(result.UserId, AccountEmails.BaseUri(http.Request));
                return TypedResults.Created($"{ApiEndpoints.Prefix}/public/workspaces/{slug}", result);
            })
            .RequireRateLimiting(RateLimitPolicies.Auth)
            .WithName("JoinWorkspace")
            .WithSummary("Creates a member account in a workspace that accepts members through its link.");

        return group;
    }

    /// <summary>
    /// Endpoint filter for the Identity API group: its <c>/register</c> would create accounts outside every
    /// workspace, so it is refused in favour of the workspace sign-up / join endpoints.
    /// </summary>
    public static async ValueTask<object?> RefuseIdentityRegister(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsPost(request.Method) && request.Path.Value?.EndsWith("/register", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Registration happens per workspace",
                detail: $"Create a workspace with POST {ApiEndpoints.Prefix}/public/workspaces or join one with POST {ApiEndpoints.Prefix}/public/workspaces/{{slug}}/register.");
        }

        return await next(context);
    }
}
