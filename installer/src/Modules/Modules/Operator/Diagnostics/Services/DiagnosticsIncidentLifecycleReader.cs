using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public sealed record DiagnosticsIncidentLifecycleReadResult(
    IReadOnlyDictionary<string, DiagnosticsIncidentLifecycleProjection> Projections,
    string? WarningCode = null);

/// <summary>
/// Read-side integration boundary for incident lifecycle state. All operator
/// projections use the same 01A lifecycle projector. If the durable overlay
/// cannot be inspected, MEM fails safe by treating observed incidents as open
/// attention and emits a bounded warning rather than hiding a fault.
/// </summary>
public sealed class DiagnosticsIncidentLifecycleReader(
    DiagnosticsIncidentLifecycleProjector projector,
    ILogger<DiagnosticsIncidentLifecycleReader> logger)
{
    public const string UnavailableWarningCode =
        "diagnostics.incident_lifecycle_unavailable";

    public async Task<DiagnosticsIncidentLifecycleReadResult> ProjectEventsAsync(
        IReadOnlyCollection<MemDiagnosticEvent> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);

        try
        {
            return new DiagnosticsIncidentLifecycleReadResult(
                await projector.ProjectEventsAsync(events, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                ex,
                "MEM diagnostics could not inspect durable incident lifecycle state; observed incidents remain open by default.");

            return new DiagnosticsIncidentLifecycleReadResult(
                BuildOpenFallback(events),
                UnavailableWarningCode);
        }
    }

    public DiagnosticsIncidentLifecycleReadResult OpenFallback(
        IReadOnlyCollection<MemDiagnosticEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return new DiagnosticsIncidentLifecycleReadResult(BuildOpenFallback(events));
    }

    private static IReadOnlyDictionary<string, DiagnosticsIncidentLifecycleProjection> BuildOpenFallback(
        IEnumerable<MemDiagnosticEvent> events)
    {
        return events
            .Where(@event => !string.IsNullOrWhiteSpace(@event.IncidentId))
            .Select(@event => @event.IncidentId!)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                incidentId => incidentId,
                incidentId => new DiagnosticsIncidentLifecycleProjection(
                    incidentId,
                    DiagnosticsIncidentLifecycleStates.Open,
                    Reopened: false,
                    StoredDisposition: null,
                    UpdatedAtUtc: null,
                    UpdatedByOperatorId: null,
                    ObservedThroughAtUtc: null,
                    ObservedThroughEventId: null,
                    SnoozedUntilUtc: null,
                    ResolutionCode: null,
                    Revision: null),
                StringComparer.Ordinal);
    }

}
