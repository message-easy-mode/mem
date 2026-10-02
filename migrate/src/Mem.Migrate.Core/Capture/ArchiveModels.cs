using System.Text.Json.Serialization;

namespace Mem.Migrate.Core.Capture;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CaptureLifecycleStatus
{
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record MigrationArchiveProducer(
    string Product,
    string Version);

public sealed record MigrationArchiveSource(
    string Product,
    string Version,
    string LegacyMigration,
    string StartFingerprint,
    string CompletionFingerprint);

public sealed record MigrationArchiveCapture(
    string Kind,
    bool SourceFrozen,
    bool RehearsalOnly,
    bool SourceChangedDuringCapture,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string ConsistencyMethod);

public sealed record MigrationArchiveStack(
    Guid SourceStackId,
    Guid MatrixServiceId,
    Guid? ElementServiceId,
    string Slug,
    string DisplayName,
    string MatrixServerName,
    string? MatrixPublicUrl,
    string? ElementPublicUrl,
    string? SynapseImage,
    string? SynapseImageId,
    string SqlitePath,
    string HomeserverConfigurationPath,
    string SigningKeyPath,
    string[] AdditionalConfigurationPaths,
    string? MediaPath,
    string? ElementConfigurationPath);

public sealed record MigrationArchiveLimits(
    long MaximumEntryBytes,
    long MaximumExpandedBytes,
    int MaximumEntries,
    long ExpandedBytes,
    int EntryCount);

public sealed record MigrationArchiveIncludedFile(
    string Path,
    long SizeBytes,
    string Sha256);

public sealed record MigrationArchiveManifest(
    string Schema,
    int SchemaVersion,
    MigrationArchiveProducer Producer,
    string MigrationId,
    DateTimeOffset CreatedAtUtc,
    MigrationArchiveSource Source,
    MigrationArchiveCapture Capture,
    MigrationArchiveStack[] Stacks,
    MigrationArchiveIncludedFile[] IncludedFiles,
    MigrationArchiveLimits Limits);

public sealed record MigrationArchiveChecksum(
    string Path,
    long SizeBytes,
    string Sha256);

public sealed record MigrationArchiveChecksumIndex(
    string Schema,
    int SchemaVersion,
    MigrationArchiveChecksum[] Files);

public sealed record CapturePlanSummary(
    int StackCount,
    long EstimatedExpandedBytes,
    long AvailableBytes,
    long RequiredAvailableBytes,
    int EstimatedEntryCount);

public sealed record CaptureEvidenceReport(
    string Schema,
    int SchemaVersion,
    string CaptureId,
    Guid SourceStackId,
    string SourceStackSlug,
    string MatrixServerName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string StartSourceFingerprint,
    string CompletionSourceFingerprint,
    bool SourceChangedDuringCapture,
    bool SourceFrozen,
    bool RehearsalOnly,
    string ConsistencyMethod,
    CapturePlanSummary Plan,
    string[] Warnings);

public sealed record CaptureReport(
    string Schema,
    int SchemaVersion,
    string CaptureId,
    Guid SourceStackId,
    string SourceStackSlug,
    string MatrixServerName,
    CaptureLifecycleStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string StartSourceFingerprint,
    string CompletionSourceFingerprint,
    bool SourceChangedDuringCapture,
    bool RehearsalOnly,
    string ArchivePath,
    string ArchiveSha256,
    long ArchiveBytes,
    string? EncryptedArchivePath,
    string? EncryptedArchiveSha256,
    long? EncryptedArchiveBytes,
    bool PlaintextArchiveRetained,
    CapturePlanSummary Plan,
    string[] Warnings,
    string[] NextSteps,
    string? StableSourceIdentity = null);

public sealed record MigrationArchiveInspection(
    string InputPath,
    string InputSha256,
    long InputBytes,
    string VerifiedZipSha256,
    long VerifiedZipBytes,
    MigrationArchiveManifest Manifest,
    bool EncryptedInput);

public sealed record MigrationArchiveVerificationFinding(
    string Code,
    string Message);

public sealed record MigrationArchiveVerificationResult(
    bool Valid,
    string InputPath,
    string InputSha256,
    long InputBytes,
    string VerifiedZipSha256,
    long VerifiedZipBytes,
    bool EncryptedInput,
    MigrationArchiveManifest? Manifest,
    int VerifiedFileCount,
    long VerifiedExpandedBytes,
    MigrationArchiveVerificationFinding[] Findings);

public sealed record StoredCaptureRun(
    string CaptureId,
    CaptureLifecycleStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string SourceFingerprint,
    string? ArchivePath,
    string? ArchiveSha256,
    string? ReportJson,
    string? ErrorCode,
    string? ErrorMessage);
