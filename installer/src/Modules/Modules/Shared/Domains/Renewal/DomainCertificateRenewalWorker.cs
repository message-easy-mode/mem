using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace Modules.Shared.Domains.Renewal;

public sealed class DomainCertificateRenewalWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<DomainCertificateRenewalWorker> logger,
    IMemDiagnosticEventWriter? diagnostics = null,
    IMemDiagnosticEventReader? diagnosticReader = null,
    DomainCertificateRenewalWakeSignal? wakeSignal = null) : BackgroundService
{
    internal static readonly TimeSpan ScanInterval = TimeSpan.FromHours(1);
    internal const string ScanIncidentId = "inc_domain_renewal_worker_scan";
    internal const string ScanFailureEventCode = "domains.certificate.renewal_scan_failed";
    internal const string ScanRecoveredEventCode = "domains.certificate.renewal_scan_recovered";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunScanSafelyAsync(stoppingToken);

        try
        {
            if (wakeSignal is null)
            {
                using var timer = new PeriodicTimer(ScanInterval, timeProvider);
                while (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await RunScanSafelyAsync(stoppingToken);
                }

                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    stoppingToken);
                var interval = Task.Delay(ScanInterval, timeProvider, waitCancellation.Token);
                var signal = wakeSignal.WaitAsync(waitCancellation.Token).AsTask();

                await Task.WhenAny(interval, signal);
                await waitCancellation.CancelAsync();

                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                await RunScanSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    internal async Task RunScanSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var orchestrator = scope.ServiceProvider
                .GetRequiredService<DomainCertificateRenewalOrchestrator>();

            var result = await orchestrator.ScanAsync(cancellationToken);
            await RecordScanRecoveryIfNeededAsync(result, cancellationToken);

            if (result.OperationsStarted > 0 ||
                result.AwaitingActivation > 0 ||
                result.Failed > 0)
            {
                logger.LogInformation(
                    "Automatic certificate renewal scan completed. DomainsEvaluated={DomainsEvaluated} RenewalCyclesDue={RenewalCyclesDue} OperationsStarted={OperationsStarted} AwaitingActivation={AwaitingActivation} Failed={Failed}",
                    result.DomainsEvaluated,
                    result.RenewalCyclesDue,
                    result.OperationsStarted,
                    result.AwaitingActivation,
                    result.Failed);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Do not attach arbitrary exception text to this recurring worker log:
            // workflow incidents contain only bounded safe failure classification.
            logger.LogError(
                "Automatic certificate renewal scan failed. FailureType={FailureType}",
                exception.GetType().Name);

            await diagnostics.TryWriteWorkflowEventAsync(
                new MemDiagnosticWriteRequest(
                    Severity: MemDiagnosticSeverities.Error,
                    EventCode: ScanFailureEventCode,
                    Source: nameof(DomainCertificateRenewalWorker),
                    Feature: "domains",
                    Stage: "automatic-renewal-scan",
                    Message: "MEM could not complete the automatic certificate renewal scan.",
                    IncidentId: ScanIncidentId,
                    CreateIncident: true,
                    Resource: new MemDiagnosticResource(
                        Kind: "renewal-worker",
                        Id: "automatic-certificate-renewal",
                        DisplayName: "Automatic certificate renewal",
                        WorkspacePath: "/domains/renewal"),
                    Observed: new Dictionary<string, string?>
                    {
                        ["scanResult"] = "failed"
                    },
                    Details: new Dictionary<string, string?>
                    {
                        ["failureType"] = exception.GetType().Name,
                        ["scanIntervalHours"] = ScanInterval.TotalHours.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                    },
                    SuggestedAction: "Open Diagnostics and Domains > Renewal, confirm the Control Plane can read Domain renewal state, then allow the hourly server-owned scan to retry.",
                    Retryable: true));
        }
    }

    private async Task RecordScanRecoveryIfNeededAsync(
        DomainCertificateRenewalScanResult result,
        CancellationToken cancellationToken)
    {
        if (diagnostics is null || diagnosticReader is null)
        {
            return;
        }

        try
        {
            var page = await diagnosticReader.QueryAsync(
                new MemDiagnosticQuery(
                    IncidentId: ScanIncidentId,
                    PageSize: 5),
                cancellationToken);
            var latest = page.Events
                .Where(@event => string.Equals(
                    @event.IncidentId,
                    ScanIncidentId,
                    StringComparison.Ordinal))
                .OrderByDescending(@event => @event.TimestampUtc)
                .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
                .FirstOrDefault();

            if (latest is null ||
                !string.Equals(
                    latest.EventCode,
                    ScanFailureEventCode,
                    StringComparison.Ordinal))
            {
                return;
            }

            var writeResult = await diagnostics.TryWriteWorkflowEventAsync(
                new MemDiagnosticWriteRequest(
                    Severity: MemDiagnosticSeverities.Information,
                    EventCode: ScanRecoveredEventCode,
                    Source: nameof(DomainCertificateRenewalWorker),
                    Feature: "domains",
                    Stage: "automatic-renewal-scan",
                    Message: "The automatic certificate renewal scan recovered and completed successfully.",
                    IncidentId: ScanIncidentId,
                    CreateIncident: false,
                    Resource: new MemDiagnosticResource(
                        Kind: "renewal-worker",
                        Id: "automatic-certificate-renewal",
                        DisplayName: "Automatic certificate renewal",
                        WorkspacePath: "/domains/renewal"),
                    Observed: new Dictionary<string, string?>
                    {
                        ["scanResult"] = "succeeded"
                    },
                    Details: new Dictionary<string, string?>
                    {
                        [MemDiagnosticIncidentLifecycle.StateDetailKey] =
                            MemDiagnosticIncidentLifecycle.ResolvedState,
                        [MemDiagnosticIncidentLifecycle.ResolutionCodeDetailKey] =
                            MemDiagnosticIncidentLifecycle.SelfRecoveredResolutionCode,
                        ["domainsEvaluated"] = result.DomainsEvaluated.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ["renewalCyclesDue"] = result.RenewalCyclesDue.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ["operationsStarted"] = result.OperationsStarted.ToString(
                            System.Globalization.CultureInfo.InvariantCulture)
                    },
                    Retryable: false));

            if (writeResult is not null &&
                writeResult.Stored &&
                !string.IsNullOrWhiteSpace(writeResult.IncidentId) &&
                writeResult.TimestampUtc.HasValue)
            {
                var incidentId = writeResult.IncidentId!;
                var timestampUtc = writeResult.TimestampUtc.Value;
                using var scope = scopeFactory.CreateScope();
                var incidentLifecycle = scope.ServiceProvider
                    .GetService<IMemDiagnosticIncidentLifecycleWriter>();
                if (incidentLifecycle is not null)
                {
                    await incidentLifecycle.ResolveSelfRecoveredAsync(
                        incidentId,
                        timestampUtc,
                        writeResult.EventId,
                        CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Diagnostics recovery is best-effort and must not change worker truth.
        }
    }
}
