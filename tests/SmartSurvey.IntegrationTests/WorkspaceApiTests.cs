using System.Net;
using System.Net.Http.Json;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Dashboard;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Application.Users;
using SmartSurvey.Application.Workspaces;
using SmartSurvey.Domain.Entities;
using static SmartSurvey.IntegrationTests.ApiTestData;

namespace SmartSurvey.IntegrationTests;

/// <summary>
/// Workspaces over HTTP: sign-up and join, isolation of every endpoint group, disabled and pending
/// workspaces, and the boundary between workspace admins and super admins.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class WorkspaceApiTests(ApiFactory factory)
{
    private const string Password = "Workspace123!";
    private static readonly string[] StarterTemplateTitles = ["Customer satisfaction", "Team pulse check", "Event feedback"];

    private readonly HttpClient _admin = factory.Admin();
    private readonly HttpClient _super = factory.SuperAdmin();

    /// <summary>Signs up a new workspace and returns its result and a signed-in client of its admin.</summary>
    private async Task<(WorkspaceSignupResult Result, HttpClient Admin, string Email)> SignUpAsync(string? name = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"founder-{suffix}@it.local";
        var result = await ReadAsync<WorkspaceSignupResult>(await factory.Anonymous().PostAsJsonAsync("/api/v1/public/workspaces", new WorkspaceSignupRequest
        {
            WorkspaceName = name ?? $"Team {suffix}", DisplayName = "Founder", Email = email, Password = Password,
        }), HttpStatusCode.Created);

        return (result, result.Status == WorkspaceStatus.Active ? await factory.SignInAsync(email, Password) : factory.Anonymous(), email);
    }

    [Fact]
    public async Task Identity_api_registration_is_refused_in_favour_of_workspaces()
    {
        var problem = await ProblemAsync(
            await factory.Anonymous().PostAsJsonAsync("/api/auth/register", new { email = "loner@it.local", password = Password }),
            HttpStatusCode.Forbidden);

        Assert.Contains("/api/v1/public/workspaces", problem.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task A_signed_up_workspace_is_its_own_isolated_room()
    {
        var mine = await CreatePublishedSurveyAsync(_admin, "Default workspace survey");
        var (result, founder, email) = await SignUpAsync();

        Assert.Equal(WorkspaceStatus.Active, result.Status);
        var workspace = await ReadAsync<WorkspaceDto>(await founder.GetAsync("/api/v1/workspace"));
        Assert.Equal(result.WorkspaceId, workspace.Id);

        // Nothing of the default workspace is visible or reachable — only the new workspace's starter templates.
        var own = await ReadAsync<PagedResult<SurveySummaryDto>>(await founder.GetAsync("/api/v1/surveys"));
        Assert.Equal(StarterTemplateTitles.Order(), own.Items.Select(s => s.Title).Order());
        Assert.All(own.Items, s => Assert.True(s.IsTemplate));
        Assert.Equal(0, (await ReadAsync<PagedResult<ReportSummaryDto>>(await founder.GetAsync("/api/v1/reports"))).TotalCount);
        Assert.Equal(0, (await ReadAsync<DashboardSummaryDto>(await founder.GetAsync("/api/v1/dashboard"))).TotalSurveys);
        Assert.Equal([email], (await ReadAsync<PagedResult<UserDto>>(await founder.GetAsync("/api/v1/users"))).Items.Select(u => u.Email));
        Assert.DoesNotContain((await ReadAsync<PagedResult<AuditLogDto>>(await founder.GetAsync("/api/v1/audit"))).Items, e => e.EntityId == mine.Id.ToString());
        await AssertStatusAsync(await founder.GetAsync($"/api/v1/surveys/{mine.Id}"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await founder.GetAsync($"/api/v1/surveys/{mine.Id}/responses"), HttpStatusCode.NotFound);
        await AssertStatusAsync(await founder.PostAsJsonAsync($"/api/v1/surveys/{mine.Id}/status", new { status = "Closed" }, ApiFactory.Json), HttpStatusCode.NotFound);
        await AssertStatusAsync(await founder.DeleteAsync($"/api/v1/surveys/{mine.Id}"), HttpStatusCode.NotFound);

        // …and the new workspace's survey is invisible to the default workspace.
        var theirs = await CreatePublishedSurveyAsync(founder, "Founder survey");
        await AssertStatusAsync(await _admin.GetAsync($"/api/v1/surveys/{theirs.Id}"), HttpStatusCode.NotFound);
        Assert.DoesNotContain((await ReadAsync<PagedResult<SurveySummaryDto>>(await _admin.GetAsync("/api/v1/surveys?pageSize=200"))).Items, s => s.Id == theirs.Id);

        // The share link works for everyone (links are system-wide).
        var session = await ReadAsync<SurveySessionDto>(await factory.Anonymous().GetAsync($"/api/v1/public/surveys/{theirs.Slug}"));
        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
    }

    [Fact]
    public async Task Admins_change_their_workspace_settings_but_members_cannot()
    {
        var (result, founder, _) = await SignUpAsync();
        var settings = new UpdateWorkspaceSettingsRequest { Name = "Renamed team", AllowSelfRegistration = true, ShowPublicSurveyList = true };

        var saved = await ReadAsync<WorkspaceDto>(await founder.PutAsJsonAsync("/api/v1/workspace", settings, ApiFactory.Json));
        Assert.Equal("Renamed team", saved.Name);

        var member = await JoinAsync(result.WorkspaceSlug);
        Assert.Equal("Renamed team", (await ReadAsync<WorkspaceDto>(await member.GetAsync("/api/v1/workspace"))).Name);
        await AssertStatusAsync(await member.PutAsJsonAsync("/api/v1/workspace", settings, ApiFactory.Json), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Joining_creates_a_member_of_that_workspace_only()
    {
        var (result, founder, _) = await SignUpAsync();
        var survey = await CreatePublishedSurveyAsync(founder, "Members survey");

        var info = await ReadAsync<PublicWorkspaceDto>(await factory.Anonymous().GetAsync($"/api/v1/public/workspaces/{result.WorkspaceSlug}"));
        Assert.True(info.AllowSelfRegistration);
        var member = await JoinAsync(result.WorkspaceSlug);

        Assert.Contains(await ReadAsync<List<AvailableSurveyDto>>(await member.GetAsync("/api/v1/public/surveys")), s => s.SurveyId == survey.Id);
        await AssertStatusAsync(await member.GetAsync("/api/v1/surveys"), HttpStatusCode.Forbidden);
        Assert.DoesNotContain(await ReadAsync<List<AvailableSurveyDto>>(await factory.Respondent().GetAsync("/api/v1/public/surveys")), s => s.SurveyId == survey.Id);

        await ProblemAsync(await factory.Anonymous().PostAsJsonAsync("/api/v1/public/workspaces/no-such-team/register",
            new JoinWorkspaceRequest { Email = "x@it.local", Password = Password }), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Disabling_a_workspace_locks_its_members_out_at_once_and_enabling_lets_them_back()
    {
        var (result, founder, email) = await SignUpAsync();
        var survey = await CreatePublishedSurveyAsync(founder, "Soon unavailable");

        await ReadAsync<WorkspaceSummaryDto>(await _super.PostAsJsonAsync($"/api/v1/system/workspaces/{result.WorkspaceId}/disable",
            new DisableWorkspaceRequest { Reason = "Maintenance" }, ApiFactory.Json));

        var problem = await ProblemAsync(await founder.GetAsync("/api/v1/workspace"), HttpStatusCode.Forbidden); // existing token
        Assert.Equal("Workspace unavailable", problem.GetProperty("title").GetString());
        await AssertStatusAsync(await factory.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = Password }), HttpStatusCode.Unauthorized);
        var session = await ReadAsync<SurveySessionDto>(await factory.Anonymous().GetAsync($"/api/v1/public/surveys/{survey.Slug}"));
        Assert.Equal(SurveyEligibility.Unavailable, session.Eligibility);
        Assert.Null(session.Survey);
        await ProblemAsync(await factory.Anonymous().GetAsync($"/api/v1/public/workspaces/{result.WorkspaceSlug}"), HttpStatusCode.NotFound);

        await ReadAsync<WorkspaceSummaryDto>(await _super.PostAsync($"/api/v1/system/workspaces/{result.WorkspaceId}/enable", null));
        await AssertStatusAsync(await founder.GetAsync("/api/v1/workspace"), HttpStatusCode.OK);
        await AssertStatusAsync(await factory.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = Password }), HttpStatusCode.OK);
    }

    [Fact]
    public async Task System_settings_control_sign_up_and_approval()
    {
        try
        {
            await PutSettingsAsync(new UpdatePlatformSettingsRequest { AllowWorkspaceSignup = true, RequireWorkspaceApproval = true, SupportEmail = "help@it.local" });
            var publicSettings = await factory.Anonymous().GetFromJsonAsync<PlatformSettingsDto>("/api/v1/public/settings", ApiFactory.Json);
            Assert.True(publicSettings!.RequireWorkspaceApproval);

            var (pending, _, email) = await SignUpAsync();
            Assert.Equal(WorkspaceStatus.PendingApproval, pending.Status);
            await AssertStatusAsync(await factory.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = Password }), HttpStatusCode.Unauthorized);

            var overview = await ReadAsync<SystemOverviewDto>(await _super.GetAsync("/api/v1/system/overview"));
            Assert.Contains(overview.PendingWorkspaces, w => w.Id == pending.WorkspaceId);
            await ReadAsync<WorkspaceSummaryDto>(await _super.PostAsync($"/api/v1/system/workspaces/{pending.WorkspaceId}/approve", null));
            await AssertStatusAsync(await factory.Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password = Password }), HttpStatusCode.OK);

            await PutSettingsAsync(new UpdatePlatformSettingsRequest { AllowWorkspaceSignup = false });
            await ProblemAsync(await factory.Anonymous().PostAsJsonAsync("/api/v1/public/workspaces", new WorkspaceSignupRequest
            {
                WorkspaceName = "Closed door", Email = "closed@it.local", Password = Password,
            }), HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            await PutSettingsAsync(new UpdatePlatformSettingsRequest { AllowWorkspaceSignup = true });
        }
    }

    [Fact]
    public async Task Super_admins_manage_workspaces_and_accounts()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var created = await ReadAsync<WorkspaceSummaryDto>(await _super.PostAsJsonAsync("/api/v1/system/workspaces", new CreateWorkspaceRequest
        {
            Name = $"Managed {suffix}", AdminEmail = $"lead-{suffix}@it.local", AdminPassword = Password,
        }, ApiFactory.Json), HttpStatusCode.Created);
        Assert.Equal([$"lead-{suffix}@it.local"], created.AdminEmails);

        var member = await ReadAsync<UserDto>(await _super.PostAsJsonAsync("/api/v1/system/accounts", new CreateUserRequest
        {
            Email = $"member-{suffix}@it.local", Password = Password, WorkspaceId = created.Id,
        }, ApiFactory.Json), HttpStatusCode.Created);
        var accounts = await ReadAsync<PagedResult<UserDto>>(await _super.GetAsync($"/api/v1/system/accounts?workspaceId={created.Id}"));
        Assert.Equal(2, accounts.TotalCount);
        Assert.All(accounts.Items, a => Assert.Equal(created.Name, a.WorkspaceName));

        await ReadAsync<UserDto>(await _super.PostAsync($"/api/v1/system/accounts/{member.Id}/lock", null));
        await ReadAsync<WorkspaceSummaryDto>(await _super.PutAsJsonAsync($"/api/v1/system/workspaces/{created.Id}", new UpdateWorkspaceRequest
        {
            Name = $"Renamed {suffix}", Slug = $"renamed-{suffix}",
        }, ApiFactory.Json));

        await ProblemAsync(await _super.DeleteAsync($"/api/v1/system/workspaces/{created.Id}?confirmName=Renamed {suffix}"), HttpStatusCode.UnprocessableEntity); // still active
        await ReadAsync<WorkspaceSummaryDto>(await _super.PostAsync($"/api/v1/system/workspaces/{created.Id}/disable", null));
        await AssertStatusAsync(await _super.DeleteAsync($"/api/v1/system/workspaces/{created.Id}?confirmName={Uri.EscapeDataString($"Renamed {suffix}")}"), HttpStatusCode.NoContent);
        await AssertStatusAsync(await _super.GetAsync($"/api/v1/system/workspaces/{created.Id}"), HttpStatusCode.NotFound);

        var audit = await ReadAsync<PagedResult<AuditLogDto>>(await _super.GetAsync($"/api/v1/system/audit?entityId={created.Id}&pageSize=50"));
        Assert.Contains(audit.Items, e => e.Action == AuditActions.WorkspaceCreated);
        Assert.Contains(audit.Items, e => e.Action == AuditActions.WorkspaceDeleted);
    }

    [Theory]
    [InlineData("/api/v1/surveys")]
    [InlineData("/api/v1/reports")]
    [InlineData("/api/v1/dashboard")]
    [InlineData("/api/v1/users")]
    [InlineData("/api/v1/audit")]
    public async Task Super_admins_cannot_open_workspace_content(string path) =>
        await AssertStatusAsync(await _super.GetAsync(path), HttpStatusCode.Forbidden);

    [Theory]
    [InlineData("/api/v1/system/overview")]
    [InlineData("/api/v1/system/workspaces")]
    [InlineData("/api/v1/system/accounts")]
    [InlineData("/api/v1/system/settings")]
    [InlineData("/api/v1/system/audit")]
    public async Task Workspace_admins_and_members_cannot_open_the_system_area(string path)
    {
        await AssertStatusAsync(await _admin.GetAsync(path), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await factory.Respondent().GetAsync(path), HttpStatusCode.Forbidden);
        await AssertStatusAsync(await factory.Anonymous().GetAsync(path), HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> JoinAsync(string slug)
    {
        var email = $"member-{Guid.NewGuid().ToString("N")[..12]}@it.local";
        await ReadAsync<JoinWorkspaceResult>(await factory.Anonymous().PostAsJsonAsync($"/api/v1/public/workspaces/{slug}/register",
            new JoinWorkspaceRequest { DisplayName = "Member", Email = email, Password = Password }), HttpStatusCode.Created);
        return await factory.SignInAsync(email, Password);
    }

    private async Task PutSettingsAsync(UpdatePlatformSettingsRequest settings) =>
        await ReadAsync<PlatformSettingsDto>(await _super.PutAsJsonAsync("/api/v1/system/settings", settings, ApiFactory.Json));
}
