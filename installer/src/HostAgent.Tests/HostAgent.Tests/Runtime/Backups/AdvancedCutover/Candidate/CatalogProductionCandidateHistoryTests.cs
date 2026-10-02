using System.Text.Json;
using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace HostAgent.Tests.Runtime.Backups.AdvancedCutover.Candidate;

public sealed class CatalogProductionCandidateHistoryTests
{
    [Fact]
    public async Task Finds_active_catalog_candidate_and_preserves_catalog_identity_in_history_summary()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-catalog-candidate-history-{Guid.NewGuid():N}");

        try
        {
            var active = CreateCandidate(
                candidateId: "candidate-active",
                catalogEntryId: "bkp_candidate_history",
                targetStackSlug: "demo-stack-candidate",
                status: "private_candidate_ready",
                destroyed: false);

            var destroyed = CreateCandidate(
                candidateId: "candidate-destroyed",
                catalogEntryId: "bkp_candidate_history",
                targetStackSlug: "demo-stack-candidate",
                status: "destroyed",
                destroyed: true);

            await WriteCandidateAsync(dataRoot, active);
            await WriteCandidateAsync(dataRoot, destroyed);

            var history = new RuntimeStackBackupProductionCandidateHistoryService(
                new TestConfiguration(dataRoot));

            var found = await history.FindActiveCatalogCandidateAsync(
                "bkp_candidate_history",
                "demo-stack-candidate",
                CancellationToken.None);

            var activeFound = Assert.IsType<RuntimeStackBackupProductionCandidateResult>(
                found);
            Assert.Equal("candidate-active", activeFound.CandidateId);
            Assert.Equal("bkp_candidate_history", activeFound.CatalogEntryId);
            Assert.Equal("backup-catalog", activeFound.SourceKind);
            Assert.Equal("restore-session-history", activeFound.RestoreSessionId);

            var listed = await history.ListCandidatesAsync(
                validationId: null,
                status: null,
                max: 10,
                CancellationToken.None);

            var summary = Assert.Single(
                listed.Candidates.Where(candidate =>
                    candidate.CandidateId == "candidate-active"));

            Assert.Equal("bkp_candidate_history", summary.CatalogEntryId);
            Assert.Equal("backup-catalog", summary.SourceKind);
            Assert.Equal("restore-session-history", summary.RestoreSessionId);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Finds_one_active_migration_candidate_without_catalog_or_restore_identity()
    {
        var dataRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-migration-candidate-history-{Guid.NewGuid():N}");

        try
        {
            var active = CreateCandidate(
                candidateId: "candidate-migration-active",
                catalogEntryId: "unused",
                targetStackSlug: "migration-production-candidate",
                status: "private_candidate_ready",
                destroyed: false) with
            {
                ValidationId = "mig_candidate_history",
                CatalogEntryId = null,
                SourceKind = "migration-session",
                RestoreSessionId = null
            };

            var other = active with
            {
                CandidateId = "candidate-other-migration",
                ValidationId = "mig_other",
                PrivateRuntimeId = "candidate-other-migration"
            };

            await WriteCandidateAsync(dataRoot, active);
            await WriteCandidateAsync(dataRoot, other);

            var history = new RuntimeStackBackupProductionCandidateHistoryService(
                new TestConfiguration(dataRoot));

            var found = await history.FindActiveMigrationCandidateAsync(
                "mig_candidate_history",
                targetStackSlug: null,
                ct: CancellationToken.None);

            var migrationCandidate = Assert.IsType<RuntimeStackBackupProductionCandidateResult>(found);
            Assert.Equal("candidate-migration-active", migrationCandidate.CandidateId);
            Assert.Equal("mig_candidate_history", migrationCandidate.ValidationId);
            Assert.Equal("migration-session", migrationCandidate.SourceKind);
            Assert.Null(migrationCandidate.CatalogEntryId);
            Assert.Null(migrationCandidate.RestoreSessionId);
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Catalog_request_maps_to_the_existing_private_candidate_request_shape()
    {
        var request = new CatalogProductionCandidateRequest(
            KeepOnFailure: null,
            PostgresImage: "postgres:16",
            SynapseImage: "matrixdotorg/synapse:latest",
            ElementImage: "vectorim/element-web:latest",
            TargetStackSlug: "demo-stack-candidate",
            RestoreMode: "recreate-production");

        var candidateRequest = request.ToCandidateRequest();

        Assert.False(candidateRequest.KeepOnFailure);
        Assert.Equal("postgres:16", candidateRequest.PostgresImage);
        Assert.Equal("matrixdotorg/synapse:latest", candidateRequest.SynapseImage);
        Assert.Equal("vectorim/element-web:latest", candidateRequest.ElementImage);
        Assert.Equal("demo-stack-candidate", candidateRequest.TargetStackSlug);
        Assert.Equal("recreate-production", candidateRequest.RestoreMode);
    }

    private static async Task WriteCandidateAsync(
        string dataRoot,
        RuntimeStackBackupProductionCandidateResult candidate)
    {
        var path = Path.Combine(
            dataRoot,
            "production-restore",
            "candidates",
            candidate.CandidateId);

        Directory.CreateDirectory(path);

        await File.WriteAllTextAsync(
            Path.Combine(path, "production-candidate-result.json"),
            JsonSerializer.Serialize(
                candidate,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static RuntimeStackBackupProductionCandidateResult CreateCandidate(
        string candidateId,
        string catalogEntryId,
        string targetStackSlug,
        string status,
        bool destroyed)
    {
        return new RuntimeStackBackupProductionCandidateResult(
            Source: "control-plane",
            Status: status,
            Mode: "recreate-production-private-candidate",
            CandidateId: candidateId,
            ValidationId: catalogEntryId,
            RestoreMode: "recreate-production",
            StartedAtUtc: DateTimeOffset.UtcNow,
            FinishedAtUtc: null,
            TargetStackSlug: targetStackSlug,
            MatrixServerName: "matrix.demo.test",
            PrivateRuntimeId: candidateId,
            PrivateRuntimeStatus: status,
            WorkspacePath: "/tmp/workspace",
            RuntimePath: "/tmp/workspace/runtime",
            MatrixDataPath: "/tmp/workspace/runtime/matrix",
            ElementDataPath: "/tmp/workspace/runtime/element",
            NetworkName: $"mem-restore-staging-{candidateId}",
            NetworkId: null,
            PostgresContainerName: $"postgres-{candidateId}",
            PostgresContainerId: null,
            SynapseContainerName: $"synapse-{candidateId}",
            SynapseContainerId: null,
            ElementContainerName: null,
            ElementContainerId: null,
            ElementImage: "vectorim/element-web:latest",
            DatabaseName: "synapse_restore",
            DatabaseUser: "mem_restore",
            Safety: new RuntimeStackBackupProductionCandidateSafetySummary(
                PrivateOnly: true,
                DockerNetworkInternal: true,
                PublicRoutesCreated: false,
                DnsChanged: false,
                CertificatesChanged: false,
                NpmRoutesChanged: false,
                ProductionContainersTouched: false,
                ProductionDatabasesTouched: false,
                PublicCutoverPerformed: false,
                ProductionExecutionLocked: true,
                RequiresExplicitDestroy: true,
                Notes: []),
            Database: new RuntimeStackBackupProductionCandidateDatabaseSummary(
                ImportSucceeded: true,
                PublicTableCount: 173,
                SynapseKnownTableCount: 6,
                UsersCount: 0,
                EventsCount: 0,
                RoomsCount: 0,
                StateEventsCount: 0),
            Runtime: new RuntimeStackBackupProductionCandidateRuntimeSummary(
                HomeserverConfigExtracted: true,
                HomeserverConfigPatched: true,
                SigningKeyExtracted: true,
                MediaStoreExtracted: false,
                MediaFiles: 0,
                MediaBytes: 0,
                ElementConfigExtracted: true,
                ElementConfigPatched: false,
                ElementContainerStarted: false,
                ElementHealthPassed: false,
                ElementHealthResponse: null,
                ElementLogsTail: null,
                PostgresContainerStarted: true,
                SynapseContainerStarted: true,
                SynapseHealthPassed: true,
                HealthResponse: "OK",
                SynapseLogsTail: null),
            Checks: [],
            Warnings: [],
            Errors: [],
            Destroy: destroyed
                ? new RuntimeStackBackupProductionCandidateDestroySummary(
                    DestroyedAtUtc: DateTimeOffset.UtcNow,
                    PrivateRuntimeDestroyed: true,
                    ElementContainerRemoved: false,
                    Warnings: [])
                : null,
            Detail: "Test candidate.",
            CatalogEntryId: catalogEntryId,
            SourceKind: "backup-catalog",
            RestoreSessionId: "restore-session-history");
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
