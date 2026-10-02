using System.Text.Json.Serialization;

namespace Mem.Migrate.Core.Capture;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceWorkflowStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SourceWorkflowStage
{
    RequestValidated,
    CaptureSelected,
    Capturing,
    CaptureReady,
    Packaging,
    ReadyForDownload,
    PackageDeleted,
    RecoveryRequired
}

public sealed record SecureIntakeRequest(
    string Schema,
    int SchemaVersion,
    string IntakeId,
    string? PackageRevisionId,
    string RequestKind,
    string AgeRecipient,
    string RecipientFingerprint,
    DateTimeOffset ExpiresAtUtc,
    string TargetControlPlaneVersion,
    string? SourceStackId = null);

public sealed record SourceWorkflowRecord(
    string WorkflowId,
    SourceWorkflowStatus Status,
    SourceWorkflowStage Stage,
    string AssessmentId,
    string SourceFingerprint,
    string SelectedSourceStackId,
    string IntakeId,
    string? PackageRevisionId,
    string RequestKind,
    string AgeRecipient,
    string RecipientFingerprint,
    DateTimeOffset RequestExpiresAtUtc,
    string TargetControlPlaneVersion,
    DateTimeOffset EncryptionReadinessAcknowledgedAtUtc,
    string? CaptureId,
    string? PackageReportJson,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? CancellationRequestedAtUtc,
    string? FailureCode,
    string? FailureSummary,
    long Revision,
    string? StableSourceIdentity = null);

public sealed record EligibleSourceCapture(
    string CaptureId,
    Guid SourceStackId,
    string SourceStackSlug,
    string MatrixServerName,
    DateTimeOffset CompletedAtUtc,
    string ArchivePath,
    long ArchiveBytes,
    string ArchiveSha256,
    string SourceFingerprint,
    bool RehearsalOnly,
    string? StableSourceIdentity = null);

public interface ISourceWorkflowJournal
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<SourceWorkflowRecord?> GetLatestAsync(
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceWorkflowRecord>> ListRecentAsync(
        string selectedSourceStackId,
        int limit,
        CancellationToken cancellationToken);

    Task<SourceWorkflowRecord?> GetAsync(
        string workflowId,
        CancellationToken cancellationToken);

    Task SaveAsync(
        SourceWorkflowRecord workflow,
        CancellationToken cancellationToken);
}

public interface ISourceCaptureArchiveDiscovery
{
    Task<IReadOnlyList<EligibleSourceCapture>> ListEligibleArchivesAsync(
        CancellationToken cancellationToken,
        bool requireFinalFrozen = false);

    Task<EligibleSourceCapture> ResolveEligibleArchiveAsync(
        string captureId,
        CancellationToken cancellationToken,
        bool requireFinalFrozen = false);

    Task<string> ResolveSingleEligibleArchiveAsync(
        CancellationToken cancellationToken,
        bool requireFinalFrozen = false);
}

public interface IPackageForIntakeService
{
    Task<PackageForIntakeReport> PackageAsync(
        PackageForIntakeOptions options,
        CancellationToken cancellationToken);
}
