using SmartSurvey.Application.Common;
using SmartSurvey.Application.Exports;

namespace SmartSurvey.Application.Reports;

/// <summary>Report definition management, execution and export (admin).</summary>
public interface IReportService
{
    /// <summary>Paged report list.</summary>
    Task<PagedResult<ReportSummaryDto>> ListAsync(ReportQuery query, CancellationToken ct = default);

    /// <summary>Report definition. Throws <see cref="NotFoundException"/>.</summary>
    Task<ReportDefinitionDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates a report (validates that referenced questions belong to the survey).</summary>
    Task<ReportDefinitionDto> CreateAsync(ReportDefinitionDto dto, CancellationToken ct = default);

    /// <summary>Replaces a report definition (widgets reconciled by id).</summary>
    Task<ReportDefinitionDto> UpdateAsync(Guid id, ReportDefinitionDto dto, CancellationToken ct = default);

    /// <summary>Deletes a report.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Copies a report ("Copy of …").</summary>
    Task<ReportDefinitionDto> DuplicateAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Builds (but does not save) a sensible default report for a survey: summary KPIs, responses
    /// over time and one widget per question (charts for choice/numeric questions, text lists for
    /// text questions).
    /// </summary>
    Task<ReportDefinitionDto> BuildDefaultAsync(Guid surveyId, CancellationToken ct = default);

    /// <summary>Executes a saved report.</summary>
    Task<ReportResult> RunAsync(Guid id, CancellationToken ct = default);

    /// <summary>Executes an unsaved definition (live preview in the builder).</summary>
    Task<ReportResult> PreviewAsync(ReportDefinitionDto dto, CancellationToken ct = default);

    /// <summary>Executes a saved report and exports it in the requested format.</summary>
    Task<ExportFile> ExportAsync(Guid id, ExportFormat format, CancellationToken ct = default);
}

/// <summary>
/// Computes a <see cref="ReportResult"/> from a definition: loads the survey, applies filters to
/// the responses and aggregates data for each widget (database-side where possible).
/// </summary>
public interface IReportEngine
{
    /// <summary>Executes the definition.</summary>
    Task<ReportResult> ExecuteAsync(ReportDefinitionDto definition, CancellationToken ct = default);
}
