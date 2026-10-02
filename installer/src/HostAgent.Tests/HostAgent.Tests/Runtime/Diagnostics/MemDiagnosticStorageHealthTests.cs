using HostAgent.Runtime.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Shared.Diagnostics;

namespace HostAgent.Tests.Runtime.Diagnostics;

public sealed class MemDiagnosticStorageHealthTests
{
    [Fact]
    public void Storage_capacity_probe_reports_ready_low_critical_and_unknown_without_exposing_a_path()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-storage-capacity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var probe = new MemStorageCapacityProbe();
            var ready = probe.GetHealth(
                root,
                0,
                0,
                "low",
                "critical",
                "unknown");
            var low = probe.GetHealth(
                root,
                long.MaxValue,
                0,
                "low",
                "critical",
                "unknown");
            var critical = probe.GetHealth(
                root,
                long.MaxValue,
                long.MaxValue,
                "low",
                "critical",
                "unknown");
            var unknown = probe.GetHealth(
                "invalid\0path",
                1024,
                512,
                "low",
                "critical",
                "unknown");

            Assert.Equal("ready", ready.Status);
            Assert.Null(ready.WarningCode);
            Assert.NotNull(ready.AvailableBytes);
            Assert.Equal("low", low.Status);
            Assert.Equal("low", low.WarningCode);
            Assert.Equal("critical", critical.Status);
            Assert.Equal("critical", critical.WarningCode);
            Assert.Equal("unknown", unknown.Status);
            Assert.Equal("unknown", unknown.WarningCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Safe_event_store_health_degrades_on_low_disk_without_disabling_the_store()
    {
        var options = new MemDiagnosticsOptions
        {
            RootPath = "/data/diagnostics",
            LowDiskWarningBytes = 1024,
            CriticalDiskWarningBytes = 512
        };
        var health = new MemDiagnosticHealthState(
            options,
            new TestHostEnvironment(),
            new FixedStorageProbe(new MemStorageCapacityHealth(
                "low",
                AvailableBytes: 900,
                TotalBytes: 10000,
                WarningCode: "diagnostics.safe_event_store_disk_low")));

        var snapshot = health.GetHealth();

        Assert.True(snapshot.Enabled);
        Assert.Equal("degraded", snapshot.Status);
        Assert.NotNull(snapshot.Storage);
        Assert.Equal("low", snapshot.Storage!.Status);
        Assert.Equal(
            "diagnostics.safe_event_store_disk_low",
            snapshot.Storage.WarningCode);
    }

    [Fact]
    public void Safe_event_store_health_recovers_persistent_activity_after_process_restart()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-diagnostic-activity-{Guid.NewGuid():N}");
        var events = Path.Combine(root, "events", "2026", "08", "03");
        Directory.CreateDirectory(events);
        var eventFile = Path.Combine(events, "diagnostics.ndjson");
        File.WriteAllText(eventFile, "{\"eventId\":\"evt_existing\"}\n");
        var expectedWriteAtUtc = new DateTime(2026, 8, 3, 1, 2, 3, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(eventFile, expectedWriteAtUtc);

        try
        {
            var health = new MemDiagnosticHealthState(
                new MemDiagnosticsOptions { RootPath = root },
                new TestHostEnvironment(),
                new FixedStorageProbe(new MemStorageCapacityHealth(
                    "ready",
                    AvailableBytes: 10_000,
                    TotalBytes: 20_000,
                    WarningCode: null)));

            var snapshot = health.GetHealth();

            Assert.True(snapshot.Enabled);
            Assert.Equal("ready", snapshot.Status);
            Assert.True(snapshot.HasEverRecordedEvent);
            Assert.Equal(0, snapshot.StoredEventCount);
            Assert.Equal(
                new DateTimeOffset(expectedWriteAtUtc),
                snapshot.LastWriteAtUtc);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Safe_event_store_can_be_disabled_independently_without_probing_storage()
    {
        var probe = new ThrowingStorageProbe();
        var health = new MemDiagnosticHealthState(
            new MemDiagnosticsOptions
            {
                Enabled = false,
                RootPath = "/data/diagnostics"
            },
            new TestHostEnvironment(),
            probe);

        var snapshot = health.GetHealth();

        Assert.False(snapshot.Enabled);
        Assert.Equal("disabled", snapshot.Status);
        Assert.Null(snapshot.Storage);
        Assert.False(probe.WasCalled);
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

    private sealed class ThrowingStorageProbe : IMemStorageCapacityProbe
    {
        public bool WasCalled { get; private set; }

        public MemStorageCapacityHealth GetHealth(
            string path,
            long lowWarningBytes,
            long criticalWarningBytes,
            string lowWarningCode,
            string criticalWarningCode,
            string unavailableWarningCode)
        {
            WasCalled = true;
            throw new InvalidOperationException("Storage must not be probed when disabled.");
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "HostAgent.Tests";

        public string ContentRootPath { get; set; } = Path.GetTempPath();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
