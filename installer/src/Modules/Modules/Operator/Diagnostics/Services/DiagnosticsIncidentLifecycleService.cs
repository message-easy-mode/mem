using Infrastructure.Data.Entities.Diagnostics;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Services.Identity;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

/// <summary>
/// Persists Diagnostics incident lifecycle dispositions. Operator actions are audited;
/// proven server-owned self-recovery uses the reserved system actor and no operator audit.
/// </summary>
public sealed class DiagnosticsIncidentLifecycleService(
    MemDbContext db,
    IMemOperatorAuditService audit,
    TimeProvider timeProvider) : IMemDiagnosticIncidentLifecycleWriter
{
    private static readonly TimeSpan MaximumSnooze = TimeSpan.FromDays(30);

    public Task AcknowledgeAsync(
        DiagnosticsIncidentEventWatermark observedThrough,
        Guid actorOperatorId,
        string? correlationId,
        CancellationToken cancellationToken) =>
        ApplyAsync(
            observedThrough,
            DiagnosticsIncidentLifecycleStates.Acknowledged,
            actorOperatorId,
            correlationId,
            snoozedUntilUtc: null,
            resolutionCode: null,
            auditEventType: "diagnostics.incident.acknowledged",
            cancellationToken: cancellationToken);

    public Task SnoozeAsync(
        DiagnosticsIncidentEventWatermark observedThrough,
        DateTimeOffset snoozedUntilUtc,
        Guid actorOperatorId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (snoozedUntilUtc <= now || snoozedUntilUtc > now.Add(MaximumSnooze))
        {
            throw new ArgumentOutOfRangeException(
                nameof(snoozedUntilUtc),
                "Diagnostics incidents may be snoozed into the future for at most 30 days.");
        }

        return ApplyAsync(
            observedThrough,
            DiagnosticsIncidentLifecycleStates.Snoozed,
            actorOperatorId,
            correlationId,
            snoozedUntilUtc,
            resolutionCode: null,
            auditEventType: "diagnostics.incident.snoozed",
            cancellationToken: cancellationToken);
    }

    public Task ResolveAsync(
        DiagnosticsIncidentEventWatermark observedThrough,
        string resolutionCode,
        Guid actorOperatorId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var normalizedResolutionCode = NormalizeResolutionCode(resolutionCode);
        return ApplyAsync(
            observedThrough,
            DiagnosticsIncidentLifecycleStates.Resolved,
            actorOperatorId,
            correlationId,
            snoozedUntilUtc: null,
            resolutionCode: normalizedResolutionCode,
            auditEventType: "diagnostics.incident.resolved",
            cancellationToken: cancellationToken);
    }

    public async Task ResolveSelfRecoveredAsync(
        string incidentId,
        DateTimeOffset observedThroughAtUtc,
        string observedThroughEventId,
        CancellationToken cancellationToken = default)
    {
        var watermark = DiagnosticsIncidentLifecycleProjector.NormalizeWatermark(
            new DiagnosticsIncidentEventWatermark(
                incidentId,
                observedThroughAtUtc,
                observedThroughEventId));
        var now = timeProvider.GetUtcNow();
        var entity = await db.DiagnosticsIncidentDispositions
            .SingleOrDefaultAsync(
                x => x.IncidentId == watermark.IncidentId,
                cancellationToken);

        if (entity is null)
        {
            entity = new DiagnosticsIncidentDispositionEntity
            {
                IncidentId = watermark.IncidentId,
                Revision = 1
            };
            db.DiagnosticsIncidentDispositions.Add(entity);
        }
        else
        {
            entity.Revision = checked(entity.Revision + 1);
        }

        entity.Disposition = DiagnosticsIncidentLifecycleStates.Resolved;
        entity.UpdatedAtUtc = now;
        entity.UpdatedByOperatorId = Guid.Empty;
        entity.ObservedThroughAtUtc = watermark.ObservedThroughAtUtc;
        entity.ObservedThroughEventId = watermark.ObservedThroughEventId;
        entity.SnoozedUntilUtc = null;
        entity.ResolutionCode = DiagnosticsIncidentResolutionCodes.SelfRecovered;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> ReopenAsync(
        string incidentId,
        Guid actorOperatorId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        EnsureActor(actorOperatorId);
        var normalizedIncidentId = DiagnosticsIncidentLifecycleProjector.NormalizeIncidentId(
            incidentId);
        var entity = await db.DiagnosticsIncidentDispositions
            .SingleOrDefaultAsync(
                x => x.IncidentId == normalizedIncidentId,
                cancellationToken);
        if (entity is null)
        {
            return false;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.DiagnosticsIncidentDispositions.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(
            "diagnostics.incident.reopened",
            normalizedIncidentId,
            resolutionCode: null,
            actorOperatorId: actorOperatorId,
            correlationId: correlationId,
            cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private async Task ApplyAsync(
        DiagnosticsIncidentEventWatermark observedThrough,
        string disposition,
        Guid actorOperatorId,
        string? correlationId,
        DateTimeOffset? snoozedUntilUtc,
        string? resolutionCode,
        string auditEventType,
        CancellationToken cancellationToken)
    {
        EnsureActor(actorOperatorId);
        var watermark = DiagnosticsIncidentLifecycleProjector.NormalizeWatermark(
            observedThrough);
        var now = timeProvider.GetUtcNow();
        var entity = await db.DiagnosticsIncidentDispositions
            .SingleOrDefaultAsync(
                x => x.IncidentId == watermark.IncidentId,
                cancellationToken);

        if (entity is null)
        {
            entity = new DiagnosticsIncidentDispositionEntity
            {
                IncidentId = watermark.IncidentId,
                Revision = 1
            };
            db.DiagnosticsIncidentDispositions.Add(entity);
        }
        else
        {
            entity.Revision = checked(entity.Revision + 1);
        }

        entity.Disposition = disposition;
        entity.UpdatedAtUtc = now;
        entity.UpdatedByOperatorId = actorOperatorId;
        entity.ObservedThroughAtUtc = watermark.ObservedThroughAtUtc;
        entity.ObservedThroughEventId = watermark.ObservedThroughEventId;
        entity.SnoozedUntilUtc = snoozedUntilUtc;
        entity.ResolutionCode = resolutionCode;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(
            auditEventType,
            watermark.IncidentId,
            resolutionCode,
            actorOperatorId,
            correlationId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task WriteAuditAsync(
        string eventType,
        string incidentId,
        string? resolutionCode,
        Guid actorOperatorId,
        string? correlationId,
        CancellationToken cancellationToken) =>
        audit.WriteAsync(
            new MemOperatorAuditEventWrite(
                EventType: eventType,
                Outcome: "succeeded",
                ActorOperatorId: actorOperatorId,
                CorrelationId: NormalizeCorrelationId(correlationId),
                ReasonCode: BuildAuditReason(incidentId, resolutionCode)),
            cancellationToken);

    private static string BuildAuditReason(
        string incidentId,
        string? resolutionCode) =>
        string.IsNullOrWhiteSpace(resolutionCode)
            ? incidentId
            : $"{incidentId}:{resolutionCode}";

    private static string NormalizeResolutionCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("resolutionCode is required.", nameof(value));
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (!DiagnosticsIncidentResolutionCodes.IsKnown(normalized))
        {
            throw new ArgumentException(
                "resolutionCode is not supported.",
                nameof(value));
        }

        return normalized;
    }

    private static string? NormalizeCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
            .Take(128)
            .ToArray());
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static void EnsureActor(Guid actorOperatorId)
    {
        if (actorOperatorId == Guid.Empty)
        {
            throw new ArgumentException(
                "A durable operator identity is required.",
                nameof(actorOperatorId));
        }
    }
}
