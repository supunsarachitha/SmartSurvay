namespace SmartSurvey.Web.Components.Shared;

/// <summary>Toast severity.</summary>
public enum ToastLevel
{
    /// <summary>Informational.</summary>
    Info,

    /// <summary>Success.</summary>
    Success,

    /// <summary>Warning.</summary>
    Warning,

    /// <summary>Error.</summary>
    Error,
}

/// <summary>A toast notification.</summary>
/// <param name="Id">Unique id.</param>
/// <param name="Level">Severity.</param>
/// <param name="Title">Bold title.</param>
/// <param name="Message">Body text.</param>
public sealed record ToastMessage(Guid Id, ToastLevel Level, string Title, string? Message);

/// <summary>
/// Scoped (per circuit) toast notification hub. Interactive pages call <see cref="Success"/>,
/// <see cref="Error"/>, …; the <see cref="ToastHost"/> island in the admin layout renders them.
/// </summary>
public sealed class ToastService
{
    private readonly List<ToastMessage> _toasts = [];
    private readonly object _lock = new();

    /// <summary>Raised when the toast list changes.</summary>
    public event Action? Changed;

    /// <summary>Current toasts (oldest first).</summary>
    public IReadOnlyList<ToastMessage> Toasts
    {
        get
        {
            lock (_lock)
            {
                return [.. _toasts];
            }
        }
    }

    /// <summary>Shows a toast and returns its id.</summary>
    public Guid Show(ToastLevel level, string title, string? message = null)
    {
        var toast = new ToastMessage(Guid.NewGuid(), level, title, message);
        lock (_lock)
        {
            _toasts.Add(toast);
            if (_toasts.Count > 5)
            {
                _toasts.RemoveAt(0);
            }
        }

        Changed?.Invoke();
        return toast.Id;
    }

    /// <summary>Success toast.</summary>
    public Guid Success(string title, string? message = null) => Show(ToastLevel.Success, title, message);

    /// <summary>Error toast.</summary>
    public Guid Error(string title, string? message = null) => Show(ToastLevel.Error, title, message);

    /// <summary>Warning toast.</summary>
    public Guid Warning(string title, string? message = null) => Show(ToastLevel.Warning, title, message);

    /// <summary>Info toast.</summary>
    public Guid Info(string title, string? message = null) => Show(ToastLevel.Info, title, message);

    /// <summary>Removes a toast.</summary>
    public void Dismiss(Guid id)
    {
        lock (_lock)
        {
            _toasts.RemoveAll(t => t.Id == id);
        }

        Changed?.Invoke();
    }
}
