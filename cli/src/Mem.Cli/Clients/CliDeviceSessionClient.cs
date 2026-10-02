using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Mem.Cli.Config;
using Mem.Cli.Output;

namespace Mem.Cli.Clients;

/// <summary>
/// Narrow bearer-session projection for a stored MEM CLI device credential.
/// This client is intentionally separate from the transitional HostAgentClient:
/// it never uses installer-token unlock cookies, browser cookies, or the
/// retired Host Agent shared secret.
/// </summary>
public interface ICliDeviceSessionClient : IDisposable
{
    Task<CliDeviceSessionStatusResult> GetCurrentAsync(
        string deviceCredential,
        CancellationToken ct = default);

    Task<CliDeviceSessionRevocationResult> RevokeCurrentAsync(
        string deviceCredential,
        CancellationToken ct = default);
}

public sealed record CliDeviceSessionStatusResult(
    string Status,
    string? DisplayName = null,
    IReadOnlyList<string>? Roles = null,
    DateTimeOffset? IdleExpiresAtUtc = null,
    DateTimeOffset? AbsoluteExpiresAtUtc = null);

public sealed record CliDeviceSessionRevocationResult(
    string Status,
    DateTimeOffset? RevokedAtUtc = null);

public sealed class CliDeviceSessionClient : ICliDeviceSessionClient
{
    private const string SessionPath = "api/auth/cli-device/session";

    private readonly HttpClient _client;

    public CliDeviceSessionClient(CliOptions options)
        : this(
            options,
            new HttpClientHandler
            {
                // CLI device credentials are bearer credentials, not browser
                // cookies. Do not send or accept cookie state on this path.
                UseCookies = false
            })
    {
    }

    /// <summary>
    /// Testable transport boundary. The client owns the supplied handler for
    /// its lifetime, matching the existing client test conventions.
    /// </summary>
    public CliDeviceSessionClient(
        CliOptions options,
        HttpMessageHandler messageHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(messageHandler);

        _client = new HttpClient(
            messageHandler,
            disposeHandler: true)
        {
            BaseAddress = new Uri(
                options.HostAgentUrl.TrimEnd('/') + "/",
                UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(20)
        };
    }

    public async Task<CliDeviceSessionStatusResult> GetCurrentAsync(
        string deviceCredential,
        CancellationToken ct = default)
    {
        if (!CliDeviceCredentialValidator.IsValid(deviceCredential))
        {
            return new CliDeviceSessionStatusResult("unauthenticated");
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                SessionPath);
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    deviceCredential.Trim());

            using var response = await _client.SendAsync(
                request,
                ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden)
            {
                return new CliDeviceSessionStatusResult(
                    "unauthenticated");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new CliDeviceSessionStatusResult(
                    "unavailable");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<SessionResponse>(
                    CliOutput.JsonOptions(),
                    ct);

            if (payload is null ||
                !string.Equals(
                    payload.Status,
                    "authenticated",
                    StringComparison.Ordinal))
            {
                return new CliDeviceSessionStatusResult(
                    "unavailable");
            }

            return new CliDeviceSessionStatusResult(
                "authenticated",
                payload.DisplayName,
                SanitizeRoles(payload.Roles),
                payload.IdleExpiresAtUtc,
                payload.AbsoluteExpiresAtUtc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new CliDeviceSessionStatusResult(
                "unavailable");
        }
    }

    public async Task<CliDeviceSessionRevocationResult> RevokeCurrentAsync(
        string deviceCredential,
        CancellationToken ct = default)
    {
        if (!CliDeviceCredentialValidator.IsValid(deviceCredential))
        {
            return new CliDeviceSessionRevocationResult("unauthenticated");
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                SessionPath);
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    deviceCredential.Trim());

            using var response = await _client.SendAsync(
                request,
                ct);

            if (response.StatusCode is HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden)
            {
                return new CliDeviceSessionRevocationResult(
                    "unauthenticated");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new CliDeviceSessionRevocationResult(
                    "unavailable");
            }

            var payload = await response.Content
                .ReadFromJsonAsync<RevocationResponse>(
                    CliOutput.JsonOptions(),
                    ct);

            if (payload is { Status: "revoked" })
            {
                return new CliDeviceSessionRevocationResult(
                    "revoked",
                    payload.RevokedAtUtc);
            }

            return new CliDeviceSessionRevocationResult("unavailable");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new CliDeviceSessionRevocationResult(
                "unavailable");
        }
    }

    public void Dispose() => _client.Dispose();

    private static IReadOnlyList<string> SanitizeRoles(
        IReadOnlyList<string>? roles) =>
        roles is null
            ? []
            : roles
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(role => role, StringComparer.Ordinal)
                .ToArray();

    private sealed record SessionResponse(
        string? Status,
        string? DisplayName,
        IReadOnlyList<string>? Roles,
        DateTimeOffset? IdleExpiresAtUtc,
        DateTimeOffset? AbsoluteExpiresAtUtc);

    private sealed record RevocationResponse(
        string? Status,
        DateTimeOffset? RevokedAtUtc);
}
