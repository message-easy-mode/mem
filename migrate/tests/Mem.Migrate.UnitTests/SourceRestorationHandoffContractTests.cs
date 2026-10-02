using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Legacy.V010.Cutover;

namespace Mem.Migrate.UnitTests;

public sealed class SourceRestorationHandoffContractTests
{
    [Fact]
    public void Installer_v1_payload_hash_is_deterministic_and_content_bound()
    {
        var payload = CreatePayload("source-a");
        var same = CreatePayload("source-a");
        var changed = CreatePayload("source-b");

        Assert.Equal(
            V010SourceRestorationService.ComputePayloadSha256(payload),
            V010SourceRestorationService.ComputePayloadSha256(same));
        Assert.NotEqual(
            V010SourceRestorationService.ComputePayloadSha256(payload),
            V010SourceRestorationService.ComputePayloadSha256(changed));
        Assert.Equal(
            "mem.migration.source-restoration-handoff.v1",
            V010SourceRestorationService.HandoffSchemaVersion);
    }


    [Fact]
    public void Completion_payload_hash_is_deterministic_and_content_bound()
    {
        var payload = new SourceRestorationCompletionPayload(
            RestorationAttemptId: "mm01cb-proof",
            CompletedAtUtc: new DateTime(2026, 7, 19, 8, 0, 0, DateTimeKind.Utc),
            SourceHandoffId: "mpsh_test",
            SourceHandoffSha256: new string('1', 64),
            MigrationId: "mig_test",
            SourceMigrationId: "source-migration",
            TargetRollbackExecutionId: "mpre_test",
            FreezeAttemptId: "mm06b-proof",
            FreezePlanId: "mm06a-proof",
            FreezePlanHash: new string('2', 64),
            SourceFingerprint: new string('3', 64),
            SourceStackSlug: "legacy-stack",
            MatrixServerName: "matrix.example.test",
            SourceRestored: true,
            RestartPoliciesRestored: true,
            OriginalRunningStatesRestored: true,
            MatrixVerified: true,
            ElementVerified: true,
            TargetRollbackAuthorityVerified: true,
            DevelopmentExternalControlPlane: false,
            Containers: [],
            TargetRouteEvidence: []);
        var changed = payload with { ElementVerified = false };

        Assert.Equal(
            SourceRestorationRenderer.ComputePayloadSha256(payload),
            SourceRestorationRenderer.ComputePayloadSha256(payload));
        Assert.NotEqual(
            SourceRestorationRenderer.ComputePayloadSha256(payload),
            SourceRestorationRenderer.ComputePayloadSha256(changed));
        Assert.Equal(
            "mem.migration.source-restoration-completion.v1",
            SourceRestorationRenderer.CompletionSchemaVersion);
    }

    private static SourceRestorationHandoffPayload CreatePayload(
        string sourceFingerprint) =>
        new(
            HandoffId: "mpsh_test",
            CreatedAtUtc: new DateTime(2026, 7, 19, 7, 0, 0, DateTimeKind.Utc),
            MigrationId: "mig_test",
            AdoptionPlanId: "madp_test",
            CutoverExecutionId: "mpce_test",
            TargetRollbackExecutionId: "mpre_test",
            PlanSha256: new string('1', 64),
            PackageRevisionId: "mpr_final",
            PackageRevisionSha256: new string('2', 64),
            SourceMigrationId: "source-migration",
            SourceId: "source-1",
            SourceFingerprint: sourceFingerprint,
            SourceStackSlug: "legacy-stack",
            MatrixServerName: "matrix.example.test",
            SourceFrozen: true,
            MigrationAccepted: false,
            TargetRoutesRestored: true,
            TargetRuntimeRoutesRemoved: true,
            TargetContainersStopped: true,
            Routes:
            [
                new SourceRestorationRouteEvidence(
                    "matrix",
                    "matrix.example.test",
                    "restored",
                    4,
                    "http",
                    "legacy-matrix",
                    8008,
                    true,
                    new string('3', 64)),
                new SourceRestorationRouteEvidence(
                    "element-web",
                    "chat.example.test",
                    "restored",
                    5,
                    "http",
                    "legacy-element",
                    80,
                    true,
                    new string('4', 64))
            ],
            TargetContainers:
            [
                new SourceRestorationTargetContainerEvidence(
                    "matrix",
                    "mem-matrix-example",
                    "container-id-1",
                    true),
                new SourceRestorationTargetContainerEvidence(
                    "element-web",
                    "mem-element-example",
                    "container-id-2",
                    true)
            ]);
}
