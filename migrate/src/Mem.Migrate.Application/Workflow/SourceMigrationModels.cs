using Mem.Migrate.Core.Capture;

namespace Mem.Migrate.Application.Workflow;

public sealed record SecureIntakeRequestInput(
    string? Schema,
    int SchemaVersion,
    string? IntakeId,
    string? PackageRevisionId,
    string? RequestKind,
    string? AgeRecipient,
    string? RecipientFingerprint,
    DateTimeOffset? ExpiresAtUtc,
    string? TargetControlPlaneVersion,
    string? SourceStackId);

public sealed record SourceWorkflowRequestView(
    string IntakeId,
    string? PackageRevisionId,
    string RequestKind,
    string RecipientFingerprint,
    DateTimeOffset ExpiresAtUtc,
    string TargetControlPlaneVersion);

public sealed record SourceCaptureOptionView(
    string CaptureId,
    string SourceStackSlug,
    string MatrixServerName,
    DateTimeOffset CompletedAtUtc,
    long ArchiveBytes,
    string ArchiveSha256,
    string SourceFingerprint,
    string CaptureKind,
    bool EligibleForRequest,
    bool Selected);

public sealed record SourcePackageView(
    string FileName,
    long SizeBytes,
    string Sha256,
    DateTimeOffset CompletedAtUtc,
    string CaptureKind,
    bool SourceFrozen,
    bool RehearsalOnly,
    int StackCount,
    string LocalState,
    bool PackageFileAvailable,
    bool ReportAvailable);

public sealed record SourceWorkflowActions(
    bool CanCreatePreviewCapture,
    bool CanSelectCapture,
    bool CanCreatePackage,
    bool CanCancel,
    bool CanDownloadPackage,
    bool CanDownloadReport,
    bool CanDeletePackage,
    string? BlockedReason);

public sealed record SourceWorkflowView(
    int SchemaVersion,
    string WorkflowId,
    string Status,
    string Stage,
    string AssessmentId,
    string SelectedSourceStackId,
    SourceWorkflowRequestView Request,
    DateTimeOffset EncryptionReadinessAcknowledgedAtUtc,
    string? CaptureId,
    SourcePackageView? Package,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureCode,
    string? FailureSummary,
    SourceWorkflowActions Actions);

public sealed record SourceWorkflowOverviewView(
    int SchemaVersion,
    string SourceStackId,
    bool OperationRunning,
    bool CanStartNewWorkflow,
    string? StartNewBlockedReason,
    SourceWorkflowView? CurrentWorkflow,
    IReadOnlyList<SourceWorkflowView> PreviousWorkflows);

public sealed record SourceWorkflowImportResult(
    SourceWorkflowView Workflow,
    IReadOnlyList<SourceCaptureOptionView> Captures);

public sealed class SourceWorkflowConflictException(string message)
    : InvalidOperationException(message);

public sealed class SourceWorkflowNotFoundException(string message)
    : InvalidOperationException(message);


public sealed record SourcePackageDownloadDescriptor(
    string FullPath,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DateTimeOffset LastModifiedUtc);

public sealed record SourcePackageReportDescriptor(
    string FileName,
    string ContentType,
    byte[] Contents);

public interface ISourcePackageLifecycleService
{
    Task<SourcePackageDownloadDescriptor> GetPackageDownloadAsync(
        string workflowId,
        CancellationToken cancellationToken);

    Task<SourcePackageReportDescriptor> GetPackageReportAsync(
        string workflowId,
        CancellationToken cancellationToken);

    Task<SourceWorkflowView> DeletePackageAsync(
        string workflowId,
        CancellationToken cancellationToken);
}
