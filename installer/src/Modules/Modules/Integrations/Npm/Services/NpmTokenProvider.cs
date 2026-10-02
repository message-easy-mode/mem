using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Contracts;

namespace Modules.Integrations.Npm.Services;

public interface INpmTokenProvider
{
    Task<string> GetTokenAsync(string baseUrl, CancellationToken ct);
    void Invalidate();
}

public sealed class NpmTokenProvider(
    NpmApiClient npmApiClient,
    INpmApiCredentialProvider credentialProvider,
    IMemoryCache cache,
    ILogger<NpmTokenProvider> log) : INpmTokenProvider
{
    private static readonly TimeSpan TokenTtl = TimeSpan.FromMinutes(3);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _lastCacheKey;

    public async Task<string> GetTokenAsync(string baseUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("baseUrl required", nameof(baseUrl));
        }

        var credential = await credentialProvider.ResolveCurrentAsync(ct)
            ?? throw new InvalidOperationException(
                "No protected Nginx Proxy Manager administrator credential is available.");

        if (!credential.VerifiedAtUtc.HasValue)
        {
            throw new InvalidOperationException(
                "The protected Nginx Proxy Manager administrator credential has not been verified.");
        }

        var cacheKey = CacheKey(
            credential.InstallationId,
            credential.VerifiedAtUtc.Value,
            baseUrl);
        _lastCacheKey = cacheKey;

        if (cache.TryGetValue<string>(cacheKey, out var existing) &&
            !string.IsNullOrWhiteSpace(existing))
        {
            return existing!;
        }

        await _lock.WaitAsync(ct);

        try
        {
            if (cache.TryGetValue<string>(cacheKey, out existing) &&
                !string.IsNullOrWhiteSpace(existing))
            {
                return existing!;
            }

            var token = await npmApiClient.LoginAsync(
                baseUrl,
                credential.Identity,
                credential.Secret,
                ct);

            cache.Set(cacheKey, token, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TokenTtl
            });

            log.LogDebug(
                "Cached NPM API token for verified protected credential for {Seconds}s",
                (int)TokenTtl.TotalSeconds);

            return token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate()
    {
        if (!string.IsNullOrWhiteSpace(_lastCacheKey))
        {
            cache.Remove(_lastCacheKey);
        }

        log.LogDebug("Invalidated cached NPM API token");
    }

    private static string CacheKey(
        Guid installationId,
        DateTime verifiedAtUtc,
        string baseUrl) =>
        $"aio.npm.token.v2:{installationId:N}:{verifiedAtUtc.Ticks}:{baseUrl.Trim().TrimEnd('/').ToLowerInvariant()}";
}
