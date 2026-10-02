using HostAgent.Matrix.Users;
using HostAgent.Runtime.Backups.StandardRecreate;
using HostAgent.Runtime.Manifests;
using Microsoft.Extensions.Logging.Abstractions;

namespace HostAgent.Tests.Runtime.Backups.StandardRecreate;

public sealed class StandardRecreateUserInventoryFinalizerTests
{
    [Fact]
    public async Task StandardRecreateUserInventory_success_returns_aggregate_safe_evidence()
    {
        var synchronizedAtUtc = new DateTimeOffset(2026, 7, 10, 1, 2, 3, TimeSpan.Zero);
        var synchronizer = new RecordingSynchronizer(new RuntimeStackUserInventorySynchronizationResult(
            RuntimeStackId: StackId,
            Source: SynapsePostgresUserInventoryReader.InventorySource,
            Accounts:
            [
                new SynapseUserInventoryAccount(
                    MatrixUserId: "@restored-admin:matrix.example.test",
                    Username: "restored-admin",
                    IsAdmin: true,
                    IsDeactivated: false,
                    CreatedAtUtc: synchronizedAtUtc.UtcDateTime),
                new SynapseUserInventoryAccount(
                    MatrixUserId: "@restored-user:matrix.example.test",
                    Username: "restored-user",
                    IsAdmin: false,
                    IsDeactivated: false,
                    CreatedAtUtc: synchronizedAtUtc.UtcDateTime)
            ],
            InsertedCount: 2,
            UpdatedCount: 0,
            MissingCount: 0,
            SynchronizedAtUtc: synchronizedAtUtc));
        var sut = CreateSut(synchronizer);

        var result = await sut.FinalizeAsync(CreateManifest(), restoredDatabaseUserCount: 2, ct: CancellationToken.None);

        Assert.Equal(1, synchronizer.CallCount);
        Assert.Equal(StackId, synchronizer.Manifest?.StackId);
        Assert.Equal(RuntimeStackUserInventoryStates.Synchronized, result.Summary.Status);
        Assert.Equal(SynapsePostgresUserInventoryReader.InventorySource, result.Summary.Source);
        Assert.Equal(2, result.Summary.UserCount);
        Assert.Equal(1, result.Summary.ActiveAdminCount);
        Assert.Equal(synchronizedAtUtc, result.Summary.SynchronizedAtUtc);
        Assert.Null(result.Summary.ErrorCode);
        Assert.True(result.Check.Passed);
        Assert.Equal("info", result.Check.Severity);
        Assert.Equal(StandardRecreateUserInventoryFinalizer.CheckCode, result.Check.Code);
        Assert.Null(result.Warning);

        var serialized = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("restored-admin", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("restored-user", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("matrix.example.test", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardRecreateUserInventory_count_mismatch_is_safe_warning_not_failure()
    {
        var synchronizedAtUtc = DateTimeOffset.UtcNow;
        var sut = CreateSut(new RecordingSynchronizer(new RuntimeStackUserInventorySynchronizationResult(
            RuntimeStackId: StackId,
            Source: SynapsePostgresUserInventoryReader.InventorySource,
            Accounts:
            [
                new SynapseUserInventoryAccount(
                    MatrixUserId: "@restored-user:matrix.example.test",
                    Username: "restored-user",
                    IsAdmin: false,
                    IsDeactivated: false,
                    CreatedAtUtc: synchronizedAtUtc.UtcDateTime)
            ],
            InsertedCount: 1,
            UpdatedCount: 0,
            MissingCount: 0,
            SynchronizedAtUtc: synchronizedAtUtc)));

        var result = await sut.FinalizeAsync(
            CreateManifest(),
            restoredDatabaseUserCount: 2,
            ct: CancellationToken.None);

        Assert.True(result.Check.Passed);
        Assert.Equal(RuntimeStackUserInventoryStates.Synchronized, result.Summary.Status);
        Assert.Contains("reported 2 Matrix users", result.Warning!, StringComparison.Ordinal);
        Assert.Contains("returned 1", result.Warning!, StringComparison.Ordinal);
        Assert.DoesNotContain("restored-user", result.Warning!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardRecreateUserInventory_known_failure_is_retryable_and_non_throwing()
    {
        var sut = CreateSut(new ThrowingSynchronizer(
            new RuntimeStackUserInventoryException(
                "synapse_users_schema_unsupported",
                "safe detail")));

        var result = await sut.FinalizeAsync(CreateManifest(), restoredDatabaseUserCount: null, ct: CancellationToken.None);

        Assert.Equal(RuntimeStackUserInventoryStates.Failed, result.Summary.Status);
        Assert.Equal(SynapsePostgresUserInventoryReader.InventorySource, result.Summary.Source);
        Assert.Equal("synapse_users_schema_unsupported", result.Summary.ErrorCode);
        Assert.False(result.Check.Passed);
        Assert.Equal("warning", result.Check.Severity);
        Assert.Contains("Retry synchronization", result.Warning!, StringComparison.Ordinal);
        Assert.DoesNotContain("safe detail", result.Warning!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardRecreateUserInventory_unexpected_failure_uses_generic_safe_code()
    {
        var sut = CreateSut(new ThrowingSynchronizer(
            new InvalidOperationException("postgres password=not-safe")));

        var result = await sut.FinalizeAsync(CreateManifest(), restoredDatabaseUserCount: null, ct: CancellationToken.None);

        Assert.Equal("matrix_user_inventory_unavailable", result.Summary.ErrorCode);
        Assert.DoesNotContain("password", result.Check.Detail!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("not-safe", result.Warning!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StandardRecreateUserInventory_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var sut = CreateSut(new ThrowingSynchronizer(
            new OperationCanceledException(cts.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sut.FinalizeAsync(CreateManifest(), restoredDatabaseUserCount: null, ct: cts.Token));
    }

    private static readonly Guid StackId = Guid.Parse("b03116fd-e3ba-4cb4-a721-d590a971cf0e");

    private static StandardRecreateUserInventoryFinalizer CreateSut(
        IRuntimeStackUserInventorySynchronizer synchronizer) =>
        new(
            synchronizer,
            NullLogger<StandardRecreateUserInventoryFinalizer>.Instance);

    private static RuntimeStackManifest CreateManifest() =>
        new(
            Source: "control-plane",
            StackId: StackId,
            Slug: "restored-stack",
            LastVerifiedStatus: "public_routes_verified",
            LastVerifiedAtUtc: DateTimeOffset.UtcNow,
            Matrix: new RuntimeStackServiceManifest(
                InstanceId: Guid.NewGuid(),
                ServiceKey: "matrix",
                ContainerId: "matrix-container",
                ContainerName: "mem-matrix-restored-stack",
                HostPort: 0,
                DataPath: "/runtime/matrix",
                ServerName: "matrix.example.test",
                PublicHost: "matrix.example.test",
                PublicBaseUrl: "https://matrix.example.test",
                InternalHost: "mem-matrix-restored-stack",
                InternalBaseUrl: "http://mem-matrix-restored-stack:8008",
                PublicRouteId: "route-1",
                InternalRouteId: null,
                NpmCertificateId: 1,
                RuntimeMetadata: new Dictionary<string, string?>()),
            Element: null,
            Warnings: [],
            Metadata: new Dictionary<string, string?>());

    private sealed class RecordingSynchronizer(
        RuntimeStackUserInventorySynchronizationResult result)
        : IRuntimeStackUserInventorySynchronizer
    {
        public int CallCount { get; private set; }
        public RuntimeStackManifest? Manifest { get; private set; }

        public Task<RuntimeStackUserInventorySynchronizationResult> SynchronizeAsync(
            RuntimeStackManifest manifest,
            CancellationToken ct)
        {
            CallCount++;
            Manifest = manifest;
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingSynchronizer(Exception exception)
        : IRuntimeStackUserInventorySynchronizer
    {
        public Task<RuntimeStackUserInventorySynchronizationResult> SynchronizeAsync(
            RuntimeStackManifest manifest,
            CancellationToken ct) =>
            Task.FromException<RuntimeStackUserInventorySynchronizationResult>(exception);
    }
}
