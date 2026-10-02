namespace Shared.Diagnostics;

/// <summary>
/// Resolves a server-owned MEM logical resource to its current Docker
/// container identity. Browser-provided Docker names or identifiers are never
/// accepted by this contract.
/// </summary>
public interface IMemDockerResourceLocator
{
    Task<MemDockerResourceLocation?> LocateForIncidentAsync(
        string incidentId,
        IReadOnlyCollection<MemDiagnosticEvent> incidentEvents,
        CancellationToken cancellationToken = default);

    Task<MemDockerResourceLocation?> LocateAsync(
        MemDiagnosticResource resource,
        CancellationToken cancellationToken = default);
}

public sealed record MemDockerResourceLocation(
    MemDiagnosticResource Resource,
    string ContainerId,
    string LogicalName);
