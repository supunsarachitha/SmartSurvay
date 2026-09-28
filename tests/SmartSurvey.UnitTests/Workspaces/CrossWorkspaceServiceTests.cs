using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Dashboard;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Application.Surveys.Validation;
using SmartSurvey.Domain.Entities;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Infrastructure.Exports;
using SmartSurvey.UnitTests.Reports;
using SmartSurvey.UnitTests.Responses;
using SmartSurvey.UnitTests.TestSupport;
using static SmartSurvey.UnitTests.Responses.ResponseTestData;

namespace SmartSurvey.UnitTests.Workspaces;

/// <summary>
/// Every service, used by the admin of <see cref="TestWorkspaces.DefaultId"/>, against data of
/// <see cref="TestWorkspaces.OtherId"/>: it must be invisible and untouchable. Plus the respondent rules
/// across workspaces (members of other workspaces answer as guests; disabled workspaces are unavailable).
/// </summary>
public sealed class CrossWorkspaceServiceTests : IAsyncLifetime
{
    private static readonly Guid Theirs = TestWorkspaces.OtherId;
    private ReportTestHarness _h = null!;
    private SampleSurvey _mine = null!;
    private SampleSurvey _foreign = null!;
    private SurveyResponse _foreignResponse = null!;
    private ReportDefinition _foreignReport = null!;

    public async Task InitializeAsync()
    {
        _h = await ReportTestHarness.CreateAsync();
        var r = _h.Responses;
        _mine = await r.SeedSurveyAsync(title: "Mine");
        _foreign = await r.SeedSurveyAsync(title: "Theirs", workspaceId: Theirs);
        await r.SeedResponseAsync(_mine, null, ResponseStatus.Completed);
        _foreignResponse = await r.SeedResponseAsync(_foreign, null, ResponseStatus.Completed, workspaceId: Theirs);
        _foreignReport = new ReportDefinition { Name = "Their report", SurveyId = _foreign.Definition.Id };
        await _h.Db.SeedInWorkspaceAsync(Theirs, _foreignReport);
        _h.User.ActAsAdmin();
    }

    public async Task DisposeAsync() => await _h.Responses.DisposeAsync();

    private SurveyService Surveys => new(_h.Db, _h.User, _h.Db.Time, _h.Audit, new SurveyDefinitionValidator(), NullLogger<SurveyService>.Instance);

    private ResponseService Responses => _h.Responses.Service;

    [Fact]
    public async Task Surveys_of_other_workspaces_are_invisible_and_untouchable()
    {
        var foreignId = _foreign.Definition.Id;

        var list = await Surveys.ListAsync(new SurveyQuery());
        Assert.Equal([_mine.Definition.Id], list.Items.Select(s => s.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Surveys.GetAsync(foreignId));
        Assert.Null(await Surveys.FindBySlugAsync(_foreign.Definition.Slug!));
        await Assert.ThrowsAsync<NotFoundException>(() => Surveys.UpdateAsync(foreignId, _foreign.Definition));
        await Assert.ThrowsAsync<NotFoundException>(() => Surveys.ChangeStatusAsync(foreignId, SurveyStatus.Closed));
        await Assert.ThrowsAsync<NotFoundException>(() => Surveys.DuplicateAsync(foreignId, new DuplicateSurveyRequest()));
        await Assert.ThrowsAsync<NotFoundException>(() => Surveys.ExportDefinitionAsync(foreignId));
        await Assert.ThrowsAsync<NotFoundException>(() => Surveys.DeleteAsync(foreignId));

        await using var db = _h.Db.CreateContext();
        Assert.Equal(SurveyStatus.Published, (await db.Surveys.SingleAsync(s => s.Id == foreignId)).Status);
    }

    [Fact]
    public async Task Survey_links_are_unique_across_workspaces()
    {
        Assert.False(await Surveys.IsSlugAvailableAsync(_foreign.Definition.Slug!));

        var copy = SampleSurveys.CustomerFeedback("Clash").Definition;
        copy.Slug = _foreign.Definition.Slug;
        await Assert.ThrowsAsync<ConflictException>(() => Surveys.CreateAsync(copy));
    }

    [Fact]
    public async Task Responses_of_other_workspaces_are_invisible_and_untouchable()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Responses.ListForSurveyAsync(_foreign.Definition.Id, new ResponseQuery()));
        await Assert.ThrowsAsync<NotFoundException>(() => Responses.GetAsync(_foreignResponse.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => Responses.DeleteAsync(_foreignResponse.Id));

        var export = new ResponseExportService(_h.Db, _h.User, _h.Db.Time, _h.Audit, NullLogger<ResponseExportService>.Instance);
        await Assert.ThrowsAsync<NotFoundException>(() => export.ExportResponsesAsync(_foreign.Definition.Id, ExportFormat.Csv));

        Assert.NotNull(await _h.Responses.FindResponseAsync(_foreignResponse.Id));
    }

    [Fact]
    public async Task Reports_of_other_workspaces_are_invisible_and_cannot_target_their_surveys()
    {
        var reports = await _h.Service.ListAsync(new ReportQuery());
        Assert.DoesNotContain(reports.Items, r => r.Id == _foreignReport.Id);
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Service.GetAsync(_foreignReport.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Service.RunAsync(_foreignReport.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Service.ExportAsync(_foreignReport.Id, ExportFormat.Csv));
        await Assert.ThrowsAsync<NotFoundException>(() => _h.Service.DeleteAsync(_foreignReport.Id));

        var error = await Assert.ThrowsAsync<AppValidationException>(() =>
            _h.Service.CreateAsync(new ReportDefinitionDto { Name = "Spy", SurveyId = _foreign.Definition.Id }));
        Assert.Contains(nameof(ReportDefinitionDto.SurveyId), error.Errors.Keys);
        await Assert.ThrowsAnyAsync<AppException>(() => _h.Service.BuildDefaultAsync(_foreign.Definition.Id));
        await Assert.ThrowsAnyAsync<AppException>(() => _h.Service.PreviewAsync(new ReportDefinitionDto { Name = "Peek", SurveyId = _foreign.Definition.Id }));
    }

    [Fact]
    public async Task The_dashboard_counts_only_the_own_workspace()
    {
        var summary = await new DashboardService(_h.Db, _h.User, _h.Db.Time).GetSummaryAsync();

        Assert.Equal(1, summary.TotalSurveys);
        Assert.Equal(1, summary.CompletedResponses);
    }

    [Fact]
    public async Task Audit_logs_are_separate_per_workspace_and_system_events_are_for_super_admins()
    {
        var audit = new AuditService(_h.Db, _h.User, _h.Db.Time, NullLogger<AuditService>.Instance);
        await audit.LogAsync("test.mine", "Test", null);
        await audit.LogInWorkspaceAsync(Theirs, "test.theirs", "Test", null);
        _h.User.ActAsSuperAdmin();
        await audit.LogAsync("test.system", "Test", null);

        await Assert.ThrowsAsync<ForbiddenException>(() => audit.ListAsync(new AuditQuery()));
        Assert.Equal(["test.system"], (await audit.ListSystemAsync(new AuditQuery())).Items.Select(e => e.Action));

        _h.User.ActAsAdmin();
        Assert.Equal(["test.mine"], (await audit.ListAsync(new AuditQuery())).Items.Select(e => e.Action));
        await Assert.ThrowsAsync<ForbiddenException>(() => audit.ListSystemAsync(new AuditQuery()));
    }

    [Fact]
    public async Task Members_of_other_workspaces_answer_public_surveys_as_guests()
    {
        _h.User.ActAsRespondent(); // member of the default workspace

        var session = await Responses.StartOrResumeAsync(_foreign.Definition.Slug!);
        Assert.Equal(SurveyEligibility.Eligible, session.Eligibility);
        Assert.Null(session.DraftResponseId);

        var result = await Responses.SubmitAsync(_foreign.Definition.Id, ValidRequest(_foreign));
        var stored = await _h.Responses.FindResponseAsync(result.ResponseId);
        Assert.Null(stored!.RespondentId); // no link to an account of another workspace
        Assert.Equal(Theirs, stored.WorkspaceId);
        Assert.Contains(Theirs, _h.Audit.Workspaces); // recorded in the survey's workspace

        await Assert.ThrowsAsync<ForbiddenException>(() => Responses.SaveDraftAsync(_foreign.Definition.Id, ValidRequest(_foreign)));
        Assert.DoesNotContain(await Responses.ListMineAsync(), m => m.SurveyId == _foreign.Definition.Id);
    }

    [Fact]
    public async Task Members_only_surveys_refuse_members_of_other_workspaces()
    {
        var membersOnly = await _h.Responses.SeedSurveyAsync(s => s.AllowAnonymous = false, title: "Members only", workspaceId: Theirs);
        _h.User.ActAsRespondent();

        var session = await Responses.StartOrResumeAsync(membersOnly.Definition.Slug!);
        Assert.Equal(SurveyEligibility.OtherWorkspace, session.Eligibility);
        Assert.Null(session.Survey);
        await Assert.ThrowsAsync<ForbiddenException>(() => Responses.SubmitAsync(membersOnly.Definition.Id, ValidRequest(membersOnly)));

        _h.User.ActAsAnonymous();
        var guest = await Responses.StartOrResumeAsync(membersOnly.Definition.Slug!);
        Assert.Equal(SurveyEligibility.LoginRequired, guest.Eligibility);
        Assert.Equal(("Other workspace", "other"), (guest.WorkspaceName, guest.WorkspaceSlug)); // for the join link
    }

    [Fact]
    public async Task A_workspace_page_lists_only_its_public_surveys_to_outsiders()
    {
        await _h.Responses.SeedSurveyAsync(s => s.AllowAnonymous = false, title: "Members only", workspaceId: Theirs);
        _h.User.ActAsRespondent();

        Assert.Equal([_mine.Definition.Id], (await Responses.ListAvailableAsync()).Select(s => s.SurveyId)); // own workspace
        var theirs = await Responses.ListAvailableAsync("other");
        Assert.Equal([_foreign.Definition.Id], theirs.Select(s => s.SurveyId));
        Assert.All(theirs, s => Assert.False(s.HasDraft || s.HasCompleted));

        await using (var db = _h.Db.CreateContext())
        {
            await db.Workspaces.Where(w => w.Id == Theirs).ExecuteUpdateAsync(w => w.SetProperty(x => x.ShowPublicSurveyList, false));
        }

        Assert.Empty(await Responses.ListAvailableAsync("other"));
        Assert.Empty(await Responses.ListAvailableAsync("no-such-workspace"));
    }

    [Fact]
    public async Task Surveys_of_disabled_workspaces_are_unavailable()
    {
        await using (var db = _h.Db.CreateContext())
        {
            await db.Workspaces.Where(w => w.Id == Theirs).ExecuteUpdateAsync(w => w.SetProperty(x => x.Status, WorkspaceStatus.Disabled));
        }

        _h.User.ActAsAnonymous();
        var unavailable = await Responses.StartOrResumeAsync(_foreign.Definition.Slug!);
        Assert.Equal(SurveyEligibility.Unavailable, unavailable.Eligibility);
        Assert.Null(unavailable.WorkspaceSlug); // a disabled workspace is not advertised
        await Assert.ThrowsAsync<BusinessRuleException>(() => Responses.SubmitAsync(_foreign.Definition.Id, ValidRequest(_foreign)));
        Assert.Null(await Responses.GetCompletionAsync(_foreign.Definition.Slug!));
        Assert.Empty(await Responses.ListAvailableAsync("other"));
        await Assert.ThrowsAsync<NotFoundException>(() => Responses.UnlockAsync(_foreign.Definition.Slug!, "whatever"));

        // Surveys of active workspaces are unaffected.
        Assert.Equal(SurveyEligibility.Eligible, (await Responses.StartOrResumeAsync(_mine.Definition.Slug!)).Eligibility);
    }
}
