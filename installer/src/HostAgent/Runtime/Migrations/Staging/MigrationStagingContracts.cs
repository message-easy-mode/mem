namespace HostAgent.Runtime.Migrations.Staging;

public sealed record StartMigrationStagingRequest(
    string? TargetStackSlug = null,
    string? RetryOfStagingRunId = null);

public sealed record MigrationStagingRunDto(
    string StagingRunId,
    string MigrationId,
    string CandidateArtifactId,
    string? RetryOfStagingRunId,
    string Status,
    string CurrentStep,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? DestroyedAtUtc,
    bool PrivateOnly,
    bool PublicRoutesCreated,
    bool DatabaseImportSucceeded,
    bool SynapseHealthPassed,
    bool ElementConfigPresent,
    string? ElementConfigSha256,
    bool ElementContainerStarted,
    bool ElementHealthPassed,
    bool ElementSynapseConnectivityPassed,
    bool ElementNetworkAttached,
    string? ElementImageReference,
    string? SynapseImageReference,
    long? UsersCount,
    long? RoomsCount,
    long? EventsCount,
    string? MatrixServerName,
    string? FailureCode,
    string? FailureSummary,
    bool RetirementReviewAvailable);
