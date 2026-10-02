using HostAgent.Docker;
using HostAgent.Docker.Models;
using HostAgent.Matrix.Provisioning;
using Microsoft.Extensions.Logging.Abstractions;

namespace HostAgent.Tests.Services;

public sealed class SynapseConfigGeneratorCancellationTests
{
    [Fact]
    public async Task STACK_CREATE_REL_01A_docker_wait_cancellation_without_caller_cancellation_is_classified()
    {
        var docker = new CancellingDockerHost();
        var generator = new SynapseConfigGenerator(
            docker,
            Microsoft.Extensions.Options.Options.Create(new MatrixBootstrapOptions { SharedSecret = "test-shared-secret" }),
            NullLogger<SynapseConfigGenerator>.Instance);
        var path = Path.Combine(Path.GetTempPath(), $"mem-stack-create-rel-01a-{Guid.NewGuid():N}");

        try
        {
            var exception = await Assert.ThrowsAsync<SynapseConfigGenerationCanceledException>(() =>
                generator.GenerateAsync(
                    Guid.NewGuid(),
                    "matrix.example.test",
                    path,
                    reportStats: false,
                    CancellationToken.None,
                    image: "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));

            Assert.Equal("wait-for-exit", exception.Stage);
            Assert.Equal("container-1", exception.ContainerId);
            Assert.False(exception.CallerCancellationRequested);
            Assert.Equal(
                "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                docker.CreatedImage);
            Assert.True(docker.LogToken.CanBeCanceled);
            Assert.False(docker.LogToken.IsCancellationRequested);
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
    }

    private sealed class CancellingDockerHost : IDockerHost
    {
        public CancellationToken LogToken { get; private set; }
        public string? CreatedImage { get; private set; }

        public Task<string> CreateContainerAsync(DockerContainerSpec spec, CancellationToken ct)
        {
            CreatedImage = spec.Image;
            return Task.FromResult("container-1");
        }

        public Task StartContainerAsync(string containerId, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<int> WaitForExitAsync(string containerId, CancellationToken ct) =>
            Task.FromException<int>(new TaskCanceledException("docker wait cancelled"));

        public Task<string> GetLogsAsync(string containerId, int tail, CancellationToken ct)
        {
            LogToken = ct;
            return Task.FromResult("bounded diagnostic log capture");
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct) => throw new NotSupportedException();
        public Task RemoveContainerAsync(string containerId, bool force, CancellationToken ct) => throw new NotSupportedException();
        public Task<DockerContainerInspection> InspectAsync(string containerId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(string labelKey, string labelValue, CancellationToken ct) => throw new NotSupportedException();
        public Task EnsureNetworkConnectedAsync(string containerId, string networkName, string? alias, CancellationToken ct) => throw new NotSupportedException();
    }
}
