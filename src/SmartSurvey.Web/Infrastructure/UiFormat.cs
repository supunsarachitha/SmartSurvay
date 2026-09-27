using System.Globalization;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>Formatting helpers shared by Blazor components.</summary>
public static class UiFormat
{
    /// <summary>"1,234".</summary>
    public static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>"87.5%" from a 0..1 ratio.</summary>
    public static string Percent(double ratio, int decimals = 1) =>
        (ratio * 100).ToString("F" + decimals, CultureInfo.InvariantCulture) + "%";

    /// <summary>"4m 12s" / "1h 03m" / "38s".</summary>
    public static string Duration(double? seconds)
    {
        if (seconds is null or < 0)
        {
            return "—";
        }

        var ts = TimeSpan.FromSeconds(seconds.Value);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes:00}m"
            : ts.TotalMinutes >= 1 ? $"{ts.Minutes}m {ts.Seconds:00}s" : $"{ts.Seconds}s";
    }

    /// <summary>"just now", "5 minutes ago", "3 days ago", or a date for older values.</summary>
    public static string Relative(DateTime? utc, DateTime? nowUtc = null)
    {
        if (utc is null)
        {
            return "—";
        }

        var now = nowUtc ?? DateTime.UtcNow;
        var delta = now - utc.Value;
        if (delta < TimeSpan.Zero)
        {
            return "in the future";
        }

        return delta.TotalSeconds switch
        {
            < 60 => "just now",
            < 3600 => Plural((int)delta.TotalMinutes, "minute") + " ago",
            < 86400 => Plural((int)delta.TotalHours, "hour") + " ago",
            < 86400 * 30 => Plural((int)delta.TotalDays, "day") + " ago",
            _ => utc.Value.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>"1 response" / "3 responses".</summary>
    public static string Plural(int count, string singular, string? plural = null) =>
        $"{count.ToString("N0", CultureInfo.InvariantCulture)} {(count == 1 ? singular : plural ?? singular + "s")}";

    /// <summary>Bootstrap icon class for a question type.</summary>
    public static string QuestionTypeIcon(Domain.Enums.QuestionType type) => type switch
    {
        Domain.Enums.QuestionType.ShortText => "bi-input-cursor-text",
        Domain.Enums.QuestionType.LongText => "bi-textarea-t",
        Domain.Enums.QuestionType.Radio => "bi-ui-radios",
        Domain.Enums.QuestionType.Checkbox => "bi-ui-checks",
        Domain.Enums.QuestionType.Dropdown => "bi-menu-button-wide",
        Domain.Enums.QuestionType.Number => "bi-123",
        Domain.Enums.QuestionType.Email => "bi-envelope-at",
        Domain.Enums.QuestionType.Date => "bi-calendar-event",
        Domain.Enums.QuestionType.Rating => "bi-star-half",
        Domain.Enums.QuestionType.Scale => "bi-sliders",
        _ => "bi-question-circle",
    };

    /// <summary>Bootstrap icon class for a widget type.</summary>
    public static string WidgetTypeIcon(Domain.Enums.WidgetType type) => type switch
    {
        Domain.Enums.WidgetType.SummaryStats => "bi-speedometer2",
        Domain.Enums.WidgetType.QuestionTable => "bi-table",
        Domain.Enums.WidgetType.BarChart => "bi-bar-chart",
        Domain.Enums.WidgetType.HorizontalBarChart => "bi-bar-chart-steps",
        Domain.Enums.WidgetType.PieChart => "bi-pie-chart",
        Domain.Enums.WidgetType.DoughnutChart => "bi-circle",
        Domain.Enums.WidgetType.LineChart => "bi-graph-up",
        Domain.Enums.WidgetType.CrossTab => "bi-grid-3x3",
        Domain.Enums.WidgetType.TextResponses => "bi-chat-left-text",
        Domain.Enums.WidgetType.RawResponses => "bi-list-columns",
        _ => "bi-square",
    };
}
