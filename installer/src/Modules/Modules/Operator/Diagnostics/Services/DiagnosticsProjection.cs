using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

internal static class DiagnosticsProjection
{
    public static DiagnosticsEventProjection Event(
        MemDiagnosticEvent source,
        bool includeOwnerFacts)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new DiagnosticsEventProjection(
            source.SchemaVersion,
            source.EventId,
            source.TimestampUtc,
            source.Severity,
            source.EventCode,
            source.Source,
            source.Feature,
            source.Stage,
            source.Message,
            source.IncidentId,
            source.TraceId,
            source.SpanId,
            source.RequestId,
            source.CorrelationId,
            source.OperationId,
            Resource(source.Resource, includeOwnerFacts),
            source.Expected,
            source.Observed,
            source.Details,
            source.Exception,
            source.SuggestedAction,
            source.Retryable,
            source.RedactionsApplied,
            source.Truncated);
    }

    public static DiagnosticsResourceProjection? Resource(
        MemDiagnosticResource? source,
        bool includeOwnerFacts)
    {
        if (source is null)
        {
            return null;
        }

        return new DiagnosticsResourceProjection(
            source.Kind,
            source.Id,
            source.DisplayName,
            source.StackId,
            source.StackSlug,
            source.Service,
            includeOwnerFacts ? source.WorkspacePath : null);
    }

    public static DiagnosticsIncidentLifecycle Lifecycle(
        DiagnosticsIncidentLifecycleProjection source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new DiagnosticsIncidentLifecycle(
            source.State,
            source.Reopened,
            source.StoredDisposition,
            source.UpdatedAtUtc,
            source.UpdatedByOperatorId,
            source.ObservedThroughAtUtc,
            source.ObservedThroughEventId,
            source.SnoozedUntilUtc,
            source.ResolutionCode,
            source.Revision);
    }

    public static int SeverityRank(string severity) =>
        severity.ToLowerInvariant() switch
        {
            MemDiagnosticSeverities.Critical => 4,
            MemDiagnosticSeverities.Error => 3,
            MemDiagnosticSeverities.Warning => 2,
            MemDiagnosticSeverities.Information => 1,
            _ => 0
        };
}
