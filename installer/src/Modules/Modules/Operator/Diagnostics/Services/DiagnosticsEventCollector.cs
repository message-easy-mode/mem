using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public sealed record DiagnosticsCollectedEvents(
    IReadOnlyList<MemDiagnosticEvent> Events,
    bool Truncated,
    IReadOnlyList<string> Warnings);

public sealed class DiagnosticsEventCollector(
    IMemDiagnosticEventReader reader,
    DiagnosticsApiOptions options,
    TimeProvider timeProvider)
{
    public Task<DiagnosticsCollectedEvents> CollectOverviewAsync(
        CancellationToken cancellationToken) =>
        CollectPagedAsync(
            new MemDiagnosticQuery(
                FromUtc: timeProvider.GetUtcNow().AddHours(-24),
                UntilUtc: timeProvider.GetUtcNow(),
                PageSize: options.MaximumPageSize),
            options.OverviewMaximumEvents,
            cancellationToken);

    public Task<DiagnosticsCollectedEvents> CollectIncidentAsync(
        string incidentId,
        int maximumEvents,
        CancellationToken cancellationToken) =>
        CollectAcrossRetentionAsync(
            new MemDiagnosticQuery(
                IncidentId: incidentId,
                PageSize: options.MaximumPageSize),
            maximumEvents,
            cancellationToken);

    public Task<DiagnosticsCollectedEvents> CollectEventAsync(
        string eventId,
        CancellationToken cancellationToken) =>
        CollectAcrossRetentionAsync(
            new MemDiagnosticQuery(
                EventId: eventId,
                PageSize: 1),
            maximumEvents: 1,
            cancellationToken);

    private async Task<DiagnosticsCollectedEvents> CollectAcrossRetentionAsync(
        MemDiagnosticQuery template,
        int maximumEvents,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var earliest = now.AddDays(-options.RetentionDays);
        var until = now;
        var events = new List<MemDiagnosticEvent>(Math.Min(maximumEvents, 256));
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var truncated = false;

        while (until > earliest && events.Count < maximumEvents)
        {
            var from = until.AddHours(-options.MaximumQueryWindowHours);
            if (from < earliest)
            {
                from = earliest;
            }

            var remaining = maximumEvents - events.Count;
            var result = await CollectPagedAsync(
                template with
                {
                    FromUtc = from,
                    UntilUtc = until,
                    PageSize = Math.Min(options.MaximumPageSize, remaining)
                },
                remaining,
                cancellationToken);

            events.AddRange(result.Events);
            warnings.UnionWith(result.Warnings);
            truncated |= result.Truncated;

            if (events.Count >= maximumEvents ||
                (template.EventId is not null && events.Count > 0))
            {
                break;
            }

            until = from;
        }

        var distinctEvents = events
            .GroupBy(@event => @event.EventId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderByDescending(@event => @event.TimestampUtc)
            .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
            .Take(maximumEvents)
            .ToArray();

        return new DiagnosticsCollectedEvents(
            distinctEvents,
            truncated || events.Count >= maximumEvents,
            warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private async Task<DiagnosticsCollectedEvents> CollectPagedAsync(
        MemDiagnosticQuery initial,
        int maximumEvents,
        CancellationToken cancellationToken)
    {
        var events = new List<MemDiagnosticEvent>(Math.Min(maximumEvents, 256));
        var warnings = new HashSet<string>(StringComparer.Ordinal);
        var query = initial;
        var truncated = false;

        while (events.Count < maximumEvents)
        {
            var remaining = maximumEvents - events.Count;
            var page = await reader.QueryAsync(
                query with
                {
                    PageSize = Math.Min(
                        query.PageSize ?? options.MaximumPageSize,
                        Math.Min(options.MaximumPageSize, remaining))
                },
                cancellationToken);

            events.AddRange(page.Events.Take(remaining));
            warnings.UnionWith(page.Warnings);

            if (string.IsNullOrWhiteSpace(page.NextCursor))
            {
                break;
            }

            if (events.Count >= maximumEvents)
            {
                truncated = true;
                break;
            }

            query = query with { Cursor = page.NextCursor };
        }

        return new DiagnosticsCollectedEvents(
            events,
            truncated,
            warnings.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }
}
