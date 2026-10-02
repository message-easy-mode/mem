using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Fingerprints;

namespace Mem.Migrate.UnitTests;

public sealed class SourceFingerprintTests
{
    [Fact]
    public void Fingerprint_is_stable_when_container_order_changes()
    {
        var first = AssessmentTestData.Container("a", "image:a");
        var second = AssessmentTestData.Container("b", "image:b");
        var host = new HostObservation(
            "Linux",
            "X64",
            "host",
            "operator",
            "/tmp/work",
            1000,
            true,
            DateTimeOffset.UtcNow);
        var config = new SystemConfigObservation(
            true,
            new Uri("http://127.0.0.1:7000/api/system/config"),
            "MatrixEasyMode",
            "0.1.0",
            null,
            null);
        var database = new LegacyDatabaseObservation(
            false,
            [],
            DateTimeOffset.UtcNow);
        var files = new LegacyFileSystemObservation(
            [],
            DateTimeOffset.UtcNow);

        var left = SourceFingerprint.Compute(
            host,
            AssessmentTestData.Docker([first, second]),
            config,
            database,
            files);
        var right = SourceFingerprint.Compute(
            host,
            AssessmentTestData.Docker([second, first]),
            config,
            database,
            files);

        Assert.Equal(left, right);
    }
    [Fact]
    public void Exact_fingerprint_remains_sensitive_to_live_database_and_file_drift()
    {
        var stackId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var matrixId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var host = new HostObservation(
            "Linux",
            "X64",
            "host",
            "operator",
            "/tmp/work",
            1000,
            true,
            DateTimeOffset.UtcNow);
        var config = new SystemConfigObservation(
            true,
            new Uri("http://127.0.0.1:7000/api/system/config"),
            "MatrixEasyMode",
            "0.1.0",
            null,
            null);
        var stack = AssessmentTestData.Stack(stackId, matrixId);
        var service = AssessmentTestData.MatrixService(
            stackId,
            matrixId,
            "/srv/mem/demo/synapse");
        var database = AssessmentTestData.ExactDatabase([stack], [service]) with
        {
            RowCounts = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["Stacks"] = 1,
                ["Messages"] = 100
            }
        };
        var files = AssessmentTestData.StackFiles(stackId, matrixId);
        var laterDatabase = database with
        {
            RowCounts = new Dictionary<string, long>(StringComparer.Ordinal)
            {
                ["Stacks"] = 1,
                ["Messages"] = 101
            }
        };
        var laterFiles = files with
        {
            SqliteDatabase = files.SqliteDatabase with
            {
                SizeBytes = files.SqliteDatabase.SizeBytes + 4096,
                LastWriteAtUtc = files.SqliteDatabase.LastWriteAtUtc?.AddSeconds(5)
            },
            MediaStore = files.MediaStore with
            {
                TotalBytes = files.MediaStore.TotalBytes + 1024,
                FileCount = files.MediaStore.FileCount + 1
            }
        };

        var first = SourceFingerprint.Compute(
            host,
            AssessmentTestData.Docker(),
            config,
            new LegacyDatabaseObservation(true, [database], DateTimeOffset.UtcNow),
            new LegacyFileSystemObservation([files], DateTimeOffset.UtcNow));
        var second = SourceFingerprint.Compute(
            host,
            AssessmentTestData.Docker(),
            config,
            new LegacyDatabaseObservation(true, [laterDatabase], DateTimeOffset.UtcNow),
            new LegacyFileSystemObservation([laterFiles], DateTimeOffset.UtcNow));

        Assert.NotEqual(first, second);
    }

}
