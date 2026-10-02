using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Legacy.V010;

namespace Mem.Migrate.UnitTests;

public sealed class V010ClassifierTests
{
    [Fact]
    public void Exact_database_and_complete_stack_is_supported()
    {
        var stackId = Guid.NewGuid();
        var matrixId = Guid.NewGuid();
        var api = AssessmentTestData.Container(
            "mem-api",
            "ghcr.io/matrix-easy-mode/mem-api:0.1.0",
            "api");
        var web = AssessmentTestData.Container(
            "mem-web",
            "ghcr.io/matrix-easy-mode/mem-web:0.1.0",
            "web");
        var matrix = AssessmentTestData.Container(
            "mem-matrix",
            "matrixdotorg/synapse:latest",
            labels: new Dictionary<string, string>
            {
                ["mem.instanceId"] = matrixId.ToString("D"),
                ["mem.serviceKey"] = "matrix"
            });

        var result = V010Classifier.Classify(
            AssessmentTestData.Docker(
                [api, web, matrix],
                [
                    new DockerNetworkObservation(
                        "network-id",
                        "mem-gateway",
                        "bridge",
                        "local",
                        false,
                        new Dictionary<string, string>())
                ]),
            new SystemConfigObservation(
                true,
                new Uri("http://127.0.0.1:7000/api/system/config"),
                "MatrixEasyMode",
                "0.1.0",
                null,
                null),
            new LegacyDatabaseObservation(
                true,
                [
                    AssessmentTestData.ExactDatabase(
                        [AssessmentTestData.Stack(stackId, matrixId)],
                        [
                            AssessmentTestData.MatrixService(
                                stackId,
                                matrixId,
                                "/srv/mem/demo/synapse")
                        ])
                ],
                DateTimeOffset.UtcNow),
            new LegacyFileSystemObservation(
                [AssessmentTestData.StackFiles(stackId, matrixId)],
                DateTimeOffset.UtcNow));

        Assert.Equal(
            AssessmentClassification.ConfirmedSupportedV010,
            result.Classification);
        Assert.True(result.CanProceedToCapture);
        Assert.DoesNotContain(
            result.Findings,
            finding => finding.Severity is FindingSeverity.Blocker);
    }

    [Fact]
    public void Missing_sqlite_is_partial_and_blocked()
    {
        var stackId = Guid.NewGuid();
        var matrixId = Guid.NewGuid();
        var database = AssessmentTestData.ExactDatabase(
            [AssessmentTestData.Stack(stackId, matrixId)],
            [
                AssessmentTestData.MatrixService(
                    stackId,
                    matrixId,
                    "/srv/mem/demo/synapse")
            ]);

        var result = V010Classifier.Classify(
            AssessmentTestData.Docker(
                [
                    AssessmentTestData.Container(
                        "mem-api",
                        "mem-api:0.1.0"),
                    AssessmentTestData.Container(
                        "mem-matrix",
                        "matrixdotorg/synapse:latest")
                ]),
            new SystemConfigObservation(
                true,
                new Uri("http://127.0.0.1:7000/api/system/config"),
                "MatrixEasyMode",
                "0.1.0",
                null,
                null),
            new LegacyDatabaseObservation(
                true,
                [database],
                DateTimeOffset.UtcNow),
            new LegacyFileSystemObservation(
                [AssessmentTestData.StackFiles(stackId, matrixId, false)],
                DateTimeOffset.UtcNow));

        Assert.Equal(
            AssessmentClassification.PartialRepairableV010,
            result.Classification);
        Assert.False(result.CanProceedToCapture);
        Assert.Contains(
            result.Findings,
            finding => finding.Code == "synapse_sqlite_missing");
    }

    [Fact]
    public void Multiple_exact_databases_are_ambiguous()
    {
        var result = V010Classifier.Classify(
            AssessmentTestData.Docker(),
            new SystemConfigObservation(
                false,
                null,
                null,
                null,
                "unreachable",
                "unreachable"),
            new LegacyDatabaseObservation(
                true,
                [
                    AssessmentTestData.ExactDatabase(),
                    AssessmentTestData.ExactDatabase()
                ],
                DateTimeOffset.UtcNow),
            new LegacyFileSystemObservation([], DateTimeOffset.UtcNow));

        Assert.Equal(
            AssessmentClassification.AmbiguousMultipleInstallations,
            result.Classification);
        Assert.False(result.CanProceedToCapture);
    }

    [Fact]
    public void Current_target_version_is_not_treated_as_legacy()
    {
        var result = V010Classifier.Classify(
            AssessmentTestData.Docker(),
            new SystemConfigObservation(
                true,
                new Uri("http://127.0.0.1:7000/api/system/config"),
                "MatrixEasyMode",
                "0.1.1",
                null,
                null),
            new LegacyDatabaseObservation(
                false,
                [],
                DateTimeOffset.UtcNow),
            new LegacyFileSystemObservation([], DateTimeOffset.UtcNow));

        Assert.Equal(
            AssessmentClassification.CurrentV011Present,
            result.Classification);
    }
}
