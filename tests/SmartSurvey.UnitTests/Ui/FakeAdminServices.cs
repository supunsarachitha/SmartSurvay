using SmartSurvey.Application.Common;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.UnitTests.Ui;

/// <summary><see cref="ISurveyService"/> serving configurable surveys (read operations only).</summary>
public sealed class FakeSurveyService : ISurveyService
{
    public List<SurveyDefinitionDto> Surveys { get; } = [];

    public Task<PagedResult<SurveySummaryDto>> ListAsync(SurveyQuery query, CancellationToken ct = default)
    {
        var items = Surveys.Select(s => new SurveySummaryDto
        {
            Id = s.Id, Title = s.Title, Slug = s.Slug ?? string.Empty, Status = s.Status, QuestionCount = s.AllQuestions().Count(),
        }).ToList();
        return Task.FromResult(new PagedResult<SurveySummaryDto>(items, items.Count, query.Page, query.PageSize));
    }

    public Task<SurveyDefinitionDto> GetAsync(Guid id, CancellationToken ct = default) =>
        Surveys.FirstOrDefault(s => s.Id == id) is { } survey
            ? Task.FromResult(survey)
            : Task.FromException<SurveyDefinitionDto>(new NotFoundException("Survey", id));

    public Task<IReadOnlyList<SurveySummaryDto>> ListTemplatesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto?> FindBySlugAsync(string slug, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto> CreateAsync(SurveyDefinitionDto dto, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto> UpdateAsync(Guid id, SurveyDefinitionDto dto, CancellationToken ct = default) => throw new NotSupportedException();
    public Task DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto> ChangeStatusAsync(Guid id, SurveyStatus status, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto> DuplicateAsync(Guid id, DuplicateSurveyRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyExportDocument> ExportDefinitionAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SurveyDefinitionDto> ImportDefinitionAsync(SurveyExportDocument document, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> IsSlugAvailableAsync(string slug, Guid? excludeSurveyId = null, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> CountAnswersAsync(Guid questionId, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>
/// <see cref="IReportService"/> recording what the UI asks for. Previews echo one empty widget result per
/// widget; <see cref="SaveError"/> makes create/update fail.
/// </summary>
public sealed class FakeReportService : IReportService
{
    public List<ReportDefinitionDto> Saved { get; } = [];

    public List<ReportDefinitionDto> Previews { get; } = [];

    public List<(Guid Id, ExportFormat Format)> Exports { get; } = [];

    public ReportDefinitionDto? Existing { get; set; }

    public ReportResult RunResult { get; set; } = new() { ReportName = "Saved report", SurveyTitle = "Customer feedback", TotalResponses = 3 };

    public ReportDefinitionDto? DefaultReport { get; set; }

    public Exception? SaveError { get; set; }

    public Task<PagedResult<ReportSummaryDto>> ListAsync(ReportQuery query, CancellationToken ct = default) =>
        Task.FromResult(PagedResult<ReportSummaryDto>.Empty(query.Page, query.PageSize));

    public Task<ReportDefinitionDto> GetAsync(Guid id, CancellationToken ct = default) =>
        Existing is { } report && report.Id == id ? Task.FromResult(report) : Task.FromException<ReportDefinitionDto>(new NotFoundException("Report", id));

    public Task<ReportDefinitionDto> CreateAsync(ReportDefinitionDto dto, CancellationToken ct = default) => SaveAsync(dto, Guid.NewGuid());

    public Task<ReportDefinitionDto> UpdateAsync(Guid id, ReportDefinitionDto dto, CancellationToken ct = default) => SaveAsync(dto, id);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;

    public Task<ReportDefinitionDto> DuplicateAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<ReportDefinitionDto> BuildDefaultAsync(Guid surveyId, CancellationToken ct = default) =>
        Task.FromResult(DefaultReport ?? throw new NotSupportedException());

    public Task<ReportResult> RunAsync(Guid id, CancellationToken ct = default) => Task.FromResult(RunResult);

    public Task<ReportResult> PreviewAsync(ReportDefinitionDto dto, CancellationToken ct = default)
    {
        Previews.Add(dto);
        return Task.FromResult(new ReportResult
        {
            ReportName = dto.Name,
            TotalResponses = 7,
            Widgets = dto.Widgets.Select(w => new WidgetResult { WidgetId = w.Id, Title = $"Preview of {w.Type}", Type = w.Type }).ToList(),
        });
    }

    public Task<ExportFile> ExportAsync(Guid id, ExportFormat format, CancellationToken ct = default)
    {
        Exports.Add((id, format));
        return Task.FromResult(new ExportFile([1, 2, 3], format.ContentType(), $"report.{format.FileExtension()}"));
    }

    private Task<ReportDefinitionDto> SaveAsync(ReportDefinitionDto dto, Guid id)
    {
        if (SaveError is { } error)
        {
            return Task.FromException<ReportDefinitionDto>(error);
        }

        dto.Id = id;
        Saved.Add(dto);
        return Task.FromResult(dto);
    }
}

/// <summary><see cref="Application.Dashboard.IDashboardService"/> returning a configurable summary.</summary>
public sealed class FakeDashboardService : Application.Dashboard.IDashboardService
{
    public Application.Dashboard.DashboardSummaryDto Summary { get; set; } = new();

    public Task<Application.Dashboard.DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default) => Task.FromResult(Summary);
}

/// <summary><see cref="Application.Users.IUserAdminService"/> over an in-memory list, recording every change.</summary>
public sealed class FakeUserAdminService : Application.Users.IUserAdminService
{
    public List<Application.Users.UserDto> Users { get; } = [];

    public List<string> Calls { get; } = [];

    public Application.Users.CreateUserRequest? Created { get; private set; }

    public Exception? CreateError { get; set; }

    public Application.Users.UserQuery? LastQuery { get; private set; }

    public Task<PagedResult<Application.Users.UserDto>> ListAsync(Application.Users.UserQuery query, CancellationToken ct = default)
    {
        LastQuery = query;
        return Task.FromResult(new PagedResult<Application.Users.UserDto>(Users, Users.Count, query.Page, query.PageSize));
    }

    public Task<Application.Users.UserDto> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Users.Single(u => u.Id == id));

    public Task<Application.Users.UserDto> CreateAsync(Application.Users.CreateUserRequest request, CancellationToken ct = default)
    {
        if (CreateError is { } error)
        {
            return Task.FromException<Application.Users.UserDto>(error);
        }

        Created = request;
        var user = new Application.Users.UserDto { Id = Guid.NewGuid(), Email = request.Email, DisplayName = request.DisplayName, Roles = request.Roles };
        Users.Add(user);
        return Task.FromResult(user);
    }

    public Task<Application.Users.UserDto> SetRolesAsync(Guid id, IReadOnlyCollection<string> roles, CancellationToken ct = default) =>
        Record($"roles:{id}:{string.Join(",", roles)}", id);

    public Task<Application.Users.UserDto> LockAsync(Guid id, CancellationToken ct = default) => Record($"lock:{id}", id);

    public Task<Application.Users.UserDto> UnlockAsync(Guid id, CancellationToken ct = default) => Record($"unlock:{id}", id);

    public Task DeleteAsync(Guid id, CancellationToken ct = default) => Record($"delete:{id}", id);

    private Task<Application.Users.UserDto> Record(string call, Guid id)
    {
        Calls.Add(call);
        return Task.FromResult(Users.Single(u => u.Id == id));
    }
}

/// <summary><see cref="Application.Audit.IAuditService"/> returning configurable entries and recording queries.</summary>
public sealed class FakeAuditService : Application.Audit.IAuditService
{
    public List<Application.Audit.AuditLogDto> Entries { get; } = [];

    public List<Application.Audit.AuditQuery> Queries { get; } = [];

    public Task LogAsync(string action, string entityType, string? entityId, string? details = null, CancellationToken ct = default) => Task.CompletedTask;

    public Task<PagedResult<Application.Audit.AuditLogDto>> ListAsync(Application.Audit.AuditQuery query, CancellationToken ct = default)
    {
        Queries.Add(query);
        return Task.FromResult(new PagedResult<Application.Audit.AuditLogDto>(Entries, Entries.Count, query.Page, query.PageSize));
    }
}

/// <summary><see cref="Application.Branding.IBrandingService"/> keeping branding in memory and recording updates.</summary>
public sealed class RecordingBrandingService : Application.Branding.IBrandingService
{
    public Application.Branding.BrandingDto Current { get; set; } = new() { ProductName = "Acme Surveys" };

    public List<Application.Branding.UpdateBrandingRequest> Updates { get; } = [];

    public List<(int Bytes, string? FileName)> Logos { get; } = [];

    public Exception? UpdateError { get; set; }

    public Task<Application.Branding.BrandingDto> GetAsync(CancellationToken ct = default) => Task.FromResult(Current);

    public Task<Application.Branding.BrandingDto> UpdateAsync(Application.Branding.UpdateBrandingRequest request, CancellationToken ct = default)
    {
        if (UpdateError is { } error)
        {
            return Task.FromException<Application.Branding.BrandingDto>(error);
        }

        Updates.Add(request);
        Current = Current with { ProductName = request.ProductName, Tagline = request.Tagline, IconName = request.IconName, Version = Current.Version + 1 };
        return Task.FromResult(Current);
    }

    public Task<Application.Branding.BrandingDto> SetLogoAsync(byte[] content, string? fileName, CancellationToken ct = default)
    {
        Logos.Add((content.Length, fileName));
        Current = Current with { HasLogo = true, LogoContentType = "image/png", Version = Current.Version + 1 };
        return Task.FromResult(Current);
    }

    public Task<Application.Branding.BrandingDto> RemoveLogoAsync(CancellationToken ct = default)
    {
        Current = Current with { HasLogo = false, LogoContentType = null };
        return Task.FromResult(Current);
    }

    public Task<Application.Branding.BrandingDto> ResetAsync(CancellationToken ct = default)
    {
        Current = new Application.Branding.BrandingDto();
        return Task.FromResult(Current);
    }

    public Task<Application.Branding.BrandingLogo?> GetLogoAsync(CancellationToken ct = default) => Task.FromResult<Application.Branding.BrandingLogo?>(null);
}

/// <summary><see cref="IResponseExportService"/> recording raw exports.</summary>
public sealed class FakeResponseExportService : IResponseExportService
{
    public List<(Guid SurveyId, ExportFormat Format, bool IncludeInProgress)> Calls { get; } = [];

    public IReadOnlyList<ExportFormat> SupportedFormats { get; } = [ExportFormat.Csv, ExportFormat.Xlsx, ExportFormat.Json];

    public Task<ExportFile> ExportResponsesAsync(Guid surveyId, ExportFormat format, bool includeInProgress = false, CancellationToken ct = default)
    {
        Calls.Add((surveyId, format, includeInProgress));
        return Task.FromResult(new ExportFile([1], format.ContentType(), $"responses.{format.FileExtension()}"));
    }
}
