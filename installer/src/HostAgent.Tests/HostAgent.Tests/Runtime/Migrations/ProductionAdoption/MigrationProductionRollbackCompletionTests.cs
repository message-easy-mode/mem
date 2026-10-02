using HostAgent.Runtime.Migrations.ProductionAdoption;

namespace HostAgent.Tests.Runtime.Migrations.ProductionAdoption;

public sealed class MigrationProductionRollbackCompletionTests
{
    [Fact]
    public void Completion_payload_hash_is_deterministic_and_content_bound()
    {
        var payload = CreateCompletion("fingerprint-a");
        var same = CreateCompletion("fingerprint-a");
        var changed = CreateCompletion("fingerprint-b");

        Assert.Equal(
            MigrationProductionRollbackCompletionService.ComputeCompletionPayloadSha256(payload),
            MigrationProductionRollbackCompletionService.ComputeCompletionPayloadSha256(same));
        Assert.NotEqual(
            MigrationProductionRollbackCompletionService.ComputeCompletionPayloadSha256(payload),
            MigrationProductionRollbackCompletionService.ComputeCompletionPayloadSha256(changed));
    }

    [Fact]
    public void Completion_must_bind_to_the_exact_target_handoff()
    {
        var handoff = CreateHandoff("fingerprint-a");
        var completion = CreateCompletion("fingerprint-a") with
        {
            SourceHandoffSha256 = MigrationProductionRollbackService.ComputePayloadSha256(handoff)
        };

        MigrationProductionRollbackCompletionService.ValidateCompletionBindings(completion, handoff);

        var changed = completion with { TargetRollbackExecutionId = "mpre_other" };
        Assert.Throws<InvalidDataException>(() =>
            MigrationProductionRollbackCompletionService.ValidateCompletionBindings(changed, handoff));
    }

    [Fact]
    public void Completion_requires_verified_matrix_and_element_restoration()
    {
        var valid = CreateCompletion(new string('b', 64));
        MigrationProductionRollbackCompletionService.ValidateSourceRestorationProof(valid);

        var invalid = valid with
        {
            Containers = valid.Containers.Select(item =>
                item.Role == "element" ? item with { ServiceVerified = false } : item).ToArray()
        };
        Assert.Throws<InvalidDataException>(() =>
            MigrationProductionRollbackCompletionService.ValidateSourceRestorationProof(invalid));
    }

    private static MigrationProductionSourceRestorationCompletionPayload CreateCompletion(
        string fingerprint) =>
        new(
            RestorationAttemptId: "mm01cb-test",
            CompletedAtUtc: new DateTime(2026, 7, 20, 8, 0, 0, DateTimeKind.Utc),
            SourceHandoffId: "mpsh_test",
            SourceHandoffSha256: new string('f', 64),
            MigrationId: "mig_test",
            SourceMigrationId: "source-migration",
            TargetRollbackExecutionId: "mpre_test",
            FreezeAttemptId: "freeze-test",
            FreezePlanId: "plan-test",
            FreezePlanHash: new string('a', 64),
            SourceFingerprint: fingerprint,
            SourceStackSlug: "legacy-stack",
            MatrixServerName: "matrix.example.test",
            SourceRestored: true,
            RestartPoliciesRestored: true,
            OriginalRunningStatesRestored: true,
            MatrixVerified: true,
            ElementVerified: true,
            TargetRollbackAuthorityVerified: true,
            DevelopmentExternalControlPlane: false,
            Containers:
            [
                Container("matrix"),
                Container("element"),
            ],
            TargetRouteEvidence:
            [
                new MigrationProductionSourceRestorationRouteEvidence(
                    "matrix", "matrix.example.test", "restored", 4, "http", "legacy-matrix", 8008, true, new string('1', 64)),
                new MigrationProductionSourceRestorationRouteEvidence(
                    "element-web", "chat.example.test", "restored", 5, "http", "legacy-element", 80, true, new string('2', 64)),
            ]);

    private static MigrationProductionSourceRestorationHandoffPayload CreateHandoff(
        string fingerprint) =>
        new(
            HandoffId: "mpsh_test",
            CreatedAtUtc: new DateTime(2026, 7, 20, 7, 0, 0, DateTimeKind.Utc),
            MigrationId: "mig_test",
            AdoptionPlanId: "madp_test",
            CutoverExecutionId: "mpce_test",
            TargetRollbackExecutionId: "mpre_test",
            PlanSha256: new string('3', 64),
            PackageRevisionId: "mpr_final",
            PackageRevisionSha256: new string('4', 64),
            SourceMigrationId: "source-migration",
            SourceId: "source-1",
            SourceFingerprint: fingerprint,
            SourceStackSlug: "legacy-stack",
            MatrixServerName: "matrix.example.test",
            SourceFrozen: true,
            MigrationAccepted: false,
            TargetRoutesRestored: true,
            TargetRuntimeRoutesRemoved: true,
            TargetContainersStopped: true,
            Routes:
            [
                new MigrationProductionSourceRestorationRouteEvidence(
                    "matrix", "matrix.example.test", "restored", 4, "http", "legacy-matrix", 8008, true, new string('1', 64)),
                new MigrationProductionSourceRestorationRouteEvidence(
                    "element-web", "chat.example.test", "restored", 5, "http", "legacy-element", 80, true, new string('2', 64)),
            ],
            TargetContainers:
            [
                new MigrationProductionSourceRestorationContainerEvidence("matrix", "mem-matrix-target", "target-matrix", true),
                new MigrationProductionSourceRestorationContainerEvidence("element-web", "mem-element-target", "target-element", true),
            ]);

    private static MigrationProductionSourceRestorationCompletionContainerResult Container(
        string role) =>
        new(
            Role: role,
            ContainerId: $"{role}-id",
            ContainerName: $"legacy-{role}",
            ImageId: $"sha256:{role}",
            OriginalRestartPolicy: "unless-stopped",
            WasRunning: true,
            FinalState: "running",
            FinalRestartPolicy: "unless-stopped",
            FinalHealth: "healthy",
            RunningStateRestored: true,
            RestartPolicyRestored: true,
            ServiceVerified: true);
}
