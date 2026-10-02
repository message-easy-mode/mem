using System.Security.Claims;
using Modules.Integrations.Seq.Services;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsLoggingHealthService(
    IMemLocalLogHealthReader localLogHealth,
    IMemDiagnosticHealthReader diagnosticHealth,
    SeqDiagnosticsOptions seqOptions,
    ISeqHealthReader seqHealth,
    DiagnosticsCapabilityService capabilities,
    TimeProvider timeProvider,
    SeqEffectiveConfigurationProvider? effectiveConfigurationProvider = null)
{
    public DiagnosticsLoggingHealthResponse Get(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var operatorCapabilities = capabilities.GetCapabilities(principal);
        var effectiveSeqOptions = effectiveConfigurationProvider?.CreateEffectiveOptions() ?? seqOptions;
        var local = localLogHealth.GetHealth();
        var store = diagnosticHealth.GetHealth();
        var seq = seqHealth.GetHealth();
        var warnings = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(local.WarningCode))
        {
            warnings.Add(local.WarningCode);
        }

        if (!string.IsNullOrWhiteSpace(local.Storage?.WarningCode))
        {
            warnings.Add(local.Storage.WarningCode);
        }

        if (!string.IsNullOrWhiteSpace(store.Storage?.WarningCode))
        {
            warnings.Add(store.Storage.WarningCode);
        }

        foreach (var code in new[]
                 {
                     store.LastWriteErrorCode,
                     store.LastReadWarningCode,
                     store.LastRetentionErrorCode,
                     seq.WarningCode
                 })
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                warnings.Add(code);
            }
        }

        var seqDegraded = effectiveSeqOptions.SinkEnabled &&
                          !string.Equals(
                              seq.Status,
                              "ready",
                              StringComparison.OrdinalIgnoreCase);
        var partial = !string.Equals(local.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
                      !string.Equals(store.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
                      seqDegraded;
        var status = partial ? "degraded" : "ready";

        return new DiagnosticsLoggingHealthResponse(
            ObservedAtUtc: timeProvider.GetUtcNow(),
            Status: status,
            LocalRecorder: new DiagnosticsLocalRecorderHealth(
                local.Enabled,
                local.Status,
                local.PersistentRecorderConfigured,
                local.PersistentRecorderActive,
                operatorCapabilities.CanViewOwnerHealthFacts
                    ? local.PersistentFilePath
                    : null,
                local.LastFileWriteAtUtc,
                local.RetainedFileCount,
                local.RetainedBytes,
                local.SerilogSelfLogMessageCount,
                local.WarningCode,
                Storage(local.Storage)),
            SafeEventStore: new DiagnosticsSafeEventStoreHealth(
                store.Enabled,
                store.Status,
                store.LastWriteAtUtc,
                store.LastReadAtUtc,
                store.StoredEventCount,
                store.DroppedEventCount,
                store.MalformedLineCount,
                store.LastWriteErrorCode,
                store.LastReadWarningCode,
                store.LastRetentionRunAtUtc,
                store.LastRetentionDeletedFileCount,
                store.LastRetentionDeletedBytes,
                store.LastRetentionErrorCode,
                Storage(store.Storage),
                HasEverRecordedEvent: store.HasEverRecordedEvent),
            Seq: new DiagnosticsSeqHealth(
                seq.Status,
                effectiveSeqOptions.SinkEnabled,
                effectiveSeqOptions.ManagementEnabled,
                seq.SinkConfigured,
                operatorCapabilities.CanViewOwnerHealthFacts
                    ? NormalizeServerUrl(effectiveSeqOptions.UiUrl)
                    : null,
                seq.Reachable,
                seq.LastCheckedAtUtc,
                seq.LastSuccessAtUtc,
                seq.WarningCode),
            Capabilities: operatorCapabilities,
            Partial: partial,
            Warnings: warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static DiagnosticsStorageCapacityHealth? Storage(
        MemStorageCapacityHealth? storage) =>
        storage is null
            ? null
            : new DiagnosticsStorageCapacityHealth(
                storage.Status,
                storage.AvailableBytes,
                storage.TotalBytes,
                storage.WarningCode);

    private static string? NormalizeServerUrl(string? value)
    {
        if (!SeqDiagnosticsOptionsValidator.TryNormalizeUrl(value, out var uri) ||
            uri is null)
        {
            return null;
        }

        return uri.ToString().TrimEnd('/');
    }
}
