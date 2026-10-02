using HostAgent.Runtime.Migrations.ProductionAdoption;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;

namespace HostAgent.Tests.Runtime.Migrations.ProductionAdoption;

public sealed class MigrationProductionRollbackTests
{
    [Fact]
    public void Every_target_rollback_acknowledgement_is_required()
    {
        var missing = MigrationProductionRollbackService.GetMissingAcknowledgements(
            new ExecuteMigrationProductionRollbackRequest(
                Operator: null,
                Note: null,
                PreviewId: "mprv_test",
                ExecuteTargetRollback: false,
                AcknowledgeMigrationNotAccepted: false,
                AcknowledgeRestoresPreCutoverRoutes: false,
                AcknowledgeStopsTargetContainers: false,
                AcknowledgePreservesTargetData: false,
                AcknowledgeSourceRemainsFrozen: false,
                AcknowledgeSourceRestorationRequiresHandoff: false,
                AcknowledgeNoAutomaticSourceHostMutation: false));

        Assert.Equal(
        [
            "executeTargetRollback",
            "acknowledgeMigrationNotAccepted",
            "acknowledgeRestoresPreCutoverRoutes",
            "acknowledgeStopsTargetContainers",
            "acknowledgePreservesTargetData",
            "acknowledgeSourceRemainsFrozen",
            "acknowledgeSourceRestorationRequiresHandoff",
            "acknowledgeNoAutomaticSourceHostMutation",
        ], missing);
    }

    [Fact]
    public void Target_route_must_match_completed_cutover_identity_and_forward_target()
    {
        var checkpoint = new MigrationProductionCutoverService.RouteCheckpoint(
            ServiceKey: "matrix",
            PublicHost: "matrix.example.test",
            DesiredForwardHost: "mem-matrix-example",
            DesiredForwardPort: 8008,
            ExistingRouteFound: true,
            ExistingRouteId: 4,
            ExistingForwardScheme: "http",
            ExistingForwardHost: "legacy-matrix",
            ExistingForwardPort: 8008,
            ExistingCertificateId: 3,
            ExistingSslForced: true,
            ExistingHttp2: true,
            ExistingEnabled: true,
            ExistingAdvancedConfig: string.Empty,
            ExistingAdvancedConfigSha256: Sha256(string.Empty),
            AlreadyTargetsProductionRuntime: false,
            Action: "update");
        var current = CreateProxyHost(
            id: 11,
            forwardHost: "mem-matrix-example",
            forwardPort: 8008);

        Assert.True(MigrationProductionRollbackService.TargetRouteMatches(
            checkpoint,
            expectedRouteId: 11,
            current));
        Assert.False(MigrationProductionRollbackService.TargetRouteMatches(
            checkpoint,
            expectedRouteId: 12,
            current));
        Assert.False(MigrationProductionRollbackService.TargetRouteMatches(
            checkpoint,
            expectedRouteId: 11,
            current with { forward_host = "drifted-target" }));
    }

    [Fact]
    public void Exact_npm_snapshot_detects_non_target_configuration_drift()
    {
        var current = CreateProxyHost(
            id: 11,
            forwardHost: "mem-matrix-example",
            forwardPort: 8008);
        var snapshot = NpmProxyHostService.CaptureSnapshot(current);

        Assert.True(NpmProxyHostService.SnapshotMatches(snapshot, current));
        Assert.False(NpmProxyHostService.SnapshotMatches(
            snapshot,
            current with { trust_forwarded_proto = true }));
        Assert.False(NpmProxyHostService.SnapshotMatches(
            snapshot,
            current with { advanced_config = "proxy_read_timeout 600;" }));
    }

    [Fact]
    public void Source_handoff_payload_hash_is_deterministic_and_content_bound()
    {
        var payload = CreatePayload("fingerprint-a");
        var same = CreatePayload("fingerprint-a");
        var changed = CreatePayload("fingerprint-b");

        Assert.Equal(
            MigrationProductionRollbackService.ComputePayloadSha256(payload),
            MigrationProductionRollbackService.ComputePayloadSha256(same));
        Assert.NotEqual(
            MigrationProductionRollbackService.ComputePayloadSha256(payload),
            MigrationProductionRollbackService.ComputePayloadSha256(changed));
    }

    private static MigrationProductionSourceRestorationHandoffPayload CreatePayload(
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
                new MigrationProductionSourceRestorationRouteEvidence(
                    "matrix",
                    "matrix.example.test",
                    "restored",
                    4,
                    "http",
                    "legacy-matrix",
                    8008,
                    true,
                    new string('3', 64)),
            ],
            TargetContainers:
            [
                new MigrationProductionSourceRestorationContainerEvidence(
                    "matrix",
                    "mem-matrix-example",
                    "container-id",
                    true),
            ]);

    private static NpmProxyHost CreateProxyHost(
        int id,
        string forwardHost,
        int forwardPort) =>
        new(
            id,
            ["matrix.example.test"],
            forwardHost,
            forwardPort,
            0,
            3,
            "http",
            string.Empty,
            null,
            [],
            true,
            true,
            true,
            true,
            false,
            true,
            false,
            false,
            false);

    private static string Sha256(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
