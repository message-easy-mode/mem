using System.Security.Claims;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;
using Shared.Exceptions;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsIncidentService(
    IMemDiagnosticEventReader reader,
    DiagnosticsEventCollector collector,
    DiagnosticsCapabilityService capabilities,
    DiagnosticsWorkspaceLinkBuilder workspaceLinks,
    DiagnosticsQueryParser queryParser,
    DiagnosticsApiOptions options,
    MemDbContext db,
    ILogger<DiagnosticsIncidentService> logger,
    DiagnosticsIncidentLifecycleReader? lifecycle = null)
{
    public const string LifecycleAll = "all";

    public Task<DiagnosticsIncidentPageResponse> QueryAsync(
        MemDiagnosticQuery query,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken) =>
        QueryAsync(query, LifecycleAll, principal, cancellationToken);

    public async Task<DiagnosticsIncidentPageResponse> QueryAsync(
        MemDiagnosticQuery query,
        string lifecycleFilter,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(principal);

        MemDiagnosticEventPage page;
        try
        {
            page = await reader.QueryAsync(
                query with { RequireIncidentId = true },
                cancellationToken);
        }
        catch (MemDiagnosticCursorException)
        {
            throw;
        }
        catch (ArgumentException ex)
        {
            throw new MemProblemException(
                StatusCodes.Status400BadRequest,
                "diagnostics_query_invalid",
                "The diagnostics query is invalid",
                "The supplied diagnostics filter could not be accepted.",
                feature: "diagnostics",
                innerException: ex);
        }

        var includeOwnerFacts = capabilities
            .GetCapabilities(principal)
            .CanViewOwnerHealthFacts;
        var lifecycleRead = await ProjectLifecycleAsync(page.Events, cancellationToken);
        var incidents = page.Events
            .Where(@event => !string.IsNullOrWhiteSpace(@event.IncidentId))
            .GroupBy(@event => @event.IncidentId!, StringComparer.Ordinal)
            .Select(group => BuildSummary(
                group,
                includeOwnerFacts,
                lifecycleRead.Projections.GetValueOrDefault(group.Key)))
            .Where(incident => MatchesLifecycle(incident, lifecycleFilter))
            .OrderByDescending(incident => incident.LastSeenAtUtc)
            .ThenByDescending(incident => incident.IncidentId, StringComparer.Ordinal)
            .ToArray();

        return new DiagnosticsIncidentPageResponse(
            page.FromUtc,
            page.UntilUtc,
            page.PageSize,
            page.WindowClamped,
            incidents,
            page.NextCursor,
            Partial: page.Warnings.Count > 0 ||
                     !string.IsNullOrWhiteSpace(lifecycleRead.WarningCode),
            MergeWarnings(page.Warnings, lifecycleRead.WarningCode));
    }

    public async Task<DiagnosticsIncidentDetailResponse> GetAsync(
        string incidentId,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var normalized = queryParser.ParseRequiredIdentifier(
            incidentId,
            "incidentId",
            "inc_",
            80);
        var collected = await collector.CollectIncidentAsync(
            normalized,
            maximumEvents: options.IncidentMaximumEvents,
            cancellationToken);
        var events = collected.Events
            .Where(@event => string.Equals(
                @event.IncidentId,
                normalized,
                StringComparison.Ordinal))
            .OrderByDescending(@event => @event.TimestampUtc)
            .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
            .ToArray();

        EnsureIncidentExists(events);

        var operatorCapabilities = capabilities.GetCapabilities(principal);
        var operationRead = await ReadOperationsAsync(events, cancellationToken);
        var lifecycleRead = await ProjectLifecycleAsync(events, cancellationToken);
        var technicalEvents = operatorCapabilities.CanReadTechnicalEvents
            ? events
                .Select(@event => DiagnosticsProjection.Event(
                    @event,
                    operatorCapabilities.CanViewOwnerHealthFacts))
                .ToArray()
            : null;

        return new DiagnosticsIncidentDetailResponse(
            BuildSummary(
                events,
                operatorCapabilities.CanViewOwnerHealthFacts,
                lifecycleRead.Projections.GetValueOrDefault(normalized)),
            operatorCapabilities,
            events.Length,
            technicalEvents,
            operationRead.Operations,
            collected.Truncated,
            MergeWarnings(
                collected.Warnings,
                operationRead.WarningCode,
                lifecycleRead.WarningCode));
    }

    internal async Task<DiagnosticsIncidentEventWatermark> LoadWatermarkAsync(
        string incidentId,
        CancellationToken cancellationToken)
    {
        var normalized = queryParser.ParseRequiredIdentifier(
            incidentId,
            "incidentId",
            "inc_",
            80);
        var collected = await collector.CollectIncidentAsync(
            normalized,
            maximumEvents: Math.Min(options.IncidentMaximumEvents, 50),
            cancellationToken);
        var latest = collected.Events
            .Where(@event => string.Equals(
                @event.IncidentId,
                normalized,
                StringComparison.Ordinal))
            .OrderByDescending(@event => @event.TimestampUtc)
            .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
            .FirstOrDefault();

        if (latest is null)
        {
            ThrowIncidentNotFound();
        }

        return new DiagnosticsIncidentEventWatermark(
            normalized,
            latest!.TimestampUtc,
            latest.EventId);
    }

    internal async Task<(DiagnosticsIncidentSummary Summary, IReadOnlyList<MemDiagnosticEvent> Events, IReadOnlyList<DiagnosticsOperationSummary> Operations, bool Truncated, IReadOnlyList<string> Warnings)> LoadForReportAsync(
        string incidentId,
        CancellationToken cancellationToken)
    {
        var normalized = queryParser.ParseRequiredIdentifier(
            incidentId,
            "incidentId",
            "inc_",
            80);
        var collected = await collector.CollectIncidentAsync(
            normalized,
            maximumEvents: options.SupportReportMaximumEvents,
            cancellationToken);
        var events = collected.Events
            .Where(@event => string.Equals(
                @event.IncidentId,
                normalized,
                StringComparison.Ordinal))
            .OrderByDescending(@event => @event.TimestampUtc)
            .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
            .ToArray();

        EnsureIncidentExists(events);

        var operationRead = await ReadOperationsAsync(events, cancellationToken);
        var lifecycleRead = await ProjectLifecycleAsync(events, cancellationToken);
        return (
            BuildSummary(
                events,
                includeOwnerFacts: false,
                lifecycleRead.Projections.GetValueOrDefault(normalized)),
            events,
            operationRead.Operations,
            collected.Truncated,
            MergeWarnings(
                collected.Warnings,
                operationRead.WarningCode,
                lifecycleRead.WarningCode));
    }

    private DiagnosticsIncidentSummary BuildSummary(
        IEnumerable<MemDiagnosticEvent> source,
        bool includeOwnerFacts,
        DiagnosticsIncidentLifecycleProjection? lifecycleProjection)
    {
        var events = source
            .OrderByDescending(@event => @event.TimestampUtc)
            .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
            .ToArray();
        var primary = events
            .OrderByDescending(@event => DiagnosticsProjection.SeverityRank(@event.Severity))
            .ThenByDescending(@event => @event.TimestampUtc)
            .First();
        var latest = events[0];
        var resource = primary.Resource ?? latest.Resource;
        lifecycleProjection ??= OpenLifecycle(primary.IncidentId!);

        return new DiagnosticsIncidentSummary(
            primary.IncidentId!,
            primary.Severity,
            primary.EventCode,
            primary.Feature,
            primary.Stage,
            primary.Message,
            events.Min(@event => @event.TimestampUtc),
            events.Max(@event => @event.TimestampUtc),
            events.Length,
            events.Any(@event => @event.Retryable),
            events.Any(@event => @event.Truncated),
            DiagnosticsProjection.Resource(resource, includeOwnerFacts),
            workspaceLinks.Build(resource),
            DiagnosticsProjection.Lifecycle(lifecycleProjection));
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

    private static bool MatchesLifecycle(
        DiagnosticsIncidentSummary incident,
        string lifecycleFilter) =>
        string.Equals(lifecycleFilter, LifecycleAll, StringComparison.Ordinal) ||
        string.Equals(
            incident.Lifecycle?.State ?? DiagnosticsIncidentLifecycleStates.Open,
            lifecycleFilter,
            StringComparison.Ordinal);

    private async Task<OperationReadResult> ReadOperationsAsync(
        IReadOnlyCollection<MemDiagnosticEvent> events,
        CancellationToken cancellationToken)
    {
        var operationIds = events
            .Where(@event => @event.OperationId.HasValue)
            .Select(@event => @event.OperationId!.Value)
            .Distinct()
            .Take(50)
            .ToArray();
        if (operationIds.Length == 0)
        {
            return new OperationReadResult(
                Array.Empty<DiagnosticsOperationSummary>(),
                WarningCode: null);
        }

        try
        {
            var operations = await db.RuntimeOperations
                .AsNoTracking()
                .Where(operation => operationIds.Contains(operation.Id))
                .OrderByDescending(operation => operation.RequestedAtUtc)
                .Select(operation => new
                {
                    operation.Id,
                    operation.RuntimeStackId,
                    operation.Operation,
                    operation.Status,
                    operation.RequestedAtUtc,
                    operation.StartedAtUtc,
                    operation.CompletedAtUtc
                })
                .ToArrayAsync(cancellationToken);

            return new OperationReadResult(
                operations
                    .Select(operation => new DiagnosticsOperationSummary(
                        operation.Id,
                        operation.RuntimeStackId,
                        operation.Operation,
                        operation.Status,
                        ToOffset(operation.RequestedAtUtc),
                        ToOffset(operation.StartedAtUtc),
                        ToOffset(operation.CompletedAtUtc)))
                    .ToArray(),
                WarningCode: null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            logger.LogWarning(
                ex,
                "MEM diagnostics could not load correlated runtime-operation summaries.");
            return new OperationReadResult(
                Array.Empty<DiagnosticsOperationSummary>(),
                "diagnostics.operation_summary_unavailable");
        }
    }

    private static void EnsureIncidentExists(IReadOnlyCollection<MemDiagnosticEvent> events)
    {
        if (events.Count == 0)
        {
            ThrowIncidentNotFound();
        }
    }

    private static void ThrowIncidentNotFound() =>
        throw new MemProblemException(
            StatusCodes.Status404NotFound,
            "diagnostics_incident_not_found",
            "Diagnostic incident not found",
            "The diagnostic incident has expired or does not exist.",
            feature: "diagnostics");

    private static IReadOnlyList<string> MergeWarnings(
        IReadOnlyList<string> source,
        params string?[] additional)
    {
        var warnings = new HashSet<string>(source, StringComparer.Ordinal);
        foreach (var warning in additional)
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                warnings.Add(warning);
            }
        }

        return warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    private sealed record OperationReadResult(
        IReadOnlyList<DiagnosticsOperationSummary> Operations,
        string? WarningCode);

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue ? ToOffset(value.Value) : null;
}
