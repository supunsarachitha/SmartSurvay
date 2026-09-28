namespace SmartSurvey.Web.Infrastructure;

/// <summary>
/// Allows at most a number of attempts within a sliding time window (thread-safe). Registered per Blazor circuit,
/// so it limits one browser connection. Deliberately not per IP: many respondents may share one address
/// (offices, schools, mobile carriers); the REST API has its own per-IP rate limiter.
/// </summary>
public abstract class SlidingWindowThrottle
{
    private readonly TimeProvider _time;
    private readonly int _limit;
    private readonly TimeSpan _window;
    private readonly Queue<DateTimeOffset> _recent = new();
    private readonly object _gate = new();

    /// <summary>Creates the throttle.</summary>
    protected SlidingWindowThrottle(TimeProvider time, int limit, TimeSpan window)
    {
        _time = time;
        _limit = limit;
        _window = window;
    }

    /// <summary>Records an attempt; false when the limit is reached (the attempt is not counted).</summary>
    public bool TryAcquire()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            while (_recent.Count > 0 && now - _recent.Peek() >= _window)
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= _limit)
            {
                return false;
            }

            _recent.Enqueue(now);
            return true;
        }
    }
}

/// <summary>
/// Caps how fast one circuit can submit survey responses — interactive submissions travel over the circuit's
/// WebSocket, which the API's per-IP limiter never sees.
/// </summary>
/// <param name="time">Clock.</param>
public sealed class SubmissionThrottle(TimeProvider time) : SlidingWindowThrottle(time, Limit, Window)
{
    /// <summary>Submissions allowed per window.</summary>
    public const int Limit = 5;

    /// <summary>Sliding window length.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
}

/// <summary>Caps password guesses for protected surveys on one circuit (brute-force protection).</summary>
/// <param name="time">Clock.</param>
public sealed class PasswordAttemptThrottle(TimeProvider time) : SlidingWindowThrottle(time, Limit, Window)
{
    /// <summary>Password attempts allowed per window.</summary>
    public const int Limit = 10;

    /// <summary>Sliding window length.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
}
