using System.Text.Json.Serialization;
using Shared.ControlPlane.Runtime;
using Shared.Diagnostics;

namespace Modules.Operator.Diagnostics.Contracts;

public sealed record DiagnosticsCapabilities(
    bool CanReadTechnicalEvents,
    bool CanGenerateSupportReport,
    bool CanViewOwnerHealthFacts,
    bool CanReadDockerEvidence,
    bool CanVerifyPipeline = false,
    bool CanOpenPortainer = false,
    bool CanManageIncidentLifecycle = false);

public sealed record DiagnosticsOverviewCounts(
    int Information,
    int Warning,
    int Error,
    int Critical,
    int IncidentCount,
    int EventCount);

public sealed record DiagnosticsAttentionItem(
    string IncidentId,
    string Severity,
    string EventCode,
    string Feature,
    string? Stage,
    string Summary,
    DateTimeOffset LastSeenAtUtc,
    string Href);

public sealed record DiagnosticsAttentionResponse(
    int SchemaVersion,
    DateTimeOffset ObservedAtUtc,
    string State,
    int Total,
    string? HighestSeverity,
    IReadOnlyList<DiagnosticsAttentionItem> Items,
    bool Partial,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsActiveContext(
    string State,
    string Kind,
    string Code,
    string Feature,
    string? Stage,
    string? ResourceName,
    string? Summary,
    DateTimeOffset UpdatedAtUtc,
    string? WorkspaceHref,
    string? IncidentId,
    string? IncidentHref);

public sealed record DiagnosticsOverviewResponse(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string Status,
    DiagnosticsOverviewCounts Counts,
    DiagnosticsLoggingHealthResponse LoggingHealth,
    DiagnosticsCapabilities Capabilities,
    DiagnosticsActiveContext? ActiveContext,
    bool Partial,
    bool Truncated,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsResourceProjection(
    string Kind,
    string Id,
    string? DisplayName,
    string? StackId,
    string? StackSlug,
    string? Service,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? WorkspacePath);

public sealed record DiagnosticsEventProjection(
    int SchemaVersion,
    string EventId,
    DateTimeOffset TimestampUtc,
    string Severity,
    string EventCode,
    string Source,
    string Feature,
    string? Stage,
    string Message,
    string? IncidentId,
    string? TraceId,
    string? SpanId,
    string? RequestId,
    string? CorrelationId,
    Guid? OperationId,
    DiagnosticsResourceProjection? Resource,
    IReadOnlyDictionary<string, string>? Expected,
    IReadOnlyDictionary<string, string>? Observed,
    IReadOnlyDictionary<string, string>? Details,
    MemDiagnosticException? Exception,
    string? SuggestedAction,
    bool Retryable,
    bool RedactionsApplied,
    bool Truncated);

public sealed record DiagnosticsEventPageResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset UntilUtc,
    int PageSize,
    bool WindowClamped,
    IReadOnlyList<DiagnosticsEventProjection> Events,
    string? NextCursor,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsOperationSummary(
    Guid OperationId,
    Guid? RuntimeStackId,
    string Operation,
    string Status,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record DiagnosticsIncidentLifecycle(
    string State,
    bool Reopened,
    string? StoredDisposition,
    DateTimeOffset? UpdatedAtUtc,
    Guid? UpdatedByOperatorId,
    DateTimeOffset? ObservedThroughAtUtc,
    string? ObservedThroughEventId,
    DateTimeOffset? SnoozedUntilUtc,
    string? ResolutionCode,
    int? Revision);

public sealed record DiagnosticsIncidentSummary(
    string IncidentId,
    string Severity,
    string EventCode,
    string Feature,
    string? Stage,
    string Message,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    int OccurrenceCount,
    bool Retryable,
    bool Truncated,
    DiagnosticsResourceProjection? Resource,
    string? WorkspaceLink,
    DiagnosticsIncidentLifecycle? Lifecycle = null);

public sealed record DiagnosticsIncidentSnoozeRequest(
    DateTimeOffset SnoozedUntilUtc);

public sealed record DiagnosticsIncidentResolveRequest(
    string ResolutionCode);

public sealed record DiagnosticsIncidentPageResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset UntilUtc,
    int PageSize,
    bool WindowClamped,
    IReadOnlyList<DiagnosticsIncidentSummary> Incidents,
    string? NextCursor,
    bool Partial,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsIncidentDetailResponse(
    DiagnosticsIncidentSummary Incident,
    DiagnosticsCapabilities Capabilities,
    int RelatedEventCount,
    IReadOnlyList<DiagnosticsEventProjection>? TechnicalEvents,
    IReadOnlyList<DiagnosticsOperationSummary> Operations,
    bool Truncated,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsStorageCapacityHealth(
    string Status,
    long? AvailableBytes,
    long? TotalBytes,
    string? WarningCode);

public sealed record DiagnosticsLocalRecorderHealth(
    bool Enabled,
    string Status,
    bool PersistentRecorderConfigured,
    bool PersistentRecorderActive,
    string? PersistentFilePath,
    DateTimeOffset? LastFileWriteAtUtc,
    int RetainedFileCount,
    long RetainedBytes,
    int SerilogSelfLogMessageCount,
    string? WarningCode,
    DiagnosticsStorageCapacityHealth? Storage = null);

public sealed record DiagnosticsSafeEventStoreHealth(
    bool Enabled,
    string Status,
    DateTimeOffset? LastWriteAtUtc,
    DateTimeOffset? LastReadAtUtc,
    long StoredEventCount,
    long DroppedEventCount,
    long MalformedLineCount,
    string? LastWriteErrorCode,
    string? LastReadWarningCode,
    DateTimeOffset? LastRetentionRunAtUtc,
    int LastRetentionDeletedFileCount,
    long LastRetentionDeletedBytes,
    string? LastRetentionErrorCode,
    DiagnosticsStorageCapacityHealth? Storage = null,
    bool HasEverRecordedEvent = false);

public sealed record DiagnosticsSeqHealth(
    string Status,
    bool SinkEnabled,
    bool ManagementEnabled,
    bool Configured,
    string? ServerUrl,
    bool Reachable = false,
    DateTimeOffset? LastCheckedAtUtc = null,
    DateTimeOffset? LastSuccessAtUtc = null,
    string? WarningCode = null);

public sealed record DiagnosticsLoggingHealthResponse(
    DateTimeOffset ObservedAtUtc,
    string Status,
    DiagnosticsLocalRecorderHealth LocalRecorder,
    DiagnosticsSafeEventStoreHealth SafeEventStore,
    DiagnosticsSeqHealth Seq,
    DiagnosticsCapabilities Capabilities,
    bool Partial,
    IReadOnlyList<string> Warnings);


public sealed record DiagnosticsSeqDeliveryOverview(
    bool Enabled,
    bool DesiredEnabled,
    string ConfigurationState,
    string SecretState,
    bool RequiresApiRestartToChange,
    bool RestartRequired,
    DateTimeOffset? PreferenceUpdatedAtUtc,
    string ActivationState,
    DateTimeOffset? ActivationVerifiedAtUtc,
    string? LastActivationVerificationId,
    global::Shared.ControlPlane.Runtime.MemRestartContract Restart);

public sealed record DiagnosticsSeqRuntimeOverview(
    bool ManagementEnabled,
    bool Present,
    bool Managed,
    string State,
    bool Running,
    bool UsesApprovedRuntime,
    string ExpectedVersion,
    string DataRetentionState,
    bool PublishesPublicIngress,
    string? WarningCode);

public sealed record DiagnosticsSeqHealthOverview(
    string Status,
    bool Reachable,
    DateTimeOffset? LastCheckedAtUtc,
    DateTimeOffset? LastSucceededAtUtc,
    string? WarningCode);

public sealed record DiagnosticsSeqUiOverview(
    bool Configured,
    bool Available,
    string? Url,
    bool Configurable);

public sealed record DiagnosticsSeqOverviewCapabilities(
    bool CanReviewSetup,
    bool CanOpenUi,
    bool CanDeploy,
    bool CanStart,
    bool CanStop,
    bool CanRestart,
    bool CanRemove,
    bool CanEnableDelivery,
    bool CanDisableDelivery,
    bool CanCheckHealth,
    bool CanVerifyDelivery = false,
    bool CanConnect = false,
    bool RequiresRecentStepUp = true);

public sealed record DiagnosticsSeqConnectionOverview(
    string CredentialState,
    string VerificationState,
    DateTimeOffset? VerifiedAtUtc,
    string? ApiKeyId,
    string? LastVerificationId,
    string? LastEventId);

public sealed record DiagnosticsSeqOverviewResponse(
    int SchemaVersion,
    DateTimeOffset ObservedAtUtc,
    bool Configured,
    DiagnosticsSeqDeliveryOverview Delivery,
    DiagnosticsSeqRuntimeOverview Runtime,
    DiagnosticsSeqHealthOverview Health,
    DiagnosticsSeqUiOverview Ui,
    DiagnosticsSeqOverviewCapabilities Capabilities,
    IReadOnlyList<string> Warnings,
    DiagnosticsSeqConnectionOverview? Connection = null);

public sealed record DiagnosticsSeqUiAuthorityUpdateRequest(
    string? Url);

public sealed record DiagnosticsSeqUiAuthorityUpdateResponse(
    int SchemaVersion,
    bool Configured,
    string? Url,
    DateTimeOffset UpdatedAtUtc);

public sealed record DiagnosticsSeqSetupImageReview(
    string ApprovedReference,
    string ExpectedVersion,
    bool Local,
    bool ImmutableIdentityAvailable,
    string? WarningCode);

public sealed record DiagnosticsSeqSetupStorageReview(
    bool ServerOwned,
    string State,
    string DisplayName,
    string? WarningCode);

public sealed record DiagnosticsSeqSetupSecretReview(
    bool AdministratorPasswordHashAvailable,
    bool IngestionApiKeyRequired,
    bool IngestionApiKeyAvailable);

public sealed record DiagnosticsSeqSetupUiAuthorityReview(
    bool Configured);

public sealed record DiagnosticsSeqSetupReviewResponse(
    int SchemaVersion,
    string ReviewId,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool ReadyForDeployment,
    bool ReadyForDelivery,
    DiagnosticsSeqSetupImageReview Image,
    DiagnosticsSeqSetupStorageReview Storage,
    DiagnosticsSeqSetupSecretReview Secrets,
    DiagnosticsSeqSetupUiAuthorityReview UiAuthority,
    bool EulaAccepted,
    bool PublishesPublicIngress,
    string RuntimeOwnershipState,
    IReadOnlyList<string> ActionCodes,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsSeqBootstrapOverviewResponse(
    int SchemaVersion,
    DateTimeOffset ObservedAtUtc,
    string State,
    bool SetupAvailable,
    string ApprovedVersion,
    string ImageState,
    string StorageState,
    string RuntimeOwnershipState,
    bool EulaAccepted,
    string AdministratorSecretState,
    string IngestionCredentialState,
    string UiAuthorityState,
    bool CanStartSetup,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsSeqBootstrapReviewRequest(
    bool AcceptEula,
    string? PrivateUiUrl,
    bool EnableEventDelivery = true);

public sealed record DiagnosticsSeqBootstrapImageReview(
    string ApprovedReference,
    string ExpectedVersion,
    bool Local,
    bool ImmutableIdentityAvailable,
    bool WillPullDuringSetup,
    string? WarningCode);

public sealed record DiagnosticsSeqBootstrapStorageReview(
    bool ServerOwned,
    string State,
    string DisplayName,
    string? WarningCode);

public sealed record DiagnosticsSeqBootstrapSecurityReview(
    bool EulaAcceptedInReview,
    bool AdministratorPasswordRequired,
    bool AuthenticatedIngestionRequired,
    bool CurrentAdministratorPasswordRequired = false);

public sealed record DiagnosticsSeqBootstrapRuntimeReview(
    string OwnershipState,
    int SelectedHostPort,
    bool PublishesPublicIngress);

public sealed record DiagnosticsSeqBootstrapReviewResponse(
    int SchemaVersion,
    string ReviewId,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool Ready,
    DiagnosticsSeqBootstrapImageReview Image,
    DiagnosticsSeqBootstrapStorageReview Storage,
    DiagnosticsSeqBootstrapRuntimeReview Runtime,
    DiagnosticsSeqBootstrapSecurityReview Security,
    bool PrivateUiAuthorityConfigured,
    IReadOnlyList<string> ActionCodes,
    IReadOnlyList<string> Warnings,
    bool EnableEventDelivery = true);

public sealed record DiagnosticsSeqBootstrapExecuteRequest(
    string ReviewId,
    string AdministratorPassword,
    string AdministratorPasswordConfirmation,
    string ConnectionAdministratorPassword = "");

public sealed record DiagnosticsSeqBootstrapExecuteResponse(
    int SchemaVersion,
    Guid OperationId,
    string Status,
    DateTimeOffset AcceptedAtUtc);

public sealed record DiagnosticsSeqBootstrapOperationCheck(
    string Code,
    string Status,
    string? WarningCode = null);

public sealed record DiagnosticsSeqBootstrapOperationResponse(
    int SchemaVersion,
    Guid OperationId,
    string Status,
    string CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<DiagnosticsSeqBootstrapOperationCheck> Checks,
    string? WarningCode,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsSeqConnectionRequest(
    string AdministratorPassword);

public sealed record DiagnosticsSeqConnectionResponse(
    int SchemaVersion,
    Guid OperationId,
    string Status,
    string CredentialState,
    string VerificationState,
    string ApiKeyId,
    string VerificationId,
    string EventId,
    DateTimeOffset VerifiedAtUtc,
    bool ReusedCredential,
    DiagnosticsSeqOverviewResponse Overview);

public sealed record DiagnosticsSeqDeliveryVerificationResponse(
    int SchemaVersion,
    Guid OperationId,
    string Status,
    string VerificationId,
    DateTimeOffset EmittedAtUtc,
    DiagnosticsSeqOverviewResponse Overview);

public sealed record DiagnosticsSeqDeliveryChangeRequest(bool Enabled);

public sealed record DiagnosticsSeqOperationResponse(
    int SchemaVersion,
    Guid OperationId,
    string Operation,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    bool DataRetained,
    DiagnosticsSeqOverviewResponse Overview,
    IReadOnlyList<string> Warnings);



public sealed record DiagnosticsPortainerLinks(
    string? Home,
    string? Environment,
    string? Containers);

public sealed record DiagnosticsPortainerCapabilities(
    bool CanOpenHome,
    bool CanOpenEnvironment,
    bool CanOpenContainers,
    bool CanOpenExactResource);

public sealed record DiagnosticsPortainerOverviewResponse(
    int SchemaVersion,
    DateTimeOffset ObservedAtUtc,
    bool Available,
    bool Managed,
    string RuntimeState,
    string OwnershipState,
    string? Version,
    string ApprovedVersion,
    bool EnvironmentConfigured,
    bool ExactResourceLinksSupported,
    DiagnosticsPortainerLinks Links,
    DiagnosticsPortainerCapabilities Capabilities,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsPipelineSelfTestCheck(
    string Code,
    string Status,
    string? WarningCode = null);

public sealed record DiagnosticsPipelineSelfTestResponse(
    int SchemaVersion,
    string VerificationId,
    string Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    string? EventId,
    IReadOnlyList<DiagnosticsPipelineSelfTestCheck> Checks,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsDockerEvidenceContainer(
    string LogicalName,
    string ObservedState,
    long? ExitCode,
    string? Health,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    long RestartCount,
    string? Image);

public sealed record DiagnosticsDockerEvidenceLogTail(
    int RequestedLines,
    int ReturnedLines,
    int MaximumCharacters,
    string Content,
    bool Truncated,
    bool RedactionsApplied);

public sealed record DiagnosticsDockerEvidenceResponse(
    bool Available,
    DiagnosticsResourceProjection? Resource,
    DateTimeOffset? ObservedAtUtc,
    DiagnosticsDockerEvidenceContainer? Container,
    DiagnosticsDockerEvidenceLogTail? LogTail,
    string? WarningCode,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsSupportReportRequest(
    string IncidentId,
    bool IncludeDockerEvidence = false);

public sealed record DiagnosticsSupportReportRedaction(
    string PolicyVersion,
    bool RedactionsApplied,
    IReadOnlyList<string> OmittedContent);

public sealed record DiagnosticsSupportReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string MemVersion,
    MemControlPlaneRuntimeContextProjection RuntimeContext,
    DiagnosticsIncidentSummary Incident,
    IReadOnlyList<DiagnosticsEventProjection> Events,
    IReadOnlyList<DiagnosticsOperationSummary> Operations,
    DiagnosticsLoggingHealthResponse LoggingHealth,
    DiagnosticsDockerEvidenceResponse? DockerEvidence,
    DiagnosticsSupportReportRedaction Redaction,
    bool Truncated,
    IReadOnlyList<string> Warnings);

public sealed record DiagnosticsE2eFixtureIncidentResponse(
    string EventId,
    string IncidentId,
    string? TraceId,
    string? CorrelationId);
