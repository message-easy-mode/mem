using Shared.ControlPlane.Runtime;
namespace Modules.Integrations.Seq.Services;

public sealed record SeqRuntimeHealthVerification(
    bool Passed,
    string Status,
    DateTimeOffset ObservedAtUtc,
    string? WarningCode);

public interface ISeqRuntimeHealthVerifier
{
    Task<SeqRuntimeHealthVerification> VerifyAsync(
        CancellationToken cancellationToken);
}

/// <summary>
/// Performs one bounded, secret-free health probe for an operator-requested
/// lifecycle action. Unlike the background sink probe, this can verify the
/// managed runtime even while event delivery is intentionally disabled.
/// </summary>
public sealed class SeqRuntimeHealthVerifier(
    IHttpClientFactory httpClientFactory,
    SeqDiagnosticsOptions options,
    SeqHealthState healthState,
    ISeqRuntimeStatusReader runtimeStatusReader,
    IMemManagedServiceAuthorityResolver authorityResolver,
    TimeProvider timeProvider,
    SeqRuntimeContextProfile? runtimeProfile = null) : ISeqRuntimeHealthVerifier
{
    public async Task<SeqRuntimeHealthVerification> VerifyAsync(
        CancellationToken cancellationToken)
    {
        var observedAt = timeProvider.GetUtcNow();
        var baseUri = await ResolveHealthBaseUriAsync(cancellationToken);
        if (baseUri is null)
        {
            healthState.RecordUnavailable(
                observedAt,
                "diagnostics.seq_health_url_invalid");
            return new SeqRuntimeHealthVerification(
                Passed: false,
                Status: "invalid",
                ObservedAtUtc: observedAt,
                WarningCode: "diagnostics.seq_health_url_invalid");
        }

        var healthUri = new Uri(baseUri, "/health");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.ProbeTimeoutSeconds));

        try
        {
            var client = httpClientFactory.CreateClient("seq-probe");
            using var response = await client.GetAsync(
                healthUri,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                healthState.RecordUnavailable(
                    observedAt,
                    "diagnostics.seq_health_failed");
                return new SeqRuntimeHealthVerification(
                    Passed: false,
                    Status: "failed",
                    ObservedAtUtc: observedAt,
                    WarningCode: "diagnostics.seq_health_failed");
            }

            healthState.RecordReady(observedAt);
            return new SeqRuntimeHealthVerification(
                Passed: true,
                Status: "healthy",
                ObservedAtUtc: observedAt,
                WarningCode: null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            healthState.RecordUnavailable(
                observedAt,
                "diagnostics.seq_probe_timeout");
            return new SeqRuntimeHealthVerification(
                Passed: false,
                Status: "timeout",
                ObservedAtUtc: observedAt,
                WarningCode: "diagnostics.seq_probe_timeout");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidOperationException)
        {
            healthState.RecordUnavailable(
                observedAt,
                "diagnostics.seq_unavailable");
            return new SeqRuntimeHealthVerification(
                Passed: false,
                Status: "unavailable",
                ObservedAtUtc: observedAt,
                WarningCode: "diagnostics.seq_unavailable");
        }
    }

    private async Task<Uri?> ResolveHealthBaseUriAsync(
        CancellationToken cancellationToken)
    {
        var runtime = await runtimeStatusReader.GetStatusAsync(cancellationToken);
        try
        {
            return authorityResolver.Resolve(
                MemManagedServicePurposes.Health,
                SeqManagedServiceAuthoritySourceFactory.Create(
                    options,
                    runtime,
                    runtimeProfile)).Authority;
        }
        catch (MemManagedServiceAuthorityException)
        {
            return null;
        }
    }

}
