using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Domain.Entities;
using SmartSurvey.UnitTests.TestSupport;

namespace SmartSurvey.UnitTests.Workspaces;

/// <summary><see cref="WorkspaceService"/>, <see cref="PlatformSettingsService"/> and <see cref="WorkspaceStatusCache"/>.</summary>
public sealed class WorkspaceServiceTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _db = new();
    private readonly RecordingAuditService _audit = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private WorkspaceService Workspaces => new(_db, _db.CurrentUser, _audit);

    private PlatformSettingsService Settings => new(_db, _db.CurrentUser, _audit);

    private static UpdateWorkspaceSettingsRequest ValidSettings() => new()
    {
        Name = "  Research team ",
        Description = "We ask good questions.",
        ContactEmail = "team@test.local",
        AllowSelfRegistration = false,
        ShowPublicSurveyList = true,
    };

    [Fact]
    public async Task Members_read_their_own_workspace()
    {
        _db.CurrentUser.ActAsRespondent();

        var workspace = await Workspaces.GetCurrentAsync();

        Assert.Equal(TestWorkspaces.DefaultId, workspace.Id);
        Assert.Equal("test", workspace.Slug);
    }

    [Fact]
    public async Task People_without_a_workspace_have_no_current_workspace()
    {
        _db.CurrentUser.ActAsSuperAdmin();
        await Assert.ThrowsAsync<ForbiddenException>(() => Workspaces.GetCurrentAsync());

        _db.CurrentUser.ActAsAnonymous();
        await Assert.ThrowsAsync<ForbiddenException>(() => Workspaces.GetCurrentAsync());
    }

    [Fact]
    public async Task Admins_save_their_workspace_settings_and_it_is_audited()
    {
        var saved = await Workspaces.UpdateSettingsAsync(ValidSettings());

        Assert.Equal("Research team", saved.Name);
        Assert.Equal("team@test.local", saved.ContactEmail);
        Assert.False(saved.AllowSelfRegistration);
        Assert.Equal("test", saved.Slug); // the address is managed by super admins
        Assert.Contains(_audit.Entries, e => e.Action == AuditActions.WorkspaceSettingsUpdated && e.EntityId == TestWorkspaces.DefaultId.ToString());

        await using var db = _db.CreateContext();
        Assert.Equal("Other workspace", (await db.Workspaces.SingleAsync(w => w.Id == TestWorkspaces.OtherId)).Name);
    }

    [Fact]
    public async Task Only_admins_change_settings_and_invalid_values_are_rejected()
    {
        var invalid = ValidSettings();
        invalid.Name = " ";
        invalid.ContactEmail = "not-an-address";
        var error = await Assert.ThrowsAsync<AppValidationException>(() => Workspaces.UpdateSettingsAsync(invalid));
        Assert.Contains(nameof(UpdateWorkspaceSettingsRequest.Name), error.Errors.Keys);
        Assert.Contains(nameof(UpdateWorkspaceSettingsRequest.ContactEmail), error.Errors.Keys);

        _db.CurrentUser.ActAsRespondent();
        await Assert.ThrowsAsync<ForbiddenException>(() => Workspaces.UpdateSettingsAsync(ValidSettings()));
        _db.CurrentUser.ActAsSuperAdmin();
        await Assert.ThrowsAsync<ForbiddenException>(() => Workspaces.UpdateSettingsAsync(ValidSettings()));
    }

    [Fact]
    public async Task Public_lookup_finds_only_active_workspaces()
    {
        _db.CurrentUser.ActAsAnonymous();
        Assert.Equal(TestWorkspaces.OtherId, (await Workspaces.FindPublicAsync(" OTHER "))!.Id);
        Assert.Null(await Workspaces.FindPublicAsync("missing"));

        await SetStatusAsync(TestWorkspaces.OtherId, WorkspaceStatus.Disabled);
        Assert.Null(await Workspaces.FindPublicAsync("other"));
        await SetStatusAsync(TestWorkspaces.OtherId, WorkspaceStatus.PendingApproval);
        Assert.Null(await Workspaces.FindPublicAsync("other"));
    }

    [Fact]
    public async Task System_settings_have_defaults_and_only_super_admins_change_them()
    {
        var defaults = await Settings.GetAsync();
        Assert.True(defaults.AllowWorkspaceSignup);
        Assert.False(defaults.RequireWorkspaceApproval);

        var change = new UpdatePlatformSettingsRequest { AllowWorkspaceSignup = false, RequireWorkspaceApproval = true, SupportEmail = " help@test.local " };
        await Assert.ThrowsAsync<ForbiddenException>(() => Settings.UpdateAsync(change)); // workspace admin

        _db.CurrentUser.ActAsSuperAdmin();
        await Assert.ThrowsAsync<AppValidationException>(() => Settings.UpdateAsync(new UpdatePlatformSettingsRequest { SupportEmail = "nope" }));
        var saved = await Settings.UpdateAsync(change);
        Assert.Equal("help@test.local", saved.SupportEmail);

        _db.CurrentUser.ActAsAnonymous(); // the sign-up page reads them
        Assert.Equal(saved, await Settings.GetAsync());
        Assert.Contains(_audit.Entries, e => e.Action == AuditActions.PlatformSettingsUpdated);
    }

    [Fact]
    public async Task Status_cache_serves_cached_values_until_invalidated_or_expired()
    {
        var services = new ServiceCollection().AddSingleton<IAppDbContextFactory>(_db).BuildServiceProvider();
        var cache = new WorkspaceStatusCache(services.GetRequiredService<IServiceScopeFactory>(), _db.Time);

        Assert.Equal(WorkspaceStatus.Active, await cache.GetStatusAsync(TestWorkspaces.OtherId));
        Assert.Null(await cache.GetStatusAsync(Guid.NewGuid()));

        await SetStatusAsync(TestWorkspaces.OtherId, WorkspaceStatus.Disabled);
        Assert.Equal(WorkspaceStatus.Active, await cache.GetStatusAsync(TestWorkspaces.OtherId)); // cached

        cache.Invalidate(TestWorkspaces.OtherId);
        Assert.Equal(WorkspaceStatus.Disabled, await cache.GetStatusAsync(TestWorkspaces.OtherId));

        await SetStatusAsync(TestWorkspaces.OtherId, WorkspaceStatus.Active);
        _db.Time.Advance(WorkspaceStatusCache.Ttl + TimeSpan.FromSeconds(1));
        Assert.Equal(WorkspaceStatus.Active, await cache.GetStatusAsync(TestWorkspaces.OtherId)); // expired → reloaded
    }

    [Theory]
    [InlineData("acme", true)]
    [InlineData("acme-research-2", true)]
    [InlineData("ab", false)]
    [InlineData("Acme", false)]
    [InlineData("-acme", false)]
    [InlineData("acme_research", false)]
    public void Workspace_addresses_follow_the_slug_rules(string slug, bool valid) =>
        Assert.Equal(valid, WorkspaceSlugs.IsValid(slug));

    [Fact]
    public void Addresses_are_generated_from_names_within_the_limits()
    {
        Assert.Equal("acme-research", WorkspaceSlugs.FromRequestOrName(null, "Acme Research!"));
        Assert.Equal("my-own", WorkspaceSlugs.FromRequestOrName(" My-Own ", "ignored"));
        Assert.True(WorkspaceSlugs.IsValid(WorkspaceSlugs.FromRequestOrName(null, "Q")));
        Assert.True(WorkspaceSlugs.IsValid(WorkspaceSlugs.FromRequestOrName(null, new string('x', 200))));
        Assert.True(WorkspaceSlugs.WithSuffix(new string('x', 60), 12).Length <= Workspace.SlugMaxLength);
    }

    private async Task SetStatusAsync(Guid id, WorkspaceStatus status)
    {
        await using var db = _db.CreateContext();
        await db.Workspaces.Where(w => w.Id == id).ExecuteUpdateAsync(w => w.SetProperty(x => x.Status, status));
    }
}
