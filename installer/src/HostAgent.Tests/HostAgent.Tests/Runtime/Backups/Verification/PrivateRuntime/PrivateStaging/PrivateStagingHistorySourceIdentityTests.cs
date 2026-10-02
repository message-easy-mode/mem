using System.Text.Json;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace HostAgent.Tests.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

public sealed class PrivateStagingHistorySourceIdentityTests
{
    [Fact]
    public async Task Catalog_run_preserves_catalog_identity_without_a_fabricated_validation_reference()
    {
        var dataRoot = CreateDataRoot();

        try
        {
            await WriteRunAsync(
                dataRoot,
                CreateRun(
                    stagingId: "staging-catalog",
                    sourceKind: PrivateStagingSourceKinds.BackupCatalog,
                    catalogEntryId: "bkp_private_staging_history",
                    validationId: null,
                    uploadedZipPath: null));

            var history = new PrivateStagingHistoryService(
                new TestConfiguration(dataRoot));

            var listed = await history.ListRunsAsync(
                validationId: null,
                status: null,
                max: 10,
                CancellationToken.None);

            var run = Assert.Single(listed.Runs);
            Assert.Equal(PrivateStagingSourceKinds.BackupCatalog, run.SourceKind);
            Assert.Equal("bkp_private_staging_history", run.CatalogEntryId);
            Assert.Null(run.ValidationId);
            Assert.Null(run.UploadedZipName);
            Assert.Null(run.UploadedZipPath);

            var safeListed = await history.ListSafeRunsAsync(
                validationId: null,
                status: null,
                max: 10,
                CancellationToken.None);

            var safeRun = Assert.Single(safeListed.Runs);
            Assert.Equal(PrivateStagingSourceKinds.BackupCatalog, safeRun.SourceKind);
            Assert.Equal("bkp_private_staging_history", safeRun.CatalogEntryId);
            Assert.Null(safeRun.ValidationId);

            var detail = await history.GetRunAsync(
                "staging-catalog",
                CancellationToken.None);

            var persisted = Assert.IsType<PrivateStagingRunResult>(detail?.Run);
            Assert.Equal(PrivateStagingSourceKinds.BackupCatalog, persisted.SourceKind);
            Assert.Equal("bkp_private_staging_history", persisted.CatalogEntryId);
            Assert.Null(persisted.ValidationId);
            Assert.Null(persisted.UploadedZipPath);
        }
        finally
        {
            DeleteDataRoot(dataRoot);
        }
    }

    [Fact]
    public void Private_staging_runner_contract_exposes_catalog_bound_execution_only()
    {
        var method = Assert.Single(typeof(IPrivateStagingRunner).GetMethods());

        Assert.Equal(
            nameof(IPrivateStagingRunner.CreatePrivateSynapseFromCatalogAsync),
            method.Name);

        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(PrivateStagingRunRequest), parameters[1].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[2].ParameterType);
    }

    private static PrivateStagingRunResult CreateRun(
        string stagingId,
        string sourceKind,
        string? catalogEntryId,
        string? validationId,
        string? uploadedZipPath)
    {
        return new PrivateStagingRunResult(
            Source: "control-plane",
            Status: "ready",
            Mode: "private-synapse-staging",
            StagingId: stagingId,
            ValidationId: validationId,
            StartedAtUtc: DateTimeOffset.UtcNow,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            UploadedZipPath: uploadedZipPath,
            WorkspacePath: "/mem-data/restore-staging/runs/" + stagingId,
            RuntimePath: "/mem-data/restore-staging/runs/" + stagingId + "/runtime",
            MatrixDataPath: "/mem-data/restore-staging/runs/" + stagingId + "/runtime/matrix",
            ElementDataPath: null,
            DatabaseDumpPath: "/mem-data/restore-staging/runs/" + stagingId + "/database/synapse.sql",
            NetworkName: "mem-restore-staging-" + stagingId,
            NetworkId: null,
            PostgresContainerName: "mem-restore-staging-postgres-" + stagingId,
            PostgresContainerId: null,
            SynapseContainerName: "mem-restore-staging-synapse-" + stagingId,
            SynapseContainerId: null,
            PostgresImage: "postgres:16",
            SynapseImage: "matrixdotorg/synapse:latest",
            DatabaseName: "synapse_restore_staging",
            DatabaseUser: "mem_restore_staging",
            MatrixServerName: "matrix.history.test",
            TargetStackSlug: "history-test",
            Safety: new PrivateStagingSafetySummary(
                PrivateOnly: true,
                DockerNetworkInternal: true,
                PublicRoutesCreated: false,
                DnsChanged: false,
                CertificatesChanged: false,
                ProductionContainersTouched: false,
                ProductionDatabasesTouched: false,
                RequiresExplicitDestroy: true,
                Notes: []),
            Database: new PrivateStagingDatabaseSummary(
                ImportSucceeded: true,
                PublicTableCount: 173,
                SynapseKnownTableCount: 6,
                UsersCount: 1,
                EventsCount: 11,
                RoomsCount: 1,
                StateEventsCount: 8),
            Runtime: new PrivateStagingRuntimeSummary(
                HomeserverConfigExtracted: true,
                HomeserverConfigPatched: true,
                SigningKeyExtracted: true,
                MediaStoreExtracted: true,
                MediaFiles: 0,
                MediaBytes: 0,
                ElementConfigExtracted: false,
                PostgresContainerStarted: true,
                SynapseContainerStarted: true,
                SynapseHealthPassed: true,
                HealthResponse: "OK",
                SynapseLogsTail: null),
            Checks: [],
            Warnings: [],
            Errors: [],
            Destroy: null,
            Detail: "Test private staging run.",
            SourceKind: sourceKind,
            CatalogEntryId: catalogEntryId);
    }

    private static async Task WriteRunAsync(
        string dataRoot,
        PrivateStagingRunResult result)
    {
        var directory = Path.Combine(
            dataRoot,
            "restore-staging",
            "history",
            result.StagingId);

        Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
            Path.Combine(directory, "restore-staging-result.json"),
            JsonSerializer.Serialize(
                result,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static string CreateDataRoot() =>
        Path.Combine(
            Path.GetTempPath(),
            $"mem-private-staging-history-{Guid.NewGuid():N}");

    private static void DeleteDataRoot(string dataRoot)
    {
        if (Directory.Exists(dataRoot))
        {
            Directory.Delete(dataRoot, recursive: true);
        }
    }

    private sealed class TestConfiguration : IConfiguration
    {
        private readonly string _dataRoot;

        public TestConfiguration(string dataRoot)
        {
            _dataRoot = dataRoot;
        }

        public string? this[string key]
        {
            get => string.Equals(key, "HostAgent:DataRoot", StringComparison.Ordinal)
                ? _dataRoot
                : null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => NoopChangeToken.Instance;

        public IConfigurationSection GetSection(string key) =>
            new TestConfigurationSection(key, this[key]);
    }

    private sealed class TestConfigurationSection : IConfigurationSection
    {
        public TestConfigurationSection(string key, string? value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }
        public string Path => Key;
        public string? Value { get; set; }

        public string? this[string key]
        {
            get => null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => NoopChangeToken.Instance;

        public IConfigurationSection GetSection(string key) =>
            new TestConfigurationSection(key, null);
    }

    private sealed class NoopChangeToken : IChangeToken
    {
        public static NoopChangeToken Instance { get; } = new();

        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;

        public IDisposable RegisterChangeCallback(
            Action<object?> callback,
            object? state) =>
            NoopDisposable.Instance;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
