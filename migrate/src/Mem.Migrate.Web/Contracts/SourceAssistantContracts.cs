using Mem.Migrate.Application.Assessment;

namespace Mem.Migrate.Web.Contracts;

internal sealed record AccessLoginRequest(string? AccessCode);

internal sealed record AccessSessionResponse(
    int SchemaVersion,
    bool Authenticated,
    string? CsrfToken,
    DateTimeOffset? ExpiresAtUtc);

internal sealed record HealthResponse(
    int SchemaVersion,
    string Status,
    string Application,
    string Version,
    DateTimeOffset StartedAtUtc);

internal sealed record HostStatusResponse(
    int SchemaVersion,
    string Status,
    string Listener,
    bool LoopbackOnly,
    bool RemoteAccessAcknowledged,
    int SessionIdleMinutes,
    int SessionAbsoluteMinutes,
    bool SourceOperationsAvailable);

internal sealed record SourceAssessmentEnvelope(
    int SchemaVersion,
    bool Available,
    bool Running,
    string? SelectedSourceStackId,
    SourceAssessmentView? Assessment);

internal sealed record SourceRequestImportRequest(
    Mem.Migrate.Application.Workflow.SecureIntakeRequestInput Request,
    bool EncryptionReadinessAcknowledged);

internal sealed record SourceWorkflowEnvelope(
    int SchemaVersion,
    bool Available,
    bool Running,
    Mem.Migrate.Application.Workflow.SourceWorkflowView? Workflow);

internal sealed record SourceWorkflowOverviewResponse(
    int SchemaVersion,
    string SourceStackId,
    bool OperationRunning,
    bool CanStartNewWorkflow,
    string? StartNewBlockedReason,
    Mem.Migrate.Application.Workflow.SourceWorkflowView? CurrentWorkflow,
    IReadOnlyList<Mem.Migrate.Application.Workflow.SourceWorkflowView> PreviousWorkflows);
