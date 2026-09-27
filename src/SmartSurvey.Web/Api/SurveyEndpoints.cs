using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Responses;
using SmartSurvey.Application.Surveys;

namespace SmartSurvey.Web.Api;

/// <summary><c>/api/v1/surveys</c> — survey design and response management (admin).</summary>
public static class SurveyEndpoints
{
    /// <summary>Maps the survey endpoints.</summary>
    public static RouteGroupBuilder MapSurveyEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (ISurveyService surveys, [AsParameters] SurveyListParameters query, CancellationToken ct) =>
                surveys.ListAsync(query.ToQuery(), ct))
            .WithName("ListSurveys")
            .WithSummary("Lists surveys (search, status, template filter, paging).");

        group.MapGet("/templates", (ISurveyService surveys, CancellationToken ct) => surveys.ListTemplatesAsync(ct))
            .WithName("ListSurveyTemplates")
            .WithSummary("Lists survey templates (non-archived).");

        group.MapGet("/slug-available", async (ISurveyService surveys, string slug, Guid? excludeId, CancellationToken ct) =>
                new SlugAvailabilityDto(slug, await surveys.IsSlugAvailableAsync(slug, excludeId, ct)))
            .WithName("CheckSurveySlug")
            .WithSummary("Checks whether a link name (slug) is free.");

        group.MapGet("/{id:guid}", (ISurveyService surveys, Guid id, CancellationToken ct) => surveys.GetAsync(id, ct))
            .WithName("GetSurvey")
            .WithSummary("Returns the full survey definition (sections, questions, options, logic).");

        group.MapPost("/", async (ISurveyService surveys, SurveyDefinitionDto definition, CancellationToken ct) =>
            {
                var created = await surveys.CreateAsync(definition, ct);
                return TypedResults.Created($"{ApiEndpoints.Prefix}/surveys/{created.Id}", created);
            })
            .WithName("CreateSurvey")
            .WithSummary("Creates a survey (always as a draft).");

        group.MapPut("/{id:guid}", (ISurveyService surveys, Guid id, SurveyDefinitionDto definition, CancellationToken ct) =>
                surveys.UpdateAsync(id, definition, ct))
            .WithName("UpdateSurvey")
            .WithSummary("Replaces the survey design. Send the 'version' you loaded; a stale version returns 409.");

        group.MapDelete("/{id:guid}", async (ISurveyService surveys, Guid id, CancellationToken ct) =>
            {
                await surveys.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteSurvey")
            .WithSummary("Deletes a survey with all of its responses and reports.");

        group.MapPost("/{id:guid}/status", (ISurveyService surveys, Guid id, ChangeSurveyStatusRequest request, CancellationToken ct) =>
                surveys.ChangeStatusAsync(id, request.Status, ct))
            .WithName("ChangeSurveyStatus")
            .WithSummary("Publishes, closes, archives or reopens a survey.");

        group.MapPost("/{id:guid}/duplicate", async (ISurveyService surveys, Guid id, DuplicateSurveyRequest? request, CancellationToken ct) =>
            {
                var copy = await surveys.DuplicateAsync(id, request ?? new DuplicateSurveyRequest(), ct);
                return TypedResults.Created($"{ApiEndpoints.Prefix}/surveys/{copy.Id}", copy);
            })
            .WithName("DuplicateSurvey")
            .WithSummary("Copies a survey (or creates a survey from a template, or saves a survey as a template).");

        group.MapGet("/{id:guid}/definition", (ISurveyService surveys, Guid id, CancellationToken ct) =>
                surveys.ExportDefinitionAsync(id, ct))
            .WithName("ExportSurveyDefinition")
            .WithSummary("Exports the survey design as a portable JSON document.");

        group.MapPost("/import", async (ISurveyService surveys, SurveyExportDocument document, CancellationToken ct) =>
            {
                var imported = await surveys.ImportDefinitionAsync(document, ct);
                return TypedResults.Created($"{ApiEndpoints.Prefix}/surveys/{imported.Id}", imported);
            })
            .WithName("ImportSurveyDefinition")
            .WithSummary("Creates a draft survey from an exported JSON document.");

        group.MapGet("/{id:guid}/responses", (IResponseService responses, Guid id, [AsParameters] ResponseListParameters query, CancellationToken ct) =>
                responses.ListForSurveyAsync(id, query.ToQuery(), ct))
            .WithName("ListSurveyResponses")
            .WithSummary("Lists the responses of a survey (status, date range, search, paging).");

        group.MapGet("/{id:guid}/responses/export", async (
                IResponseExportService exports, Guid id, string? format, bool? includeInProgress, CancellationToken ct) =>
            {
                var exportFormat = ApiHelpers.ParseFormat(format ?? "csv", exports.SupportedFormats);
                return ApiHelpers.File(await exports.ExportResponsesAsync(id, exportFormat, includeInProgress ?? false, ct));
            })
            .WithName("ExportSurveyResponses")
            .WithSummary("Downloads the raw responses (format=csv|xlsx|json; includeInProgress=true adds drafts).")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream");

        return group;
    }
}
