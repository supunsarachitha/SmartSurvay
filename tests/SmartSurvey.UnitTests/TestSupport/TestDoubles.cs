using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Identity;

namespace SmartSurvey.UnitTests.TestSupport;

/// <summary>Settable <see cref="ICurrentUser"/> for tests.</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    /// <summary>Well-known admin id used by <see cref="Admin"/>.</summary>
    public static readonly Guid AdminId = Guid.Parse("00000000-0000-0000-0000-00000000a001");

    /// <summary>Well-known respondent id used by <see cref="Respondent"/>.</summary>
    public static readonly Guid RespondentId = Guid.Parse("00000000-0000-0000-0000-00000000b001");

    /// <inheritdoc />
    public Guid? UserId { get; set; }

    /// <inheritdoc />
    public string? UserName { get; set; }

    /// <summary>Roles of the user.</summary>
    public HashSet<string> Roles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public bool IsAuthenticated => UserId.HasValue;

    /// <inheritdoc />
    public bool IsAdmin => IsInRole(AppRoles.Admin);

    /// <inheritdoc />
    public bool IsInRole(string role) => IsAuthenticated && Roles.Contains(role);

    /// <summary>An administrator.</summary>
    public static TestCurrentUser Admin() => new TestCurrentUser { UserId = AdminId, UserName = "admin@test.local" }.WithRoles(AppRoles.Admin);

    /// <summary>A regular logged-in respondent.</summary>
    public static TestCurrentUser Respondent(Guid? id = null) =>
        new TestCurrentUser { UserId = id ?? RespondentId, UserName = "user@test.local" }.WithRoles(AppRoles.User);

    /// <summary>An anonymous visitor.</summary>
    public static TestCurrentUser Anonymous() => new();

    /// <summary>Switches this instance to act as another user (keeps object identity for services holding it).</summary>
    public TestCurrentUser ActAs(Guid? userId, string? userName, params string[] roles)
    {
        UserId = userId;
        UserName = userName;
        Roles.Clear();
        foreach (var role in roles)
        {
            Roles.Add(role);
        }

        return this;
    }

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

    /// <inheritdoc />
    public Task<PagedResult<AuditLogDto>> ListAsync(AuditQuery query, CancellationToken ct = default) =>
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
