using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.ControlPlane;
using Infrastructure.Data.Entities.Diagnostics;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Data.Entities.Migrations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class MemDbContext : IdentityDbContext<MemOperator, IdentityRole<Guid>, Guid>
{
    public MemDbContext(DbContextOptions<MemDbContext> options) : base(options) { }

    public DbSet<RuntimeServiceEntity> RuntimeServices => Set<RuntimeServiceEntity>();

    public DbSet<RuntimeStackEntity> RuntimeStacks => Set<RuntimeStackEntity>();
    public DbSet<RuntimeServiceInstanceEntity> RuntimeServiceInstances => Set<RuntimeServiceInstanceEntity>();
    public DbSet<RuntimeRouteEntity> RuntimeRoutes => Set<RuntimeRouteEntity>();

    public DbSet<InstallationEntity> Installations => Set<InstallationEntity>();
    public DbSet<InstallationStepExecutionEntity> InstallationStepExecutions => Set<InstallationStepExecutionEntity>();
    public DbSet<InstallationSecretEntity> InstallationSecrets => Set<InstallationSecretEntity>();
    public DbSet<DomainEntity> Domains => Set<DomainEntity>();
    public DbSet<CertificateEntity> Certificates => Set<CertificateEntity>();
    public DbSet<DomainSecretEntity> DomainSecrets => Set<DomainSecretEntity>();
    public DbSet<DomainCertificateRenewalPolicyEntity> DomainCertificateRenewalPolicies => Set<DomainCertificateRenewalPolicyEntity>();
    public DbSet<RuntimeReadinessReportEntity> RuntimeReadinessReports => Set<RuntimeReadinessReportEntity>();
    public DbSet<RuntimeOperationEntity> RuntimeOperations => Set<RuntimeOperationEntity>();
    public DbSet<BackupCatalogEntryEntity> BackupCatalogEntries => Set<BackupCatalogEntryEntity>();
    public DbSet<RestoreAttemptEntity> RestoreAttempts => Set<RestoreAttemptEntity>();
    public DbSet<RestoreTargetClaimEntity> RestoreTargetClaims => Set<RestoreTargetClaimEntity>();

    public DbSet<ControlPlaneUserEntity> ControlPlaneUsers => Set<ControlPlaneUserEntity>();
    public DbSet<ControlPlaneUserRoleEntity> ControlPlaneUserRoles => Set<ControlPlaneUserRoleEntity>();

    public DbSet<MemOperatorAuditEventEntity> MemOperatorAuditEvents => Set<MemOperatorAuditEventEntity>();

    public DbSet<DiagnosticsIncidentDispositionEntity> DiagnosticsIncidentDispositions =>
        Set<DiagnosticsIncidentDispositionEntity>();

    public DbSet<MemBootstrapGrantEntity> MemBootstrapGrants => Set<MemBootstrapGrantEntity>();

    public DbSet<MemOperatorEnrollmentGrantEntity> MemOperatorEnrollmentGrants => Set<MemOperatorEnrollmentGrantEntity>();

    public DbSet<MemOperatorStepUpGrantEntity> MemOperatorStepUpGrants => Set<MemOperatorStepUpGrantEntity>();

    public DbSet<MemSecuritySettingsEntity> MemSecuritySettings => Set<MemSecuritySettingsEntity>();

    public DbSet<MemCliDeviceAuthorizationAttemptEntity> MemCliDeviceAuthorizationAttempts =>
        Set<MemCliDeviceAuthorizationAttemptEntity>();

    public DbSet<MemCliDeviceSessionEntity> MemCliDeviceSessions =>
        Set<MemCliDeviceSessionEntity>();

    public DbSet<RuntimeStackUserEntity> RuntimeStackUsers => Set<RuntimeStackUserEntity>();

    public DbSet<RuntimeStackSecretEntity> RuntimeStackSecrets => Set<RuntimeStackSecretEntity>();

    public DbSet<RuntimeStackDatabaseEntity> RuntimeStackDatabases => Set<RuntimeStackDatabaseEntity>();

    public DbSet<MigrationIntakeEntity> MigrationIntakes => Set<MigrationIntakeEntity>();
    public DbSet<MigrationSourceEntity> MigrationSources => Set<MigrationSourceEntity>();
    public DbSet<MigrationPackageRevisionEntity> MigrationPackageRevisions =>
        Set<MigrationPackageRevisionEntity>();
    public DbSet<MigrationConversionAttemptEntity> MigrationConversionAttempts =>
        Set<MigrationConversionAttemptEntity>();
    public DbSet<MigrationCandidateArtifactEntity> MigrationCandidateArtifacts =>
        Set<MigrationCandidateArtifactEntity>();
    public DbSet<MigrationStagingRetirementEntity> MigrationStagingRetirements =>
        Set<MigrationStagingRetirementEntity>();
    public DbSet<MigrationStagingRunEntity> MigrationStagingRuns =>
        Set<MigrationStagingRunEntity>();
    public DbSet<MigrationProductionAdoptionEntity> MigrationProductionAdoptions =>
        Set<MigrationProductionAdoptionEntity>();
    public DbSet<MigrationProductionAuthorityEntity> MigrationProductionAuthorities =>
        Set<MigrationProductionAuthorityEntity>();
    public DbSet<MigrationAcceptanceEntity> MigrationAcceptances =>
        Set<MigrationAcceptanceEntity>();
    public DbSet<LegacyRetentionRecordEntity> LegacyRetentionRecords =>
        Set<LegacyRetentionRecordEntity>();
    public DbSet<MigrationBaselineBackupHandoffEntity> MigrationBaselineBackupHandoffs =>
        Set<MigrationBaselineBackupHandoffEntity>();
    public DbSet<MigrationTwoServerQualificationEntity> MigrationTwoServerQualifications =>
        Set<MigrationTwoServerQualificationEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MemDbContext).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InstallationEntity).Assembly);
    }
}