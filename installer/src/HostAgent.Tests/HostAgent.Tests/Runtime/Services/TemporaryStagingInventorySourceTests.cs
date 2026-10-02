using System.Text.Json;
using Docker.DotNet;
using HostAgent.Runtime.Services.TemporaryStaging;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Tests.Runtime.Services;

public sealed class TemporaryStagingInventorySourceTests
{
    [Fact]
    public async Task Migration_owner_is_loaded_from_the_exact_staging_candidate_and_session_relationship()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = new MigrationIntakeEntity { Id = Guid.NewGuid(), IntakeId = "mig_one", DisplayName = "Example", CreatedAtUtc = DateTime.UtcNow };
        var conversion = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(), ConversionAttemptId = "conv_one", MigrationIntake = intake,
            SourcePackageSha256 = new string('a', 64), SourceAdapterId = "mem-v010", SourceAdapterVersion = "0.1.0",
            ConverterId = "worker", ConverterVersion = "v2", Status = "completed", CurrentStep = "candidate-created",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        var candidate = new MigrationCandidateArtifactEntity
        {
            Id = Guid.NewGuid(), CandidateArtifactId = "mca_one", ConversionAttempt = conversion,
            ArtifactKind = "synapse-postgresql-conversion", ArtifactSchemaVersion = "v1",
            SourcePackageSha256 = new string('a', 64), ArtifactSha256 = new string('b', 64),
            ManifestSha256 = new string('c', 64), ChecksumsSha256 = new string('d', 64), ProvenanceJson = "{}",
            VerificationStatus = "verified", RetentionState = "active", StorageKind = "server-filesystem",
            ArtifactPath = "/private/do-not-project", CreatedAtUtc = DateTime.UtcNow
        };
        fixture.Db.MigrationStagingRuns.Add(new MigrationStagingRunEntity
        {
            Id = Guid.NewGuid(), StagingRunId = "mst_one", MigrationIntake = intake, CandidateArtifact = candidate,
            PrivateRuntimeStagingId = "stage-one", Status = "verified", CurrentStep = "verified",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var owner = Assert.Single(await fixture.Source.ReadOwnersAsync(CancellationToken.None));
        Assert.Equal("mig_one", owner.OwnerId);
        Assert.Equal("stage-one", owner.StagingId);
        Assert.Equal("mca_one", owner.SourceIdentity);
        Assert.True(owner.BindingValid);
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Restore_owner_requires_private_test_operation_evidence_not_just_matching_catalog()
    {
        await using var fixture = await Fixture.CreateAsync();
        var attempt = new RestoreAttemptEntity
        {
            Id = Guid.NewGuid(), RestoreSessionId = "rs_one", SourceKind = "backup-catalog", SourceKey = "catalog:bkp_one",
            SourceCatalogEntryIdSnapshot = "bkp_one", SourceDisplayNameSnapshot = "Example", SourceOriginKindSnapshot = "local-captured",
            Status = "completed", CurrentStage = "backup-ready", SessionDirectoryPath = "/private/do-not-project",
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        fixture.Db.RestoreAttempts.Add(attempt);
        await fixture.Db.SaveChangesAsync();
        Assert.Empty(await fixture.Source.ReadOwnersAsync(CancellationToken.None));
        fixture.Db.RuntimeOperations.Add(new RuntimeOperationEntity
        {
            Id = Guid.NewGuid(), RestoreAttempt = attempt, Operation = "restore.private-test", Status = "succeeded",
            RequestedAtUtc = DateTime.UtcNow,
            EvidenceJson = """{"sourceKind":"backup-catalog","catalogEntryId":"bkp_one","stagingId":"stage-one","status":"ready"}"""
        });
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        var owner = Assert.Single(await fixture.Source.ReadOwnersAsync(CancellationToken.None));
        Assert.Equal("rs_one", owner.OwnerId);
        Assert.Equal("stage-one", owner.StagingId);
        Assert.True(owner.BindingValid);
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task History_projects_resource_ids_but_not_paths_or_raw_reports()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WriteHistoryAsync("stage-one", """
        {"stagingId":"stage-one","sourceKind":"migration-candidate","status":"ready",
         "workspacePath":"/private/do-not-project","detail":"secret-report-do-not-project",
         "safety":{"privateOnly":true},"runtime":{},"postgresContainerId":"postgres-id",
         "synapseContainerId":"synapse-id","elementContainerId":"element-id","networkId":"network-id"}
        """);
        var run = Assert.Single(await fixture.Source.ReadHistoryAsync(CancellationToken.None));
        Assert.Equal(4, run.Resources.Count);
        Assert.False(run.Destroyed);
        var json = JsonSerializer.Serialize(run);
        Assert.DoesNotContain("do-not-project", json);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{\"stagingId\":\"different-id\",\"safety\":{},\"runtime\":{}}")]
    public async Task Corrupt_or_mismatched_history_is_not_reported_as_empty(string contents)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.WriteHistoryAsync("stage-one", contents);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Source.ReadHistoryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Linked_history_is_refused_without_reading_the_target()
    {
        await using var fixture = await Fixture.CreateAsync();
        var outside = Path.Combine(fixture.Root, "outside");
        Directory.CreateDirectory(outside);
        var history = Path.Combine(fixture.Root, "restore-staging", "history");
        Directory.CreateDirectory(Path.GetDirectoryName(history)!);
        Directory.CreateSymbolicLink(history, outside);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Source.ReadHistoryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Missing_history_root_is_a_confirmed_empty_read()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Empty(await fixture.Source.ReadHistoryAsync(CancellationToken.None));
    }

    [Fact]
    public async Task History_limit_is_explicit_not_silent_truncation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var history = Path.Combine(fixture.Root, "restore-staging", "history");
        for (var index = 0; index <= TemporaryStagingInventorySource.MaximumRecords; index++)
            Directory.CreateDirectory(Path.Combine(history, $"stage-{index}"));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Source.ReadHistoryAsync(CancellationToken.None));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DockerClient _docker;
        private Fixture(SqliteConnection connection, MemDbContext db, string root)
        {
            _connection = connection;
            Db = db;
            Root = root;
            _docker = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["MEM_DATA_ROOT"] = root }).Build();
            Source = new TemporaryStagingInventorySource(db, _docker, configuration);
        }
        public MemDbContext Db { get; }
        public string Root { get; }
        public TemporaryStagingInventorySource Source { get; }
        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new MemDbContext(new DbContextOptionsBuilder<MemDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var root = Path.Combine(Path.GetTempPath(), $"mem-staging-inventory-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new Fixture(connection, db, root);
        }
        public async Task WriteHistoryAsync(string id, string contents)
        {
            var path = Path.Combine(Root, "restore-staging", "history", id);
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, "restore-staging-result.json"), contents);
        }
        public async ValueTask DisposeAsync()
        {
            _docker.Dispose();
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
