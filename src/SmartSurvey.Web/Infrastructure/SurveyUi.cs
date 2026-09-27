using System.Text.Json;
using System.Text.Json.Serialization;
using SmartSurvey.Application.Surveys;
using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>A lifecycle action offered in the UI for a survey status change.</summary>
/// <param name="Target">Status after the action.</param>
/// <param name="Label">Button/menu label, e.g. "Publish".</param>
/// <param name="Icon">Bootstrap icon class.</param>
/// <param name="Confirmation">Confirmation question shown before the change.</param>
/// <param name="Danger">True for actions that stop data collection.</param>
public sealed record SurveyStatusAction(SurveyStatus Target, string Label, string Icon, string Confirmation, bool Danger);

/// <summary>Survey-related UI helpers shared by the admin pages.</summary>
public static class SurveyUi
{
    /// <summary>JSON settings for survey definition files (indented, camelCase, enums as strings).</summary>
    public static readonly JsonSerializerOptions DefinitionJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Largest survey definition file accepted by the import dialog.</summary>
    public const long MaxImportBytes = 2 * 1024 * 1024;

    /// <summary>The lifecycle actions allowed from <paramref name="status"/>.</summary>
    public static IReadOnlyList<SurveyStatusAction> ActionsFor(SurveyStatus status) =>
        SurveyStatusTransitions.AllowedTargets(status).Select(target => Describe(status, target)).ToList();

    /// <summary>Label, icon and confirmation text of one transition.</summary>
    public static SurveyStatusAction Describe(SurveyStatus from, SurveyStatus to) => (from, to) switch
    {
        (SurveyStatus.Closed, SurveyStatus.Published) => new(to, "Reopen", "bi-play-circle",
            "Reopen this survey? Respondents can answer it again.", false),
        (_, SurveyStatus.Published) => new(to, "Publish", "bi-send",
            "Publish this survey? It becomes available to respondents right away (within its schedule).", false),
        (_, SurveyStatus.Closed) => new(to, "Close", "bi-stop-circle",
            "Close this survey? No new responses are accepted; existing responses and reports stay available.", true),
        (_, SurveyStatus.Archived) => new(to, "Archive", "bi-archive",
            "Archive this survey? It is hidden from respondents and becomes read-only until restored.", true),
        (_, SurveyStatus.Draft) => new(to, "Restore to draft", "bi-arrow-counterclockwise",
            "Restore this survey to draft so it can be edited again?", false),
        _ => new(to, to.ToString(), "bi-arrow-right", $"Change the status to {to}?", false),
    };

    /// <summary>File name of an exported definition, e.g. <c>customer-feedback.survey.json</c>.</summary>
    public static string DefinitionFileName(SurveyDefinitionDto survey) =>
        $"{(string.IsNullOrWhiteSpace(survey.Slug) ? "survey" : survey.Slug)}.survey.json";
}
