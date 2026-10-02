using Mem.Migrate.Application.Assessment;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Reporting;

namespace Mem.Migrate.Web.Tests;

public sealed class SourceAssessmentStoredJsonCompatibilityTests
{
    [Fact]
    public async Task Reloads_legacy_public_json_with_redacted_row_count_entry()
    {
        var result = NewAssessment();
        var reports = new AssessmentReportWriter().Render(
            result,
            includeSensitivePaths: false);
        var legacyPublicJson = reports.PublicJson
            .Replace(
                "\"identity_refresh_tokens\": 7",
                "\"identity_refresh_tokens\": \"<redacted>\"",
                StringComparison.Ordinal)
            .Replace(
                "\"activePasswordResetRequests\": 9",
                "\"activePasswordResetRequests\": \"<redacted>\"",
                StringComparison.Ordinal);

        Assert.Contains(
            "\"identity_refresh_tokens\": \"<redacted>\"",
            legacyPublicJson,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"activePasswordResetRequests\": \"<redacted>\"",
            legacyPublicJson,
            StringComparison.Ordinal);

        var journal = new FakeAssessmentJournal(
            new StoredAssessmentReport(
                result.AssessmentId,
                result.CompletedAtUtc,
                result.Classification,
                result.SourceFingerprint,
                legacyPublicJson,
                reports.PublicMarkdown));
        var service = new SourceAssessmentApplicationService(
            new AssessmentOptions
            {
                WorkspacePath = Path.GetTempPath(),
                OutputPath = Path.GetTempPath()
            },
            null!,
            journal);

        var view = await service.GetLatestAsync(CancellationToken.None);

        Assert.NotNull(view);
        Assert.Equal(result.AssessmentId, view.AssessmentId);
        Assert.Equal(result.SourceFingerprint, view.SourceFingerprint);
        Assert.Single(view.Stacks);
    }

    private static AssessmentResult NewAssessment()
    {
        var now = new DateTimeOffset(
            2026,
            7,
            27,
            0,
            0,
            0,
            TimeSpan.Zero);
        var stackId = Guid.NewGuid();
        var matrixServiceId = Guid.NewGuid();
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
        var matrix = new LegacyServiceRecord(
            matrixServiceId,
            stackId,
            "matrix",
            2,
            "image",
            "version",
            "container",
            null,
            null,
            "matrix.example.test",
            "/srv/private/testing",
            false,
            "matrix.example.test",
            null,
            null,
            null,
            null,
            null,
            "matrix.example.test",
            null,
            null,
            "forward",
            8008,
            null,
            null);
        var candidate = new LegacyDatabaseCandidateObservation(
            "postgres-id",
            "mem-postgres",
            "mem",
            "mem",
            "16",
            true,
            true,
            [],
            ["identity_refresh_tokens"],
            [],
            [],
            new Dictionary<string, long>
            {
                ["identity_refresh_tokens"] = 7
            },
            [stack],
            [matrix],
            [],
            0,
            9,
            0,
            null,
            null);
        var file = new FileObservation(
            "/srv/private/testing/file",
            true,
            true,
            false,
            100,
            now,
            null);
        var stackFiles = new LegacyStackFileObservation(
            stackId,
            matrixServiceId,
            "matrix-id",
            "/srv/private/testing",
            file with { Path = "/srv/private/testing/homeserver.yaml" },
            file with { Path = "/srv/private/testing/homeserver.db" },
            file with { Path = "/srv/private/testing/server.signing.key" },
            new DirectorySizeObservation(
                "/srv/private/testing/media_store",
                true,
                4096,
                3,
                true,
                null),
            new HomeserverConfigurationObservation(
                "/srv/private/testing/homeserver.yaml",
                true,
                "matrix.example.test",
                "sqlite3",
                "/srv/private/testing/homeserver.db",
                "/srv/private/testing/media_store",
                "/srv/private/testing/server.signing.key",
                null),
            null,
            null,
            null,
            []);

        return new AssessmentResult(
            "mem-migrate-assessment",
            1,
            "assessment-compatibility",
            now,
            now.AddSeconds(1),
            AssessmentClassification.ConfirmedSupportedV010,
            MigrationRecommendation.NewServerRecommended,
            true,
            new string('a', 64),
            new HostObservation(
                "Ubuntu",
                "x64",
                "private-host",
                "private-user",
                "/srv/private/work",
                1000,
                true,
                now),
            new DockerInventoryObservation(
                true,
                "29",
                null,
                null,
                [],
                [],
                now),
            new SystemConfigObservation(
                false,
                null,
                null,
                null,
                "legacy_system_config_unreachable",
                "Unavailable."),
            new LegacyDatabaseObservation(
                true,
                [candidate],
                now),
            new LegacyFileSystemObservation(
                [stackFiles],
                now),
            [],
            []);
    }

    private sealed class FakeAssessmentJournal(
        StoredAssessmentReport report) : IAssessmentJournal
    {
        public Task InitializeAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SaveAsync(
            AssessmentResult result,
            string publicJson,
            string publicMarkdown,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<StoredAssessmentReport?> GetLatestAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<StoredAssessmentReport?>(report);
    }
}
