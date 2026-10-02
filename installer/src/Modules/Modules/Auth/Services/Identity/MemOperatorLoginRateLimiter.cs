using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Modules.Auth.Configuration;

namespace Modules.Auth.Services.Identity;

/// <summary>
/// Bounds anonymous password, TOTP, and recovery-code verification work for
/// the small, privileged MEM control plane. This is deliberately one shared
/// application budget: the current deployment topology has not yet established
/// a trustworthy forwarded client-address contract, so treating
/// X-Forwarded-For as a partition key
/// would be unsafe.
///
/// Identity's per-account lockout remains the account-specific defence. This
/// limiter protects the service from bursts across unknown or many accounts.
/// </summary>
public interface IMemOperatorLoginRateLimiter
{
    MemOperatorLoginRateLimitLease TryAcquire();
}

public sealed record MemOperatorLoginRateLimitLease(
    bool Acquired,
    TimeSpan? RetryAfter);

public sealed class MemOperatorLoginRateLimiter : IMemOperatorLoginRateLimiter, IDisposable
{
    private readonly FixedWindowRateLimiter _limiter;

    public MemOperatorLoginRateLimiter(
        IOptions<MemOperatorIdentityOptions> identityOptions)
    {
        ArgumentNullException.ThrowIfNull(identityOptions);

        var options = identityOptions.Value;

        _limiter = new FixedWindowRateLimiter(
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = options.LoginRateLimitPermitLimit,
                Window = TimeSpan.FromSeconds(options.LoginRateLimitWindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    public MemOperatorLoginRateLimitLease TryAcquire()
    {
        using var lease = _limiter.AttemptAcquire(permitCount: 1);

        if (lease.IsAcquired)
        {
            return new MemOperatorLoginRateLimitLease(
                Acquired: true,
                RetryAfter: null);
        }

        return new MemOperatorLoginRateLimitLease(
            Acquired: false,
            RetryAfter: lease.TryGetMetadata(
                MetadataName.RetryAfter,
                out var retryAfter)
                ? retryAfter
                : null);
    }

    public void Dispose()
    {
        _limiter.Dispose();
    }
}
