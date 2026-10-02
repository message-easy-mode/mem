using HostAgent.Runtime.Migrations.Assurance;
using HostAgent.Runtime.Migrations.Qualification;
using HostAgent.Runtime.ServiceRuntime;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;

namespace HostAgent.Tests.Runtime.Migrations.Qualification;

public sealed class MigrationTwoServerQualificationClosureTests
{
    [Fact]
    public void Closure_payload_hash_is_deterministic_and_content_bound()
    {
        var payload = CreatePayload();
        var same = CreatePayload();
        var changed = payload with { BaselineCatalogEntryId = "bkp_other" };

        Assert.Equal(
            MigrationTwoServerQualificationClosureService.ComputePayloadSha256(payload),
            MigrationTwoServerQualificationClosureService.ComputePayloadSha256(same));
        Assert.NotEqual(
            MigrationTwoServerQualificationClosureService.ComputePayloadSha256(payload),
            MigrationTwoServerQualificationClosureService.ComputePayloadSha256(changed));
    }

    [Fact]
    public void Closure_requires_all_operator_acknowledgements()
    {
        var valid = new MigrationTwoServerQualificationClosureRequest(
            QualificationEvidenceReviewed: true,
            DistinctHostEvidenceReviewed: true,
            NormalLifecycleReviewed: true,
            SourceRetentionReviewed: true,
            ReleaseHandoffAcknowledged: true,
            Note: null);

        MigrationTwoServerQualificationClosureService.ValidateAcknowledgements(valid);

        Assert.Throws<InvalidOperationException>(() =>
            MigrationTwoServerQualificationClosureService.ValidateAcknowledgements(
                valid with { SourceRetentionReviewed = false }));
    }

    [Fact]
    public void Closure_reobserves_the_exact_qualified_target_host()
    {
        var qualified = CreateHost('a', 'b', "target-host", "target-docker");
        var current = qualified with { MachineName = "target-host-renamed" };

        MigrationTwoServerQualificationClosureService.ValidateTargetHostStillMatches(
            qualified,
            current);

        Assert.Throws<InvalidDataException>(() =>
            MigrationTwoServerQualificationClosureService.ValidateTargetHostStillMatches(
                qualified,
                current with { DockerEngineIdSha256 = new string('c', 64) }));
    }

    [Fact]
    public void Closure_readiness_requires_normal_runtime_routes_and_available_baseline()
    {
        var context = CreateContext();

        Assert.Empty(
            MigrationTwoServerQualificationClosureService.BuildClosureBlockers(context));

        context.RuntimeStack!.Routes.Single(route => route.ServiceKey == ServiceKeys.ElementWeb)
            .Status = "failed";
        Assert.Contains(
            MigrationTwoServerQualificationClosureService.BuildClosureBlockers(context),
            blocker => blocker.Contains("route ownership", StringComparison.OrdinalIgnoreCase));

        context.RuntimeStack.Routes.Single(route => route.ServiceKey == ServiceKeys.ElementWeb)
            .Status = "verified";
        context.BaselineCatalogEntry!.PayloadState = "removed";
        Assert.Contains(
            MigrationTwoServerQualificationClosureService.BuildClosureBlockers(context),
            blocker => blocker.Contains("Backup Catalog", StringComparison.OrdinalIgnoreCase));
    }

    private static MigrationTwoServerQualificationClosureContext CreateContext()
    {
        var now = new DateTime(2026, 7, 20, 16, 0, 0, DateTimeKind.Utc);
        var stackId = Guid.NewGuid();
        var matrixInstanceId = Guid.NewGuid();
        var elementInstanceId = Guid.NewGuid();
        var baseline = new MigrationBaselineBackupHandoffEntity
        {
            HandoffId = "mbh_test",
            Status = "created",
            BackupId = "backup_test",
            CatalogEntryId = "bkp_test",
            CompletedAtUtc = now,
        };
        var acceptance = new MigrationAcceptanceEntity
        {
            AcceptanceId = "macc_test",
            PublicVerificationEvidenceSha256 = new string('1', 64),
            AcceptedAtUtc = now,
            BaselineBackupHandoff = baseline,
        };
        var plan = new MigrationProductionAdoptionEntity
        {
            AdoptionPlanId = "madp_test",
            Status = "accepted-baseline-backup-created",
            RuntimeStackId = stackId,
            TargetStackSlug = "migrated-stack",
            MatrixInstanceId = matrixInstanceId,
            ElementInstanceId = elementInstanceId,
            MatrixPublicHost = "matrix.example.test",
            ElementPublicHost = "chat.example.test",
            MatrixNpmRouteId = "101",
            ElementNpmRouteId = "102",
            ProductionVerificationStatus = "passed",
            ProductionVerificationId = "mpvf_test",
            ProductionVerificationCompletedAtUtc = now,
            ProductionVerificationEvidenceSha256 = new string('1', 64),
        };
        var qualification = new MigrationTwoServerQualificationEntity
        {
            Status = "qualified",
            AdoptionPlanId = "madp_test",
            ProductionVerificationId = "mpvf_test",
            AcceptanceId = "macc_test",
            BaselineBackupHandoffId = "mbh_test",
            SourceEvidenceSha256 = new string('2', 64),
            QualificationEvidenceSha256 = new string('3', 64),
            DistinctMachineIdentity = true,
            DistinctDockerEngineIdentity = true,
            DevelopmentExternalControlPlane = false,
            RuntimeStackId = stackId,
            BaselineCatalogEntryId = "bkp_test",
        };
        var intake = new MigrationIntakeEntity
        {
            IntakeId = "mig_test",
            ProductionAdoption = plan,
            Acceptance = acceptance,
            BaselineBackupHandoff = baseline,
            TwoServerQualification = qualification,
        };
        var stack = new RuntimeStackEntity
        {
            Id = stackId,
            Slug = "migrated-stack",
            Status = "ready",
            ServiceInstances =
            [
                new RuntimeServiceInstanceEntity
                {
                    InstanceId = matrixInstanceId,
                    ServiceKey = ServiceKeys.Matrix,
                    Status = "ready",
                },
                new RuntimeServiceInstanceEntity
                {
                    InstanceId = elementInstanceId,
                    ServiceKey = ServiceKeys.ElementWeb,
                    Status = "ready",
                },
            ],
            Routes =
            [
                CreateRoute(ServiceKeys.Matrix, "matrix.example.test", "101"),
                CreateRoute(ServiceKeys.ElementWeb, "chat.example.test", "102"),
            ],
        };
        var catalog = new BackupCatalogEntryEntity
        {
            CatalogEntryId = "bkp_test",
            PayloadState = "available",
            IntegrityStatus = "valid",
        };
        return new MigrationTwoServerQualificationClosureContext(
            intake,
            qualification,
            stack,
            catalog);
    }

    private static RuntimeRouteEntity CreateRoute(
        string serviceKey,
        string publicHost,
        string routeId) =>
        new()
        {
            ServiceKey = serviceKey,
            IsPublic = true,
            PublicHost = publicHost,
            ProviderRouteId = routeId,
            SslExpected = true,
            SslConfigured = true,
            ForceSsl = true,
            Status = "verified",
        };

    private static MigrationTwoServerQualificationClosureEvidencePayload CreatePayload() =>
        new(
            ClosureId: "mtqc_test",
            ClosedAtUtc: new DateTime(2026, 7, 20, 16, 0, 0, DateTimeKind.Utc),
            MemVersion: "0.2.0",
            MigrationId: "mig_test",
            Assurance: new MigrationAssuranceSummary(
                AuthorityType: MigrationProductionAuthorityTypes.FinalFrozen,
                ProductionAuthorityId: null,
                AuthorityEvidenceSha256: null,
                PackageRevisionId: "mpr_final",
                CaptureKind: "final",
                SourceFrozen: true,
                RehearsalOnly: false,
                FormalSourceFreezeEvidenceCollected: true,
                FinalRecapturePerformed: true,
                PostCaptureWritesIndependentlyExcluded: true,
                RollbackAssurance: MigrationAssuranceProjection.CoordinatedRollback,
                TwoServerQualificationRequired: true,
                Detail: "High assurance."),
            SourceMigrationId: "source_migration",
            PackageRevisionId: "mpr_final",
            EncryptedPackageSha256: new string('1', 64),
            SourceFingerprint: new string('2', 64),
            SourceStackSlug: "legacy-stack",
            MatrixServerName: "matrix.example.test",
            QualificationId: "mtq_test",
            QualificationEvidenceSha256: new string('3', 64),
            SourceEvidenceAttemptId: "mm01e-source-test",
            SourceEvidenceSha256: new string('4', 64),
            AdoptionPlanId: "madp_test",
            RuntimeStackId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            RuntimeStackSlug: "migrated-stack",
            ProductionVerificationId: "mpvf_test",
            ProductionVerificationEvidenceSha256: new string('5', 64),
            ProductionVerificationCompletedAtUtc: new DateTime(2026, 7, 20, 15, 0, 0, DateTimeKind.Utc),
            AcceptanceId: "macc_test",
            AcceptanceEvidenceSha256: new string('5', 64),
            AcceptedAtUtc: new DateTime(2026, 7, 20, 15, 10, 0, DateTimeKind.Utc),
            BaselineBackupHandoffId: "mbh_test",
            BaselineBackupId: "backup_test",
            BaselineCatalogEntryId: "bkp_test",
            BaselineCatalogPayloadState: "available",
            BaselineCatalogIntegrityStatus: "valid",
            BaselineCompletedAtUtc: new DateTime(2026, 7, 20, 15, 20, 0, DateTimeKind.Utc),
            BaselineBackupBytes: 12345,
            BaselineBackupFiles: 20,
            BaselineBackupWarnings: 0,
            SourceHost: CreateHost('a', 'b', "source-host", "source-docker"),
            TargetHost: CreateHost('c', 'd', "target-host", "target-docker"),
            DistinctMachineIdentity: true,
            DistinctDockerEngineIdentity: true,
            SourceContainers:
            [
                new MigrationTwoServerSourceContainerEvidence(
                    Role: "matrix",
                    ContainerId: "source-matrix-id",
                    ContainerName: "source-matrix",
                    ImageId: "sha256:matrix",
                    WriterContainer: true,
                    WasRunningBeforeFreeze: true,
                    CurrentState: "exited",
                    CurrentlyRunning: false,
                    CurrentRestartPolicy: "no",
                    IdentityMatched: true,
                    FrozenStatePreserved: true),
            ],
            PublicRoutes:
            [
                new MigrationTwoServerQualificationClosureRoute(
                    ServiceKey: ServiceKeys.Matrix,
                    PublicHost: "matrix.example.test",
                    PublicBaseUrl: "https://matrix.example.test",
                    Provider: "npm",
                    ProviderRouteId: "101",
                    ForwardScheme: "http",
                    ForwardHost: "matrix-container",
                    ForwardPort: 8008,
                    IsPublic: true,
                    SslExpected: true,
                    SslConfigured: true,
                    ForceSsl: true,
                    Status: "verified",
                    LastVerifiedAtUtc: new DateTime(2026, 7, 20, 15, 0, 0, DateTimeKind.Utc)),
            ],
            QualificationEvidenceReviewed: true,
            DistinctHostEvidenceReviewed: true,
            NormalLifecycleReviewed: true,
            SourceRetentionReviewed: true,
            ReleaseHandoffAcknowledged: true,
            Note: null);

    private static MigrationTwoServerHostIdentity CreateHost(
        char machineHash,
        char dockerHash,
        string machineName,
        string dockerName) =>
        new(
            MachineIdSha256: new string(machineHash, 64),
            MachineName: machineName,
            OperatingSystem: "Linux",
            Architecture: "X64",
            DockerEngineIdSha256: new string(dockerHash, 64),
            DockerName: dockerName,
            DockerServerVersion: "27.0.0");
}
