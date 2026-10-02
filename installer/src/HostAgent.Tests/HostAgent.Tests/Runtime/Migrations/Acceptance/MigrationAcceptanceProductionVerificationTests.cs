using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Runtime.Migrations.Acceptance;
using Infrastructure.Data.Entities.Migrations;

namespace HostAgent.Tests.Runtime.Migrations.Acceptance;

public sealed class MigrationAcceptanceProductionVerificationTests
{
    [Fact]
    public void Passed_production_verification_is_acceptance_eligible()
    {
        var now = DateTime.UtcNow;
        var plan = CreatePlan(now);

        var blockers = MigrationAcceptancePolicy.BuildEligibilityBlockers(plan, now, staleReason: null);

        Assert.Empty(blockers);
    }

    [Fact]
    public void Elapsed_validity_metadata_does_not_block_acceptance()
    {
        var now = DateTime.UtcNow;
        var plan = CreatePlan(now);
        plan.ProductionVerificationValidUntilUtc = now.AddSeconds(-1);

        var blockers = MigrationAcceptancePolicy.BuildEligibilityBlockers(plan, now, staleReason: null);

        Assert.Empty(blockers);
    }

    [Fact]
    public void Acceptance_binds_the_exact_hash_valid_production_verification_evidence()
    {
        var now = DateTime.UtcNow;
        var plan = CreatePlan(now);
        var evidence = CreateEvidence(plan, "mig-prod", now);
        plan.ProductionVerificationEvidenceJson = evidence;
        plan.ProductionVerificationEvidenceSha256 = Sha256(evidence);

        MigrationAcceptanceService.ValidateProductionVerificationEvidence(plan, "mig-prod", now);
    }

    [Fact]
    public void Legacy_validity_metadata_mismatch_does_not_block_acceptance_after_expiry_retirement()
    {
        var now = DateTime.UtcNow;
        var plan = CreatePlan(now);
        var legacyValidUntilUtc = now.AddMinutes(30);
        plan.ProductionVerificationValidUntilUtc = null;
        var evidence = CreateEvidence(plan, "mig-prod", now, legacyValidUntilUtc);
        plan.ProductionVerificationEvidenceJson = evidence;
        plan.ProductionVerificationEvidenceSha256 = Sha256(evidence);

        MigrationAcceptanceService.ValidateProductionVerificationEvidence(plan, "mig-prod", now);
    }

    [Fact]
    public void Tampered_production_verification_evidence_is_rejected()
    {
        var now = DateTime.UtcNow;
        var plan = CreatePlan(now);
        var evidence = CreateEvidence(plan, "mig-prod", now);
        plan.ProductionVerificationEvidenceJson = evidence.Replace(
            "\"passed\":true",
            "\"passed\":false",
            StringComparison.Ordinal);
        plan.ProductionVerificationEvidenceSha256 = Sha256(evidence);

        var exception = Assert.Throws<InvalidDataException>(() =>
            MigrationAcceptanceService.ValidateProductionVerificationEvidence(plan, "mig-prod", now));

        Assert.Contains("SHA-256", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static MigrationProductionAdoptionEntity CreatePlan(DateTime now)
    {
        var readinessReportId = Guid.NewGuid();
        var plan = new MigrationProductionAdoptionEntity
        {
            AdoptionPlanId = "mpa_test",
            Status = "production-verification-passed",
            PlanSha256 = new string('a', 64),
            RuntimeStackId = Guid.NewGuid(),
            TargetStackSlug = "migrated-stack",
            MatrixServerName = "matrix.example.test",
            CutoverExecutionId = "mpcx_test",
            CutoverStatus = "public-awaiting-verification",
            PublicRoutesCreated = true,
            RuntimePromotionCompleted = true,
            TargetPublicAtUtc = now.AddMinutes(-5),
            ProductionVerificationId = "mpvf_test",
            ProductionVerificationStatus = "passed",
            ProductionVerificationCompletedAtUtc = now.AddMinutes(-1),
            ProductionVerificationValidUntilUtc = now.AddMinutes(29),
            ProductionVerificationCheckCount = 1,
            ProductionVerificationFailedCheckCount = 0,
            ProductionVerificationEvidenceJson = "{}",
            ProductionVerificationEvidenceSha256 = new string('b', 64),
            ProductionVerificationReadinessReportId = readinessReportId,
            PackageRevision = new MigrationPackageRevisionEntity
            {
                PackageRevisionId = "mpr_test",
                Purpose = "final",
                Status = "package-validated",
                SourceFrozen = true,
                RehearsalOnly = false,
            },
            CandidateArtifact = new MigrationCandidateArtifactEntity
            {
                CandidateArtifactId = "mca_test",
                VerificationStatus = "verified",
                RetentionState = "active",
            },
            StagingRun = new MigrationStagingRunEntity
            {
                StagingRunId = "mstg_test",
                Status = "verified",
                PrivateOnly = true,
                DatabaseImportSucceeded = true,
                SynapseHealthPassed = true,
                ElementContainerStarted = true,
                ElementHealthPassed = true,
                ElementSynapseConnectivityPassed = true,
                ElementNetworkAttached = true,
            },
        };
        return plan;
    }

    private static string CreateEvidence(
        MigrationProductionAdoptionEntity plan,
        string migrationId,
        DateTime now) =>
        CreateEvidence(plan, migrationId, now, plan.ProductionVerificationValidUntilUtc);

    private static string CreateEvidence(
        MigrationProductionAdoptionEntity plan,
        string migrationId,
        DateTime now,
        DateTime? evidenceValidUntilUtc) =>
        JsonSerializer.Serialize(new
        {
            verificationId = plan.ProductionVerificationId,
            migrationId,
            adoptionPlanId = plan.AdoptionPlanId,
            cutoverExecutionId = plan.CutoverExecutionId,
            planSha256 = plan.PlanSha256,
            packageRevisionId = plan.PackageRevision.PackageRevisionId,
            candidateArtifactId = plan.CandidateArtifact.CandidateArtifactId,
            stagingRunId = plan.StagingRun.StagingRunId,
            runtimeStackId = plan.RuntimeStackId,
            targetStackSlug = plan.TargetStackSlug,
            matrixServerName = plan.MatrixServerName,
            startedAtUtc = now.AddMinutes(-2),
            completedAtUtc = plan.ProductionVerificationCompletedAtUtc,
            validUntilUtc = evidenceValidUntilUtc,
            @operator = "owner",
            passed = true,
            readinessReportId = plan.ProductionVerificationReadinessReportId,
            checks = new[]
            {
                new { code = "check", name = "Check", success = true, detail = "passed" },
            },
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
