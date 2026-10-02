using Api.Logging;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Diagnostics;

public sealed class MemLocalLogHealthStateTests
{
    [Fact]
    public void Local_health_reports_bounded_file_facts_without_reading_log_content()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-local-log-health-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var first = Path.Combine(root, "mem-control-plane-20260801.clef");
            var second = Path.Combine(root, "mem-control-plane-20260802.clef");
            File.WriteAllText(first, "{\"@m\":\"first\"}\n");
            File.WriteAllText(second, "{\"@m\":\"second\"}\n");
            var options = new MemLoggingOptions
            {
                PersistentFileEnabled = true,
                FilePath = Path.Combine(root, "mem-control-plane-.clef"),
                RetainedFileCountLimit = 28
            };
            var state = new MemLocalLogHealthState(
                options,
                options.FilePath,
                persistentRecorderWarning: null,
                new MemSerilogSelfLogState(),
                new FixedStorageProbe(new MemStorageCapacityHealth(
                    "ready",
                    AvailableBytes: 10_000_000,
                    TotalBytes: 20_000_000,
                    WarningCode: null)));

            var health = state.GetHealth();

            Assert.Equal("ready", health.Status);
            Assert.True(health.PersistentRecorderActive);
            Assert.Equal(2, health.RetainedFileCount);
            Assert.True(health.RetainedBytes > 0);
            Assert.NotNull(health.LastFileWriteAtUtc);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void Local_recorder_health_surfaces_low_disk_without_disabling_logging()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-local-log-low-disk-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "mem-control-plane-.clef");
            File.WriteAllText(Path.Combine(root, "mem-control-plane-20260802.clef"), "{}\n");
            var options = new MemLoggingOptions
            {
                PersistentFileEnabled = true,
                FilePath = path,
                LowDiskWarningMiB = 1024,
                CriticalDiskWarningMiB = 256
            };
            var state = new MemLocalLogHealthState(
                options,
                path,
                persistentRecorderWarning: null,
                new MemSerilogSelfLogState(),
                new FixedStorageProbe(new MemStorageCapacityHealth(
                    "low",
                    AvailableBytes: 900,
                    TotalBytes: 10000,
                    WarningCode: "diagnostics.local_recorder_disk_low")));

            var health = state.GetHealth();

            Assert.True(health.PersistentRecorderActive);
            Assert.Equal("degraded", health.Status);
            Assert.NotNull(health.Storage);
            Assert.Equal("low", health.Storage!.Status);
            Assert.Equal(
                "diagnostics.local_recorder_disk_low",
                health.WarningCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FixedStorageProbe(MemStorageCapacityHealth result)
        : IMemStorageCapacityProbe
    {
        public MemStorageCapacityHealth GetHealth(
            string path,
            long lowWarningBytes,
            long criticalWarningBytes,
            string lowWarningCode,
            string criticalWarningCode,
            string unavailableWarningCode) => result;
    }

}
