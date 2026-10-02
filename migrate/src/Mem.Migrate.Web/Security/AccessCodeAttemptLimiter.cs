namespace Mem.Migrate.Web.Security;

internal sealed class AccessCodeAttemptLimiter
{
    private sealed class AttemptState
    {
        public DateTimeOffset WindowStartedAtUtc { get; set; }
        public int Failures { get; set; }
        public DateTimeOffset? LockedUntilUtc { get; set; }
    }

    private readonly object gate = new();
    private readonly Dictionary<string, AttemptState> attempts = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;
    private readonly int maximumFailures;
    private readonly TimeSpan window;
    private readonly TimeSpan lockout;

    public AccessCodeAttemptLimiter(
        TimeProvider timeProvider,
        int maximumFailures = 5,
        TimeSpan? window = null,
        TimeSpan? lockout = null)
    {
        this.timeProvider = timeProvider;
        this.maximumFailures = maximumFailures;
        this.window = window ?? TimeSpan.FromMinutes(5);
        this.lockout = lockout ?? TimeSpan.FromMinutes(5);
    }

    public bool CanAttempt(string key, out TimeSpan retryAfter)
    {
        lock (gate)
        {
            if (!attempts.TryGetValue(key, out var state))
            {
                retryAfter = TimeSpan.Zero;
                return true;
            }

            var now = timeProvider.GetUtcNow();
            if (state.LockedUntilUtc is { } lockedUntil && lockedUntil > now)
            {
                retryAfter = lockedUntil - now;
                return false;
            }

            if (now - state.WindowStartedAtUtc >= window)
            {
                attempts.Remove(key);
            }

            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    public void RecordFailure(string key)
    {
        lock (gate)
        {
            var now = timeProvider.GetUtcNow();
            if (!attempts.TryGetValue(key, out var state) || now - state.WindowStartedAtUtc >= window)
            {
                state = new AttemptState { WindowStartedAtUtc = now };
                attempts[key] = state;
            }

            state.Failures++;
            if (state.Failures >= maximumFailures)
            {
                state.LockedUntilUtc = now + lockout;
            }
        }
    }

    public void RecordSuccess(string key)
    {
        lock (gate)
        {
            attempts.Remove(key);
        }
    }
}
