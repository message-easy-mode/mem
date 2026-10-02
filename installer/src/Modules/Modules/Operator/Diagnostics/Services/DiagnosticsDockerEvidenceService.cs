using Modules.Operator.Diagnostics.Contracts;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsDockerEvidenceService(
    DiagnosticsIncidentService incidents,
    IMemDockerEvidenceReader reader)
{
    public async Task<DiagnosticsDockerEvidenceResponse> GetAsync(
        string incidentId,
        CancellationToken cancellationToken)
    {
        var loaded = await incidents.LoadForReportAsync(
            incidentId,
            cancellationToken);
        var result = await reader.ReadForIncidentAsync(
            loaded.Summary.IncidentId,
            loaded.Events,
            cancellationToken);

        return Project(result);
    }

    internal static DiagnosticsDockerEvidenceResponse Project(
        MemDockerEvidenceReadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (!result.Available || result.Evidence is null)
        {
            return new DiagnosticsDockerEvidenceResponse(
                Available: false,
                Resource: null,
                ObservedAtUtc: null,
                Container: null,
                LogTail: null,
                WarningCode: result.WarningCode,
                Warnings: result.Warnings);
        }

        var evidence = result.Evidence;
        return new DiagnosticsDockerEvidenceResponse(
            Available: true,
            Resource: DiagnosticsProjection.Resource(
                evidence.Resource,
                includeOwnerFacts: false),
            ObservedAtUtc: evidence.ObservedAtUtc,
            Container: new DiagnosticsDockerEvidenceContainer(
                evidence.Container.LogicalName,
                evidence.Container.ObservedState,
                evidence.Container.ExitCode,
                evidence.Container.Health,
                evidence.Container.StartedAtUtc,
                evidence.Container.FinishedAtUtc,
                evidence.Container.RestartCount,
                evidence.Container.Image),
            LogTail: new DiagnosticsDockerEvidenceLogTail(
                evidence.LogTail.RequestedLines,
                evidence.LogTail.ReturnedLines,
                evidence.LogTail.MaximumCharacters,
                evidence.LogTail.Content,
                evidence.LogTail.Truncated,
                evidence.LogTail.RedactionsApplied),
            WarningCode: result.WarningCode,
            Warnings: result.Warnings
                .Concat(evidence.Warnings)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }
}
