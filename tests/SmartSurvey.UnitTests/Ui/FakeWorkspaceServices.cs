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
