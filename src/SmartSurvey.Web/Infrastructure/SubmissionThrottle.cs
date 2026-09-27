namespace SmartSurvey.Web.Infrastructure;

/// <summary>
/// Caps how fast one Blazor circuit can submit survey responses (at most <see cref="Limit"/> within
/// <see cref="Window"/>). The REST API is protected by a per-IP rate limiter, but interactive submissions
/// travel over the circuit's WebSocket, which that limiter never sees. Deliberately per circuit, not per
/// IP: many respondents may share one address (offices, schools, mobile carriers).
/// </summary>
/// <param name="time">Clock.</param>
public sealed class SubmissionThrottle(TimeProvider time)
{
    /// <summary>Submissions allowed per window.</summary>
    public const int Limit = 5;

    /// <summary>Sliding window length.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private readonly Queue<DateTimeOffset> _recent = new();
    private readonly object _gate = new();

    /// <summary>Records a submission attempt; false when the limit is reached (the attempt is not counted).</summary>
    public bool TryAcquire()
    {
        lock (_gate)
        {
            var now = time.GetUtcNow();
            while (_recent.Count > 0 && now - _recent.Peek() >= Window)
            {
                _recent.Dequeue();
            }

            if (_recent.Count >= Limit)
            {
                return false;
            }

            _recent.Enqueue(now);
            return true;
        }
    }
}
