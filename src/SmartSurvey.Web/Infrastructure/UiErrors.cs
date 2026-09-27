using SmartSurvey.Application.Common;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>Turns service exceptions into short, user-facing messages for toasts and alerts.</summary>
public static class UiErrors
{
    /// <summary>Maximum number of validation messages listed in one toast.</summary>
    private const int MaxListedErrors = 3;

    /// <summary>
    /// True for exceptions the UI should show to the user (business/validation/permission errors);
    /// anything else is unexpected and should be logged and reported generically.
    /// </summary>
    public static bool IsExpected(Exception ex) => ex is AppException;

    /// <summary>A friendly message for <paramref name="ex"/>.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        AppValidationException validation => DescribeValidation(validation),
        AppException app => app.Message,
        _ => "Something went wrong. Please try again; if the problem persists, check the application log.",
    };

    private static string DescribeValidation(AppValidationException ex)
    {
        var messages = ex.Errors.SelectMany(e => e.Value).Distinct().ToList();
        if (messages.Count == 0)
        {
            return ex.Message;
        }

        var listed = string.Join(" ", messages.Take(MaxListedErrors));
        return messages.Count > MaxListedErrors ? $"{listed} (+{messages.Count - MaxListedErrors} more)" : listed;
    }
}
