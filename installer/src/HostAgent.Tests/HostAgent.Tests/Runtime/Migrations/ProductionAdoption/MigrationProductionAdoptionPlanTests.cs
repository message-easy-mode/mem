using System.Text.Json;
using HostAgent.Runtime.Migrations.ProductionAdoption;
using Infrastructure.Data.Entities.Migrations;

namespace HostAgent.Tests.Runtime.Migrations.ProductionAdoption;

public sealed class MigrationProductionAdoptionPlanTests
{
    [Theory]
    [InlineData("Davids Stack", "davids-stack")]
    [InlineData("  MEM__Migration  ", "mem-migration")]
    [InlineData("---", "migrated-stack")]
    public void Target_stack_slug_is_normalized_for_normal_runtime_ownership(
        string input,
        string expected)
    {
        Assert.Equal(expected, MigrationProductionAdoptionService.NormalizeSlug(input));
    }

    [Fact]
    public void Plan_hash_is_deterministic_and_changes_with_authoritative_staging()
    {
        var first = CreateFingerprint("mstg_final_001");
        var same = CreateFingerprint("mstg_final_001");
        var changed = CreateFingerprint("mstg_final_002");
        var changedAuthority = CreateFingerprint(
            "mstg_final_001",
            productionAuthorityId: "mpauth_changed");
        var changedElementHost = CreateFingerprint(
            "mstg_final_001",
            elementPublicHost: "element-davids.deltabox.dev");

        Assert.Equal(
            MigrationProductionAdoptionService.ComputePlanSha256(first),
            MigrationProductionAdoptionService.ComputePlanSha256(same));
        Assert.NotEqual(
            MigrationProductionAdoptionService.ComputePlanSha256(first),
            MigrationProductionAdoptionService.ComputePlanSha256(changed));
        Assert.NotEqual(
            MigrationProductionAdoptionService.ComputePlanSha256(first),
            MigrationProductionAdoptionService.ComputePlanSha256(changedAuthority));
        Assert.NotEqual(
            MigrationProductionAdoptionService.ComputePlanSha256(first),
            MigrationProductionAdoptionService.ComputePlanSha256(changedElementHost));
    }


    [Theory]
    [InlineData(" Element.Example.Test. ", "element.example.test")]
    [InlineData("chat-example.test", "chat-example.test")]
    public void Element_public_host_override_is_normalized(
        string input,
        string expected)
    {
        Assert.Equal(expected, MigrationProductionAdoptionService.NormalizeTargetHost(input));
    }

    [Theory]
    [InlineData("https://element.example.test")]
    [InlineData("element.example.test/path")]
    [InlineData("element example.test")]
    [InlineData("element.example.test:443")]
    public void Element_public_host_override_rejects_non_hostname_values(string input)
    {
        Assert.Throws<InvalidOperationException>(() =>
            MigrationProductionAdoptionService.NormalizeTargetHost(input));
    }

    [Fact]
    public void Element_public_host_must_differ_from_preserved_matrix_identity()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationProductionAdoptionService.ResolveElementPublicHost(
                "matrix.example.test",
                "https://chat.example.test",
                "matrix.example.test"));

        Assert.Contains("must be different", error.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void Cutover_preview_timestamps_serialize_with_an_explicit_utc_offset()
    {
        var createdAt = new DateTime(2026, 7, 22, 2, 19, 25, DateTimeKind.Unspecified);
        var expiresAt = createdAt.AddMinutes(15);
        var preview = new MigrationProductionCutoverPreviewDto(
            PreviewId: "mpcv_utc_contract",
            Status: "ready",
            CreatedAtUtc: MigrationProductionAdoptionService.ToUtcOffset(createdAt),
            ExpiresAtUtc: MigrationProductionAdoptionService.ToUtcOffset(expiresAt),
            SnapshotSha256: new string('a', 64),
            Routes: [],
            Blockers: []);

        var json = JsonSerializer.Serialize(preview, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"createdAtUtc\":\"2026-07-22T02:19:25+00:00\"", json);
        Assert.Contains("\"expiresAtUtc\":\"2026-07-22T02:34:25+00:00\"", json);
    }

    [Fact]
    public void Retained_cutover_preview_does_not_expire_only_because_time_passed()
    {
        var now = DateTime.UtcNow;
        var plan = new MigrationProductionAdoptionEntity
        {
            Status = "cutover-preview-ready",
            CutoverPreviewId = "mpcv_retained",
            CutoverPreviewStatus = "ready",
            CutoverPreviewExpiresAtUtc = now.AddSeconds(-1),
            PublicRoutesCreated = false,
            RuntimePromotionCompleted = false,
        };

        Assert.Equal(
            "ready",
            MigrationProductionAdoptionService.ResolveCutoverPreviewStatus(plan, now));
        Assert.Equal(
            "cutover-preview-ready",
            MigrationProductionAdoptionService.ResolveEffectiveStatus(plan, now));
    }

    [Fact]
    public void Operator_attested_preview_plan_remains_current_while_exact_authority_is_active()
    {
        var fixture = CreateOperatorAttestedPlan();

        Assert.Null(MigrationProductionAdoptionService.ResolveStaleReason(fixture.Plan));
    }

    [Fact]
    public void Acceptance_can_validate_authority_loaded_with_session_when_plan_navigation_is_not_hydrated()
    {
        var fixture = CreateOperatorAttestedPlan();
        var separatelyLoadedAuthorities = fixture.Plan.MigrationIntake.ProductionAuthorities.ToArray();
        fixture.Plan.MigrationIntake.ProductionAuthorities.Clear();

        Assert.Contains(
            "active production authority",
            MigrationProductionAdoptionService.ResolveStaleReason(fixture.Plan),
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(MigrationProductionAdoptionService.ResolveStaleReason(
            fixture.Plan,
            separatelyLoadedAuthorities));
    }

    [Fact]
    public void Operator_attested_preview_plan_becomes_stale_when_authority_is_not_active()
    {
        var fixture = CreateOperatorAttestedPlan();
        fixture.Authority.Status = MigrationProductionAuthorityStatuses.Revoked;
        fixture.Authority.ActiveMigrationKey = null;

        var reason = MigrationProductionAdoptionService.ResolveStaleReason(fixture.Plan);

        Assert.Contains("active production authority", reason, StringComparison.OrdinalIgnoreCase);
    }

    private static OperatorAttestedPlanFixture CreateOperatorAttestedPlan()
    {
        const string migrationId = "mig_operator_attested_plan";
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = migrationId,
        };
        var revision = new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            MigrationIntakeEntityId = intake.Id,
            PackageRevisionId = "mpr_preview_authority",
            Purpose = "preview",
            Status = "package-validated",
            RetentionState = "active",
            ActivePurposeKey = $"{migrationId}:preview",
            EncryptedPackageSha256 = new string('1', 64),
            DecryptedArchiveSha256 = new string('2', 64),
            ArchiveMigrationId = "source-capture",
            CaptureKind = "preview",
            SourceFrozen = false,
            RehearsalOnly = true,
        };
        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(),
            CandidateArtifactId = "mca_preview_authority",
            VerificationStatus = "verified",
            RetentionState = "active",
            SourcePackageSha256 = revision.DecryptedArchiveSha256,
        };
        var staging = new MigrationStagingRunEntity
        {
            Id = Guid.NewGuid(),
            StagingRunId = "mstg_preview_authority",
            MigrationIntakeEntityId = intake.Id,
            MigrationCandidateArtifactEntityId = candidate.Id,
            Status = "verified",
            PrivateOnly = true,
            PublicRoutesCreated = false,
            DatabaseImportSucceeded = true,
            SynapseHealthPassed = true,
            ElementContainerStarted = true,
            ElementHealthPassed = true,
            ElementSynapseConnectivityPassed = true,
            ElementNetworkAttached = true,
        };
        var authority = new MigrationProductionAuthorityEntity
        {
            Id = Guid.NewGuid(),
            ProductionAuthorityId = "mpauth_operator_attested",
            MigrationIntakeEntityId = intake.Id,
            MigrationPackageRevisionEntityId = revision.Id,
            MigrationCandidateArtifactEntityId = candidate.Id,
            MigrationStagingRunEntityId = staging.Id,
            AuthorityType = MigrationProductionAuthorityTypes.OperatorAttestedSnapshot,
            Status = MigrationProductionAuthorityStatuses.Active,
            ActiveMigrationKey = migrationId,
            EncryptedPackageSha256 = revision.EncryptedPackageSha256,
            DecryptedArchiveSha256 = revision.DecryptedArchiveSha256,
            SourceMigrationId = revision.ArchiveMigrationId,
            CaptureKind = "preview",
            SourceFrozen = false,
            RehearsalOnly = true,
        };
        intake.ProductionAuthorities.Add(authority);

        var plan = new MigrationProductionAdoptionEntity
        {
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            MigrationPackageRevisionEntityId = revision.Id,
            PackageRevision = revision,
            MigrationCandidateArtifactEntityId = candidate.Id,
            CandidateArtifact = candidate,
            MigrationStagingRunEntityId = staging.Id,
            StagingRun = staging,
        };

        return new OperatorAttestedPlanFixture(plan, authority);
    }

    private static MigrationProductionAdoptionService.AdoptionPlanFingerprint CreateFingerprint(
        string stagingRunId,
        string? productionAuthorityId = null,
        string elementPublicHost = "chat-davids.deltabox.dev") =>
        new(
            MigrationId: "mig_production_adoption",
            ProductionAuthorityId: productionAuthorityId,
            ProductionAuthorityType: MigrationProductionAuthorityTypes.FinalFrozen,
            ProductionAuthorityEvidenceSha256: null,
            PackageRevisionId: "mpr_final",
            PackageRevisionSha256: new string('1', 64),
            CandidateArtifactId: "mca_final",
            CandidateArtifactSha256: new string('2', 64),
            StagingRunId: stagingRunId,
            RuntimeStackId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            TargetStackSlug: "davids-stack",
            MatrixInstanceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            ElementInstanceId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
            MatrixServerName: "matrix-davids.deltabox.dev",
            MatrixPublicHost: "matrix-davids.deltabox.dev",
            ElementPublicHost: elementPublicHost,
            RuntimeNetworkName: "mem-gateway",
            MatrixContainerName: "mem-matrix-davids-stack",
            ElementContainerName: "mem-element-davids-stack",
            MatrixImageReference: "sha256:synapse-approved",
            MatrixImageId: "sha256:synapse-local",
            ElementImageReference: "sha256:element-approved",
            ElementImageId: "sha256:element-local",
            DatabaseName: "mem_davids_stack_11111111",
            DatabaseUsername: "mem_davids_stack_11111111",
            Routes:
            [
                new MigrationProductionAdoptionRoutePlan(
                    "matrix",
                    "matrix-davids.deltabox.dev",
                    "https://matrix-davids.deltabox.dev",
                    "http",
                    "mem-matrix-davids-stack",
                    8008,
                    "npm",
                    true),
                new MigrationProductionAdoptionRoutePlan(
                    "element-web",
                    elementPublicHost,
                    $"https://{elementPublicHost}",
                    "http",
                    "mem-element-davids-stack",
                    80,
                    "npm",
                    true),
            ]);

    private sealed record OperatorAttestedPlanFixture(
        MigrationProductionAdoptionEntity Plan,
        MigrationProductionAuthorityEntity Authority);
}
