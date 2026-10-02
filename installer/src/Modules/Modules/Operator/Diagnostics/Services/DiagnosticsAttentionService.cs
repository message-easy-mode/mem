using Microsoft.Extensions.Logging;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsAttentionService(
    DiagnosticsEventCollector collector,
    TimeProvider timeProvider,
    ILogger<DiagnosticsAttentionService> logger,
    DiagnosticsIncidentLifecycleReader? lifecycle = null)
{
    public const int MaximumItems = 5;

    public async Task<DiagnosticsAttentionResponse> GetAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > MaximumItems)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                $"Diagnostics attention limit must be between 1 and {MaximumItems}.");
        }

        try
        {
            var collected = await collector.CollectOverviewAsync(cancellationToken);
            var warnings = new HashSet<string>(collected.Warnings, StringComparer.Ordinal);
            var lifecycleRead = await ProjectLifecycleAsync(
                collected.Events,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(lifecycleRead.WarningCode))
            {
                warnings.Add(lifecycleRead.WarningCode);
            }

            var attention = collected.Events
                .Where(IsAttentionEvent)
                .Where(@event => IsSafeIncidentId(@event.IncidentId))
                .GroupBy(@event => @event.IncidentId!, StringComparer.Ordinal)
                .Where(group => IsOpen(
                    lifecycleRead.Projections.GetValueOrDefault(group.Key)))
                .Select(BuildIncident)
                .OrderByDescending(item => DiagnosticsProjection.SeverityRank(item.Severity))
                .ThenByDescending(item => item.LastSeenAtUtc)
                .ThenByDescending(item => item.IncidentId, StringComparer.Ordinal)
                .ToArray();

            var partial = collected.Truncated || warnings.Count > 0;
            var highestSeverity = attention.FirstOrDefault()?.Severity;
            var state = attention.Length == 0
                ? partial
                    ? "unavailable"
                    : "ready"
                : string.Equals(
                    highestSeverity,
                    MemDiagnosticSeverities.Warning,
                    StringComparison.OrdinalIgnoreCase)
                    ? "warning"
                    : "attention";

            return new DiagnosticsAttentionResponse(
                SchemaVersion: 1,
                ObservedAtUtc: timeProvider.GetUtcNow(),
                State: state,
                Total: attention.Length,
                HighestSeverity: highestSeverity,
                Items: attention.Take(limit).ToArray(),
                Partial: partial,
                Warnings: warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                ex,
                "MEM diagnostics could not project the bounded header attention summary.");

            return new DiagnosticsAttentionResponse(
                SchemaVersion: 1,
                ObservedAtUtc: timeProvider.GetUtcNow(),
                State: "unavailable",
                Total: 0,
                HighestSeverity: null,
                Items: Array.Empty<DiagnosticsAttentionItem>(),
                Partial: true,
                Warnings: ["diagnostics.attention_unavailable"]);
        }
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

    private static bool IsOpen(DiagnosticsIncidentLifecycleProjection? projection) =>
        projection is null ||
        string.Equals(
            projection.State,
            DiagnosticsIncidentLifecycleStates.Open,
            StringComparison.Ordinal);

    private static bool IsAttentionEvent(MemDiagnosticEvent source) =>
        DiagnosticsProjection.SeverityRank(source.Severity) >=
        DiagnosticsProjection.SeverityRank(MemDiagnosticSeverities.Warning);

    private static bool IsSafeIncidentId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is > 4 and <= 80 &&
        value.StartsWith("inc_", StringComparison.Ordinal) &&
        value.All(character =>
            char.IsLetterOrDigit(character) ||
            character is '_' or '-' or '.');

    private static DiagnosticsAttentionItem BuildIncident(
        IGrouping<string, MemDiagnosticEvent> group)
    {
        var events = group
            .OrderByDescending(@event => @event.TimestampUtc)
            .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
            .ToArray();
        var primary = events
            .OrderByDescending(@event => DiagnosticsProjection.SeverityRank(@event.Severity))
            .ThenByDescending(@event => @event.TimestampUtc)
            .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
            .First();
        var incidentId = group.Key;

        return new DiagnosticsAttentionItem(
            IncidentId: incidentId,
            Severity: primary.Severity,
            EventCode: primary.EventCode,
            Feature: primary.Feature,
            Stage: primary.Stage,
            Summary: primary.Message,
            LastSeenAtUtc: events.Max(@event => @event.TimestampUtc),
            Href: $"/diagnostics/logs?incident={Uri.EscapeDataString(incidentId)}");
    }
}
