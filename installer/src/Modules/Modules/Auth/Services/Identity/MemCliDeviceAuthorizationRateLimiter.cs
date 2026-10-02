using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Modules.Auth.Configuration;

namespace Modules.Auth.Services.Identity;

public interface IMemCliDeviceAuthorizationRateLimiter
{
    MemCliDeviceAuthorizationRateLimitLease TryAcquireStart();

    MemCliDeviceAuthorizationRateLimitLease TryAcquirePoll();
}

public sealed record MemCliDeviceAuthorizationRateLimitLease(
    bool Acquired,
    TimeSpan? RetryAfter);

/// <summary>
/// Two small shared anonymous budgets for the device flow. MEM intentionally
/// does not trust a forwarded address header as a rate-limit partition key
/// until its private proxy/topology contract establishes one.
/// </summary>
public sealed class MemCliDeviceAuthorizationRateLimiter :
    IMemCliDeviceAuthorizationRateLimiter,
    IDisposable
{
    private readonly FixedWindowRateLimiter _startLimiter;
    private readonly FixedWindowRateLimiter _pollLimiter;

    public MemCliDeviceAuthorizationRateLimiter(
        IOptions<MemCliDeviceSessionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;

        _startLimiter = CreateLimiter(
            value.AuthorizationStartRateLimitPermitLimit,
            value.AuthorizationStartRateLimitWindowSeconds);

        _pollLimiter = CreateLimiter(
            value.AuthorizationPollRateLimitPermitLimit,
            value.AuthorizationPollRateLimitWindowSeconds);
    }

    public MemCliDeviceAuthorizationRateLimitLease TryAcquireStart() =>
        TryAcquire(_startLimiter);

    public MemCliDeviceAuthorizationRateLimitLease TryAcquirePoll() =>
        TryAcquire(_pollLimiter);

    public void Dispose()
    {
        _startLimiter.Dispose();
        _pollLimiter.Dispose();
    }

    private static FixedWindowRateLimiter CreateLimiter(
        int permitLimit,
        int windowSeconds) =>
        new(new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromSeconds(windowSeconds),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });

    private static MemCliDeviceAuthorizationRateLimitLease TryAcquire(
        FixedWindowRateLimiter limiter)
    {
        using var lease = limiter.AttemptAcquire(permitCount: 1);

        return lease.IsAcquired
            ? new MemCliDeviceAuthorizationRateLimitLease(true, null)
            : new MemCliDeviceAuthorizationRateLimitLease(
                false,
                lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                    ? retryAfter
                    : null);
    }
}
