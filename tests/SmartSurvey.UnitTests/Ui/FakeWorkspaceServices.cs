using SmartSurvey.Application.Common;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Domain.Entities;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Ui;

/// <summary><see cref="IWorkspaceService"/> keeping one current workspace and public workspaces in memory.</summary>
public sealed class FakeWorkspaceService : IWorkspaceService
{
    /// <summary>The current user's workspace.</summary>
    public WorkspaceDto Current { get; set; } = new()
    {
        Id = TestWorkspaces.DefaultId, Name = "Test workspace", Slug = "test", Status = WorkspaceStatus.Active,
        AllowSelfRegistration = true, ShowPublicSurveyList = true,
    };

    /// <summary>Workspaces found by <see cref="FindPublicAsync"/>, by slug.</summary>
    public Dictionary<string, PublicWorkspaceDto> Public { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Requests passed to <see cref="UpdateSettingsAsync"/>.</summary>
    public List<UpdateWorkspaceSettingsRequest> Updates { get; } = [];

    /// <inheritdoc />
    public Task<WorkspaceDto> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(Current);

    /// <inheritdoc />
    public Task<WorkspaceDto> UpdateSettingsAsync(UpdateWorkspaceSettingsRequest request, CancellationToken ct = default)
    {
        Updates.Add(request);
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new AppValidationException(nameof(request.Name), "Please enter a name for the workspace.");
        }

        Current = Current with
        {
            Name = request.Name.Trim(), Description = request.Description, ContactEmail = request.ContactEmail,
            AllowSelfRegistration = request.AllowSelfRegistration, ShowPublicSurveyList = request.ShowPublicSurveyList,
        };
        return Task.FromResult(Current);
    }

    /// <inheritdoc />
    public Task<PublicWorkspaceDto?> FindPublicAsync(string slug, CancellationToken ct = default) =>
        Task.FromResult(Public.GetValueOrDefault(slug.Trim()));
}

/// <summary><see cref="IWorkspaceStatusProvider"/> that knows the default test workspace.</summary>
public sealed class FakeWorkspaceStatusProvider : IWorkspaceStatusProvider
{
    /// <summary>Known workspaces.</summary>
    public Dictionary<Guid, WorkspaceInfo> Workspaces { get; } = new()
    {
        [TestWorkspaces.DefaultId] = new WorkspaceInfo(TestWorkspaces.DefaultId, "Test workspace", "test", WorkspaceStatus.Active),
    };

    /// <inheritdoc />
    public Task<WorkspaceInfo?> GetAsync(Guid workspaceId, CancellationToken ct = default) =>
        Task.FromResult(Workspaces.GetValueOrDefault(workspaceId));

    /// <inheritdoc />
    public void Invalidate(Guid workspaceId)
    {
    }
}

/// <summary><see cref="IPlatformSettingsService"/> with settable settings.</summary>
public sealed class FakePlatformSettingsService : IPlatformSettingsService
{
    /// <summary>Current settings.</summary>
    public PlatformSettingsDto Settings { get; set; } = new() { AllowWorkspaceSignup = true };

    /// <inheritdoc />
    public Task<PlatformSettingsDto> GetAsync(CancellationToken ct = default) => Task.FromResult(Settings);

    /// <inheritdoc />
    public Task<PlatformSettingsDto> UpdateAsync(UpdatePlatformSettingsRequest request, CancellationToken ct = default)
    {
        Settings = new PlatformSettingsDto
        {
            AllowWorkspaceSignup = request.AllowWorkspaceSignup,
            RequireWorkspaceApproval = request.RequireWorkspaceApproval,
            SupportEmail = request.SupportEmail,
        };
        return Task.FromResult(Settings);
    }
}

/// <summary><see cref="IPlatformWorkspaceService"/> keeping workspaces in memory and recording calls.</summary>
public sealed class FakePlatformWorkspaceService : IPlatformWorkspaceService
{
    /// <summary>Workspaces.</summary>
    public List<WorkspaceSummaryDto> Workspaces { get; } = [];

    /// <summary>Calls as "Action:id" strings.</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Last list query.</summary>
    public WorkspaceListQuery? LastQuery { get; private set; }

    /// <summary>Last created workspace request.</summary>
    public CreateWorkspaceRequest? Created { get; private set; }

    /// <inheritdoc />
    public Task<PagedResult<WorkspaceSummaryDto>> ListAsync(WorkspaceListQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        var items = Workspaces.Where(w => query.Status is null || w.Status == query.Status).ToList();
        return Task.FromResult(new PagedResult<WorkspaceSummaryDto>(items, items.Count, query.Page, query.PageSize));
    }

    /// <inheritdoc />
    public Task<WorkspaceSummaryDto> GetAsync(Guid id, CancellationToken ct = default) =>
        Task.FromResult(Workspaces.SingleOrDefault(w => w.Id == id) ?? throw new NotFoundException("Workspace", id));

    /// <inheritdoc />
    public Task<WorkspaceSummaryDto> CreateAsync(CreateWorkspaceRequest request, CancellationToken ct = default)
    {
        Created = request;
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new AppValidationException(nameof(request.Name), "Please enter a name for the workspace.");
        }

        var created = new WorkspaceSummaryDto { Id = Guid.NewGuid(), Name = request.Name, Slug = request.Slug ?? "new", Status = WorkspaceStatus.Active };
        Workspaces.Add(created);
        return Task.FromResult(created);
    }

    /// <inheritdoc />
    public Task<WorkspaceSummaryDto> UpdateAsync(Guid id, UpdateWorkspaceRequest request, CancellationToken ct = default) =>
        Replace(id, "Update", w => w with { Name = request.Name, Slug = request.Slug, Description = request.Description, ContactEmail = request.ContactEmail });

    /// <inheritdoc />
    public Task<WorkspaceSummaryDto> EnableAsync(Guid id, CancellationToken ct = default) =>
        Replace(id, "Enable", w => w with { Status = WorkspaceStatus.Active, StatusReason = null });

    /// <inheritdoc />
    public Task<WorkspaceSummaryDto> ApproveAsync(Guid id, CancellationToken ct = default) =>
        Replace(id, "Approve", w => w with { Status = WorkspaceStatus.Active });

    /// <inheritdoc />
    public Task<WorkspaceSummaryDto> DisableAsync(Guid id, string? reason, CancellationToken ct = default) =>
        Replace(id, "Disable", w => w with { Status = WorkspaceStatus.Disabled, StatusReason = reason });

    /// <inheritdoc />
    public Task DeleteAsync(Guid id, string confirmName, CancellationToken ct = default)
    {
        Calls.Add($"Delete:{id}:{confirmName}");
        Workspaces.RemoveAll(w => w.Id == id);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SystemOverviewDto> GetOverviewAsync(CancellationToken ct = default) => Task.FromResult(new SystemOverviewDto
    {
        WorkspaceCount = Workspaces.Count,
        ActiveCount = Workspaces.Count(w => w.Status == WorkspaceStatus.Active),
        DisabledCount = Workspaces.Count(w => w.Status == WorkspaceStatus.Disabled),
        PendingCount = Workspaces.Count(w => w.Status == WorkspaceStatus.PendingApproval),
        RecentWorkspaces = Workspaces.ToList(),
        PendingWorkspaces = Workspaces.Where(w => w.Status == WorkspaceStatus.PendingApproval).ToList(),
    });

    private Task<WorkspaceSummaryDto> Replace(Guid id, string action, Func<WorkspaceSummaryDto, WorkspaceSummaryDto> change)
    {
        Calls.Add($"{action}:{id}");
        var index = Workspaces.FindIndex(w => w.Id == id);
        Workspaces[index] = change(Workspaces[index]);
        return Task.FromResult(Workspaces[index]);
    }
}
