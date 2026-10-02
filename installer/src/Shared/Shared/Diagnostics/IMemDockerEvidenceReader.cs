namespace Shared.Diagnostics;

/// <summary>
/// Reads a bounded, sanitized Docker evidence snapshot for an already recorded
/// MEM diagnostic incident. The caller supplies only server-loaded incident
/// events; browser-provided container names or IDs are never accepted.
/// </summary>
public interface IMemDockerEvidenceReader
{
    Task<MemDockerEvidenceReadResult> ReadForIncidentAsync(
        string incidentId,
        IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
        CancellationToken cancellationToken = default);
}
