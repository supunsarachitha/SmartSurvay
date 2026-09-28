using AngleSharp.Dom;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Dashboard;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Application.Users;
using SmartSurvey.Domain.Enums;
using SmartSurvey.Domain.Identity;
using SmartSurvey.UnitTests.TestSupport;
using SmartSurvey.Web.Components.Pages.Admin;
using SmartSurvey.Web.Components.Pages.Admin.Responses;
using SmartSurvey.Web.Components.Pages.Admin.Surveys;
using SmartSurvey.Web.Components.Pages.Admin.Users;
using SmartSurvey.Web.Components.Shared;
using SmartSurvey.Web.Infrastructure;
using BrandingPage = SmartSurvey.Web.Components.Pages.SystemConsole.Branding;

namespace SmartSurvey.UnitTests.Ui;

/// <summary>bUnit tests of the administration pages: dashboard, responses, response detail, users, audit log, branding.</summary>
public sealed class AdminPagesTests : UiTestBase
{
    private readonly SampleSurvey _s = SampleSurveys.CustomerFeedback();
    private readonly FakeSurveyService _surveys = new();
    private readonly FakeDashboardService _dashboard = new();
    private readonly FakeUserAdminService _users = new();
    private readonly FakeAuditService _audit = new();
    private readonly RecordingBrandingService _branding = new();
    private readonly FakeResponseExportService _exports = new();

    public AdminPagesTests()
    {
        _s.Definition.Id = Guid.NewGuid();
        _s.Definition.Status = SurveyStatus.Published;
        _surveys.Surveys.Add(_s.Definition);
        Services.AddSingleton<ISurveyService>(_surveys);
        Services.AddSingleton<IDashboardService>(_dashboard);
        Services.AddSingleton<IUserAdminService>(_users);
        Services.AddSingleton<IAuditService>(_audit);
        Services.AddSingleton<IBrandingService>(_branding);
        Services.AddSingleton<IResponseExportService>(_exports);
        Services.AddScoped<ToastService>();
        SignInAsAdmin();
    }

    private BunitNavigationManager Navigation => Services.GetRequiredService<BunitNavigationManager>();

    private static void Click<T>(IRenderedComponent<T> cut, string text, string selector = "button") where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll(selector).First(b => b.TextContent.Contains(text, StringComparison.Ordinal)).Click();

    // The confirm dialog's primary button (rendered in the modal footer).
    private static void Confirm<T>(IRenderedComponent<T> cut, string text) where T : Microsoft.AspNetCore.Components.IComponent =>
        cut.FindAll(".ss-modal button").Last(b => b.TextContent.Contains(text, StringComparison.Ordinal)).Click();

    [Fact]
    public void Dashboard_shows_kpis_trend_top_surveys_and_latest_responses()
    {
        var responseId = Guid.NewGuid();
        _dashboard.Summary = new DashboardSummaryDto
        {
            TotalSurveys = 4, PublishedSurveys = 2, DraftSurveys = 1, ClosedSurveys = 1,
            CompletedResponses = 1234, ResponsesLast7Days = 56, CompletionRate = .875, InProgressResponses = 3, AverageDurationSeconds = 250,
            TotalUsers = 9, TotalReports = 2,
            ResponsesPerDay = Enumerable.Range(0, 30).Select(i => new DailyCountDto(new DateOnly(2026, 9, 1).AddDays(i), i % 4)).ToList(),
            TopSurveys = [new SurveySummaryDto { Id = _s.Definition.Id, Title = "Customer feedback", CompletedResponses = 800 }],
            RecentResponses = [new RecentResponseDto { ResponseId = responseId, SurveyId = _s.Definition.Id, SurveyTitle = "Customer feedback", RespondentName = "Rita", SubmittedAt = DateTime.UtcNow }],
        };

        var cut = Render<Dashboard>();

        Assert.Equal(["4", "1,234", "87.5%", "9"], cut.FindAll(".stat-value").Select(v => v.TextContent.Trim()));
        Assert.Contains("2 live · 1 draft · 1 closed", cut.Markup);
        Assert.Contains("avg. 4m 10s", cut.Markup);
        Assert.NotNull(cut.Find(".chart-container svg"));
        Assert.NotNull(cut.Find($"a[href='admin/surveys/{_s.Definition.Id}/responses']"));
        Assert.NotNull(cut.Find($"a[href='admin/responses/{responseId}']"));
    }

    [Fact]
    public void Dashboard_welcomes_new_installations()
    {
        var cut = Render<Dashboard>();

        Assert.Contains("Let's create your first survey", cut.Markup);
        Assert.Empty(cut.FindAll(".stat-card"));
    }

    private ResponseSummaryDto AddResponse(string name, ResponseStatus status = ResponseStatus.Completed)
    {
        var row = new ResponseSummaryDto
        {
            Id = Guid.NewGuid(), SurveyId = _s.Definition.Id, RespondentName = name, RespondentEmail = $"{name.ToLowerInvariant()}@test.local",
            Status = status, StartedAt = DateTime.UtcNow.AddMinutes(-10), SubmittedAt = status == ResponseStatus.Completed ? DateTime.UtcNow : null,
            AnswerCount = 5, DurationSeconds = 95,
        };
        Responses.SurveyResponses.Add(row);
        return row;
    }

    [Fact]
    public void Responses_page_lists_filters_exports_and_deletes()
    {
        var rita = AddResponse("Rita");
        AddResponse("Sam", ResponseStatus.InProgress);

        var cut = Render<SurveyResponses>(p => p.Add(x => x.Id, _s.Definition.Id));

        Assert.Equal(2, cut.FindAll("tbody tr").Count);
        Assert.Contains("1m 35s", cut.Markup);
        Assert.Contains("In progress", cut.Markup);

        cut.Find("select[aria-label='Filter by status']").Change(ResponseStatus.Completed.ToString());
        Assert.Equal(ResponseStatus.Completed, Responses.ResponseQueries[^1].Status);

        cut.Find("#export-drafts").Change(true);
        Click(cut, "Excel workbook", ".dropdown-item");
        Assert.Equal((_s.Definition.Id, ExportFormat.Xlsx, true), Assert.Single(_exports.Calls));

        cut.Find($"button[aria-label='Delete response of Rita']").Click();
        Confirm(cut, "Delete response");
        Assert.Equal([rita.Id], Responses.Deleted);
        Assert.Single(cut.FindAll("tbody tr"));
    }

    [Fact]
    public void Share_page_links_use_the_public_address_when_configured()
    {
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new EmbeddingOptions()));
        PublicSite.PublicBaseUrl = "https://surveys.example.com";

        var cut = Render<SurveyShare>(p => p.Add(x => x.Id, _s.Definition.Id));

        Assert.Equal($"https://surveys.example.com/s/{_s.Definition.Slug}", cut.Find(".code-box").TextContent.Trim());
        Assert.Contains($"src=\"https://surveys.example.com/embed/s/{_s.Definition.Slug}\"", cut.Markup.Replace("&quot;", "\""));
    }

    [Fact]
    public void Share_page_links_use_the_request_address_by_default()
    {
        Services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new EmbeddingOptions()));

        var cut = Render<SurveyShare>(p => p.Add(x => x.Id, _s.Definition.Id));

        Assert.Equal($"http://localhost/s/{_s.Definition.Slug}", cut.Find(".code-box").TextContent.Trim());
    }

    [Fact]
    public void Responses_page_without_responses_invites_to_share()
    {
        var cut = Render<SurveyResponses>(p => p.Add(x => x.Id, _s.Definition.Id));

        Assert.Contains("No responses yet", cut.Markup);
        Assert.NotNull(cut.Find($"a[href='admin/surveys/{_s.Definition.Id}/share']"));
    }

    [Fact]
    public void Response_detail_groups_answers_by_page_and_deletes()
    {
        var id = Guid.NewGuid();
        Responses.Detail = new ResponseDetailDto
        {
            Id = id, SurveyId = _s.Definition.Id, SurveyTitle = "Customer feedback", RespondentName = "Rita Respondent", RespondentEmail = "rita@test.local",
            Status = ResponseStatus.Completed, StartedAt = DateTime.UtcNow.AddMinutes(-3), SubmittedAt = DateTime.UtcNow, UserAgent = "Mozilla/5.0",
            Answers =
            [
                new AnswerDetailDto { QuestionId = Guid.NewGuid(), QuestionCode = "Q1", QuestionText = "Did you enjoy it?", QuestionType = QuestionType.Radio, SectionTitle = "Experience", DisplayValue = "Yes", IsAnswered = true },
                new AnswerDetailDto { QuestionId = Guid.NewGuid(), QuestionCode = "Q2", QuestionText = "Why not?", QuestionType = QuestionType.LongText, SectionTitle = "Experience" },
                new AnswerDetailDto { QuestionId = Guid.NewGuid(), QuestionCode = "Q3", QuestionText = "Rate us", QuestionType = QuestionType.Rating, SectionTitle = "About you", DisplayValue = "4", IsAnswered = true },
            ],
        };

        var cut = Render<ResponseDetail>(p => p.Add(x => x.Id, id));

        Assert.Contains("Response from Rita Respondent", cut.Find("h1").TextContent);
        Assert.Equal(["Experience", "About you"], cut.FindAll("h2.text-uppercase").Select(h => h.TextContent.Trim()));
        Assert.Contains("Not answered", cut.Markup);
        Assert.Contains("2 of 3 questions", cut.Markup);
        Assert.Contains("3m 00s", cut.Markup);

        Click(cut, "Delete");
        Confirm(cut, "Delete response");

        Assert.Equal([id], Responses.Deleted);
        Assert.EndsWith($"/admin/surveys/{_s.Definition.Id}/responses", Navigation.Uri);
    }

    [Fact]
    public void Missing_response_shows_a_friendly_message()
    {
        var cut = Render<ResponseDetail>(p => p.Add(x => x.Id, Guid.NewGuid()));

        Assert.Contains("This response can't be opened", cut.Markup);
    }

    private UserDto AddUser(string email, bool admin = false, Guid? id = null, bool locked = false)
    {
        var user = new UserDto
        {
            Id = id ?? Guid.NewGuid(), Email = email, Roles = admin ? [AppRoles.Admin, AppRoles.User] : [AppRoles.User],
            IsLockedOut = locked, EmailConfirmed = true, CreatedAt = DateTime.UtcNow.AddDays(-3),
        };
        _users.Users.Add(user);
        return user;
    }

    [Fact]
    public void Users_page_marks_the_current_admin_and_protects_their_account()
    {
        AddUser("admin@test.local", admin: true, id: TestCurrentUser.AdminId);
        var other = AddUser("rita@test.local");

        var cut = Render<UserList>();

        var rows = cut.FindAll("tbody tr");
        Assert.Contains("You", rows[0].TextContent);
        Assert.True(rows[0].QuerySelectorAll(".dropdown-item").Single(i => i.TextContent.Contains("Delete account")).HasAttribute("disabled"));
        Assert.False(rows[1].QuerySelectorAll(".dropdown-item").Single(i => i.TextContent.Contains("Delete account")).HasAttribute("disabled"));

        rows[1].QuerySelectorAll(".dropdown-item").Single(i => i.TextContent.Contains("Make administrator")).Click();
        Confirm(cut, "Make administrator");
        Assert.Equal([$"roles:{other.Id}:Admin,User"], _users.Calls);

        cut.FindAll("tbody tr")[1].QuerySelectorAll(".dropdown-item").Single(i => i.TextContent.Contains("Lock account")).Click();
        Confirm(cut, "Lock account");
        Assert.Equal($"lock:{other.Id}", _users.Calls[^1]);
    }

    [Fact]
    public void Users_page_creates_users_and_shows_validation_errors()
    {
        AddUser("admin@test.local", admin: true, id: TestCurrentUser.AdminId);
        var cut = Render<UserList>();

        Click(cut, "New user");
        cut.Find("#nu-email").Input("new@test.local");
        cut.Find("#nu-password").Input("short");
        _users.CreateError = new AppValidationException("Password", "Passwords must be at least 8 characters.");
        Click(cut, "Create user");

        Assert.Contains("Passwords must be at least 8 characters.", cut.Markup);
        Assert.Contains("is-invalid", cut.Find("#nu-password").ClassName);

        _users.CreateError = null;
        cut.Find("#nu-password").Input("Str0ng!Pass");
        cut.Find("#nu-admin").Change(true);
        Click(cut, "Create user");

        Assert.Equal("new@test.local", _users.Created?.Email);
        Assert.Equal([AppRoles.Admin, AppRoles.User], _users.Created?.Roles);
        Assert.Equal(2, cut.FindAll("tbody tr").Count);
    }

    [Fact]
    public void Users_page_sets_a_new_password()
    {
        AddUser("admin@test.local", admin: true, id: TestCurrentUser.AdminId);
        var rita = AddUser("rita@test.local");
        var cut = Render<UserList>();

        cut.FindAll("tbody tr")[1].QuerySelectorAll(".dropdown-item").Single(i => i.TextContent.Contains("Set new password")).Click();
        _users.PasswordError = new AppValidationException("Password", "Passwords must have at least one digit ('0'-'9').");
        cut.Find("#pw-new").Input("weakpass");
        Click(cut, "Set password");
        Assert.Contains("at least one digit", cut.Markup);

        _users.PasswordError = null;
        cut.Find("#pw-new").Input("N3w-Secret!");
        Click(cut, "Set password");

        Assert.Equal($"password:{rita.Id}:N3w-Secret!", Assert.Single(_users.Calls));
        Assert.Empty(cut.FindAll("#pw-new")); // dialog closed
    }

    [Fact]
    public void Audit_log_lists_entries_with_links_and_filters()
    {
        var surveyId = Guid.NewGuid();
        _audit.Entries.Add(new AuditLogDto
        {
            Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, UserName = "admin@test.local", Action = AuditActions.SurveyStatusChanged,
            EntityType = "Survey", EntityId = surveyId.ToString(), Details = "Draft → Published",
        });
        _audit.Entries.Add(new AuditLogDto { Id = Guid.NewGuid(), Timestamp = DateTime.UtcNow, Action = AuditActions.ResponseSubmitted, EntityType = "Response", EntityId = "not-a-guid" });

        var cut = Render<AuditLog>();

        Assert.Contains("Survey status changed", cut.Markup);
        Assert.NotNull(cut.Find($"a[href='admin/surveys/{surveyId}/edit']"));
        Assert.Contains("System / guest", cut.Markup);

        cut.Find("select[aria-label='Filter by type']").Change("Report");

        Assert.Equal("Report", _audit.Queries[^1].EntityType);
    }

    [Theory]
    [InlineData("survey.status_changed", "Survey status changed")]
    [InlineData("branding.updated", "Branding updated")]
    [InlineData("", "")]
    public void Audit_actions_get_readable_labels(string action, string expected) => Assert.Equal(expected, SmartSurvey.Web.Components.Admin.Audit.AuditLogView.ActionLabel(action));

    [Fact]
    public void Branding_page_saves_name_and_icon_and_refreshes_the_layout()
    {
        var cut = Render<BrandingPage>();
        Assert.True(cut.FindAll("button").First(b => b.TextContent.Contains("Saved")).HasAttribute("disabled"));

        cut.Find("#b-name").Input("Pulse");
        cut.Find("button[aria-label='bi-rocket-takeoff']").Click();

        Assert.Contains("Pulse", cut.Find(".brand-preview-bar").TextContent);
        Assert.NotNull(cut.Find(".brand-preview-bar i.bi-rocket-takeoff"));

        Click(cut, "Save changes");

        var update = Assert.Single(_branding.Updates);
        Assert.Equal(("Pulse", "bi-rocket-takeoff"), (update.ProductName, update.IconName));
        Assert.Contains(Navigation.History, h => h.Options.ReplaceHistoryEntry); // Refresh() re-renders the layout
    }

    [Fact]
    public void Branding_validation_errors_are_shown_on_the_fields()
    {
        _branding.UpdateError = new AppValidationException("IconName", "Please choose a valid Bootstrap icon (e.g. bi-ui-checks).");
        var cut = Render<BrandingPage>();

        cut.Find("#b-icon").Input("not an icon");
        Click(cut, "Save changes");

        Assert.Contains("valid Bootstrap icon", cut.Markup);
        Assert.Contains("is-invalid", cut.Find("#b-icon").ClassName);
        Assert.NotNull(cut.Find(".brand-preview-bar i.bi-ui-checks")); // preview falls back to the default icon
    }

    [Fact]
    public void Branding_reset_restores_the_defaults_after_confirmation()
    {
        _branding.Current = new BrandingDto { ProductName = "Pulse", IconName = "bi-star" };
        var cut = Render<BrandingPage>();

        Click(cut, "Reset to defaults");
        Confirm(cut, "Reset");

        Assert.Equal(BrandingDefaults.ProductName, cut.Find("#b-name").GetAttribute("value"));
    }
}
