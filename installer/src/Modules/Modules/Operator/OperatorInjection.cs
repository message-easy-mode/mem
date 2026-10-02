using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Operator.Dashboard;
using Modules.Operator.Diagnostics.Services;
using Modules.Operator.Domains;
using Modules.Operator.Migrations;
using Modules.Operator.Migrations.Conversion;
using Modules.Operator.Migrations.Runtime;
using Modules.Operator.Migrations.Workspace;
using Modules.Operator.Npm;
using Modules.Shared.RuntimeImages;
using Shared.Diagnostics;

namespace Modules.Operator;

public static class OperatorInjection
{
    public static IServiceCollection AddOperatorApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddScoped<DashboardOverviewService>();
        services.AddScoped<DashboardActivityReader>();
        services.AddScoped<IDashboardRuntimeProbe, DashboardRuntimeProbe>();
        services.AddScoped<NpmCredentialManagementService>();
        services.AddScoped<OperatorDomainDeletionService>();
        services.AddScoped<OperatorDomainCertificateDeletionService>();

        var diagnosticsApiOptions = new DiagnosticsApiOptions();
        configuration
            .GetSection(DiagnosticsApiOptions.SafeEventsSectionName)
            .Bind(diagnosticsApiOptions);
        DiagnosticsApiOptions.Validate(diagnosticsApiOptions);
        services.AddSingleton(diagnosticsApiOptions);
        services.AddScoped<DiagnosticsCapabilityService>();
        services.AddScoped<DiagnosticsWorkspaceLinkBuilder>();
        services.AddScoped<DiagnosticsQueryParser>();
        services.AddScoped<DiagnosticsEventCollector>();
        services.AddScoped<DiagnosticsEventService>();
        services.AddScoped<DiagnosticsIncidentService>();
        services.AddScoped<DiagnosticsIncidentLifecycleProjector>();
        services.AddScoped<DiagnosticsIncidentLifecycleReader>();
        services.AddScoped<DiagnosticsIncidentLifecycleService>();
        services.AddScoped<IMemDiagnosticIncidentLifecycleWriter>(serviceProvider =>
            serviceProvider.GetRequiredService<DiagnosticsIncidentLifecycleService>());
        services.AddScoped<DiagnosticsIncidentActionService>();
        services.AddScoped<DiagnosticsDockerEvidenceService>();
        services.AddScoped<DiagnosticsLoggingHealthService>();
        services.AddScoped<DiagnosticsActiveContextService>();
        services.AddScoped<DiagnosticsOverviewService>();
        services.AddScoped<DiagnosticsAttentionService>();
        services.AddScoped<DiagnosticsSeqService>();
        services.AddScoped<DiagnosticsSeqUiAuthorityService>();
        services.AddScoped<DiagnosticsSeqBootstrapService>();
        services.AddSingleton<SeqBootstrapOperationQueue>();
        services.AddScoped<DiagnosticsSeqBootstrapExecutionService>();
        services.AddScoped<DiagnosticsSeqBootstrapOperationProcessor>();
        services.AddHostedService<DiagnosticsSeqBootstrapWorker>();
        services.AddScoped<DiagnosticsSeqLifecycleService>();
        services.AddScoped<DiagnosticsSeqConnectionService>();
        services.AddScoped<DiagnosticsSeqDeliveryVerificationService>();
        services.AddScoped<IDiagnosticsIncidentResourceReader, DiagnosticsIncidentResourceReader>();
        services.AddScoped<DiagnosticsPortainerLinkBuilder>();
        services.AddScoped<DiagnosticsPortainerService>();
        services.AddSingleton<DiagnosticsPipelineSelfTestService>();
        services.AddScoped<DiagnosticsSupportReportSizeLimiter>();
        services.AddScoped<DiagnosticsSupportReportService>();

        var diagnosticsE2eFixtureOptions = new DiagnosticsE2eFixtureOptions();
        configuration
            .GetSection(DiagnosticsE2eFixtureOptions.SectionName)
            .Bind(diagnosticsE2eFixtureOptions);
        services.AddSingleton(diagnosticsE2eFixtureOptions);
        services.AddScoped<MigrationSessionProjectionService>();
        services.AddScoped<MigrationSessionInventoryService>();
        services.AddScoped<MigrationSessionLifecycleService>();
        services.AddScoped<MigrationSourceRequestService>();
        services.AddScoped<MigrationWorkspaceProjectionService>();
        services.AddScoped<SecureMigrationIntakeService>();
        services.AddScoped<MigrationFinalPackageRecipientService>();
        services.AddScoped<MigrationFinalPackageUploadService>();
        services.AddScoped<IAgeKeyPairGenerator, AgeKeyPairGenerator>();
        services.AddScoped<IAgePackageDecryptor, AgePackageDecryptor>();
        services.AddSingleton<MigrationConversionOperationLifetime>();
        services.AddScoped<MigrationConversionOrchestrator>();
        services.AddScoped<IMigrationConversionWorkerRunner, MigrationConversionWorkerRunner>();
        services.Configure<MemMigrateRuntimeOptions>(
            configuration.GetSection(MemMigrateRuntimeOptions.SectionName));
        services.AddSingleton<IMemMigrateRuntimeProbe, MemMigrateRuntimeProbe>();
        services.AddHostedService<MemMigrateRuntimeStartupValidator>();
        services.Configure<PostgresRuntimeImageOptions>(
            configuration.GetSection(PostgresRuntimeImageOptions.SectionName));
        services.AddScoped<IRuntimeImageInspector, DockerRuntimeImageInspector>();
        services.AddScoped<IApprovedPostgresRuntimeProvider, ApprovedPostgresRuntimeProvider>();
        services.AddScoped<IApprovedOperationalRuntimeImageProvider, ApprovedOperationalRuntimeImageProvider>();
        services.AddHostedService<ApprovedPostgresRuntimeStartupReporter>();

        // Additional operator application services are registered here as
        // their bounded feature areas are implemented.
        return services;
    }
}
