using System.Net.Http.Json;
using System.Text.Json;
using Mem.Cli.Config;
using Mem.Cli.Output;

namespace Mem.Cli.Clients;

/// <summary>
/// Narrow anonymous transport for the browser-approved named-device login
/// handshake. It deliberately never uses the transitional installer-token
/// cookie path, browser cookies, a Host Agent secret, or an existing device
/// credential.
/// </summary>
public interface ICliDeviceAuthorizationClient : IDisposable
{
    Task<CliDeviceAuthorizationStartResult> StartAsync(
        string verifierChallenge,
        string deviceLabel,
        CancellationToken ct = default);

    Task<CliDeviceAuthorizationPollResult> PollAsync(
        Guid authorizationId,
        string verifier,
        CancellationToken ct = default);
}

/// <summary>
/// Safe start result for the initiating CLI process. The browser URL and user
/// code are deliberately short-lived display values. The verifier is never a
/// property of this result and is never emitted by this client.
/// </summary>
public sealed record CliDeviceAuthorizationStartResult(
    string Status,
    Guid? AuthorizationId = null,
    string? UserCode = null,
    DateTimeOffset? ExpiresAtUtc = null,
    string? BrowserApprovalUrl = null,
    TimeSpan? RetryAfter = null);

/// <summary>
/// Safe poll result for the initiating CLI process. DeviceCredential is
/// populated only when the server returns an approved device session. It must
/// remain in process memory until the caller writes it to the OS secret store;
/// it must never be written to normal output, JSON, a profile, an environment
/// variable, or process arguments.
/// </summary>
public sealed record CliDeviceAuthorizationPollResult(
    string Status,
    string? DeviceCredential = null,
    DateTimeOffset? IdleExpiresAtUtc = null,
    DateTimeOffset? AbsoluteExpiresAtUtc = null,
    TimeSpan? RetryAfter = null);

/// <summary>
/// HTTP implementation of the anonymous device-authorization handshake. It
/// accepts only server-recognised status values and never returns server detail
/// strings or response bodies to the command layer.
/// </summary>
public sealed class CliDeviceAuthorizationClient : ICliDeviceAuthorizationClient
{
    private const string AuthorizationsPath =
        "api/auth/cli-device/authorizations";

    private readonly HttpClient _client;

    public CliDeviceAuthorizationClient(CliOptions options)
        : this(
            options,
            new HttpClientHandler
            {
                // Device authorization must not reuse or accept browser-like
                // cookies. The verifier is the only polling proof.
                UseCookies = false
            })
    {
    }

    /// <summary>
    /// Testable transport boundary. The client owns the supplied handler for
    /// its lifetime, matching the existing HostAgentClient test convention.
    /// </summary>
    public CliDeviceAuthorizationClient(
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

    public async Task<CliDeviceAuthorizationStartResult> StartAsync(
        string verifierChallenge,
        string deviceLabel,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await _client.PostAsJsonAsync(
                AuthorizationsPath,
                new StartRequest(
                    verifierChallenge,
                    deviceLabel),
                CliOutput.JsonOptions(),
                ct);

            if (!response.IsSuccessStatusCode)
            {
                return new CliDeviceAuthorizationStartResult(
                    Status: await ReadSafeStartFailureStatusAsync(
                        response,
                        ct),
                    RetryAfter: ReadRetryAfter(response));
            }

            var payload = await response.Content
                .ReadFromJsonAsync<StartResponse>(
                    CliOutput.JsonOptions(),
                    ct);

            return payload is null
                ? new CliDeviceAuthorizationStartResult(
                    "authorization_unavailable")
                : new CliDeviceAuthorizationStartResult(
                    NormalizeStartStatus(payload.Status),
                    payload.AuthorizationId,
                    payload.UserCode,
                    payload.ExpiresAtUtc,
                    payload.BrowserApprovalUrl);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new CliDeviceAuthorizationStartResult(
                "unreachable");
        }
    }

    public async Task<CliDeviceAuthorizationPollResult> PollAsync(
        Guid authorizationId,
        string verifier,
        CancellationToken ct = default)
    {
        try
        {
            using var response = await _client.PostAsJsonAsync(
                $"{AuthorizationsPath}/{authorizationId:D}/poll",
                new PollRequest(verifier),
                CliOutput.JsonOptions(),
                ct);

            if (!response.IsSuccessStatusCode)
            {
                return new CliDeviceAuthorizationPollResult(
                    Status: await ReadSafePollFailureStatusAsync(
                        response,
                        ct),
                    RetryAfter: ReadRetryAfter(response));
            }

            var payload = await response.Content
                .ReadFromJsonAsync<PollResponse>(
                    CliOutput.JsonOptions(),
                    ct);

            return payload is null
                ? new CliDeviceAuthorizationPollResult(
                    "authorization_unavailable")
                : new CliDeviceAuthorizationPollResult(
                    NormalizePollStatus(payload.Status),
                    payload.DeviceCredential,
                    payload.IdleExpiresAtUtc,
                    payload.AbsoluteExpiresAtUtc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new CliDeviceAuthorizationPollResult(
                "unreachable");
        }
    }

    public void Dispose() => _client.Dispose();

    private static async Task<string> ReadSafeStartFailureStatusAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var status = await ReadSafeStatusAsync(response, ct);

        return status is "validation_failed" or
            "rate_limited" or
            "authorization_unavailable"
            ? status
            : "authorization_unavailable";
    }

    private static async Task<string> ReadSafePollFailureStatusAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var status = await ReadSafeStatusAsync(response, ct);

        return status == "rate_limited"
            ? status
            : "authorization_unavailable";
    }

    private static async Task<string?> ReadSafeStatusAsync(
        HttpResponseMessage response,
        CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content
                .ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: ct);

            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(
                    "status",
                    out var statusProperty) ||
                statusProperty.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return statusProperty.GetString();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static TimeSpan? ReadRetryAfter(
        HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta)
        {
            return delta > TimeSpan.Zero
                ? delta
                : TimeSpan.Zero;
        }

        if (retryAfter?.Date is { } retryAt)
        {
            var delay = retryAt - DateTimeOffset.UtcNow;

            return delay > TimeSpan.Zero
                ? delay
                : TimeSpan.Zero;
        }

        return null;
    }

    private static string NormalizeStartStatus(
        string? status) =>
        status == "authorization_started"
            ? status
            : "authorization_unavailable";

    private static string NormalizePollStatus(
        string? status) =>
        status is "authorization_pending" or
            "authorized" or
            "authorization_denied" or
            "authorization_expired" or
            "authorization_invalid" or
            "authorization_consumed" or
            "authorization_unavailable"
            ? status
            : "authorization_unavailable";

    private sealed record StartRequest(
        string VerifierChallenge,
        string DeviceLabel);

    private sealed record PollRequest(
        string Verifier);

    private sealed record StartResponse(
        string? Status,
        Guid? AuthorizationId,
        string? UserCode,
        DateTimeOffset? ExpiresAtUtc,
        string? BrowserApprovalUrl);

    private sealed record PollResponse(
        string? Status,
        string? DeviceCredential,
        DateTimeOffset? IdleExpiresAtUtc,
        DateTimeOffset? AbsoluteExpiresAtUtc);
}
