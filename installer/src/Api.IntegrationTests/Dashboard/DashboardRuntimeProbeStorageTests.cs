using Modules.Operator.Dashboard;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Dashboard;

public sealed class DashboardRuntimeProbeStorageTests
{
    [Fact]
    public void Local_development_projects_the_filesystem_backing_MEM_data()
    {
        var (disk, reason) = DashboardRuntimeProbe.ProjectMemDataDisk(
            MemRuntimeModes.LocalDevelopment,
            "/workspace/mem-data",
            [
                new DashboardFilesystemDriveObservation(
                    "/",
                    TotalBytes: 100_000,
                    AvailableBytes: 25_000)
            ]);

        Assert.Null(reason);
        Assert.NotNull(disk);
        Assert.Equal(75_000, disk!.UsedBytes);
        Assert.Equal(100_000, disk.TotalBytes);
        Assert.Equal("mem_data", disk.Scope);
    }

    [Theory]
    [InlineData(MemRuntimeModes.ContainerizedDevelopment)]
    [InlineData(MemRuntimeModes.ContainerizedProduction)]
    public void Containerized_runtime_uses_the_identity_mounted_MEM_data_filesystem(
        string runtimeMode)
    {
        var (disk, reason) = DashboardRuntimeProbe.ProjectMemDataDisk(
            runtimeMode,
            "/var/lib/message-easy-mode/mem-data",
            [
                new DashboardFilesystemDriveObservation(
                    "/",
                    TotalBytes: 20_000,
                    AvailableBytes: 5_000),
                new DashboardFilesystemDriveObservation(
                    "/var/lib/message-easy-mode",
                    TotalBytes: 200_000,
                    AvailableBytes: 80_000)
            ]);

        Assert.Null(reason);
        Assert.NotNull(disk);
        Assert.Equal(120_000, disk!.UsedBytes);
        Assert.Equal(200_000, disk.TotalBytes);
        Assert.Equal("mem_data", disk.Scope);
    }

    [Theory]
    [InlineData(MemRuntimeModes.ContainerizedDevelopment)]
    [InlineData(MemRuntimeModes.ContainerizedProduction)]
    public void Containerized_runtime_fails_closed_when_only_the_container_root_is_visible(
        string runtimeMode)
    {
        var (disk, reason) = DashboardRuntimeProbe.ProjectMemDataDisk(
            runtimeMode,
            "/var/lib/message-easy-mode/mem-data",
            [
                new DashboardFilesystemDriveObservation(
                    "/",
                    TotalBytes: 20_000,
                    AvailableBytes: 5_000)
            ]);

        Assert.Null(disk);
        Assert.Equal("mem_data_filesystem_unavailable", reason);
    }

    [Fact]
    public void Unsupported_runtime_does_not_invent_storage_capacity()
    {
        var (disk, reason) = DashboardRuntimeProbe.ProjectMemDataDisk(
            MemRuntimeModes.AutomatedTest,
            "/tmp/mem-data",
            [
                new DashboardFilesystemDriveObservation(
                    "/",
                    TotalBytes: 20_000,
                    AvailableBytes: 5_000)
            ]);

        Assert.Null(disk);
        Assert.Equal("mem_data_filesystem_unavailable", reason);
    }
}
