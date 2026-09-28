using SmartSurvey.Application.Common;
using SmartSurvey.Application.Workspaces;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>The signed-in member's workspace for menus and headers (served from the workspace cache).</summary>
public sealed class CurrentWorkspace(ICurrentUser currentUser, IWorkspaceStatusProvider workspaces)
{
    /// <summary>The workspace, or null for guests and super admins.</summary>
    public async Task<WorkspaceInfo?> GetAsync(CancellationToken ct = default) =>
        currentUser.WorkspaceId is { } id ? await workspaces.GetAsync(id, ct) : null;
}
