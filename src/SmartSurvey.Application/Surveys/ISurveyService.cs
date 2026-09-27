using SmartSurvey.Application.Common;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Surveys;

/// <summary>
/// Survey design use cases (admin). Every mutating operation validates its input, writes an audit
/// log entry and throws <see cref="AppException"/> subtypes for expected failures.
/// </summary>
public interface ISurveyService
{
    /// <summary>Paged, filtered list of surveys with response counts.</summary>
    Task<PagedResult<SurveySummaryDto>> ListAsync(SurveyQuery query, CancellationToken ct = default);

    /// <summary>Templates for the "create from template" gallery.</summary>
    Task<IReadOnlyList<SurveySummaryDto>> ListTemplatesAsync(CancellationToken ct = default);

    /// <summary>Full definition. Throws <see cref="NotFoundException"/>.</summary>
    Task<SurveyDefinitionDto> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Full definition by slug (null when not found).</summary>
    Task<SurveyDefinitionDto?> FindBySlugAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Creates a survey (status Draft). Generates slug and question codes when missing, normalises
    /// order values and assigns ids to items with <see cref="Guid.Empty"/>.
    /// </summary>
    Task<SurveyDefinitionDto> CreateAsync(SurveyDefinitionDto dto, CancellationToken ct = default);

    /// <summary>
    /// Replaces the design of an existing survey, reconciling sections, questions, options and
    /// logic rules by id (insert new, update existing, delete missing — deleting a question also
    /// deletes its answers). Throws <see cref="ConflictException"/> when <c>dto.Version</c> is stale.
    /// </summary>
    Task<SurveyDefinitionDto> UpdateAsync(Guid id, SurveyDefinitionDto dto, CancellationToken ct = default);

    /// <summary>Deletes a survey with all responses and reports.</summary>
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Changes lifecycle status. Publishing requires at least one question and a non-template survey.
    /// Allowed: Draft→Published, Published→Closed, Closed→Published (reopen), any→Archived, Archived→Draft.
    /// </summary>
    Task<SurveyDefinitionDto> ChangeStatusAsync(Guid id, SurveyStatus status, CancellationToken ct = default);

    /// <summary>Deep-copies a survey design (no responses) as a new Draft (or template).</summary>
    Task<SurveyDefinitionDto> DuplicateAsync(Guid id, DuplicateSurveyRequest request, CancellationToken ct = default);

    /// <summary>Exports a portable JSON definition document.</summary>
    Task<SurveyExportDocument> ExportDefinitionAsync(Guid id, CancellationToken ct = default);

    /// <summary>Imports a definition document as a new Draft survey (all ids remapped consistently).</summary>
    Task<SurveyDefinitionDto> ImportDefinitionAsync(SurveyExportDocument document, CancellationToken ct = default);

    /// <summary>True when no other survey uses the slug.</summary>
    Task<bool> IsSlugAvailableAsync(string slug, Guid? excludeSurveyId = null, CancellationToken ct = default);

    /// <summary>Number of answers stored for a question (used to warn before deleting it in the builder).</summary>
    Task<int> CountAnswersAsync(Guid questionId, CancellationToken ct = default);
}
