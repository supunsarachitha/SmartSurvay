using Microsoft.EntityFrameworkCore;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Infrastructure.Persistence;

namespace SmartSurvey.Infrastructure.Workspaces;

/// <summary>Picks a free workspace address.</summary>
internal static class WorkspaceSlugAllocator
{
    /// <summary>
    /// A requested slug must be valid and free (<see cref="ConflictException"/> otherwise); without one a
    /// slug is generated from the name and made unique with -2, -3, … suffixes.
    /// </summary>
    /// <param name="db">Any context (the workspace table is not filtered by the data scope).</param>
    /// <param name="requested">Requested slug, may be empty.</param>
    /// <param name="name">Workspace name.</param>
    /// <param name="property">Property name for validation errors.</param>
    /// <param name="excludeWorkspaceId">Workspace being renamed (its own slug counts as free).</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<string> AllocateAsync(
        AppDbContext db, string? requested, string name, string property, Guid? excludeWorkspaceId, CancellationToken ct)
    {
        var slug = WorkspaceSlugs.FromRequestOrName(requested, name);
        if (!WorkspaceSlugs.IsValid(slug))
        {
            throw new AppValidationException(property, WorkspaceSlugs.InvalidMessage);
        }

        if (!await IsTakenAsync(db, slug, excludeWorkspaceId, ct))
        {
            return slug;
        }

        if (!string.IsNullOrWhiteSpace(requested))
        {
            throw new ConflictException($"The address '{slug}' is already used by another workspace. Please choose a different one.");
        }

        var candidate = slug;
        for (var suffix = 2; await IsTakenAsync(db, candidate, excludeWorkspaceId, ct); suffix++)
        {
            candidate = WorkspaceSlugs.WithSuffix(slug, suffix);
        }

        return candidate;
    }

    private static Task<bool> IsTakenAsync(AppDbContext db, string slug, Guid? exclude, CancellationToken ct) =>
        db.Workspaces.AnyAsync(w => w.Slug == slug && w.Id != exclude, ct);
}
