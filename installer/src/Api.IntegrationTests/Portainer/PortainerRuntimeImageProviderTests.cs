using System.Runtime.InteropServices;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Modules.Integrations.Portainer.Services;
using Modules.Shared.RuntimeImages;

namespace Api.IntegrationTests.Portainer;

public sealed class PortainerRuntimeImageProviderTests
{
    [Fact]
    public async Task Operational_resolution_uses_the_local_immutable_identity_without_pull()
    {
        var inspector = new RecordingInspector(available: true);
        var (provider, docker) = CreateProvider(inspector, Architecture.X64);

        var result = await provider.ResolveForOperationAsync(CancellationToken.None);

        Assert.Equal(PortainerRuntimeOptions.ApprovedImage, result.ApprovedReference);
        Assert.Equal(inspector.ImageId, result.ResolvedImageId);
        Assert.Equal("amd64", result.Architecture);
        Assert.False(result.PulledDuringPreparation);
        Assert.Equal(0, docker.PullCount);
        Assert.Equal(0, inspector.LegacyPullCount);
    }

    [Fact]
    public async Task Explicit_installation_preparation_pulls_once_then_resolves_the_local_identity()
    {
        var inspector = new RecordingInspector(available: false);
        var (provider, docker) = CreateProvider(inspector, Architecture.Arm64);

        var result = await provider.PrepareForInstallationAsync(CancellationToken.None);

        Assert.True(result.PulledDuringPreparation);
        Assert.Equal("arm64", result.Architecture);
        Assert.Equal(1, docker.PullCount);
        Assert.Equal(0, inspector.LegacyPullCount);
        Assert.Equal(inspector.ImageId, result.ResolvedImageId);
    }

    [Fact]
    public async Task Operational_resolution_never_pulls_when_the_approved_image_is_missing()
    {
        var inspector = new RecordingInspector(available: false);
        var (provider, docker) = CreateProvider(inspector, Architecture.X64);

        var exception = await Assert.ThrowsAsync<PortainerOperationException>(() =>
            provider.ResolveForOperationAsync(CancellationToken.None));

        Assert.Equal("portainer_approved_image_missing", exception.Code);
        Assert.Equal(0, docker.PullCount);
        Assert.Equal(0, inspector.LegacyPullCount);
    }

    [Fact]
    public async Task Invalid_local_image_identity_is_rejected()
    {
        var inspector = new RecordingInspector(
            available: true,
            imageId: "portainer:mutable");
        var (provider, _) = CreateProvider(inspector, Architecture.X64);

        var exception = await Assert.ThrowsAsync<PortainerOperationException>(() =>
            provider.ResolveForOperationAsync(CancellationToken.None));

        Assert.Equal("portainer_approved_image_invalid", exception.Code);
    }

    private static (
        PortainerRuntimeImageProvider Provider,
        RecordingDockerHost Docker) CreateProvider(
        RecordingInspector inspector,
        Architecture architecture)
    {
        var docker = new RecordingDockerHost(inspector.MakeAvailable);
        return (
            new PortainerRuntimeImageProvider(
                new PortainerRuntimeOptions(),
                inspector,
                docker,
                new FixedArchitectureReader(architecture)),
            docker);
    }

    private sealed class FixedArchitectureReader(Architecture architecture)
        : IPortainerHostArchitectureReader
    {
        public Architecture Current { get; } = architecture;
    }

    private sealed class RecordingInspector(
        bool available,
        string? imageId = null) : IRuntimeImageInspector
    {
        private bool _available = available;

        public string ImageId { get; } = imageId ??
            $"sha256:{new string('a', 64)}";

        public int LegacyPullCount { get; private set; }

        public void MakeAvailable() => _available = true;

        public Task<RuntimeImageInspection?> InspectAsync(
            string immutableReference,
            CancellationToken cancellationToken) =>
            Task.FromResult<RuntimeImageInspection?>(_available
                ? new RuntimeImageInspection(
                    ImageId,
                    [$"portainer/portainer-ce@sha256:{new string('b', 64)}"],
                    [])
                : null);

        public Task PullAsync(
            string immutableReference,
            CancellationToken cancellationToken)
        {
            LegacyPullCount++;
            _available = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDockerHost(Action onPull) : IDockerHost
    {
        public int PullCount { get; private set; }

        public Task PullImageAsync(string image, CancellationToken ct)
        {
            Assert.Equal(PortainerRuntimeOptions.ApprovedImage, image);
            PullCount++;
            onPull();
            return Task.CompletedTask;
        }

        public Task<bool> PingAsync(CancellationToken ct) =>
            Task.FromResult(true);

        public Task<bool> ImageExistsAsync(string image, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DockerContainerSummary>> ListContainersAsync(
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DockerContainerSummary>> ListByPrefixAsync(
            string namePrefix,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(
            string labelKey,
            string labelValue,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<DockerContainerInspection?> InspectByNameAsync(
            string containerName,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task EnsureNetworkAsync(
            string networkName,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task EnsureVolumeAsync(string volumeName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ConnectContainerToNetworkAsync(string containerIdOrName, string networkName, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task CopyFileToContainerAsync(string containerIdOrName, string destinationDirectory, string fileName, ReadOnlyMemory<byte> content, UnixFileMode mode, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<DockerExecResult> ExecAsync(string containerIdOrName, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string> CreateContainerAsync(
            DockerContainerSpec spec,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task StartContainerAsync(
            string containerId,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task StopContainerAsync(
            string containerId,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task RemoveContainerAsync(
            string containerId,
            bool force,
            bool removeVolumes,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string> GetLogsAsync(
            string containerId,
            int tail,
            CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
