namespace Infrastructure.Data.Entities.Diagnostics;

/// <summary>
/// Durable lifecycle overlay for one append-only Diagnostics incident. Absence of a row
/// means the incident is open. Operator actions use their real operator id; Guid.Empty is
/// reserved for server-owned self-recovery that has been proven by a later safe event.
/// </summary>
public sealed class DiagnosticsIncidentDispositionEntity
{
    public string IncidentId { get; set; } = default!;

    public string Disposition { get; set; } = default!;

    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// Operator id for human lifecycle changes; Guid.Empty identifies server-owned self-recovery.
    /// </summary>
    public Guid UpdatedByOperatorId { get; set; }

    public DateTimeOffset ObservedThroughAtUtc { get; set; }

    public string ObservedThroughEventId { get; set; } = default!;

    public DateTimeOffset? SnoozedUntilUtc { get; set; }

    public string? ResolutionCode { get; set; }

    public int Revision { get; set; } = 1;
}
