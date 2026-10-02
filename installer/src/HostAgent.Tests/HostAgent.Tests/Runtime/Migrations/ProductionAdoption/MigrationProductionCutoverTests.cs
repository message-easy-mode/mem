using System.Text.Json;
using HostAgent.Runtime.Migrations.ProductionAdoption;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Modules.Integrations.Npm.Contracts;

namespace HostAgent.Tests.Runtime.Migrations.ProductionAdoption;

public sealed class MigrationProductionCutoverTests
{
    [Fact]
    public void Every_controlled_public_cutover_acknowledgement_is_required()
    {
        var missing = MigrationProductionCutoverService.GetMissingAcknowledgements(
            new ExecuteMigrationProductionCutoverRequest(
                Operator: null,
                Note: null,
                PreviewId: "mpcv_test",
                ExecuteNpmRouteMutation: false,
                AcknowledgeSourceFrozen: false,
                AcknowledgePrivateRuntimeHealthy: false,
                AcknowledgeRouteSnapshotReviewed: false,
                AcknowledgeCreatesPublicRoutes: false,
                AcknowledgeNoDnsMutation: false,
                AcknowledgeNoCertificateMutation: false,
                AcknowledgeRollbackIsNextSlice: false,
                AcknowledgePostCutoverVerificationRequired: false,
                AcknowledgeProductionAuthority: false),
            sourceFrozen: false);

        Assert.Equal(
        [
            "executeNpmRouteMutation",
            "acknowledgeProductionAuthority",
            "acknowledgePrivateRuntimeHealthy",
            "acknowledgeRouteSnapshotReviewed",
            "acknowledgeCreatesPublicRoutes",
            "acknowledgeNoDnsMutation",
            "acknowledgeNoCertificateMutation",
            "acknowledgeRollbackIsNextSlice",
            "acknowledgePostCutoverVerificationRequired",
        ], missing);
    }

    [Fact]
    public void Final_frozen_sessions_accept_the_existing_source_frozen_acknowledgement()
    {
        var missing = MigrationProductionCutoverService.GetMissingAcknowledgements(
            new ExecuteMigrationProductionCutoverRequest(
                Operator: null,
                Note: null,
                PreviewId: "mpcv_final",
                ExecuteNpmRouteMutation: true,
                AcknowledgeSourceFrozen: true,
                AcknowledgePrivateRuntimeHealthy: true,
                AcknowledgeRouteSnapshotReviewed: true,
                AcknowledgeCreatesPublicRoutes: true,
                AcknowledgeNoDnsMutation: true,
                AcknowledgeNoCertificateMutation: true,
                AcknowledgeRollbackIsNextSlice: true,
                AcknowledgePostCutoverVerificationRequired: true),
            sourceFrozen: true);

        Assert.Empty(missing);
    }

    [Fact]
    public void Unchanged_existing_route_matches_preview_snapshot()
    {
        var expected = new MigrationProductionCutoverRouteSnapshot(
            ServiceKey: "matrix",
            PublicHost: "matrix.example.test",
            DesiredForwardHost: "mem-matrix-example",
            DesiredForwardPort: 8008,
            ExistingRouteFound: true,
            ExistingRouteId: 7,
            ExistingForwardScheme: "http",
            ExistingForwardHost: "old-matrix",
            ExistingForwardPort: 8008,
            ExistingCertificateId: 3,
            ExistingSslForced: true,
            ExistingHttp2: true,
            ExistingEnabled: true,
            ExistingAdvancedConfigSha256: Sha256(string.Empty),
            AlreadyTargetsProductionRuntime: false,
            Action: "update");
        var current = new NpmProxyHost(
            id: 7,
            domain_names: ["matrix.example.test"],
            forward_host: "old-matrix",
            forward_port: 8008,
            access_list_id: 0,
            certificate_id: 3,
            forward_scheme: "http",
            advanced_config: string.Empty,
            meta: null,
            locations: null,
            ssl_forced: true,
            http2_support: true,
            allow_websocket_upgrade: true,
            block_exploits: true,
            caching_enabled: false,
            enabled: true,
            hsts_enabled: false,
            hsts_subdomains: false,
            trust_forwarded_proto: false);

        Assert.True(MigrationProductionCutoverService.RouteSnapshotMatches(expected, current));
        Assert.False(MigrationProductionCutoverService.RouteSnapshotMatches(
            expected,
            current with { forward_host = "unexpected-target" }));
    }


    [Fact]
    public void Active_wildcard_certificate_covers_both_planned_single_label_hosts()
    {
        var domain = new DomainEntity
        {
            Id = Guid.NewGuid(),
            BaseDomain = "matrixeasyhost.com",
            DisplayName = "matrixeasyhost.com",
            Purpose = "stack",
            DnsProvider = "desec",
            Status = "Pending",
        };
        var certificate = new CertificateEntity
        {
            Id = Guid.NewGuid(),
            Domain = domain,
            DomainId = domain.Id,
            CertificateId = "cert-matrixeasyhost",
            CommonName = "*.matrixeasyhost.com",
            Provider = "desec",
            IsWildcard = true,
            IsActive = true,
            Status = "Valid",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(30),
        };
        domain.ActiveCertificateId = certificate.Id;

        var selected = MigrationProductionCertificateResolver.SelectCertificate(
            [certificate],
            [
                "matrix-e01affa9.matrixeasyhost.com",
                "element-64b263fd.matrixeasyhost.com",
            ],
            DateTime.UtcNow);

        Assert.Same(certificate, selected);
        Assert.True(MigrationProductionCertificateResolver.CertificateCoversHost(
            certificate,
            "matrix-e01affa9.matrixeasyhost.com"));
        Assert.False(MigrationProductionCertificateResolver.CertificateCoversHost(
            certificate,
            "nested.matrix-e01affa9.matrixeasyhost.com"));
    }

    [Fact]
    public void Expired_or_non_covering_certificate_is_not_selected()
    {
        var domain = new DomainEntity
        {
            Id = Guid.NewGuid(),
            BaseDomain = "example.test",
            DisplayName = "example.test",
            Purpose = "stack",
            DnsProvider = "test",
            Status = "Active",
        };
        var certificate = new CertificateEntity
        {
            Id = Guid.NewGuid(),
            Domain = domain,
            DomainId = domain.Id,
            CertificateId = "cert-example",
            CommonName = "*.example.test",
            Provider = "test",
            IsWildcard = true,
            IsActive = true,
            Status = "Valid",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-30),
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(-1),
        };

        Assert.Null(MigrationProductionCertificateResolver.SelectCertificate(
            [certificate],
            ["matrix.matrixeasyhost.com", "element.matrixeasyhost.com"],
            DateTime.UtcNow));
    }


    [Fact]
    public void Compensated_failed_cutover_can_prepare_a_fresh_preview()
    {
        var plan = CreatePrivateReadyPlan();
        plan.Status = "cutover-failed";
        plan.CutoverStatus = "failed";
        plan.CutoverPreviewId = "mpcv_previous";
        plan.CutoverPreviewSha256 = "preview-sha";
        plan.CutoverExecutionId = "mpce_previous";
        plan.CutoverStartedAtUtc = DateTime.UtcNow.AddMinutes(-2);
        plan.CutoverCompletedAtUtc = DateTime.UtcNow.AddMinutes(-1);
        plan.CutoverFailureCode = "migration_production_cutover_npm_failed";
        plan.CutoverFailureSummary = "NPM failed.";
        plan.RouteCompensationAttempted = true;
        plan.RouteCompensationCompleted = true;
        plan.CutoverEvidenceJson = "{\"failure\":\"preserved\"}";

        Assert.True(MigrationProductionCutoverService.IsSafeCompensatedCutoverFailure(plan));

        var preparedAtUtc = DateTime.UtcNow;
        var ancestry = MigrationProductionCutoverService.ResetFailedCutoverForFreshPreview(
            plan,
            "mpcv_retry",
            preparedAtUtc);

        Assert.NotNull(ancestry);
        Assert.Equal("mpce_previous", ancestry.PreviousExecutionId);
        Assert.Equal("mpcv_previous", ancestry.PreviousPreviewId);
        Assert.Equal("mpcv_retry", ancestry.SupersededByPreviewId);
        Assert.True(ancestry.RouteCompensationCompleted);
        Assert.Equal("{\"failure\":\"preserved\"}", ancestry.PreviousEvidenceJson);
        Assert.Null(plan.CutoverExecutionId);
        Assert.Null(plan.CutoverStatus);
        Assert.Null(plan.CutoverFailureCode);
        Assert.Null(plan.CutoverFailureSummary);
        Assert.False(plan.RouteCompensationAttempted);
        Assert.False(plan.RouteCompensationCompleted);
        Assert.False(plan.PublicRoutesCreated);
        Assert.False(plan.RuntimePromotionCompleted);
    }

    [Fact]
    public void Preview_ready_plan_without_execution_can_replace_a_prior_snapshot_without_expiry_semantics()
    {
        var now = DateTime.UtcNow;
        var plan = CreatePrivateReadyPlan();
        plan.Status = "cutover-preview-ready";
        plan.CutoverPreviewId = "mpcv_expired";
        plan.CutoverPreviewStatus = "ready";
        plan.CutoverPreviewCreatedAtUtc = now.AddMinutes(-20);
        plan.CutoverPreviewExpiresAtUtc = now.AddMinutes(-5);
        plan.CutoverPreviewSha256 = "expired-preview-sha";
        plan.CutoverPreviewJson = "{\"preview\":\"preserved\"}";

        Assert.True(MigrationProductionCutoverService.IsSafePreviewReplacement(plan));

        var ancestry = MigrationProductionCutoverService.CapturePreviewRefreshAncestry(
            plan,
            "mpcv_fresh",
            now);

        Assert.NotNull(ancestry);
        Assert.Equal("mpcv_expired", ancestry.PreviousPreviewId);
        Assert.Equal("operator-refresh", ancestry.ReplacementReason);
        Assert.Equal("mpcv_fresh", ancestry.SupersededByPreviewId);
        Assert.Equal("expired-preview-sha", ancestry.PreviousSnapshotSha256);
        Assert.Equal("{\"preview\":\"preserved\"}", ancestry.PreviousEvidenceJson);
    }

    [Theory]
    [InlineData("mpce_started", null, false, false)]
    [InlineData(null, "executing", false, false)]
    [InlineData(null, null, true, false)]
    [InlineData(null, null, false, true)]
    public void Preview_replacement_is_rejected_when_execution_or_public_ownership_may_exist(
        string? executionId,
        string? cutoverStatus,
        bool publicRoutesCreated,
        bool runtimePromotionCompleted)
    {
        var plan = CreatePrivateReadyPlan();
        plan.Status = "cutover-preview-ready";
        plan.CutoverPreviewId = "mpcv_active";
        plan.CutoverExecutionId = executionId;
        plan.CutoverStatus = cutoverStatus;
        plan.PublicRoutesCreated = publicRoutesCreated;
        plan.RuntimePromotionCompleted = runtimePromotionCompleted;

        Assert.False(MigrationProductionCutoverService.IsSafePreviewReplacement(plan));
        Assert.Null(MigrationProductionCutoverService.CapturePreviewRefreshAncestry(
            plan,
            "mpcv_replacement",
            DateTime.UtcNow));
    }

    [Fact]
    public void MIGRATION_CUTOVER_STATE_VERSION_CORR_01_reuses_identical_blocked_preview_without_durable_mutation()
    {
        var plan = CreatePrivateReadyPlan();
        var routes = Array.Empty<MigrationProductionCutoverRouteSnapshot>();
        var checkpoints = Array.Empty<MigrationProductionCutoverService.RouteCheckpoint>();
        var blockers = new[]
        {
            "Active certificate '*.matrixeasyhost.com' is not ready in Nginx Proxy Manager.",
        };
        SeedDurablePreview(plan, "blocked", routes, checkpoints, blockers);
        var previewId = plan.CutoverPreviewId;
        var createdAtUtc = plan.CutoverPreviewCreatedAtUtc;
        var updatedAtUtc = plan.UpdatedAtUtc;
        var snapshotSha256 = plan.CutoverPreviewSha256;
        var previewJson = plan.CutoverPreviewJson;

        var reusable = MigrationProductionCutoverService.CanReuseEquivalentPreview(
            plan,
            "blocked",
            routes,
            checkpoints,
            blockers);

        Assert.True(reusable);
        Assert.Equal(previewId, plan.CutoverPreviewId);
        Assert.Equal(createdAtUtc, plan.CutoverPreviewCreatedAtUtc);
        Assert.Equal(updatedAtUtc, plan.UpdatedAtUtc);
        Assert.Equal(snapshotSha256, plan.CutoverPreviewSha256);
        Assert.Equal(previewJson, plan.CutoverPreviewJson);
    }

    [Fact]
    public void MIGRATION_CUTOVER_STATE_VERSION_CORR_01_reuses_identical_ready_preview()
    {
        var plan = CreatePrivateReadyPlan();
        var routes = Array.Empty<MigrationProductionCutoverRouteSnapshot>();
        var checkpoints = Array.Empty<MigrationProductionCutoverService.RouteCheckpoint>();
        var blockers = Array.Empty<string>();
        SeedDurablePreview(plan, "ready", routes, checkpoints, blockers);

        Assert.True(MigrationProductionCutoverService.CanReuseEquivalentPreview(
            plan,
            "ready",
            routes,
            checkpoints,
            blockers));
    }

    [Fact]
    public void MIGRATION_CUTOVER_STATE_VERSION_CORR_01_requires_fresh_preview_when_observed_blockers_change()
    {
        var plan = CreatePrivateReadyPlan();
        var routes = Array.Empty<MigrationProductionCutoverRouteSnapshot>();
        var checkpoints = Array.Empty<MigrationProductionCutoverService.RouteCheckpoint>();
        SeedDurablePreview(
            plan,
            "blocked",
            routes,
            checkpoints,
            ["Certificate is not ready in NPM."]);

        Assert.False(MigrationProductionCutoverService.CanReuseEquivalentPreview(
            plan,
            "ready",
            routes,
            checkpoints,
            Array.Empty<string>()));
    }

    [Fact]
    public void MIGRATION_CUTOVER_STATE_VERSION_CORR_01_does_not_reuse_preview_across_failed_cutover_state()
    {
        var plan = CreatePrivateReadyPlan();
        var routes = Array.Empty<MigrationProductionCutoverRouteSnapshot>();
        var checkpoints = Array.Empty<MigrationProductionCutoverService.RouteCheckpoint>();
        SeedDurablePreview(plan, "ready", routes, checkpoints, Array.Empty<string>());
        plan.Status = "cutover-failed";
        plan.CutoverExecutionId = "mpce_failed";
        plan.CutoverStatus = "failed";
        plan.CutoverCompletedAtUtc = DateTime.UtcNow;
        plan.RouteCompensationAttempted = true;
        plan.RouteCompensationCompleted = true;

        Assert.False(MigrationProductionCutoverService.CanReuseEquivalentPreview(
            plan,
            "ready",
            routes,
            checkpoints,
            Array.Empty<string>()));
    }

    [Fact]
    public void MIGRATION_CUTOVER_STATE_VERSION_CORR_01_rejects_corrupt_durable_preview_hash()
    {
        var plan = CreatePrivateReadyPlan();
        var routes = Array.Empty<MigrationProductionCutoverRouteSnapshot>();
        var checkpoints = Array.Empty<MigrationProductionCutoverService.RouteCheckpoint>();
        SeedDurablePreview(plan, "blocked", routes, checkpoints, ["Still blocked."]);
        plan.CutoverPreviewSha256 = new string('f', 64);

        Assert.False(MigrationProductionCutoverService.CanReuseEquivalentPreview(
            plan,
            "blocked",
            routes,
            checkpoints,
            ["Still blocked."]));
    }

    [Theory]
    [InlineData(true, true, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, true)]
    public void Unsafe_failed_cutover_cannot_prepare_a_fresh_preview(
        bool publicRoutesCreated,
        bool compensationAttempted,
        bool compensationCompleted,
        bool runtimePromotionCompleted)
    {
        var plan = CreatePrivateReadyPlan();
        plan.Status = "cutover-failed";
        plan.CutoverStatus = "failed";
        plan.CutoverExecutionId = "mpce_failed";
        plan.CutoverCompletedAtUtc = DateTime.UtcNow;
        plan.PublicRoutesCreated = publicRoutesCreated;
        plan.RouteCompensationAttempted = compensationAttempted;
        plan.RouteCompensationCompleted = compensationCompleted;
        plan.RuntimePromotionCompleted = runtimePromotionCompleted;

        Assert.False(MigrationProductionCutoverService.IsSafeCompensatedCutoverFailure(plan));
        Assert.Null(MigrationProductionCutoverService.ResetFailedCutoverForFreshPreview(
            plan,
            "mpcv_retry",
            DateTime.UtcNow));
        Assert.Equal("mpce_failed", plan.CutoverExecutionId);
    }

    private static MigrationProductionAdoptionEntity CreatePrivateReadyPlan() =>
        new()
        {
            Id = Guid.NewGuid(),
            AdoptionPlanId = "madp_test",
            Status = "private-runtime-ready",
            PlanSha256 = new string('5', 64),
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            MaterializationStatus = "private-runtime-ready",
            ProductionDatabaseImported = true,
            MatrixProductionContainerStarted = true,
            MatrixProductionHealthPassed = true,
            ElementProductionContainerStarted = true,
            ElementProductionHealthPassed = true,
            RuntimeManifestSaved = true,
            DatabaseOwnershipSaved = true,
            RuntimeRecordsCreated = true,
            PublicRoutesCreated = false,
            RuntimePromotionCompleted = false,
        };


    private static void SeedDurablePreview(
        MigrationProductionAdoptionEntity plan,
        string previewStatus,
        IReadOnlyList<MigrationProductionCutoverRouteSnapshot> routes,
        IReadOnlyList<MigrationProductionCutoverService.RouteCheckpoint> checkpoints,
        IReadOnlyList<string> blockers)
    {
        plan.Status = string.Equals(previewStatus, "ready", StringComparison.OrdinalIgnoreCase)
            ? "cutover-preview-ready"
            : "private-runtime-ready";
        plan.CutoverPreviewId = "mpcv_existing";
        plan.CutoverPreviewStatus = previewStatus;
        plan.CutoverPreviewCreatedAtUtc = DateTime.UtcNow.AddSeconds(-30);
        plan.CutoverPreviewExpiresAtUtc = null;
        plan.CutoverPreviewJson = JsonSerializer.Serialize(
            new MigrationProductionCutoverService.PreviewEvidence(
                plan.AdoptionPlanId,
                plan.PlanSha256,
                routes,
                checkpoints,
                blockers),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        plan.CutoverPreviewSha256 = MigrationProductionCutoverService.ComputePreviewSha256(
            plan.PlanSha256,
            checkpoints,
            retryAncestry: null,
            previewRefreshAncestry: null);
        plan.BlockerSummary = blockers.Count == 0
            ? null
            : string.Join("; ", blockers);
    }

    private static string Sha256(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
