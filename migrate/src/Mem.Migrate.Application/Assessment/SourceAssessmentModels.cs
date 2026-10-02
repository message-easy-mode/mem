namespace Mem.Migrate.Application.Assessment;

public sealed record SourceAssessmentView(
    int SchemaVersion,
    string AssessmentId,
    DateTimeOffset CompletedAtUtc,
    string Classification,
    string Recommendation,
    bool CanProceedToCapture,
    string SourceFingerprint,
    SourceHostSummary Host,
    SourceRuntimeSummary Runtime,
    SourceAssessmentCounts Counts,
    IReadOnlyList<SourceStackSummary> Stacks,
    IReadOnlyList<SourceFindingSummary> Findings);

public sealed record SourceHostSummary(
    string OperatingSystem,
    string Architecture,
    bool IsLinux);

public sealed record SourceRuntimeSummary(
    bool DockerAvailable,
    string? DockerVersion,
    bool SystemConfigReachable,
    string? ProductName,
    string? ProductVersion,
    bool DatabaseProbeAttempted);

public sealed record SourceAssessmentCounts(
    int DockerContainers,
    int DatabaseCandidates,
    int ExactSupportedDatabases,
    int StackFileSets,
    int Stacks,
    int Blockers,
    int Warnings);

public sealed record SourceStackSummary(
    string SourceStackId,
    string Slug,
    string Name,
    string? MatrixServerName,
    string? MatrixPublicHost,
    string? ElementPublicHost,
    bool HomeserverConfigurationReady,
    bool MatrixDatabaseReady,
    bool SigningKeyReady,
    bool MediaStoreReady,
    long MediaBytes,
    int Blockers,
    int Warnings,
    bool SourceFilesReady);

public sealed record SourceFindingSummary(
    string Code,
    string Severity,
    string Message,
    string? Remediation);

public sealed record SourceAssessmentReport(
    string AssessmentId,
    DateTimeOffset CompletedAtUtc,
    string FileName,
    string Markdown);

public sealed record SourcePreflightResult(
    int SchemaVersion,
    string Status,
    bool CanRunAssessment,
    IReadOnlyList<SourcePreflightCheck> Checks);

public sealed record SourcePreflightCheck(
    string Code,
    string Status,
    string Summary,
    string? Detail = null);
