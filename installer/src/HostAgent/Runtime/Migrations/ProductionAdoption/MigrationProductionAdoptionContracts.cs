namespace HostAgent.Runtime.Migrations.ProductionAdoption;

public sealed record PrepareMigrationProductionAdoptionRequest(
    string? TargetStackSlug = null,
    string? ElementPublicHost = null);

public sealed record CreateMigrationPrivateServerRequest(
    string? TargetStackSlug,
    string? ElementPublicHost,
    bool ConfirmVerifiedSnapshotIsAuthoritative,
    bool ConfirmLaterSourceWritesAreNotIncluded,
    bool ConfirmCreatePrivateServer);

public sealed record ReviewMigrationPrivateServerTargetRequest(
    string? TargetStackSlug = null,
    string? ElementPublicHost = null);

public sealed record MigrationPrivateServerTargetCollision(
    string Field,
    string Code,
    string ResourceType,
    string ResourceValue,
    string? Owner,
    string Detail);

public sealed record MigrationPrivateServerTargetReviewResponse(
    string MigrationId,
    string SourceStackSlug,
    string SourceElementPublicHost,
    string TargetStackSlug,
    string MatrixServerName,
    string ElementPublicHost,
    string StackNameStatus,
    string MatrixAddressStatus,
    string ElementAddressStatus,
    bool CollisionFree,
    string? SuggestedTargetStackSlug,
    IReadOnlyList<MigrationPrivateServerTargetCollision> Collisions,
    string Detail);

public sealed record MakeMigrationServerLiveRequest(
    bool ConfirmMovePublicTraffic,
    bool ConfirmStopUsingOldServer,
    bool ConfirmRunLiveVerification);

public sealed record MaterializeMigrationProductionRuntimeRequest(
    string? Operator,
    string? Note,
    bool ExecutePrivateProductionMaterialization,
    bool AcknowledgeCreatesNormalRuntimeRecords,
    bool AcknowledgeMutatesProductionPostgres,
    bool AcknowledgeStartsProductionContainers,
    bool AcknowledgeNoPublicRoutes,
    bool AcknowledgeNoAutomaticRollback);


public sealed record PrepareMigrationProductionCutoverRequest(
    int? PreviewLifetimeMinutes = null);

public sealed record ExecuteMigrationProductionCutoverRequest(
    string? Operator,
    string? Note,
    string PreviewId,
    bool ExecuteNpmRouteMutation,
    bool AcknowledgeSourceFrozen,
    bool AcknowledgePrivateRuntimeHealthy,
    bool AcknowledgeRouteSnapshotReviewed,
    bool AcknowledgeCreatesPublicRoutes,
    bool AcknowledgeNoDnsMutation,
    bool AcknowledgeNoCertificateMutation,
    bool AcknowledgeRollbackIsNextSlice,
    bool AcknowledgePostCutoverVerificationRequired,
    bool AcknowledgeProductionAuthority = false);

public sealed record MigrationProductionCutoverRouteSnapshot(
    string ServiceKey,
    string PublicHost,
    string DesiredForwardHost,
    int DesiredForwardPort,
    bool ExistingRouteFound,
    int? ExistingRouteId,
    string? ExistingForwardScheme,
    string? ExistingForwardHost,
    int? ExistingForwardPort,
    int? ExistingCertificateId,
    bool? ExistingSslForced,
    bool? ExistingHttp2,
    bool? ExistingEnabled,
    string ExistingAdvancedConfigSha256,
    bool AlreadyTargetsProductionRuntime,
    string Action,
    int? SelectedCertificateId = null,
    string? SelectedCertificateRecordId = null,
    string? SelectedCertificateName = null);

public sealed record MigrationProductionCutoverPreviewDto(
    string? PreviewId,
    string Status,
    DateTimeOffset? CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? SnapshotSha256,
    IReadOnlyList<MigrationProductionCutoverRouteSnapshot> Routes,
    IReadOnlyList<string> Blockers);

public sealed record MigrationProductionCutoverExecutionDto(
    string? ExecutionId,
    string Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? TargetPublicAtUtc,
    string? MatrixRouteId,
    string? ElementRouteId,
    bool RuntimePromotionCompleted,
    bool PublicRoutesCreated,
    bool RouteCompensationAttempted,
    bool RouteCompensationCompleted,
    string? FailureCode,
    string? FailureSummary);

public sealed record MigrationProductionCutoverDto(
    MigrationProductionCutoverPreviewDto Preview,
    MigrationProductionCutoverExecutionDto Execution);


public sealed record RunMigrationProductionVerificationRequest(
    string? Operator = null,
    string? Note = null,
    int? FreshnessMinutes = null);

public sealed record MigrationProductionVerificationCheckDto(
    string Code,
    string Name,
    bool Success,
    string Detail,
    string? Url = null,
    int? StatusCode = null);

public sealed record MigrationProductionVerificationDto(
    string? VerificationId,
    string Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? ValidUntilUtc,
    bool Fresh,
    bool Passed,
    int CheckCount,
    int FailedCheckCount,
    string? EvidenceSha256,
    Guid? ReadinessReportId,
    IReadOnlyList<MigrationProductionVerificationCheckDto> Checks,
    string? FailureCode,
    string? FailureSummary);

public sealed record PrepareMigrationProductionRollbackRequest(
    int? PreviewLifetimeMinutes = null);

public sealed record ExecuteMigrationProductionRollbackRequest(
    string? Operator,
    string? Note,
    string PreviewId,
    bool ExecuteTargetRollback,
    bool AcknowledgeMigrationNotAccepted,
    bool AcknowledgeRestoresPreCutoverRoutes,
    bool AcknowledgeStopsTargetContainers,
    bool AcknowledgePreservesTargetData,
    bool AcknowledgeSourceRemainsFrozen,
    bool AcknowledgeSourceRestorationRequiresHandoff,
    bool AcknowledgeNoAutomaticSourceHostMutation);

public sealed record MigrationProductionRollbackRouteState(
    string ServiceKey,
    string PublicHost,
    int? CurrentRouteId,
    string? CurrentForwardScheme,
    string? CurrentForwardHost,
    int? CurrentForwardPort,
    bool? CurrentEnabled,
    string CurrentSnapshotSha256,
    bool MatchesExpectedTarget,
    string RestoreAction);

public sealed record MigrationProductionRollbackPreviewDto(
    string? PreviewId,
    string Status,
    DateTime? CreatedAtUtc,
    DateTime? ExpiresAtUtc,
    string? SnapshotSha256,
    IReadOnlyList<MigrationProductionRollbackRouteState> Routes,
    IReadOnlyList<string> Blockers);

public sealed record MigrationProductionRollbackExecutionDto(
    string? ExecutionId,
    string Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    bool RoutesRestored,
    bool RuntimeRoutesRemoved,
    bool TargetContainersStopped,
    bool TargetRouteCompensationAttempted,
    bool TargetRouteCompensationCompleted,
    string? SourceHandoffId,
    string? SourceHandoffSha256,
    string? FailureCode,
    string? FailureSummary);

public sealed record MigrationProductionRollbackDto(
    MigrationProductionRollbackPreviewDto Preview,
    MigrationProductionRollbackExecutionDto Execution,
    MigrationProductionCoordinatedRollbackDto Completion);

public sealed record MigrationProductionSourceRestorationRouteEvidence(
    string ServiceKey,
    string PublicHost,
    string RestoredState,
    int? RouteId,
    string? ForwardScheme,
    string? ForwardHost,
    int? ForwardPort,
    bool? Enabled,
    string SnapshotSha256);

public sealed record MigrationProductionSourceRestorationContainerEvidence(
    string ServiceKey,
    string ContainerName,
    string? ContainerId,
    bool Stopped);

public sealed record MigrationProductionSourceRestorationHandoffPayload(
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
    IReadOnlyList<MigrationProductionSourceRestorationRouteEvidence> Routes,
    IReadOnlyList<MigrationProductionSourceRestorationContainerEvidence> TargetContainers);

public sealed record MigrationProductionSourceRestorationHandoffEnvelope(
    string SchemaVersion,
    string PayloadSha256,
    MigrationProductionSourceRestorationHandoffPayload Payload);


public sealed record MigrationProductionSourceRestorationCompletionContainerResult(
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

public sealed record MigrationProductionSourceRestorationCompletionPayload(
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
    IReadOnlyList<MigrationProductionSourceRestorationCompletionContainerResult> Containers,
    IReadOnlyList<MigrationProductionSourceRestorationRouteEvidence> TargetRouteEvidence);

public sealed record MigrationProductionSourceRestorationCompletionEnvelope(
    string SchemaVersion,
    string PayloadSha256,
    MigrationProductionSourceRestorationCompletionPayload Payload);

public sealed record MigrationProductionCoordinatedRollbackDto(
    string Status,
    string? RestorationAttemptId,
    string? CompletionSha256,
    DateTime? ImportedAtUtc,
    DateTime? CompletedAtUtc,
    bool SourceRestored,
    bool RestartPoliciesRestored,
    bool OriginalRunningStatesRestored,
    bool MatrixVerified,
    bool ElementVerified,
    bool TargetRollbackAuthorityVerified,
    bool TargetRollbackStillIntact,
    bool DevelopmentExternalControlPlane);

public sealed record MigrationProductionAdoptionCollision(
    string Code,
    string ResourceType,
    string ResourceValue,
    string Detail);

public sealed record MigrationProductionAdoptionRoutePlan(
    string ServiceKey,
    string PublicHost,
    string PublicBaseUrl,
    string ForwardScheme,
    string ForwardHost,
    int ForwardPort,
    string Provider,
    bool PublicMutationDeferred);

public sealed record MigrationProductionMaterializationDto(
    string? MaterializationId,
    string Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    bool ProductionDatabaseImported,
    bool MatrixContainerStarted,
    bool MatrixHealthPassed,
    bool ElementContainerStarted,
    bool ElementHealthPassed,
    bool RuntimeManifestSaved,
    bool DatabaseOwnershipSaved,
    bool RuntimeRecordsCreated,
    bool UserInventorySynchronized,
    bool PublicRoutesCreated,
    string? FailureCode,
    string? FailureSummary);

public sealed record MigrationProductionAdoptionPlanDto(
    string AdoptionPlanId,
    string MigrationId,
    string PackageRevisionId,
    string CandidateArtifactId,
    string StagingRunId,
    string Status,
    int RevisionNumber,
    string PlanSha256,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime PreparedAtUtc,
    Guid RuntimeStackId,
    string TargetStackSlug,
    string TargetDisplayName,
    Guid MatrixInstanceId,
    Guid ElementInstanceId,
    string MatrixServerName,
    string MatrixPublicHost,
    string MatrixPublicBaseUrl,
    string ElementPublicHost,
    string ElementPublicBaseUrl,
    string RuntimeNetworkName,
    string MatrixContainerName,
    string ElementContainerName,
    string MatrixImageReference,
    string MatrixImageId,
    string ElementImageReference,
    string ElementImageId,
    string DatabaseEngine,
    string DatabaseHost,
    int DatabasePort,
    string DatabaseName,
    string DatabaseUsername,
    string DatabasePasswordSecretKind,
    long? ExpectedUsersCount,
    long? ExpectedRoomsCount,
    long? ExpectedEventsCount,
    IReadOnlyList<MigrationProductionAdoptionRoutePlan> Routes,
    IReadOnlyList<MigrationProductionAdoptionCollision> Collisions,
    bool CollisionFree,
    bool RuntimeRecordsCreated,
    bool PublicRoutesCreated,
    MigrationProductionMaterializationDto Materialization,
    MigrationProductionCutoverDto Cutover,
    MigrationProductionVerificationDto ProductionVerification,
    MigrationProductionRollbackDto Rollback,
    string? BlockerSummary);

public sealed record MigrationProductionAdoptionStateResponse(
    string Source,
    string Status,
    string MigrationId,
    bool PlanPrepared,
    MigrationProductionAdoptionPlanDto? Plan,
    string Detail);
