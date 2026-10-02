using System.Net;
using System.Net.Http.Headers;
using Core.RuntimeDefinition;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Integrations.Npm.Services;

namespace Api.IntegrationTests.Setup;

public sealed class NpmCertificateSecretBoundaryTests
{
    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_03_CORR_01_patches_restarts_and_records_current_container_activation()
    {
        var docker = new RecordingDockerHost(
            Container(NpmRuntimeRelease.ApprovedImage),
            new DockerExecResult(0, "vulnerable", "", false),
            new DockerExecResult(0, "patched", "", false),
            new DockerExecResult(0, "ready", "", false),
            new DockerExecResult(0, "activated", "", false));

        var service = CreateService(docker);

        var result = await service.EnsureHardenedAsync(CancellationToken.None);

        Assert.True(result.Changed);
        Assert.True(result.Restarted);
        Assert.Equal(1, docker.StopCalls);
        Assert.Equal(1, docker.StartCalls);
        Assert.Equal(4, docker.ExecCommands.Count);
        Assert.Contains("certificate.js", string.Join(" ", docker.ExecCommands[0]), StringComparison.Ordinal);
        Assert.Contains("vulnerable", string.Join(" ", docker.ExecCommands[0]), StringComparison.Ordinal);
        Assert.Contains("Writing Custom Certificate", string.Join(" ", docker.ExecCommands[1]), StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:81/api", string.Join(" ", docker.ExecCommands[2]), StringComparison.Ordinal);
        Assert.Contains("npm-certificate-log-boundary-v1", string.Join(" ", docker.ExecCommands[3]), StringComparison.Ordinal);
        Assert.True(docker.CurrentInspection!.Running);
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_03_CORR_01_does_not_restart_when_current_container_activation_is_proven()
    {
        var docker = new RecordingDockerHost(
            Container(NpmRuntimeRelease.ApprovedImage),
            new DockerExecResult(0, "hardened-active", "", false));

        var service = CreateService(docker);

        var result = await service.EnsureHardenedAsync(CancellationToken.None);

        Assert.False(result.Changed);
        Assert.False(result.Restarted);
        Assert.Equal(0, docker.StopCalls);
        Assert.Equal(0, docker.StartCalls);
        Assert.Single(docker.ExecCommands);
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_03_CORR_01_restarts_hardened_source_when_activation_was_not_completed()
    {
        var docker = new RecordingDockerHost(
            Container(NpmRuntimeRelease.ApprovedImage),
            new DockerExecResult(0, "hardened-inactive", "", false),
            new DockerExecResult(0, "ready", "", false),
            new DockerExecResult(0, "activated", "", false));

        var service = CreateService(docker);

        var result = await service.EnsureHardenedAsync(CancellationToken.None);

        Assert.False(result.Changed);
        Assert.True(result.Restarted);
        Assert.Equal(1, docker.StopCalls);
        Assert.Equal(1, docker.StartCalls);
        Assert.Equal(3, docker.ExecCommands.Count);
        Assert.True(docker.CurrentInspection!.Running);
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_03_CORR_01_request_cancellation_after_stop_does_not_own_NPM_restart_lifetime()
    {
        using var request = new CancellationTokenSource();
        var docker = new RecordingDockerHost(
            Container(NpmRuntimeRelease.ApprovedImage),
            new DockerExecResult(0, "vulnerable", "", false),
            new DockerExecResult(0, "patched", "", false),
            new DockerExecResult(0, "ready", "", false),
            new DockerExecResult(0, "activated", "", false))
        {
            RequestToCancelAfterStop = request
        };

        var service = CreateService(docker);

        var result = await service.EnsureHardenedAsync(request.Token);

        Assert.True(request.IsCancellationRequested);
        Assert.True(result.Restarted);
        Assert.Equal(1, docker.StopCalls);
        Assert.Equal(1, docker.StartCalls);
        Assert.True(docker.CurrentInspection!.Running);
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_03_CORR_01_failed_restart_attempt_recovers_NPM_to_running_before_returning_failure()
    {
        var docker = new RecordingDockerHost(
            Container(NpmRuntimeRelease.ApprovedImage),
            new DockerExecResult(0, "vulnerable", "", false),
            new DockerExecResult(0, "patched", "", false))
        {
            FailFirstStart = true
        };

        var service = CreateService(docker);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureHardenedAsync(CancellationToken.None));

        Assert.Contains("restored NPM to a running state", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, docker.StopCalls);
        Assert.Equal(2, docker.StartCalls);
        Assert.True(docker.CurrentInspection!.Running);
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_03_fails_closed_when_pinned_NPM_source_no_longer_matches()
    {
        var docker = new RecordingDockerHost(
            Container(NpmRuntimeRelease.ApprovedImage),
            new DockerExecResult(42, "", "unexpected-source", false));

        var service = CreateService(docker);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureHardenedAsync(CancellationToken.None));

        Assert.Contains("No certificate private key was sent to NPM", ex.Message, StringComparison.Ordinal);
        Assert.Equal(0, docker.StopCalls);
        Assert.Equal(0, docker.StartCalls);
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_03_fails_closed_for_unreviewed_NPM_image()
    {
        var docker = new RecordingDockerHost(Container("jc21/nginx-proxy-manager:latest"));
        var service = CreateService(docker);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureHardenedAsync(CancellationToken.None));

        Assert.Contains("cannot verify the private-key log boundary", ex.Message, StringComparison.Ordinal);
        Assert.Empty(docker.ExecCommands);
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_03_upload_success_does_not_read_secret_bearing_response_body()
    {
        var content = new ReadForbiddenContent();
        using var http = new HttpClient(new FixedResponseHandler(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        var client = new NpmApiClient(http);
        var (cert, key) = CreateTempPemFiles();

        try
        {
            await client.UploadCustomCertificateFilesAsync(
                "http://npm:81/api",
                "token",
                1,
                cert,
                key,
                CancellationToken.None);

            Assert.Equal(0, content.ReadAttempts);
        }
        finally
        {
            File.Delete(cert);
            File.Delete(key);
        }
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_03_upload_failure_withholds_upstream_response_body()
    {
        const string sentinel = "PRIVATE_KEY_SENTINEL";
        using var http = new HttpClient(new FixedResponseHandler(
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(sentinel)
            }));
        var client = new NpmApiClient(http);
        var (cert, key) = CreateTempPemFiles();

        try
        {
            var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
                client.UploadCustomCertificateFilesAsync(
                    "http://npm:81/api",
                    "token",
                    1,
                    cert,
                    key,
                    CancellationToken.None));

            Assert.DoesNotContain(sentinel, ex.Message, StringComparison.Ordinal);
            Assert.Contains("response content was withheld", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(cert);
            File.Delete(key);
        }
    }

    private static NpmCertificateLogSecretBoundaryService CreateService(RecordingDockerHost docker) =>
        new(docker, NullLogger<NpmCertificateLogSecretBoundaryService>.Instance);

    private static DockerContainerInspection Container(string image) =>
        new(
            Id: "npm-id",
            Name: ManagedContainerNames.Npm,
            Image: image,
            State: "running",
            Running: true,
            Ports: []);

    private static (string Certificate, string PrivateKey) CreateTempPemFiles()
    {
        var certificate = Path.GetTempFileName();
        var privateKey = Path.GetTempFileName();
        File.WriteAllText(certificate, "certificate-test-content");
        File.WriteAllText(privateKey, "private-key-test-content");
        return (certificate, privateKey);
    }

    private sealed class FixedResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.IsType<MultipartFormDataContent>(request.Content);
            Assert.Equal(new AuthenticationHeaderValue("Bearer", "token"), request.Headers.Authorization);
            return Task.FromResult(response);
        }
    }

    private sealed class ReadForbiddenContent : HttpContent
    {
        public int ReadAttempts { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            ReadAttempts++;
            throw new InvalidOperationException("Response body must not be materialised.");
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class RecordingDockerHost : IDockerHost
    {
        private DockerContainerInspection? _inspection;
        private readonly Queue<DockerExecResult> _execResults;

        public RecordingDockerHost(
            DockerContainerInspection inspection,
            params DockerExecResult[] execResults)
        {
            _inspection = inspection;
            _execResults = new Queue<DockerExecResult>(execResults);
        }

        public List<IReadOnlyList<string>> ExecCommands { get; } = [];
        public int StopCalls { get; private set; }
        public int StartCalls { get; private set; }
        public bool FailFirstStart { get; init; }
        public CancellationTokenSource? RequestToCancelAfterStop { get; init; }
        public DockerContainerInspection? CurrentInspection => _inspection;

        public Task<bool> PingAsync(CancellationToken ct) => Task.FromResult(true);
        public Task PullImageAsync(string image, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> ImageExistsAsync(string image, CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<DockerContainerSummary>> ListContainersAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<IReadOnlyList<DockerContainerSummary>> ListByPrefixAsync(string namePrefix, CancellationToken ct) => Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(string labelKey, string labelValue, CancellationToken ct) => Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);
        public Task<DockerContainerInspection?> InspectByNameAsync(string containerName, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_inspection);
        }
        public Task EnsureNetworkAsync(string networkName, CancellationToken ct) => Task.CompletedTask;
        public Task EnsureVolumeAsync(string volumeName, CancellationToken ct) => Task.CompletedTask;
        public Task ConnectContainerToNetworkAsync(string containerIdOrName, string networkName, CancellationToken ct) => Task.CompletedTask;
        public Task<string> CreateContainerAsync(DockerContainerSpec spec, CancellationToken ct) => throw new NotSupportedException();
        public Task CopyFileToContainerAsync(string containerIdOrName, string destinationDirectory, string fileName, ReadOnlyMemory<byte> content, UnixFileMode mode, CancellationToken ct) => Task.CompletedTask;

        public Task<DockerExecResult> ExecAsync(string containerIdOrName, IReadOnlyList<string> command, TimeSpan timeout, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ExecCommands.Add(command.ToArray());
            if (_execResults.Count == 0)
            {
                throw new InvalidOperationException("No test exec result remains.");
            }
            return Task.FromResult(_execResults.Dequeue());
        }

        public Task StartContainerAsync(string containerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            StartCalls++;
            if (FailFirstStart && StartCalls == 1)
            {
                throw new InvalidOperationException("simulated first start failure");
            }

            _inspection = _inspection! with { State = "running", Running = true };
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(string containerId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            StopCalls++;
            _inspection = _inspection! with { State = "exited", Running = false };
            RequestToCancelAfterStop?.Cancel();
            return Task.CompletedTask;
        }

        public Task RemoveContainerAsync(string containerId, bool force, bool removeVolumes, CancellationToken ct) => Task.CompletedTask;
        public Task<string> GetLogsAsync(string containerId, int tail, CancellationToken ct) => Task.FromResult(string.Empty);
    }
}
