namespace Infrastructure.Data.Entities.Identity;

/// <summary>
/// A deliberately structured, redaction-safe audit record for control-plane
/// identity and security actions. The model intentionally has no free-form
/// request/body/secret payload column.
/// </summary>
public sealed class MemOperatorAuditEventEntity
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }

    public string EventType { get; set; } = default!;

    public string Outcome { get; set; } = default!;

    public Guid? ActorOperatorId { get; set; }

    public Guid? SubjectOperatorId { get; set; }

    public string? CorrelationId { get; set; }

    public string? ReasonCode { get; set; }
}
