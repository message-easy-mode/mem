using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Core.Cutover;

public sealed record SourceRestorationOptions
{
    public AssessmentOptions Assessment { get; init; } = new();
    public string WorkspacePath { get; init; } = ".workspace";
    public string OutputPath { get; init; } = ".";
    public string FreezeReportPath { get; init; } = string.Empty;
    public string SourceHandoffPath { get; init; } = string.Empty;
    public string ExpectedHandoffSha256 { get; init; } = string.Empty;
    public string? RestorationAttemptId { get; init; }
    public int ReadinessTimeoutSeconds { get; init; } = 180;
    public bool DevelopmentExternalControlPlaneReady { get; init; }
    public bool Resume { get; init; }
    public bool JsonConsoleOutput { get; init; }

    public SourceRestorationOptions Normalize()
    {
        if (string.IsNullOrWhiteSpace(FreezeReportPath))
        {
            throw new ArgumentException("Source-freeze report path is required.");
        }

        if (string.IsNullOrWhiteSpace(SourceHandoffPath))
        {
            throw new ArgumentException("Source-restoration handoff path is required.");
        }

        if (!IsSha256(ExpectedHandoffSha256))
        {
            throw new ArgumentException(
                "Expected source-restoration handoff hash must be a 64-character SHA-256 value.");
        }

        if (ReadinessTimeoutSeconds is < 10 or > 900)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ReadinessTimeoutSeconds),
                "Source readiness timeout must be between 10 and 900 seconds.");
        }

        var attemptId = string.IsNullOrWhiteSpace(RestorationAttemptId)
            ? $"mm01cb-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..43]
            : RestorationAttemptId.Trim();
        if (!IsIdentifier(attemptId, 8, 96))
        {
            throw new ArgumentException(
                "Source restoration attempt ID must contain 8-96 ASCII letters, digits, '-' or '_'.");
        }

        if (Resume && string.IsNullOrWhiteSpace(RestorationAttemptId))
        {
            throw new ArgumentException(
                "--resume requires an explicit --restoration-attempt-id.");
        }

        var workspace = Path.GetFullPath(WorkspacePath);
        var output = Path.GetFullPath(OutputPath);
        var assessment = (Assessment with
        {
            WorkspacePath = Path.Combine(
                workspace,
                "source-restorations",
                attemptId,
                "source-verification-work"),
            OutputPath = Path.Combine(
                output,
                attemptId,
                "source-verification"),
            IncludeSensitivePaths = false,
            JsonConsoleOutput = false,
            NonInteractive = true
        }).Normalize();

        return this with
        {
            Assessment = assessment,
            WorkspacePath = workspace,
            OutputPath = output,
            FreezeReportPath = Path.GetFullPath(FreezeReportPath),
            SourceHandoffPath = Path.GetFullPath(SourceHandoffPath),
            ExpectedHandoffSha256 = ExpectedHandoffSha256.ToLowerInvariant(),
            RestorationAttemptId = attemptId
        };
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsIdentifier(string value, int minimum, int maximum) =>
        value.Length >= minimum &&
        value.Length <= maximum &&
        value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}

public sealed record SourceRestorationRouteEvidence(
    string ServiceKey,
    string PublicHost,
    string RestoredState,
    int? RouteId,
    string? ForwardScheme,
    string? ForwardHost,
    int? ForwardPort,
    bool? Enabled,
    string SnapshotSha256);

public sealed record SourceRestorationTargetContainerEvidence(
    string ServiceKey,
    string ContainerName,
    string? ContainerId,
    bool Stopped);

public sealed record SourceRestorationHandoffPayload(
    string HandoffId,
    DateTime CreatedAtUtc,
    string MigrationId,
    string AdoptionPlanId,
    string CutoverExecutionId,
    string TargetRollbackExecutionId,
    string PlanSha256,
    string PackageRevisionId,
    string PackageRevisionSha256,
    string SourceMigrationId,
    string SourceId,
    string SourceFingerprint,
    string SourceStackSlug,
    string MatrixServerName,
    bool SourceFrozen,
    bool MigrationAccepted,
    bool TargetRoutesRestored,
    bool TargetRuntimeRoutesRemoved,
    bool TargetContainersStopped,
    IReadOnlyList<SourceRestorationRouteEvidence> Routes,
    IReadOnlyList<SourceRestorationTargetContainerEvidence> TargetContainers);

public sealed record SourceRestorationHandoffEnvelope(
    string SchemaVersion,
    string PayloadSha256,
    SourceRestorationHandoffPayload Payload);

public sealed record SourceRestorationContainerResult(
    string Role,
    string ContainerId,
    string ContainerName,
    string ImageId,
    string OriginalRestartPolicy,
    bool WasRunning,
    string FinalState,
    string FinalRestartPolicy,
    string? FinalHealth,
    bool RunningStateRestored,
    bool RestartPolicyRestored,
    bool ServiceVerified);

public sealed record SourceRestorationReport(
    string Schema,
    int SchemaVersion,
    string Status,
    string RestorationAttemptId,
    string SourceHandoffId,
    string SourceHandoffSha256,
    string MigrationId,
    string SourceMigrationId,
    string TargetRollbackExecutionId,
    string FreezeAttemptId,
    string FreezePlanId,
    string FreezePlanHash,
    string SourceFingerprint,
    string SourceStackSlug,
    string MatrixServerName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    bool SourceRestored,
    bool RestartPoliciesRestored,
    bool OriginalRunningStatesRestored,
    bool MatrixVerified,
    bool ElementVerified,
    bool TargetRollbackAuthorityVerified,
    bool DevelopmentExternalControlPlane,
    SourceRestorationContainerResult[] Containers,
    SourceRestorationRouteEvidence[] TargetRouteEvidence,
    string CompletionEvidencePath,
    string CompletionEvidenceSha256,
    string JsonPath,
    string MarkdownPath,
    string[] Warnings,
    string[] NextSteps);


public sealed record SourceRestorationCompletionPayload(
    string RestorationAttemptId,
    DateTime CompletedAtUtc,
    string SourceHandoffId,
    string SourceHandoffSha256,
    string MigrationId,
    string SourceMigrationId,
    string TargetRollbackExecutionId,
    string FreezeAttemptId,
    string FreezePlanId,
    string FreezePlanHash,
    string SourceFingerprint,
    string SourceStackSlug,
    string MatrixServerName,
    bool SourceRestored,
    bool RestartPoliciesRestored,
    bool OriginalRunningStatesRestored,
    bool MatrixVerified,
    bool ElementVerified,
    bool TargetRollbackAuthorityVerified,
    bool DevelopmentExternalControlPlane,
    IReadOnlyList<SourceRestorationContainerResult> Containers,
    IReadOnlyList<SourceRestorationRouteEvidence> TargetRouteEvidence);

public sealed record SourceRestorationCompletionEnvelope(
    string SchemaVersion,
    string PayloadSha256,
    SourceRestorationCompletionPayload Payload);

public sealed record StoredSourceRestorationRun(
    string RestorationAttemptId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string SourceHandoffId,
    string SourceHandoffSha256,
    string FreezeAttemptId,
    string? ReportJson,
    string? ErrorCode,
    string? ErrorMessage);

public interface ISourceRestorationJournal
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<StoredSourceRestorationRun?> GetAsync(
        string restorationAttemptId,
        CancellationToken cancellationToken);

    Task StartAsync(
        string restorationAttemptId,
        DateTimeOffset startedAtUtc,
        string sourceHandoffId,
        string sourceHandoffSha256,
        string freezeAttemptId,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        SourceRestorationReport report,
        string reportJson,
        CancellationToken cancellationToken);

    Task FailAsync(
        string restorationAttemptId,
        DateTimeOffset completedAtUtc,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);
}

public interface ICutoverSourceRestorer
{
    Task<SourceRestorationContainerResult[]> RestoreAsync(
        CutoverRollbackCheckpoint checkpoint,
        IReadOnlyList<CutoverFreezeContainerResult> frozenContainers,
        string dockerCommand,
        int commandTimeoutSeconds,
        int readinessTimeoutSeconds,
        CancellationToken cancellationToken);
}
