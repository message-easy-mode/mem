using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;
using Shared.Exceptions;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsPipelineSelfTestService(
    IMemLocalLogHealthReader localLogHealth,
    IMemDiagnosticHealthReader diagnosticHealth,
    IMemDiagnosticEventWriter diagnosticWriter,
    IMemDiagnosticEventReader diagnosticReader,
    SeqDiagnosticsOptions seqOptions,
    SeqLoggingRuntimeState seqLoggingRuntimeState,
    ISeqHealthReader seqHealth,
    ISeqDeliveryStateStore seqDeliveryStateStore,
    ISeqBootstrapStateStore seqBootstrapStateStore,
    SeqDeliveryProcessIdentity seqDeliveryProcessIdentity,
    TimeProvider timeProvider,
    ILogger<DiagnosticsPipelineSelfTestService> logger)
{
    private const string VerificationEventCode = "diagnostics.pipeline_self_test";
    private const string VerificationFeature = "diagnostics";
    private const string VerificationStage = "pipeline-self-test";
    private static readonly TimeSpan StageTimeout = TimeSpan.FromSeconds(10);
    private int _running;

    public async Task<DiagnosticsPipelineSelfTestResponse> RunAsync(
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new MemProblemException(
                StatusCodes.Status409Conflict,
                "diagnostics_self_test_in_progress",
                "A diagnostics verification is already running",
                "Wait for the current diagnostics verification to finish before starting another.",
                feature: VerificationFeature);
        }

        try
        {
            return await RunCoreAsync(cancellationToken);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<DiagnosticsPipelineSelfTestResponse> RunCoreAsync(
        CancellationToken cancellationToken)
    {
        var startedAtUtc = timeProvider.GetUtcNow();
        var verificationId = $"diag_verify_{Guid.NewGuid():N}";
        var checks = new List<DiagnosticsPipelineSelfTestCheck>(5);
        var warnings = new HashSet<string>(StringComparer.Ordinal);

        logger.LogInformation(
            "MEM diagnostics pipeline verification started. VerificationId={VerificationId} EventCode={EventCode}",
            verificationId,
            VerificationEventCode);

        checks.Add(VerifyLocalRecorder(warnings));

        var storeHealth = GetStoreHealth(warnings);
        MemDiagnosticWriteResult? writeResult = null;
        MemDiagnosticEvent? readBackEvent = null;

        if (storeHealth is null)
        {
            checks.Add(new DiagnosticsPipelineSelfTestCheck(
                "safe_event_write",
                "failed",
                MemDiagnosticCodes.SelfTestSafeEventWriteFailed));
            checks.Add(new DiagnosticsPipelineSelfTestCheck(
                "safe_event_read_back",
                "not-run",
                MemDiagnosticCodes.SelfTestSafeEventWriteFailed));
            checks.Add(new DiagnosticsPipelineSelfTestCheck(
                "correlation_round_trip",
                "not-run",
                MemDiagnosticCodes.SelfTestSafeEventWriteFailed));
        }
        else if (!storeHealth.Enabled)
        {
            warnings.Add(MemDiagnosticCodes.StoreDisabled);
            checks.Add(new DiagnosticsPipelineSelfTestCheck(
                "safe_event_write",
                "disabled",
                MemDiagnosticCodes.StoreDisabled));
            checks.Add(new DiagnosticsPipelineSelfTestCheck(
                "safe_event_read_back",
                "not-run",
                MemDiagnosticCodes.StoreDisabled));
            checks.Add(new DiagnosticsPipelineSelfTestCheck(
                "correlation_round_trip",
                "not-run",
                MemDiagnosticCodes.StoreDisabled));
        }
        else
        {
            writeResult = await WriteVerificationEventAsync(
                verificationId,
                warnings,
                cancellationToken);
            checks.Add(WriteCheck(writeResult, warnings));

            if (writeResult?.Stored == true)
            {
                readBackEvent = await ReadVerificationEventAsync(
                    writeResult.EventId,
                    warnings,
                    cancellationToken);
                checks.Add(ReadBackCheck(readBackEvent, warnings));
                checks.Add(CorrelationCheck(
                    readBackEvent,
                    verificationId,
                    warnings));
            }
            else
            {
                var warningCode = writeResult?.WarningCode ??
                    MemDiagnosticCodes.SelfTestSafeEventWriteFailed;
                checks.Add(new DiagnosticsPipelineSelfTestCheck(
                    "safe_event_read_back",
                    "not-run",
                    warningCode));
                checks.Add(new DiagnosticsPipelineSelfTestCheck(
                    "correlation_round_trip",
                    "not-run",
                    warningCode));
            }
        }

        checks.Add(SeqCheck(warnings));

        var failed = checks.Any(check =>
            string.Equals(check.Status, "failed", StringComparison.Ordinal) ||
            (IsRequiredCheck(check.Code) &&
             !string.Equals(check.Status, "passed", StringComparison.Ordinal)));
        var completedAtUtc = timeProvider.GetUtcNow();
        var status = failed ? "failed" : "passed";

        logger.LogInformation(
            "MEM diagnostics pipeline verification completed. VerificationId={VerificationId} Status={Status} EventId={EventId}",
            verificationId,
            status,
            writeResult?.EventId);

        return new DiagnosticsPipelineSelfTestResponse(
            SchemaVersion: 1,
            VerificationId: verificationId,
            Status: status,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            EventId: writeResult?.EventId,
            Checks: checks,
            Warnings: warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private MemDiagnosticStoreHealth? GetStoreHealth(ISet<string> warnings)
    {
        try
        {
            return diagnosticHealth.GetHealth();
        }
        catch (Exception ex) when (
            ex is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                ex,
                "MEM diagnostics pipeline verification could not inspect the safe event store.");
            warnings.Add(MemDiagnosticCodes.SelfTestSafeEventWriteFailed);
            return null;
        }
    }

    private DiagnosticsPipelineSelfTestCheck VerifyLocalRecorder(
        ISet<string> warnings)
    {
        try
        {
            var health = localLogHealth.GetHealth();
            if (!health.Enabled || !health.PersistentRecorderConfigured)
            {
                const string warningCode = MemDiagnosticCodes.SelfTestLocalRecorderUnavailable;
                warnings.Add(warningCode);
                return new DiagnosticsPipelineSelfTestCheck(
                    "local_recorder_writable",
                    "not-configured",
                    warningCode);
            }

            if (!health.PersistentRecorderActive ||
                !string.Equals(health.Status, "ready", StringComparison.OrdinalIgnoreCase))
            {
                var warningCode = health.WarningCode ??
                    MemDiagnosticCodes.SelfTestLocalRecorderUnavailable;
                warnings.Add(warningCode);
                return new DiagnosticsPipelineSelfTestCheck(
                    "local_recorder_writable",
                    "failed",
                    warningCode);
            }

            if (health.LastFileWriteAtUtc is null)
            {
                const string warningCode =
                    MemDiagnosticCodes.SelfTestLocalRecorderNoWriteObserved;
                warnings.Add(warningCode);
                return new DiagnosticsPipelineSelfTestCheck(
                    "local_recorder_writable",
                    "failed",
                    warningCode);
            }

            return new DiagnosticsPipelineSelfTestCheck(
                "local_recorder_writable",
                "passed");
        }
        catch (Exception ex) when (
            ex is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                ex,
                "MEM diagnostics pipeline verification could not inspect the local recorder.");
            const string warningCode = MemDiagnosticCodes.SelfTestLocalRecorderUnavailable;
            warnings.Add(warningCode);
            return new DiagnosticsPipelineSelfTestCheck(
                "local_recorder_writable",
                "failed",
                warningCode);
        }
    }

    private async Task<MemDiagnosticWriteResult?> WriteVerificationEventAsync(
        string verificationId,
        ISet<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(StageTimeout);
            return await diagnosticWriter.WriteAsync(
                new MemDiagnosticWriteRequest(
                    Severity: MemDiagnosticSeverities.Information,
                    EventCode: VerificationEventCode,
                    Source: nameof(DiagnosticsPipelineSelfTestService),
                    Feature: VerificationFeature,
                    Message: "MEM diagnostics pipeline verification event.",
                    Stage: VerificationStage,
                    CreateIncident: false,
                    Details: new Dictionary<string, string?>
                    {
                        ["verificationId"] = verificationId
                    },
                    Context: new MemDiagnosticContext(
                        CorrelationId: verificationId)),
                timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(
                ex,
                "MEM diagnostics pipeline verification timed out while writing the safe event.");
            warnings.Add(MemDiagnosticCodes.SelfTestSafeEventWriteFailed);
            return null;
        }
        catch (Exception ex) when (
            ex is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                ex,
                "MEM diagnostics pipeline verification could not write the safe event.");
            warnings.Add(MemDiagnosticCodes.SelfTestSafeEventWriteFailed);
            return null;
        }
    }

    private static DiagnosticsPipelineSelfTestCheck WriteCheck(
        MemDiagnosticWriteResult? result,
        ISet<string> warnings)
    {
        if (result?.Stored == true)
        {
            return new DiagnosticsPipelineSelfTestCheck(
                "safe_event_write",
                "passed");
        }

        var warningCode = result?.WarningCode ??
            MemDiagnosticCodes.SelfTestSafeEventWriteFailed;
        warnings.Add(warningCode);
        return new DiagnosticsPipelineSelfTestCheck(
            "safe_event_write",
            string.Equals(
                warningCode,
                MemDiagnosticCodes.StoreDisabled,
                StringComparison.Ordinal)
                ? "disabled"
                : "failed",
            warningCode);
    }

    private async Task<MemDiagnosticEvent?> ReadVerificationEventAsync(
        string eventId,
        ISet<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(StageTimeout);
            var page = await diagnosticReader.QueryAsync(
                new MemDiagnosticQuery(
                    EventId: eventId,
                    PageSize: 1),
                timeout.Token);
            foreach (var warning in page.Warnings)
            {
                warnings.Add(warning);
            }

            return page.Events.FirstOrDefault(candidate => string.Equals(
                candidate.EventId,
                eventId,
                StringComparison.Ordinal));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogWarning(
                ex,
                "MEM diagnostics pipeline verification timed out while reading back the safe event. EventId={EventId}",
                eventId);
            warnings.Add(MemDiagnosticCodes.SelfTestSafeEventReadBackFailed);
            return null;
        }
        catch (Exception ex) when (
            ex is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                ex,
                "MEM diagnostics pipeline verification could not read back the safe event. EventId={EventId}",
                eventId);
            warnings.Add(MemDiagnosticCodes.SelfTestSafeEventReadBackFailed);
            return null;
        }
    }

    private static DiagnosticsPipelineSelfTestCheck ReadBackCheck(
        MemDiagnosticEvent? readBackEvent,
        ISet<string> warnings)
    {
        if (readBackEvent is not null)
        {
            return new DiagnosticsPipelineSelfTestCheck(
                "safe_event_read_back",
                "passed");
        }

        warnings.Add(MemDiagnosticCodes.SelfTestSafeEventReadBackFailed);
        return new DiagnosticsPipelineSelfTestCheck(
            "safe_event_read_back",
            "failed",
            MemDiagnosticCodes.SelfTestSafeEventReadBackFailed);
    }

    private static DiagnosticsPipelineSelfTestCheck CorrelationCheck(
        MemDiagnosticEvent? readBackEvent,
        string verificationId,
        ISet<string> warnings)
    {
        if (readBackEvent is null)
        {
            return new DiagnosticsPipelineSelfTestCheck(
                "correlation_round_trip",
                "not-run",
                MemDiagnosticCodes.SelfTestSafeEventReadBackFailed);
        }

        if (string.Equals(
                readBackEvent.CorrelationId,
                verificationId,
                StringComparison.Ordinal))
        {
            return new DiagnosticsPipelineSelfTestCheck(
                "correlation_round_trip",
                "passed");
        }

        warnings.Add(MemDiagnosticCodes.SelfTestCorrelationMismatch);
        return new DiagnosticsPipelineSelfTestCheck(
            "correlation_round_trip",
            "failed",
            MemDiagnosticCodes.SelfTestCorrelationMismatch);
    }

    private DiagnosticsPipelineSelfTestCheck SeqCheck(ISet<string> warnings)
    {
        try
        {
            var delivery = seqDeliveryStateStore.GetState();
            if (!string.IsNullOrWhiteSpace(delivery.WarningCode) &&
                string.Equals(
                    delivery.WarningCode,
                    "seq_delivery_startup_prerequisites_unavailable",
                    StringComparison.Ordinal))
            {
                warnings.Add(delivery.WarningCode);
                return new DiagnosticsPipelineSelfTestCheck(
                    "seq_delivery",
                    "failed",
                    delivery.WarningCode);
            }

            if (delivery.RestartRequired)
            {
                const string warningCode = "diagnostics.seq_restart_required";
                warnings.Add(warningCode);
                return new DiagnosticsPipelineSelfTestCheck(
                    "seq_delivery",
                    "restart-pending",
                    warningCode);
            }

            if (!delivery.EffectiveEnabled)
            {
                return new DiagnosticsPipelineSelfTestCheck(
                    "seq_delivery",
                    seqOptions.ManagementEnabled ? "disabled" : "not-configured");
            }

            if (!seqLoggingRuntimeState.SinkConfigured)
            {
                var warningCode = seqLoggingRuntimeState.WarningCode ??
                    "diagnostics.seq_sink_configuration_failed";
                warnings.Add(warningCode);
                return new DiagnosticsPipelineSelfTestCheck(
                    "seq_delivery",
                    "failed",
                    warningCode);
            }

            var bootstrapRead = seqBootstrapStateStore.Read();
            if (!string.IsNullOrWhiteSpace(bootstrapRead.WarningCode))
            {
                warnings.Add(bootstrapRead.WarningCode);
                return new DiagnosticsPipelineSelfTestCheck(
                    "seq_delivery",
                    "failed",
                    bootstrapRead.WarningCode);
            }

            var bootstrap = bootstrapRead.State;
            if (bootstrap is null ||
                bootstrap.ActiveDeliveryProcessId != seqDeliveryProcessIdentity.Value ||
                bootstrap.ActiveDeliveryVerifiedAtUtc is null ||
                string.IsNullOrWhiteSpace(bootstrap.LastActiveDeliveryVerificationId))
            {
                const string warningCode =
                    "diagnostics.seq_delivery_verification_required";
                warnings.Add(warningCode);
                return new DiagnosticsPipelineSelfTestCheck(
                    "seq_delivery",
                    "verification-required",
                    warningCode);
            }

            var health = seqHealth.GetHealth();
            if (!health.SinkConfigured ||
                !health.Reachable ||
                !string.Equals(health.Status, "ready", StringComparison.OrdinalIgnoreCase))
            {
                var warningCode = health.WarningCode ??
                    "diagnostics.seq_unavailable";
                warnings.Add(warningCode);
                return new DiagnosticsPipelineSelfTestCheck(
                    "seq_delivery",
                    "failed",
                    warningCode);
            }

            return new DiagnosticsPipelineSelfTestCheck(
                "seq_delivery",
                "passed");
        }
        catch (Exception ex) when (
            ex is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                ex,
                "MEM diagnostics pipeline verification could not inspect Seq delivery state.");
            const string warningCode = "diagnostics.seq_unavailable";
            warnings.Add(warningCode);
            return new DiagnosticsPipelineSelfTestCheck(
                "seq_delivery",
                "failed",
                warningCode);
        }
    }

    private static bool IsRequiredCheck(string code) => code is
        "local_recorder_writable" or
        "safe_event_write" or
        "safe_event_read_back" or
        "correlation_round_trip";
}
