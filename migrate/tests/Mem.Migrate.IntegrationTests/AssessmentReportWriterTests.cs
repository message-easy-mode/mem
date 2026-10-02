using System.Text.Json.Nodes;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Reporting;

namespace Mem.Migrate.IntegrationTests;

public sealed class AssessmentReportWriterTests
{
    [Fact]
    public void Public_reports_hide_paths_and_secret_canaries()
    {
        var writer = new AssessmentReportWriter();
        var result = IntegrationTestData.Result();

        var reports = writer.Render(
            result,
            includeSensitivePaths: false);

        Assert.DoesNotContain(
            "/srv/mem/private",
            reports.PublicJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "/srv/mem/private",
            reports.PublicMarkdown,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DoNotExpose",
            reports.PublicJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DoNotExpose",
            reports.PublicMarkdown,
            StringComparison.Ordinal);

        var publicReport = JsonNode.Parse(reports.PublicJson)
            ?? throw new InvalidOperationException(
                "The public assessment report was empty.");

        var publicFindingMessage = publicReport["findings"]?[0]?["message"]
            ?.GetValue<string>();

        Assert.NotNull(publicFindingMessage);
        Assert.Contains(
            "<redacted>",
            publicFindingMessage,
            StringComparison.Ordinal);

        Assert.Contains(
            "/srv/mem/private",
            reports.PrivateJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DoNotExpose",
            reports.PrivateJson,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Public_report_redacts_live_string_arrays_without_modifying_during_enumeration()
    {
        var writer = new AssessmentReportWriter();
        var result = IntegrationTestData.Result();
        var container = new DockerContainerObservation(
            Id: "container-1",
            Name: "mem-v010-postgres",
            Image: "postgres:16",
            ImageId: "sha256:test",
            State: "running",
            Health: "healthy",
            RestartPolicy: "unless-stopped",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ComposeProject: "mem-v010-dev",
            ComposeService: "postgres",
            ManagedLabels: new Dictionary<string, string>(
                StringComparer.Ordinal),
            EnvironmentNames:
            [
                "POSTGRES_DB",
                "POSTGRES_USER"
            ],
            SafeEnvironment: new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["POSTGRES_DB"] = "mem"
            },
            Ports: [],
            Mounts: [],
            Networks:
            [
                new DockerNetworkAttachment(
                    "mem-v010-gateway",
                    "172.18.0.2",
                    ["postgres", "mem-v010-postgres"])
            ]);

        result = result with
        {
            Docker = result.Docker with
            {
                Containers = [container]
            }
        };

        var exception = Record.Exception(
            () => writer.Render(
                result,
                includeSensitivePaths: false));

        Assert.Null(exception);
    }

    [Fact]
    public void Public_report_preserves_numeric_row_counts_when_table_name_contains_token()
    {
        var writer = new AssessmentReportWriter();
        var result = IntegrationTestData.Result();
        var candidate = new LegacyDatabaseCandidateObservation(
            ContainerId: "postgres-id",
            ContainerName: "mem-postgres",
            DatabaseName: "mem",
            DatabaseUser: "mem",
            ServerVersion: "16",
            AppSchemaPresent: true,
            ExactSupportedSchema: true,
            MigrationIds: [],
            TableNames: ["identity_refresh_tokens"],
            MissingTables: [],
            UnexpectedTables: [],
            RowCounts: new Dictionary<string, long>
            {
                ["identity_refresh_tokens"] = 7
            },
            Stacks: [],
            Services: [],
            PlatformRoutes: [],
            ActiveGuestChats: 0,
            ActivePasswordResetRequests: 11,
            ProvisioningJobs: 0,
            ErrorCode: null,
            ErrorMessage: null);

        result = result with
        {
            Database = new LegacyDatabaseObservation(
                ProbeAttempted: true,
                Candidates: [candidate],
                ObservedAtUtc: DateTimeOffset.UtcNow)
        };

        var reports = writer.Render(
            result,
            includeSensitivePaths: false);
        var publicReport = JsonNode.Parse(reports.PublicJson)
            ?? throw new InvalidOperationException(
                "The public assessment report was empty.");

        Assert.Equal(
            7,
            publicReport["database"]?["candidates"]?[0]?["rowCounts"]?
                ["identity_refresh_tokens"]?.GetValue<long>());
        Assert.Equal(
            11,
            publicReport["database"]?["candidates"]?[0]?
                ["activePasswordResetRequests"]?.GetValue<long>());
    }

    [Fact]
    public async Task Writes_private_and_public_files_to_separate_roots()
    {
        var output = Directory.CreateTempSubdirectory(
            "mem-migrate-report-");
        var workspace = Directory.CreateTempSubdirectory(
            "mem-migrate-private-");

        try
        {
            var writer = new AssessmentReportWriter();
            var reports = writer.Render(
                IntegrationTestData.Result(),
                includeSensitivePaths: false);

            await writer.WriteAsync(
                reports,
                output.FullName,
                workspace.FullName,
                CancellationToken.None);

            Assert.True(
                File.Exists(Path.Combine(output.FullName, "assessment.json")));
            Assert.True(
                File.Exists(Path.Combine(output.FullName, "assessment.md")));
            Assert.True(
                File.Exists(Path.Combine(
                    workspace.FullName,
                    "assessment.private.json")));
        }
        finally
        {
            output.Delete(recursive: true);
            workspace.Delete(recursive: true);
        }
    }
}
