using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Mem.Migrate.Web.Security;

internal sealed record SourceAssistantSession(
    string Key,
    string CsrfToken,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc);

internal sealed record CreatedSourceAssistantSession(
    string CookieToken,
    string CsrfToken,
    DateTimeOffset ExpiresAtUtc);

internal sealed class SourceAssistantSessionStore
{
    public const string CookieName = "mem-migrate-source-session";
    public const string ContextItemName = "mem-migrate-source-session";

    private readonly ConcurrentDictionary<string, SourceAssistantSession> sessions = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;
    private readonly TimeSpan idleTimeout;
    private readonly TimeSpan absoluteTimeout;

    public SourceAssistantSessionStore(
        TimeProvider timeProvider,
        TimeSpan idleTimeout,
        TimeSpan absoluteTimeout)
    {
        if (idleTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(idleTimeout));
        }

        if (absoluteTimeout < idleTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(absoluteTimeout),
                "Absolute session lifetime must be at least as long as the idle timeout.");
        }

        this.timeProvider = timeProvider;
        this.idleTimeout = idleTimeout;
        this.absoluteTimeout = absoluteTimeout;
    }

    public CreatedSourceAssistantSession Create()
    {
        var now = timeProvider.GetUtcNow();
        var cookieToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var csrfToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var key = HashToken(cookieToken);
        var absoluteExpiresAtUtc = now + absoluteTimeout;
        var session = new SourceAssistantSession(
            key,
            csrfToken,
            now,
            now,
            Min(now + idleTimeout, absoluteExpiresAtUtc),
            absoluteExpiresAtUtc);
        sessions[key] = session;
        return new CreatedSourceAssistantSession(cookieToken, csrfToken, session.ExpiresAtUtc);
    }

    public bool TryGet(string? cookieToken, out SourceAssistantSession? session)
    {
        session = null;
        if (string.IsNullOrWhiteSpace(cookieToken))
        {
            return false;
        }

        var key = HashToken(cookieToken);
        while (sessions.TryGetValue(key, out var current))
        {
            var now = timeProvider.GetUtcNow();
            if (current.ExpiresAtUtc <= now || current.AbsoluteExpiresAtUtc <= now)
            {
                sessions.TryRemove(key, out _);
                return false;
            }

            var refreshed = current with
            {
                LastSeenAtUtc = now,
                ExpiresAtUtc = Min(now + idleTimeout, current.AbsoluteExpiresAtUtc)
            };

            if (sessions.TryUpdate(key, refreshed, current))
            {
                session = refreshed;
                return true;
            }
        }

        return false;
    }

    public void Revoke(string? cookieToken)
    {
        if (!string.IsNullOrWhiteSpace(cookieToken))
        {
            sessions.TryRemove(HashToken(cookieToken), out _);
        }
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right) =>
        left <= right ? left : right;

    private static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        try
        {
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
