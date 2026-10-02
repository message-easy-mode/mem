using Shared.Diagnostics;

namespace HostAgent.Runtime.Migrations.ProductionAdoption;

internal static class MigrationProductionDiagnosticEvents
{
    public static Task<MemDiagnosticWriteResult?> RecordAsync(
        IMemDiagnosticEventWriter? diagnostics,
        string migrationId,
        string eventCode,
        string severity,
        string stage,
        string message,
        string? targetStackSlug = null,
        bool createIncident = false,
        Exception? exception = null,
        string? service = null,
        IReadOnlyDictionary<string, string?>? expected = null,
        IReadOnlyDictionary<string, string?>? observed = null,
        IReadOnlyDictionary<string, string?>? details = null,
        bool retryable = false,
        string? suggestedAction = null) =>
        diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: severity,
            EventCode: eventCode,
            Source: "host-agent.migration-production",
            Feature: "migration",
            Stage: stage,
            Message: message,
            CreateIncident: createIncident,
            Resource: new MemDiagnosticResource(
                Kind: "migration",
                Id: migrationId,
                DisplayName: "Migration session",
                StackSlug: targetStackSlug,
                Service: service,
                WorkspacePath: $"/migrations/{Uri.EscapeDataString(migrationId)}"),
            Expected: expected,
            Observed: observed,
            Details: details,
            Exception: exception,
            SuggestedAction: suggestedAction,
            Retryable: retryable));
}
