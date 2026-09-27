using SmartSurvey.Application.Exports;
using SmartSurvey.Application.Reports;

namespace SmartSurvey.Web.Api;

/// <summary><c>/api/v1/reports</c> — report definitions, execution and export (admin).</summary>
public static class ReportEndpoints
{
    /// <summary>Formats reports can be exported in.</summary>
    private static readonly ExportFormat[] ReportFormats = Enum.GetValues<ExportFormat>();

    /// <summary>Maps the report endpoints.</summary>
    public static RouteGroupBuilder MapReportEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (IReportService reports, [AsParameters] ReportListParameters query, CancellationToken ct) => reports.ListAsync(query.ToQuery(), ct))
            .WithName("ListReports")
            .WithSummary("Lists saved reports (survey filter, search, paging).");

        group.MapGet("/{id:guid}", (IReportService reports, Guid id, CancellationToken ct) => reports.GetAsync(id, ct))
            .WithName("GetReport")
            .WithSummary("Returns a report definition (filters and widgets).");

        group.MapPost("/", async (IReportService reports, ReportDefinitionDto definition, CancellationToken ct) =>
            {
                var created = await reports.CreateAsync(definition, ct);
                return TypedResults.Created($"{ApiEndpoints.Prefix}/reports/{created.Id}", created);
            })
            .WithName("CreateReport")
            .WithSummary("Saves a new report definition.");

        group.MapPut("/{id:guid}", (IReportService reports, Guid id, ReportDefinitionDto definition, CancellationToken ct) =>
                reports.UpdateAsync(id, definition, ct))
            .WithName("UpdateReport")
            .WithSummary("Replaces a report definition (widgets are matched by id).");

        group.MapDelete("/{id:guid}", async (IReportService reports, Guid id, CancellationToken ct) =>
            {
                await reports.DeleteAsync(id, ct);
                return TypedResults.NoContent();
            })
            .WithName("DeleteReport")
            .WithSummary("Deletes a report.");

        group.MapPost("/{id:guid}/duplicate", async (IReportService reports, Guid id, CancellationToken ct) =>
            {
                var copy = await reports.DuplicateAsync(id, ct);
                return TypedResults.Created($"{ApiEndpoints.Prefix}/reports/{copy.Id}", copy);
            })
            .WithName("DuplicateReport")
            .WithSummary("Copies a report (\"Copy of …\").");

        group.MapPost("/default/{surveyId:guid}", async Task<IResult> (IReportService reports, Guid surveyId, bool? save, CancellationToken ct) =>
            {
                var definition = await reports.BuildDefaultAsync(surveyId, ct);
                if (save != true)
                {
                    return TypedResults.Ok(definition);
                }

                var created = await reports.CreateAsync(definition, ct);
                return TypedResults.Created($"{ApiEndpoints.Prefix}/reports/{created.Id}", created);
            })
            .WithName("BuildDefaultReport")
            .WithSummary("Generates an overview report for a survey (one widget per question); save=true stores it.")
            .Produces<ReportDefinitionDto>()
            .Produces<ReportDefinitionDto>(StatusCodes.Status201Created);

        group.MapGet("/{id:guid}/run", (IReportService reports, Guid id, CancellationToken ct) => reports.RunAsync(id, ct))
            .WithName("RunReport")
            .WithSummary("Executes a saved report and returns tables, chart data and statistics.");

        group.MapPost("/preview", (IReportService reports, ReportDefinitionDto definition, CancellationToken ct) =>
                reports.PreviewAsync(definition, ct))
            .WithName("PreviewReport")
            .WithSummary("Executes an unsaved definition (live preview while building a report).");

        group.MapGet("/{id:guid}/export", async (IReportService reports, Guid id, string? format, CancellationToken ct) =>
                ApiHelpers.File(await reports.ExportAsync(id, ApiHelpers.ParseFormat(format ?? "pdf", ReportFormats), ct)))
            .WithName("ExportReport")
            .WithSummary("Downloads the report (format=pdf|csv|txt|xlsx|json).")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream");

        return group;
    }
}
