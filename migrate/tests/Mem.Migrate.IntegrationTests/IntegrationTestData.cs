using Mem.Migrate.Core.Assessment;

namespace Mem.Migrate.IntegrationTests;

internal static class IntegrationTestData
{
    public static AssessmentResult Result(
        string assessmentId = "assessment-1",
        string path = "/srv/mem/private/homeserver.db")
    {
        var now = DateTimeOffset.UtcNow;

        return new AssessmentResult(
            Schema: "mem-migrate-assessment",
            SchemaVersion: 1,
            AssessmentId: assessmentId,
            StartedAtUtc: now.AddSeconds(-1),
            CompletedAtUtc: now,
            Classification: AssessmentClassification.ConfirmedSupportedV010,
            Recommendation: MigrationRecommendation.NewServerRecommended,
            CanProceedToCapture: true,
            SourceFingerprint: new string('a', 64),
            Host: new HostObservation(
                "Linux",
                "X64",
                "host",
                "operator",
                "/srv/mem/private/work",
                10_000,
                true,
                now),
            Docker: new DockerInventoryObservation(
                true,
                "27.0.0",
                null,
                null,
                [],
                [],
                now),
            SystemConfig: new SystemConfigObservation(
                true,
                new Uri("http://127.0.0.1:7000/api/system/config"),
                "MatrixEasyMode",
                "0.1.0",
                null,
                null),
            Database: new LegacyDatabaseObservation(
                true,
                [],
                now),
            FileSystem: new LegacyFileSystemObservation(
                [
                    new LegacyStackFileObservation(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "container",
                        "/srv/mem/private",
                        File(path.Replace(
                            "homeserver.db",
                            "homeserver.yaml",
                            StringComparison.Ordinal)),
                        File(path),
                        File(path.Replace(
                            "homeserver.db",
                            "server.signing.key",
                            StringComparison.Ordinal)),
                        new DirectorySizeObservation(
                            "/srv/mem/private/media_store",
                            true,
                            10,
                            1,
                            true,
                            null),
                        new HomeserverConfigurationObservation(
                            "/srv/mem/private/homeserver.yaml",
                            true,
                            "matrix.example.test",
                            "sqlite3",
                            path,
                            "/srv/mem/private/media_store",
                            "/srv/mem/private/server.signing.key",
                            null),
                        null,
                        null,
                        null,
                        [])
                ],
                now),
            Signals: [],
            Findings:
            [
                new AssessmentFinding(
                    "safe_warning",
                    FindingSeverity.Warning,
                    "password=DoNotExpose",
                    null)
            ]);
    }

    private static FileObservation File(string path) =>
        new(
            path,
            true,
            true,
            false,
            10,
            DateTimeOffset.UtcNow,
            null);
}
