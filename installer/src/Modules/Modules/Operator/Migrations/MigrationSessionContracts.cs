namespace Modules.Operator.Migrations;

/// <summary>
/// Read-only operator projection for one migration lifecycle. The current
/// persistence remains MigrationIntakeEntity; these contracts deliberately
/// avoid exposing host paths, protected age identities, or raw manifests.
/// </summary>
public sealed record MigrationSessionSummaryDto(
    string MigrationId,
    string DisplayName,
    string SourceAdapter,
    string SourceDisplay,
    string Phase,
    string Status,
    string NextAction,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int BlockerCount,
    int WarningCount,
    int AdvisoryCount,
    bool NeedsAttention,
    int SourceCount,
    int StackCount,
    MigrationSessionHistoricalCompatibilityDto HistoricalCompatibility);

public sealed record MigrationSessionDetailDto(
    MigrationSessionSummaryDto Session,
    MigrationSessionPackageDto? Package,
    IReadOnlyList<MigrationSessionPackageRevisionDto> PackageRevisions,
    IReadOnlyList<MigrationSessionSourceDto> Sources,
    IReadOnlyList<MigrationSessionFindingDto> Findings,
    IReadOnlyList<MigrationSessionLinkedObjectDto> LinkedObjects);

public sealed record MigrationSessionPackageDto(
    string TransferMode,
    string Status,
    string? FileName,
    long? SizeBytes,
    string? EncryptedSha256,
    string? DecryptedSha256,
    DateTime? UploadedAtUtc,
    DateTime? ValidatedAtUtc,
    DateTime? ExpiresAtUtc,
    string? AgeRecipient,
    string? RecipientFingerprint,
    string? ArchiveMigrationId,
    string? ArchiveSourceProduct,
    string? ArchiveSourceVersion,
    int? ArchiveStackCount);

public sealed record MigrationSessionPackageRevisionDto(
    string PackageRevisionId,
    int RevisionNumber,
    string Purpose,
    string Status,
    string RetentionState,
    bool Active,
    string TransferMode,
    string? FileName,
    long? SizeBytes,
    string? EncryptedSha256,
    string? DecryptedSha256,
    DateTime CreatedAtUtc,
    DateTime? UploadedAtUtc,
    DateTime? ValidatedAtUtc,
    DateTime? ExpiresAtUtc,
    DateTime? SupersededAtUtc,
    DateTime? RetiredAtUtc,
    string? AgeRecipient,
    string? RecipientFingerprint,
    string? ArchiveMigrationId,
    string? ArchiveSourceProduct,
    string? ArchiveSourceVersion,
    int? ArchiveStackCount,
    string? CaptureKind,
    bool? SourceFrozen,
    bool? RehearsalOnly,
    int? VerifiedFileCount,
    long? VerifiedExpandedBytes,
    string? ValidationCode,
    string? ValidationSummary);

public sealed record MigrationSessionSourceDto(
    string SourceId,
    string Kind,
    string Product,
    string? ProductVersion,
    string? SourceFingerprint,
    DateTime? CapturedAtUtc);

public sealed record MigrationSessionFindingDto(
    string Severity,
    string Code,
    string Message,
    string? ArtifactId,
    DateTime CreatedAtUtc);

public sealed record MigrationSessionLinkedObjectDto(
    string Kind,
    string Id,
    string DisplayName,
    string Status,
    string Relationship,
    bool Historical);

public sealed record MigrationSessionProblemResponse(
    string Code,
    string Message);

public sealed record MigrationSessionHistoricalCompatibilityDto(
    bool UsesLegacyNeutralImportContract,
    string? LegacyContractVersion,
    string? LegacyContractStatus,
    string? LegacyManifestSha256,
    bool UsesCatalogRestorePath,
    int CatalogEntryCount,
    int RestoreSessionCount);
