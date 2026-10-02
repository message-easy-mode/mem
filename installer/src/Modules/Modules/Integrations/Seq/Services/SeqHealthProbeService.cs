using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace Modules.Integrations.Seq.Services;

public sealed class SeqHealthProbeService(
    IHttpClientFactory httpClientFactory,
    SeqDiagnosticsOptions options,
    SeqHealthState state,
    IMemDiagnosticEventWriter diagnosticWriter,
    TimeProvider timeProvider,
    ILogger<SeqHealthProbeService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.SinkEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProbeOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.ProbeIntervalSeconds),
                    timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public async Task ProbeOnceAsync(CancellationToken cancellationToken)
    {
        var current = state.GetHealth();
        if (!current.SinkConfigured ||
            !SeqDiagnosticsOptionsValidator.TryNormalizeUrl(
                options.HealthUrl,
                out var healthBaseUri) ||
            healthBaseUri is null)
        {
            return;
        }

        var healthUri = new Uri(healthBaseUri, "/health");
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

            if (response.IsSuccessStatusCode)
            {
                var transition = state.RecordReady(timeProvider.GetUtcNow());
                if (!string.Equals(
                        transition.Previous.Status,
                        transition.Current.Status,
                        StringComparison.Ordinal))
                {
                    var recovered = string.Equals(
                        transition.Previous.Status,
                        "unavailable",
                        StringComparison.Ordinal);
                    await WriteTransitionAsync(
                        MemDiagnosticSeverities.Information,
                        recovered ? "logging.seq_recovered" : "logging.seq_ready",
                        recovered
                            ? "Optional Seq logging has recovered."
                            : "Optional Seq logging is reachable.",
                        null);
                }

                return;
            }

            await RecordUnavailableAsync(
                "diagnostics.seq_health_failed",
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await RecordUnavailableAsync(
                "diagnostics.seq_probe_timeout",
                cancellationToken);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or IOException or InvalidOperationException)
        {
            logger.LogDebug(
                ex,
                "Optional Seq health probe failed. Seq remains non-essential.");
            await RecordUnavailableAsync(
                "diagnostics.seq_unavailable",
                cancellationToken);
        }
    }

    private async Task RecordUnavailableAsync(
        string warningCode,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var transition = state.RecordUnavailable(
            timeProvider.GetUtcNow(),
            warningCode);

        if (!string.Equals(
                transition.Previous.Status,
                transition.Current.Status,
                StringComparison.Ordinal))
        {
            await WriteTransitionAsync(
                MemDiagnosticSeverities.Warning,
                "logging.seq_unavailable",
                "Optional Seq logging is unavailable. MEM local diagnostics remain active.",
                warningCode);
        }
    }

    private Task<MemDiagnosticWriteResult?> WriteTransitionAsync(
        string severity,
        string eventCode,
        string message,
        string? warningCode) =>
        diagnosticWriter.TryWriteWorkflowEventAsync(
            new MemDiagnosticWriteRequest(
                Severity: severity,
                EventCode: eventCode,
                Source: nameof(SeqHealthProbeService),
                Feature: "logging",
                Message: message,
                Stage: "seq-health",
                Details: warningCode is null
                    ? null
                    : new Dictionary<string, string?>
                    {
                        ["warningCode"] = warningCode
                    },
                SuggestedAction: warningCode is null
                    ? null
                    : "Use MEM Diagnostics while the optional Seq service is unavailable."));
}
