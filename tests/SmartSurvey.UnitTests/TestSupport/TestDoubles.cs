using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.UnitTests.TestSupport;

/// <summary>Well-known workspaces created by <see cref="SqliteTestDatabase"/>.</summary>
public static class TestWorkspaces
{
    /// <summary>Workspace of the default admin and respondent.</summary>
    public static readonly Guid DefaultId = Guid.Parse("00000000-0000-0000-0000-0000000000d1");

    /// <summary>A second workspace for isolation tests.</summary>
    public static readonly Guid OtherId = Guid.Parse("00000000-0000-0000-0000-0000000000d2");
}

/// <summary>Settable <see cref="ICurrentUser"/> for tests.</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    /// <summary>Well-known admin id used by <see cref="Admin"/>.</summary>
    public static readonly Guid AdminId = Guid.Parse("00000000-0000-0000-0000-00000000a001");

    /// <summary>Well-known respondent id used by <see cref="Respondent"/>.</summary>
    public static readonly Guid RespondentId = Guid.Parse("00000000-0000-0000-0000-00000000b001");

    /// <summary>Well-known super admin id used by <see cref="SuperAdmin"/>.</summary>
    public static readonly Guid SuperAdminId = Guid.Parse("00000000-0000-0000-0000-00000000c001");

    /// <inheritdoc />
    public Guid? UserId { get; set; }

    /// <inheritdoc />
    public string? UserName { get; set; }

    /// <summary>Roles of the user.</summary>
    public HashSet<string> Roles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool IsAuthenticated => UserId.HasValue;

    /// <inheritdoc />
    public Guid? WorkspaceId { get; set; }

    /// <inheritdoc />
    public bool IsAdmin => IsInRole(AppRoles.Admin) && WorkspaceId.HasValue;

    /// <inheritdoc />
    public bool IsSuperAdmin => IsInRole(AppRoles.SuperAdmin);

    /// <inheritdoc />
    public bool IsInRole(string role) => IsAuthenticated && Roles.Contains(role);

    /// <summary>An administrator of <see cref="TestWorkspaces.DefaultId"/>.</summary>
    public static TestCurrentUser Admin() =>
        new TestCurrentUser { UserId = AdminId, UserName = "admin@test.local", WorkspaceId = TestWorkspaces.DefaultId }.WithRoles(AppRoles.Admin);

    /// <summary>A regular logged-in respondent of <see cref="TestWorkspaces.DefaultId"/>.</summary>
    public static TestCurrentUser Respondent(Guid? id = null) =>
        new TestCurrentUser { UserId = id ?? RespondentId, UserName = "user@test.local", WorkspaceId = TestWorkspaces.DefaultId }.WithRoles(AppRoles.User);

    /// <summary>A super admin (no workspace).</summary>
    public static TestCurrentUser SuperAdmin() =>
        new TestCurrentUser { UserId = SuperAdminId, UserName = "super@test.local" }.WithRoles(AppRoles.SuperAdmin);

    /// <summary>An anonymous visitor.</summary>
    public static TestCurrentUser Anonymous() => new();

    /// <summary>
    /// Switches this instance to act as another user (keeps object identity for services holding it).
    /// Signed-in users belong to <see cref="TestWorkspaces.DefaultId"/> unless they are super admins;
    /// use <see cref="InWorkspace"/> to move them.
    /// </summary>
    public TestCurrentUser ActAs(Guid? userId, string? userName, params string[] roles)
    {
        UserId = userId;
        UserName = userName;
        WorkspaceId = userId is null || roles.Contains(AppRoles.SuperAdmin) ? null : TestWorkspaces.DefaultId;
        Roles.Clear();
        foreach (var role in roles)
        {
            Roles.Add(role);
        }

        return this;
    }

    /// <summary>Sets the workspace of the current user (null = none).</summary>
    public TestCurrentUser InWorkspace(Guid? workspaceId)
    {
        WorkspaceId = workspaceId;
        return this;
    }

    /// <summary>Switches to a super admin.</summary>
    public TestCurrentUser ActAsSuperAdmin() => ActAs(SuperAdminId, "super@test.local", AppRoles.SuperAdmin);

    /// <summary>Switches to anonymous.</summary>
    public TestCurrentUser ActAsAnonymous() => ActAs(null, null);

    /// <summary>Switches to the admin.</summary>
    public TestCurrentUser ActAsAdmin() => ActAs(AdminId, "admin@test.local", AppRoles.Admin);

    /// <summary>Switches to the default respondent.</summary>
    public TestCurrentUser ActAsRespondent(Guid? id = null) => ActAs(id ?? RespondentId, "user@test.local", AppRoles.User);

    private TestCurrentUser WithRoles(params string[] roles)
    {
        foreach (var role in roles)
        {
            Roles.Add(role);
        }

        return this;
    }
}

/// <summary><see cref="IAuditService"/> that records entries in memory.</summary>
public sealed class RecordingAuditService : IAuditService
{
    /// <summary>Recorded entries.</summary>
    public List<(string Action, string EntityType, string? EntityId, string? Details)> Entries { get; } = [];

    /// <inheritdoc />
    public Task LogAsync(string action, string entityType, string? entityId, string? details = null, CancellationToken ct = default)
    {
        Entries.Add((action, entityType, entityId, details));
        return Task.CompletedTask;
    }

    /// <summary>Workspaces passed to <see cref="LogInWorkspaceAsync"/>, in call order.</summary>
    public List<Guid> Workspaces { get; } = [];

    /// <inheritdoc />
    public Task LogInWorkspaceAsync(Guid workspaceId, string action, string entityType, string? entityId, string? details = null, CancellationToken ct = default)
    {
        Workspaces.Add(workspaceId);
        return LogAsync(action, entityType, entityId, details, ct);
    }

    /// <inheritdoc />
    public Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery query, CancellationToken ct = default) =>
        Task.FromResult(PagedResult<AuditLogDto>.Empty(query.Page, query.PageSize));

    /// <inheritdoc />
    public Task<PagedResult<AuditLogDto>> ListSystemAsync(AuditQuery query, CancellationToken ct = default) =>
        Task.FromResult(PagedResult<AuditLogDto>.Empty(query.Page, query.PageSize));
}

/// <summary>Read-only <see cref="IBrandingService"/> returning a fixed product name.</summary>
public sealed class StubBrandingService(string productName = "Acme Surveys") : IBrandingService
{
    /// <inheritdoc />
    public Task<BrandingDto> GetAsync(CancellationToken ct = default) => Task.FromResult(new BrandingDto { ProductName = productName });

    /// <inheritdoc />
    public Task<BrandingDto> UpdateAsync(UpdateBrandingRequest request, CancellationToken ct = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<BrandingDto> SetLogoAsync(byte[] content, string? fileName, CancellationToken ct = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<BrandingDto> RemoveLogoAsync(CancellationToken ct = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<BrandingDto> ResetAsync(CancellationToken ct = default) => throw new NotSupportedException();

    /// <inheritdoc />
    public Task<BrandingLogo?> GetLogoAsync(CancellationToken ct = default) => Task.FromResult<BrandingLogo?>(null);
}
