using Microsoft.Extensions.Time.Testing;
using SmartSurvey.Web.Infrastructure;

namespace SmartSurvey.UnitTests.Ui;

public sealed class SubmissionThrottleTests
{
    [Fact]
    public void Allows_the_limit_within_the_window_then_refuses_until_it_slides()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero));
        var throttle = new SubmissionThrottle(time);

        for (var i = 0; i < SubmissionThrottle.Limit; i++)
        {
            Assert.True(throttle.TryAcquire());
            time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.False(throttle.TryAcquire());

        time.Advance(SubmissionThrottle.Window - TimeSpan.FromSeconds(SubmissionThrottle.Limit) + TimeSpan.FromMilliseconds(1));
        Assert.True(throttle.TryAcquire()); // the oldest attempt left the window
        Assert.False(throttle.TryAcquire());
    }
}
