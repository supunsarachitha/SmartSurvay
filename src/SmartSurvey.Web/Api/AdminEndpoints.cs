using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Dashboard;
using SmartSurvey.Application.Users;

namespace SmartSurvey.Web.Api;

/// <summary><c>/api/v1/dashboard</c>, <c>/users</c>, <c>/audit</c> and <c>/branding</c> (admin).</summary>
public static class AdminEndpoints
{
    /// <summary>Maps the dashboard endpoint.</summary>
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (IDashboardService dashboard, CancellationToken ct) => dashboard.GetSummaryAsync(ct))
            .WithName("GetDashboard")
            .WithSummary("KPIs, 30-day response trend, top surveys and recent responses.");

        return group;
    }

    /// <summary>Maps the user administration endpoints.</summary>
    public static RouteGroupBuilder MapUserEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (IUserAdminService users, [AsParameters] UserListParameters query, CancellationToken ct) => users.ListAsync(query.ToQuery(), ct))
            .WithName("ListUsers")
            .WithSummary("Lists users (search by e-mail/name, role filter, paging).");

        group.MapGet("/{id:guid}", (IUserAdminService users, Guid id, CancellationToken ct) => users.GetAsync(id, ct))
            .WithName("GetUser")
            .WithSummary("Returns a user.");

        group.MapPost("/", async (IUserAdminService users, CreateUserRequest request, CancellationToken ct) =>
            {
                var created = await users.CreateAsync(request, ct);
                return TypedResults.Created($"{ApiEndpoints.Prefix}/users/{created.Id}", created);
            })
            .WithName("CreateUser")
            .WithSummary("Creates a user with a password and roles.");

        group.MapPut("/{id:guid}/roles", (IUserAdminService users, Guid id, SetUserRolesRequest request, CancellationToken ct) =>
                users.SetRolesAsync(id, request.Roles, ct))
            .WithName("SetUserRoles")
            .WithSummary("Replaces the roles of a user (Admin, User).");

        group.MapPost("/{id:guid}/password", (IUserAdminService users, Guid id, SetUserPasswordRequest request, CancellationToken ct) =>
                users.SetPasswordAsync(id, request.Password, ct))
            .WithName("SetUserPassword")
            .WithSummary("Sets a new password for a user and signs them out everywhere.");

        group.MapPost("/{id:guid}/lock", (IUserAdminService users, Guid id, CancellationToken ct) => users.LockAsync(id, ct))
            .WithName("LockUser")
            .WithSummary("Locks a user out until unlocked.");

        group.MapPost("/{id:guid}/unlock", (IUserAdminService users, Guid id, CancellationToken ct) => users.UnlockAsync(id, ct))
            .WithName("UnlockUser")
            .WithSummary("Unlocks a user.");

        group.MapDelete("/{id:guid}", async (IUserAdminService users, Guid id, CancellationToken ct) =>
            {
                await users.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteUser")
            .WithSummary("Deletes a user (their responses are kept as anonymous).");

        return group;
    }

    /// <summary>Maps the audit log endpoint.</summary>
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (IAuditService audit, [AsParameters] AuditListParameters query, CancellationToken ct) => audit.ListAsync(query.ToQuery(), ct))
            .WithName("ListAuditLog")
            .WithSummary("Audit log, newest first (search, entity, date range, paging).");

        return group;
    }

    /// <summary>Maps the branding administration endpoints (reading is public: <c>GET /api/v1/public/branding</c>).</summary>
    public static RouteGroupBuilder MapBrandingEndpoints(this RouteGroupBuilder group)
    {
        group.MapPut("/", (IBrandingService branding, UpdateBrandingRequest request, CancellationToken ct) => branding.UpdateAsync(request, ct))
            .WithName("UpdateBranding")
            .WithSummary("Changes the product name, tagline and built-in icon.");

        group.MapPost("/logo", (IBrandingService branding, UploadLogoRequest request, CancellationToken ct) =>
                branding.SetLogoAsync(request.Content, request.FileName, ct))
            .WithName("UploadBrandingLogo")
            .WithSummary("Uploads a logo (base64 JSON; PNG, JPEG, GIF, WebP, ICO or SVG up to 512 KB), also used as favicon.");

        group.MapDelete("/logo", (IBrandingService branding, CancellationToken ct) => branding.RemoveLogoAsync(ct))
            .WithName("RemoveBrandingLogo")
            .WithSummary("Removes the uploaded logo (the built-in icon is used again).");

        group.MapPost("/reset", (IBrandingService branding, CancellationToken ct) => branding.ResetAsync(ct))
            .WithName("ResetBranding")
            .WithSummary("Restores the configured default branding.");

        return group;
    }
}
