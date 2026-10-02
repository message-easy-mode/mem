using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;

namespace Modules.Auth.Services.Identity;

public sealed record MemOperatorAuditEventWrite(
    string EventType,
    string Outcome,
    Guid? ActorOperatorId = null,
    Guid? SubjectOperatorId = null,
    string? CorrelationId = null,
    string? ReasonCode = null);

public interface IMemOperatorAuditService
{
    Task WriteAsync(
        MemOperatorAuditEventWrite auditEvent,
        CancellationToken ct = default);
}

/// <summary>
/// Persists structured security audit events. Its narrow write shape avoids
/// accidentally accepting request bodies, credentials, headers, TOTP values,
/// recovery codes, private keys, or arbitrary secret-bearing JSON.
/// </summary>
public sealed class MemOperatorAuditService(
    MemDbContext db,
    TimeProvider timeProvider) : IMemOperatorAuditService
{
    private const int EventTypeMaxLength = 200;
    private const int OutcomeMaxLength = 50;
    private const int CorrelationIdMaxLength = 128;
    private const int ReasonCodeMaxLength = 200;

    public async Task WriteAsync(
        MemOperatorAuditEventWrite auditEvent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        var entity = new MemOperatorAuditEventEntity
        {
            Id = Guid.NewGuid(),
            OccurredAtUtc = timeProvider.GetUtcNow(),
            EventType = RequiredValue(auditEvent.EventType, nameof(auditEvent.EventType), EventTypeMaxLength),
            Outcome = RequiredValue(auditEvent.Outcome, nameof(auditEvent.Outcome), OutcomeMaxLength),
            ActorOperatorId = auditEvent.ActorOperatorId,
            SubjectOperatorId = auditEvent.SubjectOperatorId,
            CorrelationId = OptionalValue(auditEvent.CorrelationId, nameof(auditEvent.CorrelationId), CorrelationIdMaxLength),
            ReasonCode = OptionalValue(auditEvent.ReasonCode, nameof(auditEvent.ReasonCode), ReasonCodeMaxLength)
        };

        await db.MemOperatorAuditEvents.AddAsync(entity, ct);
        await db.SaveChangesAsync(ct);
    }

    private static string RequiredValue(string value, string name, int maximumLength)
    {
        var normalized = OptionalValue(value, name, maximumLength);

        return normalized ?? throw new InvalidOperationException($"{name} is required.");
    }

    private static string? OptionalValue(string? value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();

        if (normalized.Length > maximumLength)
        {
            throw new InvalidOperationException($"{name} cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
