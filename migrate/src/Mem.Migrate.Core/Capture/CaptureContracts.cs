using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Core.Capture;

public interface ICaptureJournal
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<StoredCaptureRun?> GetAsync(
        string captureId,
        CancellationToken cancellationToken);

    Task StartAsync(
        string captureId,
        DateTimeOffset startedAtUtc,
        string sourceFingerprint,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        CaptureReport report,
        string reportJson,
        CancellationToken cancellationToken);

    Task FailAsync(
        string captureId,
        DateTimeOffset completedAtUtc,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);
}

public interface ISqliteSnapshotter
{
    Task CreateConsistentSnapshotAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken);
}

public interface IMigrationArchiveWriter
{
    Task<MigrationArchiveWriteResult> WriteAsync(
        string stagingRoot,
        string outputPath,
        ArchiveSafetyLimits limits,
        CancellationToken cancellationToken);
}

public sealed record MigrationArchiveWriteResult(
    string ArchivePath,
    string Sha256,
    long SizeBytes,
    int EntryCount,
    long ExpandedBytes);

public interface IMigrationArchiveReader
{
    Task<MigrationArchiveInspection> InspectAsync(
        string archivePath,
        ArchiveSafetyLimits limits,
        CancellationToken cancellationToken);

    Task<MigrationArchiveVerificationResult> VerifyAsync(
        string archivePath,
        ArchiveSafetyLimits limits,
        CancellationToken cancellationToken);
}

public interface IAgeEnvelope
{
    Task<AgeEnvelopeResult> EncryptAsync(
        string plaintextPath,
        string encryptedPath,
        string recipient,
        string ageCommand,
        CancellationToken cancellationToken);

    Task DecryptAsync(
        string encryptedPath,
        string plaintextPath,
        string identityPath,
        string ageCommand,
        CancellationToken cancellationToken);
}

public sealed record AgeEnvelopeResult(
    string Path,
    string Sha256,
    long SizeBytes);

public interface ISourceCaptureService
{
    Task<CaptureReport> CaptureAsync(
        CaptureOptions options,
        CancellationToken cancellationToken);
}

public sealed record ArchiveSafetyLimits(
    long MaximumEntryBytes,
    long MaximumExpandedBytes,
    int MaximumEntries,
    double MaximumCompressionRatio = 100);
