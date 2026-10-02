using Docker.DotNet;
using HostAgent.Docker;
using HostAgent.Element.Provisioning;
using HostAgent.Element.Runtime;
using HostAgent.Identity;
using HostAgent.Matrix.Provisioning;
using HostAgent.Matrix.Federation;
using HostAgent.Matrix.Federation.PrivateNetwork;
using HostAgent.Matrix.Runtime;
using HostAgent.Matrix.Users;
using HostAgent.Options;
using HostAgent.Planning;
using HostAgent.Platform;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Diagnostics;
using HostAgent.Runtime.Diagnostics.Docker;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Filesystem;
using HostAgent.Runtime.Ingress;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Maintenance;
using HostAgent.Runtime.Operations;
using HostAgent.Runtime.Readiness;
using HostAgent.Runtime.Secrets;
using HostAgent.Runtime.ServiceRuntime;
using HostAgent.Runtime.Services.TemporaryStaging;
using HostAgent.Runtime.Stacks.Destroy;
using HostAgent.Runtime.Stacks.Identity;
using HostAgent.Runtime.Storage;
using HostAgent.Runtime.Stacks.Turn;
using HostAgent.Runtime.Stacks.Diagnostics;
using HostAgent.Security;
using HostAgent.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Modules.Shared.RuntimeImages;
using Modules.Setup.Platform.Coturn;
using Shared.Diagnostics;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Backups.Workspace;
using HostAgent.Runtime.Backups.Workspace.PrivateTest;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.AdvancedCutover.Confirmation;
using HostAgent.Runtime.Backups.AdvancedCutover.Confirmation.Catalog;
using HostAgent.Runtime.Backups.AdvancedCutover.Execution;
using HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog;
using HostAgent.Runtime.Backups.AdvancedCutover.PostCutover;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview;
using HostAgent.Runtime.Backups.AdvancedCutover.Retirement;
using HostAgent.Runtime.Backups.AdvancedCutover.Readiness;
using HostAgent.Runtime.Backups.StandardRecreate;
using HostAgent.Runtime.Backups.StandardRecreate.Cleanup;
using HostAgent.Runtime.Backups.StandardRecreate.Reconciliation;
using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups.History;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups;
using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.Backups.RestoreAttempts.List;
using HostAgent.Runtime.Migrations.Assurance;
using HostAgent.Runtime.Migrations.Cutover;
using HostAgent.Runtime.Migrations.ProductionAdoption;
using HostAgent.Runtime.Migrations.Qualification;

namespace HostAgent.DependencyInjection;

public static class HostAgentServiceCollectionExtensions
{
    public static IServiceCollection AddHostAgent(
        this IServiceCollection services,
        IConfiguration configuration)
    {

        services.Configure<HostAgentOptions>(configuration.GetSection("HostAgent"));
        services.Configure<InstanceStorageOptions>(configuration.GetSection("Provisioning"));
        services.Configure<RuntimeNetworkOptions>(configuration.GetSection("RuntimeNetwork"));
        services.Configure<HostNamingOptions>(configuration.GetSection("HostNaming"));
        services.Configure<IngressCertificateOptions>(configuration.GetSection("Ingress"));
        services.Configure<MatrixBootstrapOptions>(configuration.GetSection("MatrixBootstrap"));
        services.Configure<CoturnRuntimeOptions>(
            configuration.GetSection(CoturnRuntimeOptions.SectionName));
        services.Configure<CoturnRuntimeImageOptions>(
            configuration.GetSection(CoturnRuntimeImageOptions.SectionName));

        var diagnosticsOptions = new MemDiagnosticsOptions();
        configuration
            .GetSection(MemDiagnosticsOptions.SectionName)
            .Bind(diagnosticsOptions);
        MemDiagnosticsOptionsValidator.ThrowIfInvalid(diagnosticsOptions);

        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IMemStorageCapacityProbe, MemStorageCapacityProbe>();
        services.AddSingleton(diagnosticsOptions);
        services.AddSingleton<MemDiagnosticHealthState>();
        services.AddSingleton<IMemDiagnosticHealthReader>(serviceProvider =>
            serviceProvider.GetRequiredService<MemDiagnosticHealthState>());
        services.AddSingleton<MemDiagnosticRedactor>();
        services.AddSingleton<IMemDiagnosticTextRedactor>(serviceProvider =>
            serviceProvider.GetRequiredService<MemDiagnosticRedactor>());
        services.AddSingleton<MemDiagnosticExceptionProjector>();
        services.AddSingleton<MemDiagnosticEventFactory>();
        services.AddSingleton<MemDiagnosticCursorCodec>();
        services.AddSingleton<MemDiagnosticEventFileStore>();
        services.AddSingleton<MemDiagnosticEventWriter>();
        services.AddSingleton<IMemDiagnosticEventWriter>(serviceProvider =>
            serviceProvider.GetRequiredService<MemDiagnosticEventWriter>());
        services.AddSingleton<MemDiagnosticEventReader>();
        services.AddSingleton<IMemDiagnosticEventReader>(serviceProvider =>
            serviceProvider.GetRequiredService<MemDiagnosticEventReader>());
        services.AddHostedService<MemDiagnosticRetentionService>();

        var dockerEvidenceOptions = new MemDockerEvidenceOptions();
        configuration
            .GetSection(MemDockerEvidenceOptions.SectionName)
            .Bind(dockerEvidenceOptions);
        MemDockerEvidenceOptionsValidator.ThrowIfInvalid(dockerEvidenceOptions);
        services.AddSingleton(dockerEvidenceOptions);
        services.AddSingleton<MemDockerLogDecoder>();
        services.AddSingleton<MemDockerLogSanitizer>();

        services.AddSingleton(RuntimeConnectivityDetector.Detect());

        services.AddSingleton(sp =>
        {
            var dockerUri = configuration["Docker:Uri"];
            var uri = string.IsNullOrWhiteSpace(dockerUri)
                ? new Uri("unix:///var/run/docker.sock")
                : new Uri(dockerUri);

            return new DockerClientConfiguration(uri).CreateClient();
        });

        services.AddScoped<IDockerHost, DockerHost>();
        services.AddScoped<ITemporaryStagingInventorySource, TemporaryStagingInventorySource>();
        services.AddScoped<TemporaryStagingInventoryService>();
        services.AddScoped<HostDataPathParityValidator>();
        services.AddHostedService<HostDataPathParityStartupValidator>();
        services.AddScoped<IMemDockerEvidenceSource, DockerMemDockerEvidenceSource>();
        services.AddScoped<IMemPrivateStagingEvidenceReader, MemPrivateStagingEvidenceReader>();
        services.AddScoped<IMemDiagnosticResourceResolver, MemDiagnosticResourceResolver>();
        services.AddScoped<MemDockerResourceLocator>();
        services.AddScoped<IMemDockerResourceLocator>(serviceProvider =>
            serviceProvider.GetRequiredService<MemDockerResourceLocator>());
        services.AddScoped<MemDockerEvidenceReader>();
        services.AddScoped<IMemDockerEvidenceReader>(serviceProvider =>
            serviceProvider.GetRequiredService<MemDockerEvidenceReader>());
        services.AddScoped<CreateChatStackRuntimeOperationLifetime>();
        services.AddScoped<ICreateChatStackRuntimeHandler, CreateChatStackRuntimeHandler>();
        services.AddSingleton<CreateChatStackRuntimeBackgroundDispatcher>();
        services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<CreateChatStackRuntimeBackgroundDispatcher>());

        services.AddScoped<InstancePathProvider>();
        services.AddScoped<IRoutePolicyResolver, DefaultRoutePolicyResolver>();

        services.AddScoped<SynapseConfigGenerator>();
        services.AddScoped<FederationDomainValidator>();
        services.AddScoped<FederationPolicyRequestValidator>();
        services.AddScoped<SynapseFederationConfigReader>();
        services.AddScoped<SynapseFederationConfigEditor>();
        services.AddScoped<IRuntimeStackFederationContainerInspector, DockerRuntimeStackFederationContainerInspector>();
        services.AddScoped<IRuntimeStackFederationIngressInspector, NpmRuntimeStackFederationIngressInspector>();
        services.AddScoped<IRuntimeStackFederationStateService, RuntimeStackFederationStateService>();
        services.AddScoped<IRuntimeStackFederationReviewService, RuntimeStackFederationReviewService>();
        services.AddScoped<IRuntimeStackFederationManifestResolver, RuntimeStackFederationManifestResolver>();
        services.AddScoped<IRuntimeStackFederationSnapshotService, RuntimeStackFederationSnapshotService>();
        services.AddScoped<IRuntimeStackFederationConfigTransaction, RuntimeStackFederationConfigTransaction>();
        services.AddScoped<IRuntimeStackFederationIngressTransaction, RuntimeStackFederationIngressTransaction>();
        services.AddScoped<ISynapseFederationConfigCandidateValidator, SynapseFederationConfigCandidateValidator>();
        services.AddScoped<IRuntimeStackFederationVerifier, RuntimeStackFederationVerifier>();
        services.AddScoped<IRuntimeMatrixContainerLifecycleService, RuntimeMatrixContainerLifecycleService>();
        services.AddSingleton<RuntimeStackFederationApplyLock>();
        services.AddScoped<IRuntimeStackFederationApplyService, RuntimeStackFederationApplyService>();
        services.AddScoped<PrivateNetworkAddressValidator>();
        services.AddScoped<SynapsePrivateNetworkConfigReader>();
        services.AddScoped<SynapsePrivateNetworkConfigEditor>();
        services.AddScoped<IRuntimeStackPrivateNetworkFederationService, RuntimeStackPrivateNetworkFederationService>();
        services.AddScoped<ElementConfigGenerator>();
        services.AddScoped<MatrixContainerStarter>();
        services.AddScoped<ElementContainerStarter>();

        services.AddScoped<PlatformDomainResolver>();
        services.AddScoped<ChatStackHostnamePlanner>();
        services.AddScoped<ChatStackRuntimePlanner>();
        services.AddScoped<RuntimeDirectoryPreparer>();

        services.AddScoped<IRoutePublisher, NpmRoutePublisher>();
        services.AddScoped<RuntimeReadinessVerifier>();
        services.AddScoped<RuntimeStackDoctorExecutionLimiter>();

        services.AddScoped<RuntimeStackManifestStore>();
        services.AddScoped<RuntimeStackLogoService>();
        services.AddScoped<RuntimeReadinessReportStore>();
        services.AddScoped<RuntimeOperationStore>();
        services.AddScoped<IRuntimeOperationStore>(serviceProvider =>
            serviceProvider.GetRequiredService<RuntimeOperationStore>());

        services.AddSingleton<ControlPlanePasswordHasher>();
        services.AddScoped<ControlPlaneIdentityService>();

        services.AddScoped<SynapseSharedSecretRegistrationClient>();
        services.AddScoped<ISynapseSharedSecretRegistrationClient>(serviceProvider =>
            serviceProvider.GetRequiredService<SynapseSharedSecretRegistrationClient>());
        services.AddScoped<SynapseAdminUserClient>();
        services.AddScoped<ISynapseAdminUserClient>(serviceProvider =>
            serviceProvider.GetRequiredService<SynapseAdminUserClient>());
        services.AddSingleton<IMatrixAdminAuthorityProtector, MatrixAdminAuthorityDataProtector>();
        services.AddScoped<MatrixAdminAuthorityService>();
        services.AddScoped<MatrixManagedRecoveryAuthorityService>();
        services.AddScoped<IRuntimeStackPostgresQueryExecutor, RuntimeStackPostgresQueryExecutor>();
        services.AddScoped<ISynapseUserInventoryReader, SynapsePostgresUserInventoryReader>();
        services.AddScoped<RuntimeStackUserInventoryReconciliationService>();
        services.AddScoped<IRuntimeStackUserInventorySynchronizer>(serviceProvider =>
            serviceProvider.GetRequiredService<RuntimeStackUserInventoryReconciliationService>());
        services.AddScoped<RuntimeStackUserService>();

        services.AddScoped<RuntimeStackSecretService>();
        services.AddScoped<RuntimeStackDatabaseService>();
        services.AddScoped<IRuntimeStackDatabaseService>(serviceProvider =>
            serviceProvider.GetRequiredService<RuntimeStackDatabaseService>());
        services.AddScoped<CreateChatStackRuntimeFailureFinalizer>();

        services.AddScoped<LocalBackupCaptureService>();
        services.AddScoped<LocalBackupCatalogService>();
        services.AddScoped<BackupCatalogStore>();
        services.AddScoped<BackupCatalogDeleteOperationLifetime>();
        services.AddScoped<BackupCatalogLifecycleService>();
        services.AddScoped<BackupCatalogPayloadResolver>();
        services.AddScoped<LocalCapturedBackupCatalogRegistrationService>();
        services.AddScoped<ImportedZipBackupCatalogMaterialisationService>();
        services.AddScoped<CatalogRestoreSessionService>();
        services.AddScoped<RestoreAttemptListService>();
        services.AddScoped<RuntimeStackStorageService>();
        services.AddScoped<IApprovedCoturnRuntimeProvider, ApprovedCoturnRuntimeProvider>();
        services.AddSingleton<CoturnCheckEvidenceStore>();
        services.AddScoped<CoturnRuntimeFileStore>();
        services.AddScoped<CoturnRuntimeService>();
        services.AddScoped<IPlatformCoturnSetupService>(serviceProvider =>
            serviceProvider.GetRequiredService<CoturnRuntimeService>());
        services.AddScoped<ICoturnPlatformMaintenanceRuntime>(serviceProvider =>
            serviceProvider.GetRequiredService<CoturnRuntimeService>());
        services.AddSingleton<CoturnPlatformMutationAdmissionGate>();
        services.AddScoped<CoturnPlatformInstallOperationService>();
        services.AddScoped<CoturnPlatformInstallOperationProcessor>();
        services.AddSingleton<CoturnPlatformInstallBackgroundDispatcher>();
        services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<CoturnPlatformInstallBackgroundDispatcher>());
        services.AddScoped<CoturnPlatformMaintenanceOperationLifetime>();
        services.AddScoped<CoturnPlatformMaintenanceOperationService>();
        services.AddScoped<CoturnPlatformMaintenanceOperationProcessor>();
        services.AddSingleton<CoturnPlatformMaintenanceBackgroundDispatcher>();
        services.AddSingleton<ICoturnPlatformMaintenanceDispatcher>(serviceProvider =>
            serviceProvider.GetRequiredService<CoturnPlatformMaintenanceBackgroundDispatcher>());
        services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<CoturnPlatformMaintenanceBackgroundDispatcher>());
        services.AddSingleton<CoturnStartupSupervisionState>();
        services.AddScoped<ICoturnStartupSupervisionRuntime>(serviceProvider =>
            serviceProvider.GetRequiredService<CoturnRuntimeService>());
        services.AddScoped<CoturnStartupSupervisionProcessor>();
        services.AddScoped<ICoturnStartupSupervisionProcessor>(serviceProvider =>
            serviceProvider.GetRequiredService<CoturnStartupSupervisionProcessor>());
        services.AddHostedService<CoturnStartupSupervisionHostedService>();
        services.AddScoped<SynapseTurnConfigReader>();
        services.AddScoped<SynapseTurnConfigEditor>();
        services.AddScoped<RuntimeStackTurnConfigTransaction>();
        services.AddScoped<IRuntimeStackTurnPlatformConfigurationProvider, RuntimeStackTurnPlatformConfigurationProvider>();
        services.AddScoped<IRuntimeStackTurnInspectionService, RuntimeStackTurnInspectionService>();
        services.AddSingleton<RuntimeStackTurnMutationLock>();
        services.AddScoped<IRuntimeStackTurnConnectionService, RuntimeStackTurnConnectionService>();
        services.AddScoped<IRuntimeStackTurnDisconnectionService, RuntimeStackTurnDisconnectionService>();

        services.AddScoped<RuntimeReconciliationReportService>();
        services.AddScoped<RuntimeReconciliationCleanupService>();

        services.AddScoped<RuntimeStackDestroyOperationLifetime>();
        services.AddScoped<RuntimeStackDestroyService>();
        services.AddSingleton<RuntimeStackDestroyBackgroundDispatcher>();
        services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<RuntimeStackDestroyBackgroundDispatcher>());
        services.AddScoped<PortableExportService>();
        services.AddScoped<CatalogPortableExportService>();

        services.AddScoped<ImportValidationService>();
        services.AddScoped<ValidatedImportCatalogIngestionService>();
        services.AddScoped<ValidatedImportArtifactService>();

        services.AddScoped<PrivateStagingService>();
        services.AddScoped<PrivateStagingDestroyOperationLifetime>();
        services.AddScoped<IPrivateStagingRunner, PrivateStagingRunner>();
        services.AddScoped<PrivateStagingHistoryService>();
        services.AddSingleton<HostAgent.Runtime.Migrations.Staging.MigrationStagingOperationLifetime>();
        services.AddScoped<HostAgent.Runtime.Migrations.Staging.MigrationStagingOrchestrator>();
        services.AddScoped<HostAgent.Runtime.Migrations.Staging.MigrationCompletedStagingCleanupService>();
        services.AddScoped<HostAgent.Runtime.Migrations.Staging.Retirement.MigrationStagingRetirementHistory>();
        services.AddScoped<HostAgent.Runtime.Migrations.Staging.Retirement.IMigrationStagingRetirementRuntime,
            HostAgent.Runtime.Migrations.Staging.Retirement.MigrationStagingRetirementRuntime>();
        services.AddScoped<HostAgent.Runtime.Migrations.Staging.Retirement.MigrationStagingRetirementService>();
        services.AddHostedService<HostAgent.Runtime.Migrations.Staging.Retirement.MigrationStagingRetirementWorker>();
        services.AddScoped<HostAgent.Runtime.Migrations.Staging.IMigrationPrivateStagingRunner, HostAgent.Runtime.Migrations.Staging.MigrationPrivateStagingRunner>();
        services.AddScoped<MigrationProductionAuthorityService>();
        services.AddScoped<MigrationCutoverContextResolver>();
        services.AddScoped<MigrationCutoverFacadeService>();
        services.AddScoped<MigrationProductionAdoptionService>();
        services.AddScoped<MigrationProductionRuntimeMaterializationService>();
        services.AddScoped<MigrationPrivateServerCreationService>();
        services.AddScoped<MigrationPrivateServerTargetReviewService>();
        services.AddScoped<MigrationGoLiveService>();
        services.AddScoped<MigrationProductionCertificateResolver>();
        services.AddScoped<MigrationProductionCutoverService>();
        services.AddScoped<MigrationProductionVerificationService>();
        services.AddScoped<MigrationProductionRollbackService>();
        services.AddScoped<MigrationProductionRollbackCompletionService>();
        services.AddScoped<IMigrationTargetHostIdentityProbe, MigrationTargetHostIdentityProbe>();
        services.AddScoped<MigrationTwoServerQualificationService>();
        services.AddScoped<MigrationTwoServerQualificationClosureService>();
        services.AddScoped<HostAgent.Runtime.Migrations.Acceptance.MigrationAcceptanceService>();
        services.AddScoped<HostAgent.Runtime.Migrations.Acceptance.MigrationFinishService>();
        services.AddScoped<HostAgent.Runtime.Migrations.BaselineBackup.MigrationBaselineBackupHandoffService>();

        services.AddScoped<RuntimeStackBackupProductionRestorePlanService>();
        services.AddScoped<CatalogProductionRestorePlanService>();
        services.AddScoped<RuntimeStackBackupProductionRestorePlanHistoryService>();
        services.AddScoped<RuntimeStackBackupProductionCandidateService>();
        services.AddScoped<RuntimeStackBackupProductionCandidateHistoryService>();

        services.AddScoped<RuntimeStackBackupPublicCutoverPreviewService>();
        services.AddScoped<RuntimeStackBackupPublicCutoverPreviewHistoryService>();
        services.AddScoped<RuntimeStackBackupPublicCutoverConfirmationService>();
        services.AddScoped<RuntimeStackBackupPublicCutoverConfirmationHistoryService>();
        services.AddScoped<CatalogPublicCutoverConfirmationService>();
        services.AddScoped<CatalogPublicCutoverConfirmationHistoryService>();
        services.AddScoped<RuntimeStackBackupPublicCutoverExecutionService>();
        services.AddScoped<RuntimeStackBackupPublicCutoverExecutionHistoryService>();
        services.AddScoped<CatalogPublicCutoverExecutionService>();
        services.AddScoped<CatalogPublicCutoverExecutionHistoryService>();
        services.AddScoped<CatalogPostCutoverProjectionService>();
        services.AddScoped<CatalogPreCutoverReadinessService>();

        services.AddScoped<StandardRecreatePreflightService>();
        services.AddScoped<CatalogStandardRecreatePreflightService>();
        services.AddScoped<StandardRecreateUserInventoryFinalizer>();
        services.AddScoped<StandardRecreateOperationLifetime>();
        services.AddScoped<StandardRecreateService>();
        services.AddScoped<IStandardRecreateExecutor>(serviceProvider =>
            serviceProvider.GetRequiredService<StandardRecreateService>());
        services.AddScoped<RestoreWorkspaceStandardRecreateService>();
        services.AddScoped<StandardRecreateHistoryService>();
        services.AddScoped<FailedStandardRecreateCleanupService>();
        services.AddScoped<RuntimeStackBackupProductionRecreateCleanupService>();

        services.AddScoped<RestoreAttemptWorkspaceStore>();
        services.AddScoped<RestoreStructuredLogService>();
        services.AddScoped<RestoreSupportReportService>();
        services.AddScoped<RestoreWorkspaceService>();
        services.AddScoped<RestoreWorkspacePrivateTestOperationLifetime>();
        services.AddScoped<RestoreWorkspacePrivateTestService>();
        services.AddScoped<RestorePrivateTestRetirementAuditService>();
        services.AddScoped<RestoreAttemptCoordinator>();
        services.AddScoped<AbandonedStandardRecreateReconciliationService>();
        services.AddSingleton<AbandonedStandardRecreateStartupReconciliationService>();
        services.AddSingleton<IHostedService>(serviceProvider =>
            serviceProvider.GetRequiredService<AbandonedStandardRecreateStartupReconciliationService>());

        return services;
    }
}
