using Mem.Migrate.Application.Assessment;
using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.Web.Tests;

public sealed class SourceAssessmentProjectorTests
{
    [Fact]
    public void Projects_a_safe_stack_inventory_without_private_paths()
    {
        var stackId = Guid.NewGuid();
        var matrixServiceId = Guid.NewGuid();
        var elementServiceId = Guid.NewGuid();
        var result = NewAssessment(stackId, matrixServiceId, elementServiceId);

        var projected = SourceAssessmentProjector.Project(result);

        Assert.True(projected.CanProceedToCapture);
        var stack = Assert.Single(projected.Stacks);
        Assert.Equal(stackId.ToString("D"), stack.SourceStackId);
        Assert.Equal("Testing", stack.Name);
        Assert.Equal("matrix.example.test", stack.MatrixServerName);
        Assert.Equal("chat.example.test", stack.ElementPublicHost);
        Assert.True(stack.SourceFilesReady);
        Assert.Equal(4096L, stack.MediaBytes);
        Assert.DoesNotContain("/srv/private", System.Text.Json.JsonSerializer.Serialize(projected), StringComparison.Ordinal);
    }

    private static AssessmentResult NewAssessment(Guid stackId, Guid matrixServiceId, Guid elementServiceId)
    {
        var now = new DateTimeOffset(2026, 7, 27, 0, 0, 0, TimeSpan.Zero);
        var stack = new LegacyStackRecord(
            stackId,
            Guid.NewGuid(),
            "testing",
            "Testing",
            3,
            null,
            "https://matrix.example.test",
            matrixServiceId,
            now,
            now);
        var matrix = NewService(
            matrixServiceId,
            stackId,
            "matrix",
            "matrix.example.test",
            "matrix.example.test",
            null);
        var element = NewService(
            elementServiceId,
            stackId,
            "element-web",
            null,
            null,
            "chat.example.test");

        var candidate = new LegacyDatabaseCandidateObservation(
            "postgres-id",
            "mem-postgres",
            "mem",
            "mem",
            "16",
            true,
            true,
            [],
            [],
            [],
            [],
            new Dictionary<string, long>(),
            [stack],
            [matrix, element],
            [],
            0,
            0,
            0,
            null,
            null);

        var stackFiles = new LegacyStackFileObservation(
            stackId,
            matrixServiceId,
            "matrix-id",
            "/srv/private/testing",
            NewFile("/srv/private/testing/homeserver.yaml"),
            NewFile("/srv/private/testing/homeserver.db"),
            NewFile("/srv/private/testing/server.signing.key"),
            new DirectorySizeObservation("/srv/private/testing/media_store", true, 4096, 3, true, null),
            new HomeserverConfigurationObservation(
                "/srv/private/testing/homeserver.yaml",
                true,
                "matrix.example.test",
                "sqlite3",
                "/srv/private/testing/homeserver.db",
                "/srv/private/testing/media_store",
                "/srv/private/testing/server.signing.key",
                null),
            elementServiceId,
            "/srv/private/testing/element",
            NewFile("/srv/private/testing/element/config.json"),
            []);

        return new AssessmentResult(
            "mem-migrate-assessment",
            1,
            "assessment-01",
            now,
            now.AddSeconds(2),
            AssessmentClassification.ConfirmedSupportedV010,
            MigrationRecommendation.NewServerRecommended,
            true,
            "sha256:source",
            new HostObservation("Ubuntu", "x64", "private-host", "private-user", "/srv/private/work", 1000, true, now),
            new DockerInventoryObservation(true, "28", null, null, [], [], now),
            new SystemConfigObservation(true, new Uri("http://127.0.0.1:7000/api/system/config"), "MatrixEasyMode", "0.1.0", null, null),
            new LegacyDatabaseObservation(true, [candidate], now),
            new LegacyFileSystemObservation([stackFiles], now),
            [],
            []);
    }

    private static LegacyServiceRecord NewService(
        Guid id,
        Guid stackId,
        string key,
        string? serverName,
        string? matrixPublicHost,
        string? elementPublicHost) =>
        new(
            id,
            stackId,
            key,
            2,
            "image",
            "version",
            "container",
            null,
            null,
            serverName,
            "/srv/private/data",
            false,
            matrixPublicHost,
            null,
            null,
            elementPublicHost,
            null,
            null,
            matrixPublicHost ?? elementPublicHost,
            null,
            null,
            "forward",
            8008,
            null,
            null);

    private static FileObservation NewFile(string path) =>
        new(path, true, true, false, 100, DateTimeOffset.UtcNow, null);
}
