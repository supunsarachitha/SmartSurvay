using System.Linq.Expressions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSurvey.Application.Audit;
using SmartSurvey.Application.Branding;
using SmartSurvey.Application.Common;
using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports.Engine;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Entities;

namespace SmartSurvey.Application.Reports;

/// <summary>
/// Report use cases for administrators: saved report definitions (CRUD, duplicate, default report),
/// execution through <see cref="IReportEngine"/> and export through the registered
/// <see cref="IReportExporter"/>s.
/// </summary>
/// <remarks>
/// Every operation is admin-only (defense in depth behind the UI/API policies). Incoming definitions
/// are never modified: the service normalises a private copy (<see cref="ReportDefinitionMapper"/>),
/// validates it (<see cref="ReportDefinitionValidator"/> + <see cref="ReportSurveyChecks"/>) and
/// persists that copy. Results carry the configured product name so exports are branded.
/// </remarks>
public sealed class ReportService(
    IAppDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider time,
    IAuditService audit,
    IReportEngine engine,
    IBrandingService branding,
    IEnumerable<IReportExporter> exporters,
    IValidator<ReportDefinitionDto> validator,
    ILogger<ReportService> logger) : IReportService
{
    private const string EntityName = "Report";

    /// <summary>List row projection; translated to SQL.</summary>
    private static readonly Expression<Func<ReportDefinition, ReportSummaryDto>> ToSummary = r => new ReportSummaryDto
    {
        Id = r.Id,
        Name = r.Name,
        Description = r.Description,
        SurveyId = r.SurveyId,
        SurveyTitle = r.Survey!.Title,
        WidgetCount = r.Widgets.Count,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    private readonly IReadOnlyDictionary<ExportFormat, IReportExporter> _exporters =
        exporters.GroupBy(e => e.Format).ToDictionary(g => g.Key, g => g.Last());

    /// <inheritdoc />
    public async Task<PagedResult<ReportSummaryDto>> ListAsync(ReportQuery query, CancellationToken ct = default)
    {
        EnsureAdmin();
        ArgumentNullException.ThrowIfNull(query);

        await using var db = await dbFactory.CreateAsync(ct);
        var reports = db.Reports.AsNoTracking();
        if (query.SurveyId is { } surveyId)
        {
            reports = reports.Where(r => r.SurveyId == surveyId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ToLower on both sides keeps the search case-insensitive on PostgreSQL and SQLite alike.
            var term = query.Search.Trim().ToLowerInvariant();
            reports = reports.Where(r =>
                r.Name.ToLower().Contains(term)
                || (r.Description != null && r.Description.ToLower().Contains(term)));
        }

        var total = await reports.CountAsync(ct);
        var items = await reports
            .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
            .ThenBy(r => r.Name)
            .ThenBy(r => r.Id)
            .Skip(query.Skip)
            .Take(query.PageSize)
            .Select(ToSummary)
            .ToListAsync(ct);

        return new PagedResult<ReportSummaryDto>(items, total, query.Page, query.PageSize);
    }

    /// <inheritdoc />
    public async Task<ReportDefinitionDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();

        await using var db = await dbFactory.CreateAsync(ct);
        var report = await db.Reports.AsNoTracking()
            .Include(r => r.Widgets)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(EntityName, id);

        return ReportDefinitionMapper.ToDto(report);
    }

    /// <inheritdoc />
    public async Task<ReportDefinitionDto> CreateAsync(ReportDefinitionDto dto, CancellationToken ct = default)
    {
        EnsureAdmin();
        ArgumentNullException.ThrowIfNull(dto);

        await using var db = await dbFactory.CreateAsync(ct);
        var definition = ReportDefinitionMapper.Normalize(dto);
        await ValidateAsync(db, definition, ct);

        var report = new ReportDefinition
        {
            Name = definition.Name,
            Description = definition.Description,
            SurveyId = definition.SurveyId,
            Filters = definition.Filters.Clone(),
        };
        report.Widgets.AddRange(definition.Widgets.Select(w => ReportDefinitionMapper.ToNewWidget(w, report.Id)));

        db.Reports.Add(report);
        await db.SaveChangesAsync(ct);
        await LogAsync(AuditActions.ReportCreated, report.Id, $"Created report '{report.Name}' with {report.Widgets.Count} widget(s).", ct);

        return ReportDefinitionMapper.ToDto(report);
    }

    /// <inheritdoc />
    public async Task<ReportDefinitionDto> UpdateAsync(Guid id, ReportDefinitionDto dto, CancellationToken ct = default)
    {
        EnsureAdmin();
        ArgumentNullException.ThrowIfNull(dto);

        await using var db = await dbFactory.CreateAsync(ct);
        var report = await db.Reports
            .Include(r => r.Widgets)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(EntityName, id);

        var definition = ReportDefinitionMapper.Normalize(dto);
        await ValidateAsync(db, definition, ct);

        report.Name = definition.Name;
        report.Description = definition.Description;
        report.SurveyId = definition.SurveyId;
        report.Filters = definition.Filters.Clone();
        ReconcileWidgets(db, report, definition.Widgets);

        await db.SaveChangesAsync(ct);
        await LogAsync(AuditActions.ReportUpdated, report.Id, $"Updated report '{report.Name}' ({report.Widgets.Count} widget(s)).", ct);

        return ReportDefinitionMapper.ToDto(report);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAdmin();

        await using var db = await dbFactory.CreateAsync(ct);
        var report = await db.Reports
            .Include(r => r.Widgets)
            .FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new NotFoundException(EntityName, id);

        db.Reports.Remove(report);
        await db.SaveChangesAsync(ct);
        await LogAsync(AuditActions.ReportDeleted, id, $"Deleted report '{report.Name}'.", ct);
    }

    /// <inheritdoc />
    public async Task<ReportDefinitionDto> DuplicateAsync(Guid id, CancellationToken ct = default)
    {
        var source = await GetAsync(id, ct);
        source.Id = Guid.Empty;
        source.Name = ReportFormat.Truncate($"Copy of {source.Name}", ReportLimits.NameMaxLength);
        return await CreateAsync(source, ct);
    }

    /// <inheritdoc />
    public async Task<ReportDefinitionDto> BuildDefaultAsync(Guid surveyId, CancellationToken ct = default)
    {
        EnsureAdmin();

        await using var db = await dbFactory.CreateAsync(ct);
        var survey = await db.Surveys.AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.Sections)
            .Include(s => s.Questions).ThenInclude(q => q.Options)
            .FirstOrDefaultAsync(s => s.Id == surveyId, ct)
            ?? throw new NotFoundException("Survey", surveyId);

        return DefaultReportBuilder.Build(survey.ToDefinitionDto());
    }

    /// <inheritdoc />
    public async Task<ReportResult> RunAsync(Guid id, CancellationToken ct = default)
    {
        var definition = await GetAsync(id, ct);
        return await ExecuteAsync(definition, ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Only the survey reference is required: half-configured widgets are expected while designing
    /// and are reported per widget by the engine instead of failing the whole preview.
    /// </remarks>
    public async Task<ReportResult> PreviewAsync(ReportDefinitionDto dto, CancellationToken ct = default)
    {
        EnsureAdmin();
        ArgumentNullException.ThrowIfNull(dto);

        var definition = ReportDefinitionMapper.Normalize(dto);
        if (definition.SurveyId == Guid.Empty)
        {
            throw new AppValidationException(nameof(ReportDefinitionDto.SurveyId), "Select the survey to report on.");
        }

        return await ExecuteAsync(definition, ct);
    }

    /// <inheritdoc />
    public async Task<ExportFile> ExportAsync(Guid id, ExportFormat format, CancellationToken ct = default)
    {
        EnsureAdmin();
        if (!_exporters.TryGetValue(format, out var exporter))
        {
            throw new BusinessRuleException($"Exporting reports as {format.ToString().ToUpperInvariant()} is not supported.");
        }

        var result = await RunAsync(id, ct);
        var content = await exporter.ExportAsync(result, ct);
        var fileName = ExportFormatExtensions.BuildFileName(result.ReportName, format, time.GetUtcNow().UtcDateTime);

        logger.LogInformation("Report {ReportId} exported as {Format} ({Bytes} bytes)", id, format, content.Length);
        await LogAsync(AuditActions.ReportExported, id, $"Exported report '{result.ReportName}' as {format.ToString().ToUpperInvariant()}.", ct);

        return new ExportFile(content, format.ContentType(), fileName);
    }

    /// <summary>Runs the engine and stamps the branded product name on the result.</summary>
    private async Task<ReportResult> ExecuteAsync(ReportDefinitionDto definition, CancellationToken ct)
    {
        EnsureAdmin();
        var result = await engine.ExecuteAsync(definition, ct);
        result.ProductName = (await branding.GetAsync(ct)).ProductName;
        return result;
    }

    /// <summary>Runs the structural validator and the survey-reference checks; throws on any error.</summary>
    private async Task ValidateAsync(IAppDbContext db, ReportDefinitionDto definition, CancellationToken ct)
    {
        var errors = ReportValidationErrors.From(await validator.ValidateAsync(definition, ct));
        await ReportSurveyChecks.CheckAsync(db, definition, errors, ct);
        errors.ThrowIfAny();
    }

    /// <summary>
    /// Updates widgets that still exist (matched by id), adds new ones (with fresh ids, so a foreign
    /// id can never collide with another report's widget) and removes widgets that were dropped.
    /// </summary>
    private static void ReconcileWidgets(IAppDbContext db, ReportDefinition report, List<ReportWidgetDto> widgets)
    {
        var existing = report.Widgets.ToDictionary(w => w.Id);
        var kept = new HashSet<Guid>();

        foreach (var dto in widgets)
        {
            if (existing.TryGetValue(dto.Id, out var widget))
            {
                ReportDefinitionMapper.Apply(dto, widget);
                kept.Add(widget.Id);
            }
            else
            {
                var added = ReportDefinitionMapper.ToNewWidget(dto, report.Id);
                report.Widgets.Add(added);
                db.ReportWidgets.Add(added);
                kept.Add(added.Id);
            }
        }

        foreach (var removed in report.Widgets.Where(w => !kept.Contains(w.Id)).ToList())
        {
            report.Widgets.Remove(removed);
            db.ReportWidgets.Remove(removed);
        }
    }

    private Task LogAsync(string action, Guid reportId, string details, CancellationToken ct) =>
        audit.LogAsync(action, EntityName, reportId.ToString(), details, ct);

    private void EnsureAdmin()
    {
        if (!currentUser.IsAdmin)
        {
            throw new ForbiddenException("Only administrators can manage reports.");
        }
    }
}
