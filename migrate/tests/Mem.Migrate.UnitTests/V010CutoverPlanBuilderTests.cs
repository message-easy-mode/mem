using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Target;
using Mem.Migrate.Legacy.V010.Cutover;

namespace Mem.Migrate.UnitTests;

public sealed class V010CutoverPlanBuilderTests
{
    [Fact]
    public void Builds_exact_non_mutating_plan_from_fresh_source_and_destroyed_private_stage()
    {
        var fixture = CreateFixture();

        var plan = V010CutoverPlanBuilder.Build(
            fixture.Assessment,
            fixture.Stage,
            fixture.Options,
            DateTimeOffset.Parse("2026-07-14T00:00:00Z"));

        Assert.False(plan.SourceMutationOccurred);
        Assert.False(plan.PublicRoutingMutationOccurred);
        Assert.Equal(fixture.Assessment.SourceFingerprint, plan.SourceFingerprint);
        Assert.Equal(4, plan.SourceContainersToFreeze.Length);
        Assert.Single(plan.RetainedSourceContainers);
        Assert.Single(plan.Stacks);
        Assert.Equal("matrix.example.test", plan.Stacks[0].MatrixServerName);
        Assert.True(plan.TargetEvidence.StagingDestroyed);
        Assert.Equal(
            new[] { "legacy-api", "legacy-web", "element", "matrix" },
            plan.SourceContainersToFreeze.Select(item => item.Role).ToArray());
    }

    [Fact]
    public void Rejects_source_drift()
    {
        var fixture = CreateFixture();
        var options = fixture.Options with
        {
            ExpectedSourceFingerprint = new string('f', 64)
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            V010CutoverPlanBuilder.Build(
                fixture.Assessment,
                fixture.Stage,
                options,
                DateTimeOffset.UtcNow));

        Assert.Contains("does not match", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_retained_private_staging_runtime()
    {
        var fixture = CreateFixture();
        var stage = fixture.Stage with
        {
            DestroyRequested = false,
            DestroySucceeded = false
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            V010CutoverPlanBuilder.Build(
                fixture.Assessment,
                stage,
                fixture.Options,
                DateTimeOffset.UtcNow));

        Assert.Contains("must be destroyed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_ambiguous_legacy_api_inventory()
    {
        var fixture = CreateFixture();
        var duplicateApi = Container(
            "mem-api",
            "api-2",
            "ghcr.io/matrix-easy-mode/mem-api:0.1.0",
            "api");
        var assessment = fixture.Assessment with
        {
            Docker = fixture.Assessment.Docker with
            {
                Containers = [.. fixture.Assessment.Docker.Containers, duplicateApi]
            }
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            V010CutoverPlanBuilder.Build(
                assessment,
                fixture.Stage,
                fixture.Options,
                DateTimeOffset.UtcNow));

        Assert.Contains("source-owned legacy API", error.Message, StringComparison.Ordinal);
    }


    [Fact]
    public void Rejects_retired_v011_images_as_legacy_control_plane_ownership()
    {
        var fixture = CreateFixture();
        var assessment = fixture.Assessment with
        {
            Docker = fixture.Assessment.Docker with
            {
                Containers = fixture.Assessment.Docker.Containers
                    .Where(container => container.Name is not "mem-api" and not "mem-web")
                    .Concat(
                    [
                        Container("retired-v011-mem-api", "retired-api", "mem-api:local", null),
                        Container("retired-v011-mem-web", "retired-web", "mem-web:local", null)
                    ])
                    .ToArray()
            }
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            V010CutoverPlanBuilder.Build(
                assessment,
                fixture.Stage,
                fixture.Options,
                DateTimeOffset.UtcNow));

        Assert.Contains("source-owned legacy API", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Development_external_control_plane_omits_non_source_retired_containers()
    {
        var fixture = CreateFixture();
        var assessment = fixture.Assessment with
        {
            SystemConfig = fixture.Assessment.SystemConfig with
            {
                Reachable = false,
                Endpoint = null,
                ProductName = null,
                ProductVersion = null,
                ErrorCode = "not-configured"
            },
            Docker = fixture.Assessment.Docker with
            {
                Containers = fixture.Assessment.Docker.Containers
                    .Where(container => container.Name is not "mem-api" and not "mem-web")
                    .Concat(
                    [
                        Container("retired-v011-mem-api", "retired-api", "mem-api:local", null),
                        Container("retired-v011-mem-web", "retired-web", "mem-web:local", null)
                    ])
                    .ToArray()
            }
        };
        var options = fixture.Options with
        {
            DevelopmentExternalControlPlane = true
        };

        var plan = V010CutoverPlanBuilder.Build(
            assessment,
            fixture.Stage,
            options,
            DateTimeOffset.UtcNow);

        Assert.True(plan.DevelopmentExternalControlPlane);
        Assert.Equal(2, plan.SourceContainersToFreeze.Length);
        Assert.Equal(
            new[] { "element", "matrix" },
            plan.SourceContainersToFreeze.Select(item => item.Role).ToArray());
        Assert.DoesNotContain(
            plan.SourceContainersToFreeze,
            item => item.ContainerName.StartsWith("retired-v011-", StringComparison.Ordinal));
        Assert.Contains(
            plan.Warnings,
            warning => warning.Contains("Development-only coexistence override", StringComparison.Ordinal));
    }

    [Fact]
    public void Development_external_control_plane_does_not_require_legacy_route_forward_hosts()
    {
        var fixture = CreateFixture();
        var database = Assert.Single(fixture.Assessment.Database.Candidates);
        var assessment = fixture.Assessment with
        {
            SystemConfig = fixture.Assessment.SystemConfig with
            {
                Reachable = false,
                Endpoint = null,
                ProductName = null,
                ProductVersion = null,
                ErrorCode = "not-configured"
            },
            Database = fixture.Assessment.Database with
            {
                Candidates = [database with { PlatformRoutes = [] }]
            }
        };
        var options = fixture.Options with
        {
            DevelopmentExternalControlPlane = true
        };

        var plan = V010CutoverPlanBuilder.Build(
            assessment,
            fixture.Stage,
            options,
            DateTimeOffset.UtcNow);

        Assert.True(plan.DevelopmentExternalControlPlane);
        Assert.Equal(
            new[] { "element", "matrix" },
            plan.SourceContainersToFreeze.Select(item => item.Role).ToArray());
        Assert.DoesNotContain(
            plan.SourceContainersToFreeze,
            item => item.Role is "legacy-api" or "legacy-web");
    }

    [Fact]
    public void Production_mode_rejects_missing_legacy_route_forward_hosts()
    {
        var fixture = CreateFixture();
        var database = Assert.Single(fixture.Assessment.Database.Candidates);
        var assessment = fixture.Assessment with
        {
            Database = fixture.Assessment.Database with
            {
                Candidates = [database with { PlatformRoutes = [] }]
            }
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            V010CutoverPlanBuilder.Build(
                assessment,
                fixture.Stage,
                fixture.Options,
                DateTimeOffset.UtcNow));

        Assert.Contains(
            "no legacy route forward-host evidence for legacy API",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Development_external_control_plane_requires_host_api_to_be_stopped()
    {
        var fixture = CreateFixture();
        var options = fixture.Options with
        {
            DevelopmentExternalControlPlane = true
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            V010CutoverPlanBuilder.Build(
                fixture.Assessment,
                fixture.Stage,
                options,
                DateTimeOffset.UtcNow));

        Assert.Contains("still reachable", error.Message, StringComparison.Ordinal);
    }

    private static Fixture CreateFixture()
    {
        var stackId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var matrixServiceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var elementServiceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var fingerprint = new string('a', 64);
        var containers = new[]
        {
            Container("mem-api", "api-id", "ghcr.io/matrix-easy-mode/mem-api:0.1.0", "api"),
            Container("mem-web", "web-id", "ghcr.io/matrix-easy-mode/mem-web:0.1.0", "web"),
            Container("mem-matrix-demo", "matrix-id", "matrixdotorg/synapse@sha256:test", null,
                new Dictionary<string, string> { ["mem.instanceId"] = matrixServiceId.ToString("D") }),
            Container("mem-element-demo", "element-id", "vectorim/element-web@sha256:test", null,
                new Dictionary<string, string> { ["mem.instanceId"] = elementServiceId.ToString("D") }),
            Container("mem-v010-postgres", "postgres-id", "postgres@sha256:test", "postgres")
        };
        var stack = new LegacyStackRecord(
            stackId,
            Guid.NewGuid(),
            "demo",
            "Demo",
            3,
            null,
            "https://matrix.example.test",
            matrixServiceId,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var matrixService = Service(
            matrixServiceId,
            stackId,
            "matrix",
            "matrix-id",
            "matrix.example.test",
            "matrix.example.test",
            "route-matrix",
            "mem-matrix-demo",
            8008);
        var elementService = Service(
            elementServiceId,
            stackId,
            "element-web",
            "element-id",
            null,
            "chat.example.test",
            "route-element",
            "mem-element-demo",
            80);
        var database = new LegacyDatabaseCandidateObservation(
            "postgres-id",
            "mem-v010-postgres",
            "mem",
            "postgres",
            "16",
            true,
            true,
            ["20260507085953_InitialApplicationSchema"],
            ["stacks", "service_instances"],
            [],
            [],
            new Dictionary<string, long>(),
            [stack],
            [matrixService, elementService],
            [new LegacyPlatformRouteRecord(
                "primary",
                "1",
                "admin.example.test",
                "mem-web",
                3000,
                "route-web",
                "api.example.test",
                "mem-api",
                8080,
                "route-api",
                true,
                DateTimeOffset.UtcNow)],
            0,
            0,
            0,
            null,
            null);
        var file = new FileObservation(
            "/srv/demo/homeserver.yaml",
            true,
            true,
            false,
            100,
            DateTimeOffset.UtcNow,
            null);
        var assessment = new AssessmentResult(
            "mem-migrate-assessment",
            1,
            "assessment-1",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            AssessmentClassification.ConfirmedSupportedV010,
            MigrationRecommendation.NewServerRecommended,
            true,
            fingerprint,
            new HostObservation("Linux", "x64", "source", "operator", "/tmp", 1_000_000, true, DateTimeOffset.UtcNow),
            new DockerInventoryObservation(true, "27", null, null, containers, [], DateTimeOffset.UtcNow),
            new SystemConfigObservation(true, new Uri("http://localhost:7000/api/system/config"), "MatrixEasyMode", "0.1.0", null, null),
            new LegacyDatabaseObservation(true, [database], DateTimeOffset.UtcNow),
            new LegacyFileSystemObservation(
                [new LegacyStackFileObservation(
                    stackId,
                    matrixServiceId,
                    "matrix-id",
                    "/srv/demo",
                    file,
                    file with { Path = "/srv/demo/homeserver.db" },
                    file with { Path = "/srv/demo/signing.key" },
                    new DirectorySizeObservation("/srv/demo/media_store", true, 0, 0, true, null),
                    new HomeserverConfigurationObservation(
                        "/srv/demo/homeserver.yaml",
                        true,
                        "matrix.example.test",
                        "sqlite3",
                        "/srv/demo/homeserver.db",
                        "/srv/demo/media_store",
                        "/srv/demo/signing.key",
                        null),
                    elementServiceId,
                    "/srv/element-demo",
                    file with { Path = "/srv/element-demo/config.json" },
                    [])],
                DateTimeOffset.UtcNow),
            [],
            []);
        var stage = new TargetPrivateStageReport(
            "mem-target-private-stage-report",
            1,
            "mm05d-proof",
            "Completed",
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow,
            "mm05c-proof",
            "mm05c-target",
            "http://127.0.0.1:7105",
            "bkp_proof",
            "restore-proof",
            "staging-proof",
            true,
            true,
            true,
            true,
            true,
            true,
            true,
            "/tmp/evidence.json",
            [],
            []);
        var options = new CutoverPrepareOptions
        {
            StageAttemptId = stage.StageAttemptId,
            ExpectedSourceFingerprint = fingerprint,
            PlanId = "mm06a-proof",
            ProducerVersion = "test",
            ValidForMinutes = 30
        };
        return new Fixture(assessment, stage, options);
    }

    private static DockerContainerObservation Container(
        string name,
        string id,
        string image,
        string? composeService,
        IReadOnlyDictionary<string, string>? labels = null) =>
        new(
            id,
            name,
            image,
            "sha256:" + id,
            "running",
            "healthy",
            "unless-stopped",
            DateTimeOffset.UtcNow,
            "legacy",
            composeService,
            labels ?? new Dictionary<string, string>(),
            [],
            new Dictionary<string, string>(),
            [],
            [],
            []);

    private static LegacyServiceRecord Service(
        Guid id,
        Guid stackId,
        string key,
        string containerId,
        string? matrixHost,
        string? elementHost,
        string routeId,
        string forwardHost,
        int forwardPort) =>
        new(
            id,
            stackId,
            key,
            2,
            "image",
            "version",
            containerId,
            null,
            null,
            matrixHost,
            "/srv/data",
            false,
            matrixHost,
            null,
            null,
            elementHost,
            null,
            routeId,
            matrixHost ?? elementHost,
            null,
            null,
            forwardHost,
            forwardPort,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private sealed record Fixture(
        AssessmentResult Assessment,
        TargetPrivateStageReport Stage,
        CutoverPrepareOptions Options);
}
