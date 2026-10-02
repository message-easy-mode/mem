using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public interface IDiagnosticsIncidentResourceReader
{
    Task<DiagnosticsIncidentResourceEvidence> LoadAsync(
        string incidentId,
        CancellationToken cancellationToken);
}

public sealed record DiagnosticsIncidentResourceEvidence(
    string IncidentId,
    IReadOnlyList<MemDiagnosticEvent> Events);

public sealed class DiagnosticsIncidentResourceReader(
    DiagnosticsIncidentService incidents)
    : IDiagnosticsIncidentResourceReader
{
    public async Task<DiagnosticsIncidentResourceEvidence> LoadAsync(
        string incidentId,
        CancellationToken cancellationToken)
    {
        var result = await incidents.LoadForReportAsync(
            incidentId,
            cancellationToken);
        return new DiagnosticsIncidentResourceEvidence(
            result.Summary.IncidentId,
            result.Events);
    }
}
