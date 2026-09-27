using Microsoft.JSInterop;
using SmartSurvey.Application.Exports;

namespace SmartSurvey.Web.Infrastructure;

/// <summary>
/// Scoped wrapper around the small JS helper library (<c>wwwroot/js/app.js</c>): file downloads,
/// clipboard, browser time zone. JS interop is only available in interactive components after the
/// first render — call these from event handlers or <c>OnAfterRenderAsync</c>.
/// </summary>
public sealed class BrowserInterop(IJSRuntime js)
{
    private TimeZoneInfo? _timeZone;

    /// <summary>The browser time zone if it has already been resolved (null during prerendering).</summary>
    public TimeZoneInfo? CachedTimeZone => _timeZone;

    /// <summary>Resolves (once per circuit) the browser's IANA time zone; falls back to UTC.</summary>
    public async ValueTask<TimeZoneInfo> GetTimeZoneAsync()
    {
        if (_timeZone is not null)
        {
            return _timeZone;
        }

        try
        {
            var id = await js.InvokeAsync<string>("SmartSurvey.getTimeZone");
            _timeZone = TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz) ? tz : TimeZoneInfo.Utc;
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            return TimeZoneInfo.Utc; // prerendering / disconnected: do not cache
        }

        return _timeZone;
    }

    /// <summary>Converts a UTC timestamp to the browser's local time.</summary>
    public async ValueTask<DateTime> ToLocalAsync(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), await GetTimeZoneAsync());

    /// <summary>Converts a browser-local timestamp (e.g. from datetime-local input) to UTC.</summary>
    public async ValueTask<DateTime> ToUtcAsync(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), await GetTimeZoneAsync());

    /// <summary>Streams a generated file to the browser as a download.</summary>
    public async ValueTask DownloadAsync(ExportFile file)
    {
        using var stream = new MemoryStream(file.Content);
        using var streamRef = new DotNetStreamReference(stream);
        await js.InvokeVoidAsync("SmartSurvey.downloadFile", file.FileName, file.ContentType, streamRef);
    }

    /// <summary>Copies text to the clipboard; returns false when not permitted.</summary>
    public async ValueTask<bool> CopyTextAsync(string text)
    {
        try
        {
            return await js.InvokeAsync<bool>("SmartSurvey.copyText", text);
        }
        catch (JSException)
        {
            return false;
        }
    }

    /// <summary>Smoothly scrolls to the top of the page.</summary>
    public ValueTask ScrollToTopAsync() => js.InvokeVoidAsync("SmartSurvey.scrollToTop");

    /// <summary>Scrolls the first element matching the CSS selector into view.</summary>
    public ValueTask ScrollIntoViewAsync(string selector) => js.InvokeVoidAsync("SmartSurvey.scrollIntoView", selector);
}
