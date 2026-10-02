using Mem.Migrate.Web.Security;

namespace Mem.Migrate.Web.Tests;

public sealed class AccessCodeAttemptLimiterTests
{
    [Fact]
    public void Repeated_failures_create_a_temporary_lockout()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero));
        var limiter = new AccessCodeAttemptLimiter(time, maximumFailures: 2, lockout: TimeSpan.FromMinutes(3));

        Assert.True(limiter.CanAttempt("loopback", out _));
        limiter.RecordFailure("loopback");
        limiter.RecordFailure("loopback");

        Assert.False(limiter.CanAttempt("loopback", out var retryAfter));
        Assert.Equal(TimeSpan.FromMinutes(3), retryAfter);

        time.Advance(TimeSpan.FromMinutes(3));
        Assert.True(limiter.CanAttempt("loopback", out _));
    }

    [Fact]
    public void Successful_login_clears_failure_state()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var limiter = new AccessCodeAttemptLimiter(time, maximumFailures: 2);

        limiter.RecordFailure("loopback");
        limiter.RecordSuccess("loopback");
        limiter.RecordFailure("loopback");

        Assert.True(limiter.CanAttempt("loopback", out _));
    }
}
