namespace SmartSurvey.Infrastructure.Persistence;

/// <summary>What a context instance may see and change (see <see cref="AppDbContext.UseScope"/>).</summary>
public enum DataScopeKind
{
    /// <summary>
    /// No workspace: workspace data is invisible and cannot be written. The default for every new
    /// context (fail-closed); anonymous visitors and super admins get this scope from
    /// <c>IAppDbContextFactory.CreateAsync</c>.
    /// </summary>
    None = 0,

    /// <summary>Exactly one workspace: queries return only its rows, inserts are stamped with its id.</summary>
    Workspace = 1,

    /// <summary>
    /// Unfiltered access for system code (seeding, workspace administration by super admins, the
    /// system-wide slug check). Inserts must set <c>WorkspaceId</c> explicitly.
    /// </summary>
    System = 2,
}

/// <summary>Data scope of an <see cref="AppDbContext"/>.</summary>
/// <param name="Kind">Scope kind.</param>
/// <param name="WorkspaceId">Workspace for <see cref="DataScopeKind.Workspace"/>; empty otherwise.</param>
public readonly record struct DataScope(DataScopeKind Kind, Guid WorkspaceId)
{
    /// <summary>Sees no workspace data.</summary>
    public static DataScope None => default;

    /// <summary>Unfiltered (system code only).</summary>
    public static DataScope System => new(DataScopeKind.System, Guid.Empty);

    /// <summary>One workspace.</summary>
    public static DataScope ForWorkspace(Guid workspaceId) =>
        workspaceId == Guid.Empty
            ? throw new ArgumentException("A workspace id is required.", nameof(workspaceId))
            : new(DataScopeKind.Workspace, workspaceId);

    /// <inheritdoc />
    public override string ToString() => Kind == DataScopeKind.Workspace ? $"Workspace({WorkspaceId})" : Kind.ToString();
}
