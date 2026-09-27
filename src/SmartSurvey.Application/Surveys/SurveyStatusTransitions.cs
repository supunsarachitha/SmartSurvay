using SmartSurvey.Domain.Enums;

namespace SmartSurvey.Application.Surveys;

/// <summary>
/// The survey lifecycle state machine:
/// <c>Draft → Published → Closed → Published</c> (reopen), any state <c>→ Archived</c> and
/// <c>Archived → Draft</c> (restore). The builder UI can use <see cref="AllowedTargets"/> to offer
/// only the actions that will succeed.
/// </summary>
public static class SurveyStatusTransitions
{
    /// <summary>States reachable from <paramref name="from"/> in one step.</summary>
    public static IReadOnlyList<SurveyStatus> AllowedTargets(SurveyStatus from) => from switch
    {
        SurveyStatus.Draft => [SurveyStatus.Published, SurveyStatus.Archived],
        SurveyStatus.Published => [SurveyStatus.Closed, SurveyStatus.Archived],
        SurveyStatus.Closed => [SurveyStatus.Published, SurveyStatus.Archived],
        SurveyStatus.Archived => [SurveyStatus.Draft],
        _ => [],
    };

    /// <summary>True when a survey in state <paramref name="from"/> may move to <paramref name="to"/>.</summary>
    public static bool IsAllowed(SurveyStatus from, SurveyStatus to) => AllowedTargets(from).Contains(to);

    /// <summary>Explains to a survey designer what can be done from <paramref name="from"/>.</summary>
    public static string DescribeAllowedTargets(SurveyStatus from) => from switch
    {
        SurveyStatus.Draft => "A draft survey can only be published or archived.",
        SurveyStatus.Published => "A published survey can only be closed or archived.",
        SurveyStatus.Closed => "A closed survey can only be reopened (published again) or archived.",
        SurveyStatus.Archived => "An archived survey can only be restored to draft.",
        _ => "The survey status is unknown.",
    };
}
