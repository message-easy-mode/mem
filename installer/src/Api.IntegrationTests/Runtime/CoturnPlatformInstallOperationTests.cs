using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Operations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.Platform.Coturn;

namespace Api.IntegrationTests.Runtime;

public sealed class CoturnPlatformInstallOperationTests
{
    [Fact]
    public async Task Explicit_platform_install_uses_the_Setup_boundary_and_persists_only_safe_terminal_evidence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var operations = new RuntimeOperationStore(db);
        var operationId = await operations.StartAsync(
            runtimeStackId: null,
            operation: CoturnPlatformInstallOperationService.OperationName,
            idempotencyKey: null,
            requestedBy: "owner",
            hostMutationLevel: "docker,platform-turn,host-ports",
            input: new
            {
                externalIpConfigured = true,
                publishRelayPorts = true,
                imageBoundary = "installation-approved-immutable"
            },
            CancellationToken.None);

        var setup = new RecordingSetupService();
        var processor = new CoturnPlatformInstallOperationProcessor(
            setup,
            operations,
            new FakeHostApplicationLifetime(),
            NullLogger<CoturnPlatformInstallOperationProcessor>.Instance);

        await processor.ExecuteAsync(
            operationId,
            new CoturnPlatformInstallRequest("203.0.113.9"));

        var operation = await operations.FindByIdAsync(
            operationId,
            CancellationToken.None);

        Assert.NotNull(operation);
        Assert.Equal("succeeded", operation!.Status);
        Assert.Equal("completed", operation.CurrentStep);
        Assert.NotNull(operation.CompletedAtUtc);
        Assert.Equal("203.0.113.9", setup.LastRequest?.ExternalIp);
        Assert.False(setup.LastRequest?.ForceRecreate ?? true);
        Assert.Equal(1, setup.InstallCalls);
        Assert.Equal(1, setup.InspectCalls);

        Assert.DoesNotContain("203.0.113.9", operation.InputJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("turn-secret-value", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain("turn-secret-value", operation.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("installation-approved-immutable", operation.EvidenceJson ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("mem-coturn", operation.ResultJson ?? string.Empty, StringComparison.Ordinal);
    }

    private sealed class RecordingSetupService : IPlatformCoturnSetupService
    {
        public PlatformCoturnSetupRequest? LastRequest { get; private set; }
        public int InstallCalls { get; private set; }
        public int InspectCalls { get; private set; }

        public Task<PlatformCoturnSetupResult> EnsureInstalledAsync(
            PlatformCoturnSetupRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            InstallCalls++;
            return Task.FromResult(ReadyResult());
        }

        public Task<PlatformCoturnSetupResult> InspectAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCalls++;
            return Task.FromResult(ReadyResult());
        }

        private static PlatformCoturnSetupResult ReadyResult() =>
            new(
                Ready: true,
                Status: "ready",
                Readiness: "ready",
                ContainerState: "running",
                ContainerName: "mem-coturn",
                PublicHost: "turn.example.test",
                Realm: "example.test",
                ApprovedImageReference: "coturn/coturn@sha256:approved",
                ResolvedImageId: "sha256:resolved",
                ContainerExists: true,
                Running: true,
                OwnershipVerified: true,
                ImageApproved: true,
                SecretPresent: true,
                SecretFilePermissionsApplied: true,
                RelayPortsPublished: true,
                SecurityPolicyApplied: true,
                Recreated: false,
                PublishedPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
                RequiredProductionFirewallPorts: ["3478/tcp", "3478/udp", "49160-49200/udp"],
                Warnings: [],
                Detail: null);
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();
    }
}
