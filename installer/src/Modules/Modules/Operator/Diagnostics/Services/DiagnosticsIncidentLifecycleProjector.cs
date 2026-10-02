using Infrastructure.Data.Entities.Diagnostics;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public static class DiagnosticsIncidentLifecycleStates
{
    public const string Open = "open";
    public const string Acknowledged = "acknowledged";
    public const string Snoozed = "snoozed";
    public const string Resolved = "resolved";
}

public static class DiagnosticsIncidentResolutionCodes
{
    public const string Fixed = "fixed";
    public const string SelfRecovered = "self_recovered";
    public const string NoLongerRelevant = "no_longer_relevant";
    public const string AcceptedRisk = "accepted_risk";
    public const string Duplicate = "duplicate";
    public const string FalsePositive = "false_positive";

    private static readonly HashSet<string> Known = new(StringComparer.Ordinal)
    {
        Fixed,
        SelfRecovered,
        NoLongerRelevant,
        AcceptedRisk,
        Duplicate,
        FalsePositive
    };

    public static bool IsKnown(string value) => Known.Contains(value);
}

public sealed record DiagnosticsIncidentEventWatermark(
    string IncidentId,
    DateTimeOffset ObservedThroughAtUtc,
    string ObservedThroughEventId);

public sealed record DiagnosticsIncidentLifecycleProjection(
    string IncidentId,
    string State,
    bool Reopened,
    string? StoredDisposition,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByOperatorId,
    DateTimeOffset? ObservedThroughAtUtc,
    string? ObservedThroughEventId,
    DateTimeOffset? SnoozedUntilUtc,
    string? ResolutionCode,
    int? Revision);

/// <summary>
/// Combines immutable incident event watermarks with the durable operator
/// disposition overlay. A newer event always wins over an older acknowledgement,
/// snooze, or resolution so recurring faults cannot remain hidden.
/// </summary>
public sealed class DiagnosticsIncidentLifecycleProjector(
    MemDbContext db,
    TimeProvider timeProvider)
{
    public Task<IReadOnlyDictionary<string, DiagnosticsIncidentLifecycleProjection>> ProjectEventsAsync(
        IReadOnlyCollection<MemDiagnosticEvent> events,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(events);

        var watermarks = events
            .Where(@event => !string.IsNullOrWhiteSpace(@event.IncidentId))
            .GroupBy(@event => @event.IncidentId!, StringComparer.Ordinal)
            .Select(group =>
            {
                var latest = group
                    .OrderByDescending(@event => @event.TimestampUtc)
                    .ThenByDescending(@event => @event.EventId, StringComparer.Ordinal)
                    .First();
                return new DiagnosticsIncidentEventWatermark(
                    group.Key,
                    latest.TimestampUtc,
                    latest.EventId);
            })
            .ToArray();

        return ProjectAsync(watermarks, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, DiagnosticsIncidentLifecycleProjection>> ProjectAsync(
        IReadOnlyCollection<DiagnosticsIncidentEventWatermark> incidents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(incidents);

        if (incidents.Count == 0)
        {
            return new Dictionary<string, DiagnosticsIncidentLifecycleProjection>(
                StringComparer.Ordinal);
        }

        var normalized = incidents
            .Select(NormalizeWatermark)
            .GroupBy(x => x.IncidentId, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(x => x.ObservedThroughAtUtc)
                .ThenByDescending(x => x.ObservedThroughEventId, StringComparer.Ordinal)
                .First())
            .ToArray();
        var incidentIds = normalized
            .Select(x => x.IncidentId)
            .ToArray();

        var dispositions = await db.DiagnosticsIncidentDispositions
            .AsNoTracking()
            .Where(x => incidentIds.Contains(x.IncidentId))
            .ToDictionaryAsync(x => x.IncidentId, StringComparer.Ordinal, cancellationToken);
        var now = timeProvider.GetUtcNow();

        return normalized.ToDictionary(
            watermark => watermark.IncidentId,
            watermark => Project(watermark, dispositions.GetValueOrDefault(watermark.IncidentId), now),
            StringComparer.Ordinal);
    }

    public async Task<DiagnosticsIncidentLifecycleProjection> ProjectAsync(
        DiagnosticsIncidentEventWatermark incident,
        CancellationToken cancellationToken)
    {
        var projected = await ProjectAsync([incident], cancellationToken);
        return projected[NormalizeIncidentId(incident.IncidentId)];
    }

    internal static DiagnosticsIncidentLifecycleProjection Project(
        DiagnosticsIncidentEventWatermark incident,
        DiagnosticsIncidentDispositionEntity? disposition,
        DateTimeOffset now)
    {
        var watermark = NormalizeWatermark(incident);
        if (disposition is null)
        {
            return Open(watermark.IncidentId);
        }

        var recurrence = IsBeyondWatermark(watermark, disposition);
        if (recurrence)
        {
            return new DiagnosticsIncidentLifecycleProjection(
                watermark.IncidentId,
                DiagnosticsIncidentLifecycleStates.Open,
                Reopened: true,
                disposition.Disposition,
                disposition.UpdatedAtUtc,
                disposition.UpdatedByOperatorId == Guid.Empty
                    ? null
                    : disposition.UpdatedByOperatorId,
                disposition.ObservedThroughAtUtc,
                disposition.ObservedThroughEventId,
                disposition.SnoozedUntilUtc,
                disposition.ResolutionCode,
                disposition.Revision);
        }

        var effectiveState = disposition.Disposition switch
        {
            DiagnosticsIncidentLifecycleStates.Acknowledged =>
                DiagnosticsIncidentLifecycleStates.Acknowledged,
            DiagnosticsIncidentLifecycleStates.Snoozed when
                disposition.SnoozedUntilUtc.HasValue &&
                disposition.SnoozedUntilUtc.Value > now =>
                DiagnosticsIncidentLifecycleStates.Snoozed,
            DiagnosticsIncidentLifecycleStates.Snoozed =>
                DiagnosticsIncidentLifecycleStates.Open,
            DiagnosticsIncidentLifecycleStates.Resolved =>
                DiagnosticsIncidentLifecycleStates.Resolved,
            _ => DiagnosticsIncidentLifecycleStates.Open
        };

        return new DiagnosticsIncidentLifecycleProjection(
            watermark.IncidentId,
            effectiveState,
            Reopened: false,
            disposition.Disposition,
            disposition.UpdatedAtUtc,
            disposition.UpdatedByOperatorId == Guid.Empty
                ? null
                : disposition.UpdatedByOperatorId,
            disposition.ObservedThroughAtUtc,
            disposition.ObservedThroughEventId,
            disposition.SnoozedUntilUtc,
            disposition.ResolutionCode,
            disposition.Revision);
    }

    private static DiagnosticsIncidentLifecycleProjection Open(string incidentId) =>
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

    private static bool IsBeyondWatermark(
        DiagnosticsIncidentEventWatermark current,
        DiagnosticsIncidentDispositionEntity stored)
    {
        var timestampComparison = current.ObservedThroughAtUtc.CompareTo(
            stored.ObservedThroughAtUtc);
        if (timestampComparison != 0)
        {
            return timestampComparison > 0;
        }

        return string.CompareOrdinal(
            current.ObservedThroughEventId,
            stored.ObservedThroughEventId) > 0;
    }

    internal static DiagnosticsIncidentEventWatermark NormalizeWatermark(
        DiagnosticsIncidentEventWatermark source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source with
        {
            IncidentId = NormalizeIncidentId(source.IncidentId),
            ObservedThroughEventId = NormalizeEventId(source.ObservedThroughEventId)
        };
    }

    internal static string NormalizeIncidentId(string value) =>
        NormalizeIdentifier(value, "incidentId", "inc_", 80);

    internal static string NormalizeEventId(string value) =>
        NormalizeIdentifier(value, "eventId", "evt_", 80);

    private static string NormalizeIdentifier(
        string value,
        string parameterName,
        string requiredPrefix,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength ||
            !normalized.StartsWith(requiredPrefix, StringComparison.Ordinal) ||
            normalized.Any(character =>
                !char.IsLetterOrDigit(character) &&
                character is not '_' and not '-' and not '.'))
        {
            throw new ArgumentException($"{parameterName} is invalid.", parameterName);
        }

        return normalized;
    }
}
