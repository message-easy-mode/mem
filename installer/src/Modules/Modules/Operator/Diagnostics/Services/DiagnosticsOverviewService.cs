using System.Security.Claims;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsOverviewService(
    DiagnosticsEventCollector collector,
    DiagnosticsLoggingHealthService loggingHealth,
    DiagnosticsCapabilityService capabilities,
    DiagnosticsActiveContextService activeContext,
    TimeProvider timeProvider,
    DiagnosticsIncidentLifecycleReader? lifecycle = null)
{
    public async Task<DiagnosticsOverviewResponse> GetAsync(
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var collected = await collector.CollectOverviewAsync(cancellationToken);
        var health = loggingHealth.Get(principal);
        var lifecycleRead = await ProjectLifecycleAsync(
            collected.Events,
            cancellationToken);
        var context = await activeContext.ResolveAsync(
            collected.Events,
            lifecycleRead.Projections,
            lifecycleRead.WarningCode,
            cancellationToken);
        var warnings = new HashSet<string>(collected.Warnings, StringComparer.Ordinal);
        warnings.UnionWith(health.Warnings);
        if (!string.IsNullOrWhiteSpace(lifecycleRead.WarningCode))
        {
            warnings.Add(lifecycleRead.WarningCode);
        }
        if (!string.IsNullOrWhiteSpace(context.WarningCode))
        {
            warnings.Add(context.WarningCode);
        }

        // Severity/event counts are historical facts. Lifecycle state controls
        // whether an incident still requires attention, not whether its events
        // occurred, so these counts deliberately remain event-based.
        var attentionEvents = collected.Events
            .Where(IsAttentionEvent)
            .Where(@event => IsOpen(
                @event.IncidentId!,
                lifecycleRead.Projections))
            .ToArray();

        var counts = new DiagnosticsOverviewCounts(
            Information: collected.Events.Count(@event => string.Equals(
                @event.Severity,
                MemDiagnosticSeverities.Information,
                StringComparison.OrdinalIgnoreCase)),
            Warning: collected.Events.Count(@event => string.Equals(
                @event.Severity,
                MemDiagnosticSeverities.Warning,
                StringComparison.OrdinalIgnoreCase)),
            Error: collected.Events.Count(@event => string.Equals(
                @event.Severity,
                MemDiagnosticSeverities.Error,
                StringComparison.OrdinalIgnoreCase)),
            Critical: collected.Events.Count(@event => string.Equals(
                @event.Severity,
                MemDiagnosticSeverities.Critical,
                StringComparison.OrdinalIgnoreCase)),
            IncidentCount: attentionEvents
                .Select(@event => @event.IncidentId!)
                .Distinct(StringComparer.Ordinal)
                .Count(),
            EventCount: collected.Events.Count);

        var partial = health.Partial ||
                      collected.Warnings.Count > 0 ||
                      !string.IsNullOrWhiteSpace(lifecycleRead.WarningCode) ||
                      !string.IsNullOrWhiteSpace(context.WarningCode);
        var status = DetermineStatus(attentionEvents, partial);

        return new DiagnosticsOverviewResponse(
            SchemaVersion: 1,
            GeneratedAtUtc: timeProvider.GetUtcNow(),
            Status: status,
            Counts: counts,
            LoggingHealth: health,
            Capabilities: capabilities.GetCapabilities(principal),
            ActiveContext: context.Context,
            Partial: partial,
            Truncated: collected.Truncated,
            Warnings: warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private async Task<DiagnosticsIncidentLifecycleReadResult> ProjectLifecycleAsync(
        IReadOnlyCollection<MemDiagnosticEvent> events,
        CancellationToken cancellationToken)
    {
        if (lifecycle is not null)
        {
            return await lifecycle.ProjectEventsAsync(events, cancellationToken);
        }

        return new DiagnosticsIncidentLifecycleReadResult(
            events
                .Where(@event => !string.IsNullOrWhiteSpace(@event.IncidentId))
                .Select(@event => @event.IncidentId!)
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(
                    incidentId => incidentId,
                    OpenLifecycle,
                    StringComparer.Ordinal));
    }

    private static DiagnosticsIncidentLifecycleProjection OpenLifecycle(string incidentId) =>
        new(
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
            Revision: null);

    private static bool IsOpen(
        string incidentId,
        IReadOnlyDictionary<string, DiagnosticsIncidentLifecycleProjection> projections) =>
        !projections.TryGetValue(incidentId, out var projection) ||
        string.Equals(
            projection.State,
            DiagnosticsIncidentLifecycleStates.Open,
            StringComparison.Ordinal);

    internal static bool IsAttentionEvent(MemDiagnosticEvent @event) =>
        !string.IsNullOrWhiteSpace(@event.IncidentId) &&
        IsAttentionSeverity(@event.Severity);

    private static bool IsAttentionSeverity(string severity) =>
        string.Equals(severity, MemDiagnosticSeverities.Warning, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(severity, MemDiagnosticSeverities.Error, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(severity, MemDiagnosticSeverities.Critical, StringComparison.OrdinalIgnoreCase);

    internal static string DetermineStatus(
        IReadOnlyCollection<MemDiagnosticEvent> attentionEvents,
        bool partial)
    {
        if (attentionEvents.Any(@event =>
                string.Equals(@event.Severity, MemDiagnosticSeverities.Critical, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(@event.Severity, MemDiagnosticSeverities.Error, StringComparison.OrdinalIgnoreCase)))
        {
            return "attention";
        }

        if (attentionEvents.Any(@event =>
                string.Equals(@event.Severity, MemDiagnosticSeverities.Warning, StringComparison.OrdinalIgnoreCase)))
        {
            return "warning";
        }

        return partial ? "degraded" : "ready";
    }
}
