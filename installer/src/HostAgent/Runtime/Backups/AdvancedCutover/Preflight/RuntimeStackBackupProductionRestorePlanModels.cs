namespace HostAgent.Runtime.Backups.AdvancedCutover.Preflight;

public sealed record RuntimeStackBackupProductionRestorePlanResult(
    string Source,
    string Status,
    string PlanId,
    string? ValidationId,
    string? StagingId,
    string RestoreMode,
    DateTimeOffset CreatedAtUtc,
    bool ProductionExecutionLocked,
    RuntimeStackBackupProductionRestoreMutationSummary Mutations,
    RuntimeStackBackupProductionRestoreSourceSummary BackupSource,
    RuntimeStackBackupProductionRestoreStagingEvidenceSummary StagingEvidence,
    RuntimeStackBackupProductionRestoreTargetSummary Target,
    RuntimeStackBackupProductionRestoreMatrixIdentitySummary MatrixIdentity,
    RuntimeStackBackupProductionRestoreDnsSummary Dns,
    RuntimeStackBackupProductionRestoreCertificateSummary Certificates,
    RuntimeStackBackupProductionRestoreNpmSummary Npm,
    RuntimeStackBackupProductionRestoreReplacementSummary Replacement,
    IReadOnlyList<RuntimeStackBackupProductionRestoreConfirmation> Confirmations,
    IReadOnlyList<RuntimeStackBackupProductionRestoreCheck> Checks,
    IReadOnlyList<RuntimeStackBackupProductionRestoreStep> Steps,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    string? Detail,
    string? CatalogEntryId = null,
    string? SourceKind = null);

public sealed record RuntimeStackBackupProductionRestoreMutationSummary(
    bool RuntimeChanged,
    bool ProductionContainersTouched,
    bool ProductionDatabasesTouched,
    bool DnsChanged,
    bool NpmRoutesChanged,
    bool CertificatesChanged,
    bool PublicRoutesChanged,
    bool FederationExposureChanged,
    IReadOnlyList<string> Notes);

public sealed record RuntimeStackBackupProductionRestoreSourceSummary(
    bool UploadedZipFound,
    string? UploadedZipPath,
    string? UploadedZipName,
    long? UploadedZipSizeBytes,
    bool ManifestPresent,
    int ManifestVersion,
    string? ExportKind,
    DateTimeOffset? CreatedAtUtc,
    string? CreatedBy,
    string? MemVersion,
    string? SourceStackSlug,
    string? SourceDisplayName,
    string? SourceMatrixServerName,
    string? MatrixPublicUrl,
    string? ElementPublicUrl,
    string? ManifestMatrixHost,
    string? ManifestElementHost,
    bool DatabaseDumpPresent,
    string? DatabaseDumpPath,
    bool HomeserverConfigPresent,
    string? HomeserverConfigPath,
    bool SigningKeyPresent,
    string? SigningKeyPath,
    bool MediaStorePresent,
    long MediaFiles,
    long MediaBytes,
    bool ElementConfigPresent,
    string? ElementConfigPath,
    IReadOnlyList<string> ManifestWarnings,
    string? SourceKind = null,
    string? CatalogEntryId = null);

public sealed record RuntimeStackBackupProductionRestoreStagingEvidenceSummary(
    bool StagingIdProvided,
    string? StagingId,
    bool RunFound,
    string? Status,
    string? Mode,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    string? TargetStackSlug,
    string? MatrixServerName,
    bool WasReady,
    bool Destroyed,
    bool AcceptedAsEvidence,
    bool DatabaseImportSucceeded,
    bool SynapseHealthPassed,
    bool PrivateOnly,
    bool InternalDockerNetwork,
    bool PublicRoutesCreated,
    bool DnsChanged,
    bool CertificatesChanged,
    bool ProductionContainersTouched,
    bool ProductionDatabasesTouched,
    int WarningCount,
    int ErrorCount,
    string? Detail,
    string? CatalogEntryId = null,
    string? SourceKind = null);

public sealed record RuntimeStackBackupProductionRestoreTargetSummary(
    string TargetStackSlug,
    bool StackFound,
    Guid? StackId,
    string? DisplayName,
    string? Status,
    string? LastVerifiedStatus,
    DateTimeOffset? LastVerifiedAtUtc,
    string? BaseDomain,
    Guid? DomainId,
    Guid? ActiveCertificateId,
    int? ActiveNpmCertificateId,
    string? RuntimeNetworkName,
    string? DataRoot,
    string? ManifestPath,
    string? MatrixPublicBaseUrl,
    string? ElementPublicBaseUrl,
    RuntimeStackBackupProductionRestoreServiceTarget? MatrixService,
    RuntimeStackBackupProductionRestoreServiceTarget? ElementService,
    RuntimeStackBackupProductionRestoreDatabaseTarget? Database,
    IReadOnlyList<RuntimeStackBackupProductionRestoreRouteTarget> Routes);

public sealed record RuntimeStackBackupProductionRestoreServiceTarget(
    Guid InstanceId,
    string ServiceKey,
    string? Role,
    string Status,
    string? Image,
    string? Version,
    string? ContainerName,
    string? ContainerId,
    string? NetworkName,
    string? InternalHost,
    string? InternalBaseUrl,
    string? PublicHost,
    string? PublicBaseUrl,
    int? HostPort,
    int? ContainerPort,
    string? DataPath,
    string? ConfigPath,
    string? ServerName);

public sealed record RuntimeStackBackupProductionRestoreDatabaseTarget(
    string DatabaseEngine,
    string DatabaseHost,
    int DatabasePort,
    string DatabaseName,
    string DatabaseUsername,
    string Status);

public sealed record RuntimeStackBackupProductionRestoreRouteTarget(
    string ServiceKey,
    string Provider,
    string RouteKind,
    bool IsPublic,
    string PublicHost,
    string? PublicBaseUrl,
    string ForwardScheme,
    string ForwardHost,
    int ForwardPort,
    string? ProviderRouteId,
    Guid? CertificateId,
    int? NpmCertificateId,
    bool SslExpected,
    bool SslConfigured,
    bool ForceSsl,
    bool Http2,
    string Status,
    DateTimeOffset? LastVerifiedAtUtc,
    string? LastError);

public sealed record RuntimeStackBackupProductionRestoreMatrixIdentitySummary(
    string? SourceServerName,
    string? TargetServerName,
    bool SameServerName,
    bool SigningKeyPresent,
    bool SigningKeyReuseLikely,
    bool OldPublicServerStopRequired,
    bool DuplicatePublicServerRisk,
    IReadOnlyList<string> OtherStacksWithSameServerName,
    IReadOnlyList<string> Notes);

public sealed record RuntimeStackBackupProductionRestoreDnsSummary(
    string? IntendedMatrixHost,
    string? IntendedElementHost,
    bool MatrixHostProvided,
    bool ElementHostProvided,
    RuntimeStackBackupProductionRestoreDomainMatch? MatrixDomain,
    RuntimeStackBackupProductionRestoreDomainMatch? ElementDomain,
    bool DnsMutationRequiredLater,
    bool DnsChanged,
    IReadOnlyList<string> Notes);

public sealed record RuntimeStackBackupProductionRestoreDomainMatch(
    Guid DomainId,
    string BaseDomain,
    string DisplayName,
    string Purpose,
    string DnsProvider,
    string? DnsZone,
    string Status,
    Guid? ActiveCertificateId);

public sealed record RuntimeStackBackupProductionRestoreCertificateSummary(
    bool CertificatesChanged,
    bool MatrixCertificateAvailable,
    bool ElementCertificateAvailable,
    RuntimeStackBackupProductionRestoreCertificateMatch? MatrixCertificate,
    RuntimeStackBackupProductionRestoreCertificateMatch? ElementCertificate,
    IReadOnlyList<RuntimeStackBackupProductionRestoreCertificateMatch> CandidateCertificates,
    IReadOnlyList<string> Notes);

public sealed record RuntimeStackBackupProductionRestoreCertificateMatch(
    Guid CertificateEntityId,
    Guid DomainId,
    string CertificateId,
    string CommonName,
    string Provider,
    bool IsWildcard,
    bool IsStaging,
    bool IsActive,
    string Status,
    DateTimeOffset? ExpiresAtUtc,
    string? FullchainPath,
    string? PrivateKeyPath,
    int? NpmCertificateId,
    bool ImportedToNpm,
    DateTimeOffset? LastImportedToNpmAtUtc,
    string? LastError);

public sealed record RuntimeStackBackupProductionRestoreNpmSummary(
    bool NpmRoutesChanged,
    bool MatrixRouteDiscovered,
    bool ElementRouteDiscovered,
    RuntimeStackBackupProductionRestoreNpmRouteSummary? MatrixRoute,
    RuntimeStackBackupProductionRestoreNpmRouteSummary? ElementRoute,
    IReadOnlyList<string> Notes);

public sealed record RuntimeStackBackupProductionRestoreNpmRouteSummary(
    bool FoundInRuntimeRouteRegistry,
    string Host,
    string? ServiceKey,
    string? Provider,
    string? RouteKind,
    string? ForwardScheme,
    string? ForwardHost,
    int? ForwardPort,
    string? ProviderRouteId,
    Guid? CertificateId,
    int? NpmCertificateId,
    bool? SslConfigured,
    bool? ForceSsl,
    bool? Http2,
    string? Status,
    string? LastError);

public sealed record RuntimeStackBackupProductionRestoreReplacementSummary(
    bool TargetStackExists,
    bool ReplacementModeRequested,
    bool PreCutoverBackupRequired,
    bool OldStackStopRequired,
    bool RollbackPlanRequired,
    bool SafeToExecuteNow,
    IReadOnlyList<string> RequiredBeforeExecution,
    IReadOnlyList<string> Notes);

public sealed record RuntimeStackBackupProductionRestoreConfirmation(
    string Code,
    string Label,
    string Description,
    string Severity,
    bool RequiredForPlan,
    bool RequiredForExecution,
    DateTimeOffset? AcknowledgedAtUtc);

public sealed record RuntimeStackBackupProductionRestoreCheck(
    string Code,
    string Category,
    string Severity,
    string Status,
    bool Passed,
    string Message,
    string? Detail);

public sealed record RuntimeStackBackupProductionRestoreStep(
    int Order,
    string Code,
    string Title,
    string Description,
    bool MutatesProduction,
    bool AvailableInThisSprint,
    string Status);

public sealed record RuntimeStackBackupProductionRestorePlanHistoryResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    int TotalPlans,
    IReadOnlyList<RuntimeStackBackupProductionRestorePlanSummary> Plans,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RuntimeStackBackupProductionRestorePlanDetailResponse(
    string Source,
    string Status,
    string HistoryRootPath,
    RuntimeStackBackupProductionRestorePlanResult? Plan,
    IReadOnlyList<string> Warnings,
    string? Detail);

public sealed record RuntimeStackBackupProductionRestorePlanSummary(
    string PlanId,
    string? ValidationId,
    string? StagingId,
    string RestoreMode,
    string Status,
    DateTimeOffset CreatedAtUtc,
    bool ProductionExecutionLocked,
    string? SourceStackSlug,
    string? SourceMatrixServerName,
    string TargetStackSlug,
    bool TargetStackFound,
    bool StagingEvidenceAccepted,
    bool MatrixRouteDiscovered,
    bool ElementRouteDiscovered,
    bool MatrixCertificateAvailable,
    bool ElementCertificateAvailable,
    int BlockerCount,
    int WarningCount,
    int ErrorCount,
    string? Detail,
    string? CatalogEntryId = null,
    string? SourceKind = null);