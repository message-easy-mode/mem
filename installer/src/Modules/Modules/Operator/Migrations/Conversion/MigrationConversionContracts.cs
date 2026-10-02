namespace Modules.Operator.Migrations.Conversion;

public sealed record StartMigrationConversionRequest(
    string? RetryOfConversionAttemptId = null);

public sealed record MigrationConversionSourceStackDto(
    Guid SourceStackId,
    string Slug,
    string MatrixServerName);

public sealed record MigrationConversionOptionsDto(
    string PackageRevisionId,
    MigrationConversionSourceStackDto BoundSourceStack);

public sealed record MigrationConversionAttemptDto(
    string ConversionAttemptId,
    string MigrationId,
    string Status,
    string CurrentStep,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ResultCode,
    string? FailureCode,
    string? FailureSummary,
    Guid? SourceStackId,
    string? CandidateArtifactId,
    string? CandidateArtifactKind,
    string? CandidateSourcePackageSha256,
    string? CandidateArtifactSha256,
    string? CandidateManifestSha256,
    string? CandidateChecksumsSha256,
    string? CandidateVerificationStatus,
    string? CandidateRetentionState,
    DateTime? CandidateCreatedAtUtc,
    DateTime? CandidateVerifiedAtUtc);

public sealed record MigrationConversionProblemResponse(string Code, string Message);
