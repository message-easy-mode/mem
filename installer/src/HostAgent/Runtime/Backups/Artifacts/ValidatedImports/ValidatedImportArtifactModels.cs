namespace HostAgent.Runtime.Backups.Artifacts.ValidatedImports;

/// <summary>
/// Safe operator-facing projection of a retained uploaded MEM export. This
/// intentionally avoids absolute host paths and raw archive/configuration
/// contents. Restore attempts and source artifacts remain separate lifecycles.
/// </summary>
public sealed record ValidatedImportArtifactDetailResponse(
    string Source,
    string Status,
    string ValidationId,
    string SourceKind,
    string? UploadedFileName,
    DateTimeOffset? RecordedAtUtc,
    long? ArchiveBytes,
    string ArchiveState,
    ValidatedImportValidationSummary Validation,
    ValidatedImportManifestSummary? Manifest,
    ValidatedImportRetentionSummary Retention,
    IReadOnlyList<string> Warnings,
    string? Detail);

/// <summary>
/// Read-only inventory of validated-import artifacts. The entries intentionally
/// keep the source-artifact lifecycle separate from durable Restore Sessions.
/// A removed archive may be returned only when the caller explicitly requests
/// removed source records.
/// </summary>
public sealed record ValidatedImportArtifactInventoryResponse(
    string Source,
    string Status,
    int TotalImports,
    IReadOnlyList<ValidatedImportArtifactDetailResponse> Imports,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record ValidatedImportValidationSummary(
    string Status,
    string Summary,
    int ZipEntryCount,
    long TotalUncompressedBytes,
    bool ManifestPresent,
    bool ChecksumsPresent,
    int PassedChecks,
    int FailedChecks,
    int WarningCount,
    IReadOnlyList<string> Errors);

/// <summary>
/// Deliberately small manifest projection for the recovery-source UI. It does
/// not expose raw configuration paths, credentials, signing keys, or archive
/// contents.
/// </summary>
public sealed record ValidatedImportManifestSummary(
    int ManifestVersion,
    string? MemVersion,
    string? SourceStackSlug,
    string? SourceStackDisplayName,
    string? MatrixServerName,
    int IncludedFileCount);


public sealed record ValidatedImportRetentionSummary(
    bool CanDelete,
    string? DeleteBlockReason,
    DateTimeOffset? RemovedAtUtc,
    string? RemovedBy);

/// <summary>
/// Result of intentionally removing an uploaded source archive. Validation
/// receipts and restore-session/audit history remain intact.
/// </summary>
public sealed record ValidatedImportArtifactDeleteResponse(
    string Source,
    string Status,
    string ValidationId,
    string ArchiveState,
    long DeletedBytes,
    DateTimeOffset? DeletedAtUtc,
    IReadOnlyList<string> Warnings,
    string? Detail);

/// <summary>
/// Internal archive-state projection retained for legacy advanced-cutover history
/// readers. It describes only the retained upload archive; canonical Restore
/// Workspace actions resolve executable material from the Backup Catalog.
/// </summary>
public sealed record ValidatedImportSourceAvailability(
    string ArchiveState,
    bool CanContinueRestore,
    string? Detail,
    DateTimeOffset? RemovedAtUtc);

/// <summary>
/// Raised only by legacy internal history readers when a retained upload archive
/// has been intentionally removed. Canonical catalog-backed restore actions do
/// not use this exception or the uploaded archive as a source.
/// </summary>
public sealed class UploadedSourceRemovedException : InvalidOperationException
{
    public const string ErrorCode = "uploaded_source_removed";

    public const string OperatorDetail =
        "The retained uploaded ZIP was removed. Upload the ZIP again before continuing that archived workflow.";

    public UploadedSourceRemovedException(string validationId)
        : base(OperatorDetail)
    {
        ValidationId = validationId;
    }

    public string ValidationId { get; }
}
