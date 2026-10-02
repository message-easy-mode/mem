namespace Infrastructure.Data.Entities.Migrations;

/// <summary>
/// Durable Migration-owned production adoption state. It begins as a read-only ownership plan,
/// then records private normal-runtime materialisation and the controlled public-route transition
/// while acceptance and coordinated source rollback remain separate lifecycle boundaries.
/// </summary>
public sealed class MigrationProductionAdoptionEntity
{
    public Guid Id { get; set; }
    public string AdoptionPlanId { get; set; } = default!;

    public Guid MigrationIntakeEntityId { get; set; }
    public MigrationIntakeEntity MigrationIntake { get; set; } = default!;

    public Guid MigrationPackageRevisionEntityId { get; set; }
    public MigrationPackageRevisionEntity PackageRevision { get; set; } = default!;

    public Guid MigrationCandidateArtifactEntityId { get; set; }
    public MigrationCandidateArtifactEntity CandidateArtifact { get; set; } = default!;

    public Guid MigrationStagingRunEntityId { get; set; }
    public MigrationStagingRunEntity StagingRun { get; set; } = default!;

    public string Status { get; set; } = default!;
    public int RevisionNumber { get; set; }
    public string PlanSha256 { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime PreparedAtUtc { get; set; }

    public Guid RuntimeStackId { get; set; }
    public string TargetStackSlug { get; set; } = default!;
    public string TargetDisplayName { get; set; } = default!;
    public Guid MatrixInstanceId { get; set; }
    public Guid ElementInstanceId { get; set; }

    public string MatrixServerName { get; set; } = default!;
    public string MatrixPublicHost { get; set; } = default!;
    public string MatrixPublicBaseUrl { get; set; } = default!;
    public string ElementPublicHost { get; set; } = default!;
    public string ElementPublicBaseUrl { get; set; } = default!;

    public string RuntimeNetworkName { get; set; } = default!;
    public string RuntimeDataRoot { get; set; } = default!;
    public string ManifestPath { get; set; } = default!;
    public string MatrixContainerName { get; set; } = default!;
    public string MatrixDataPath { get; set; } = default!;
    public string ElementContainerName { get; set; } = default!;
    public string ElementDataPath { get; set; } = default!;

    public string MatrixImageReference { get; set; } = default!;
    public string MatrixImageId { get; set; } = default!;
    public string ElementImageReference { get; set; } = default!;
    public string ElementImageId { get; set; } = default!;

    public string DatabaseEngine { get; set; } = default!;
    public string DatabaseHost { get; set; } = default!;
    public int DatabasePort { get; set; }
    public string DatabaseName { get; set; } = default!;
    public string DatabaseUsername { get; set; } = default!;
    public string DatabasePasswordSecretKind { get; set; } = default!;

    public long? ExpectedUsersCount { get; set; }
    public long? ExpectedRoomsCount { get; set; }
    public long? ExpectedEventsCount { get; set; }

    public string RoutePlanJson { get; set; } = default!;
    public string ProvenanceJson { get; set; } = default!;
    public string CollisionEvidenceJson { get; set; } = default!;
    public string? BlockerSummary { get; set; }

    public string? MaterializationId { get; set; }
    public string? MaterializationStatus { get; set; }
    public DateTime? MaterializationStartedAtUtc { get; set; }
    public DateTime? MaterializationCompletedAtUtc { get; set; }
    public bool ProductionDatabaseImported { get; set; }
    public bool MatrixProductionContainerStarted { get; set; }
    public bool MatrixProductionHealthPassed { get; set; }
    public bool ElementProductionContainerStarted { get; set; }
    public bool ElementProductionHealthPassed { get; set; }
    public bool RuntimeManifestSaved { get; set; }
    public bool DatabaseOwnershipSaved { get; set; }
    public bool RuntimeRecordsCreated { get; set; }
    public bool UserInventorySynchronized { get; set; }
    public string? MaterializationEvidenceJson { get; set; }
    public string? MaterializationFailureCode { get; set; }
    public string? MaterializationFailureSummary { get; set; }

    public string? CutoverPreviewId { get; set; }
    public string? CutoverPreviewStatus { get; set; }
    public DateTime? CutoverPreviewCreatedAtUtc { get; set; }
    public DateTime? CutoverPreviewExpiresAtUtc { get; set; }
    public string? CutoverPreviewSha256 { get; set; }
    public string? CutoverPreviewJson { get; set; }

    public string? CutoverExecutionId { get; set; }
    public string? CutoverStatus { get; set; }
    public DateTime? CutoverStartedAtUtc { get; set; }
    public DateTime? CutoverCompletedAtUtc { get; set; }
    public DateTime? TargetPublicAtUtc { get; set; }
    public string? MatrixNpmRouteId { get; set; }
    public string? ElementNpmRouteId { get; set; }
    public bool RuntimePromotionCompleted { get; set; }
    public bool PublicRoutesCreated { get; set; }
    public bool RouteCompensationAttempted { get; set; }
    public bool RouteCompensationCompleted { get; set; }
    public string? CutoverEvidenceJson { get; set; }
    public string? CutoverRollbackCheckpointJson { get; set; }
    public string? CutoverFailureCode { get; set; }
    public string? CutoverFailureSummary { get; set; }

    public string? ProductionVerificationId { get; set; }
    public string? ProductionVerificationStatus { get; set; }
    public DateTime? ProductionVerificationStartedAtUtc { get; set; }
    public DateTime? ProductionVerificationCompletedAtUtc { get; set; }
    public DateTime? ProductionVerificationValidUntilUtc { get; set; }
    public int ProductionVerificationCheckCount { get; set; }
    public int ProductionVerificationFailedCheckCount { get; set; }
    public string? ProductionVerificationEvidenceSha256 { get; set; }
    public string? ProductionVerificationEvidenceJson { get; set; }
    public Guid? ProductionVerificationReadinessReportId { get; set; }
    public string? ProductionVerificationFailureCode { get; set; }
    public string? ProductionVerificationFailureSummary { get; set; }

    public string? RollbackPreviewId { get; set; }
    public string? RollbackPreviewStatus { get; set; }
    public DateTime? RollbackPreviewCreatedAtUtc { get; set; }
    public DateTime? RollbackPreviewExpiresAtUtc { get; set; }
    public string? RollbackPreviewSha256 { get; set; }
    public string? RollbackPreviewJson { get; set; }

    public string? RollbackExecutionId { get; set; }
    public string? RollbackStatus { get; set; }
    public DateTime? RollbackStartedAtUtc { get; set; }
    public DateTime? RollbackCompletedAtUtc { get; set; }
    public bool RollbackRoutesRestored { get; set; }
    public bool RollbackRuntimeRoutesRemoved { get; set; }
    public bool RollbackTargetContainersStopped { get; set; }
    public bool RollbackTargetRouteCompensationAttempted { get; set; }
    public bool RollbackTargetRouteCompensationCompleted { get; set; }
    public string? RollbackSourceHandoffId { get; set; }
    public string? RollbackSourceHandoffSha256 { get; set; }
    public string? RollbackSourceHandoffJson { get; set; }
    public string? RollbackEvidenceJson { get; set; }
    public string? RollbackFailureCode { get; set; }
    public string? RollbackFailureSummary { get; set; }

    public string? RollbackCompletionStatus { get; set; }
    public string? RollbackSourceCompletionAttemptId { get; set; }
    public string? RollbackSourceCompletionSha256 { get; set; }
    public string? RollbackSourceCompletionJson { get; set; }
    public DateTime? RollbackSourceCompletionImportedAtUtc { get; set; }
    public DateTime? CoordinatedRollbackCompletedAtUtc { get; set; }
    public bool RollbackSourceRestored { get; set; }
    public bool RollbackRestartPoliciesRestored { get; set; }
    public bool RollbackOriginalRunningStatesRestored { get; set; }
    public bool RollbackMatrixVerified { get; set; }
    public bool RollbackElementVerified { get; set; }
    public bool RollbackTargetAuthorityVerified { get; set; }
    public bool RollbackTargetIntegrityVerified { get; set; }
    public bool RollbackDevelopmentExternalControlPlane { get; set; }
}
