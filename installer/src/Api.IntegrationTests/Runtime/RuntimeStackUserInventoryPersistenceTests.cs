using System.Text.Json;
using HostAgent.Matrix.Users;
using HostAgent.Runtime.Manifests;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackUserInventoryPersistenceTests
{
    [Fact]
    public async Task RuntimeStackUserInventory_synchronizes_restored_accounts_under_new_stack_identity()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Reader.Snapshot = Snapshot(
            Account("@admin:restored.test", "admin", isAdmin: true),
            Account("@alice:restored.test", "alice"));

        var result = await fixture.Service.SynchronizeAsync(
            fixture.Manifest,
            CancellationToken.None);

        Assert.Equal(2, result.InsertedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.ActiveAdminCount);

        var users = await fixture.Db.RuntimeStackUsers
            .AsNoTracking()
            .OrderBy(x => x.Username)
            .ToArrayAsync();

        Assert.Equal(2, users.Length);
        Assert.All(users, x => Assert.Equal(fixture.Manifest.StackId, x.RuntimeStackId));
        Assert.All(users, x => Assert.Equal(fixture.Manifest.Matrix.InstanceId, x.MatrixInstanceId));
        Assert.All(users, x => Assert.False(x.IsFirstAdmin));
        Assert.All(users, x => Assert.Equal("synapse-discovered", RuntimeStackUserProjectionMetadata.ReadOrigin(x)));
        Assert.True(users.Single(x => x.Username == "admin").IsAdmin);

        var database = await fixture.Db.RuntimeStackDatabases
            .AsNoTracking()
            .SingleAsync();
        var state = RuntimeStackUserInventoryMetadata.Read(database.MetadataJson);

        Assert.Equal(RuntimeStackUserInventoryStates.Synchronized, state.Status);
        Assert.Equal("synapse-postgres", state.Source);
        Assert.Equal(2, state.UserCount);
        Assert.Equal(1, state.ActiveAdminCount);
        Assert.Contains("one-postgres-database-per-runtime-stack", database.MetadataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeStackUserInventory_is_idempotent_and_preserves_mem_creation_provenance()
    {
        await using var fixture = await Fixture.CreateAsync();
        var existingId = Guid.NewGuid();

        fixture.Db.RuntimeStackUsers.Add(new RuntimeStackUserEntity
        {
            Id = existingId,
            RuntimeStackId = fixture.Manifest.StackId,
            MatrixInstanceId = fixture.Manifest.Matrix.InstanceId,
            Username = "admin",
            MatrixUserId = "@admin:restored.test",
            IsAdmin = true,
            IsFirstAdmin = true,
            Status = "active",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-3),
            UpdatedAtUtc = DateTime.UtcNow.AddDays(-3),
            MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["origin"] = "mem-created",
                ["registrationSecretSource"] = "per-stack"
            })
        });
        await fixture.Db.SaveChangesAsync();

        fixture.Reader.Snapshot = Snapshot(
            Account("@admin:restored.test", "admin", isAdmin: true));

        await fixture.Service.SynchronizeAsync(fixture.Manifest, CancellationToken.None);
        await fixture.Service.SynchronizeAsync(fixture.Manifest, CancellationToken.None);

        var user = await fixture.Db.RuntimeStackUsers.AsNoTracking().SingleAsync();

        Assert.Equal(existingId, user.Id);
        Assert.True(user.IsFirstAdmin);
        Assert.Equal("mem-created", RuntimeStackUserProjectionMetadata.ReadOrigin(user));
        Assert.Equal(1, await fixture.Db.RuntimeStackUsers.CountAsync());
    }

    [Fact]
    public async Task RuntimeStackUserInventory_reclassifies_failed_MEM_creation_without_identity_when_Synapse_later_has_account()
    {
        await using var fixture = await Fixture.CreateAsync();
        var existingId = Guid.NewGuid();

        fixture.Db.RuntimeStackUsers.Add(new RuntimeStackUserEntity
        {
            Id = existingId,
            RuntimeStackId = fixture.Manifest.StackId,
            MatrixInstanceId = fixture.Manifest.Matrix.InstanceId,
            Username = "admin",
            MatrixUserId = null,
            IsAdmin = true,
            IsFirstAdmin = true,
            Status = "failed",
            LastError = "Matrix user creation did not complete.",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["origin"] = RuntimeStackUserProjectionMetadata.MemCreated,
                ["registrationSecretSource"] = "per-stack",
                ["operationId"] = Guid.NewGuid().ToString()
            })
        });
        await fixture.Db.SaveChangesAsync();

        fixture.Reader.Snapshot = Snapshot(
            Account("@admin:restored.test", "admin", isAdmin: true));

        await fixture.Service.SynchronizeAsync(fixture.Manifest, CancellationToken.None);

        var user = await fixture.Db.RuntimeStackUsers.AsNoTracking().SingleAsync();
        Assert.Equal(existingId, user.Id);
        Assert.Equal("@admin:restored.test", user.MatrixUserId);
        Assert.Equal("active", user.Status);
        Assert.Null(user.LastError);
        Assert.Equal(
            RuntimeStackUserProjectionMetadata.MemCreationUnconfirmed,
            RuntimeStackUserProjectionMetadata.ReadOrigin(user));
    }

    [Fact]
    public async Task RuntimeStackUserInventory_updates_account_state_and_marks_only_completed_projection_rows_missing()
    {
        await using var fixture = await Fixture.CreateAsync();

        fixture.Db.RuntimeStackUsers.AddRange(
            Existing(fixture, "admin", "@admin:restored.test", "active", isAdmin: true),
            Existing(fixture, "gone", "@gone:restored.test", "active"),
            Existing(fixture, "pending", null, "pending"));
        await fixture.Db.SaveChangesAsync();

        fixture.Reader.Snapshot = Snapshot(
            Account("@admin:restored.test", "admin", isAdmin: false, isDeactivated: true));

        var result = await fixture.Service.SynchronizeAsync(
            fixture.Manifest,
            CancellationToken.None);

        Assert.Equal(1, result.MissingCount);

        var users = await fixture.Db.RuntimeStackUsers.AsNoTracking().ToArrayAsync();
        var admin = users.Single(x => x.Username == "admin");
        var gone = users.Single(x => x.Username == "gone");
        var pending = users.Single(x => x.Username == "pending");

        Assert.False(admin.IsAdmin);
        Assert.Equal("deactivated", admin.Status);
        Assert.Equal("missing", gone.Status);
        Assert.Equal("pending", pending.Status);
    }

    [Fact]
    public async Task RuntimeStackUserInventory_failure_preserves_projection_and_records_only_safe_failure_metadata()
    {
        await using var fixture = await Fixture.CreateAsync();
        var existing = Existing(
            fixture,
            "admin",
            "@admin:restored.test",
            "active",
            isAdmin: true);
        fixture.Db.RuntimeStackUsers.Add(existing);
        await fixture.Db.SaveChangesAsync();

        fixture.Reader.Exception = new RuntimeStackUserInventoryException(
            "matrix_user_inventory_query_failed",
            "The Matrix user inventory could not be read from the owned PostgreSQL database.",
            new InvalidOperationException("password=super-secret raw SQL failure"));

        var ex = await Assert.ThrowsAsync<RuntimeStackUserInventoryException>(() =>
            fixture.Service.SynchronizeAsync(fixture.Manifest, CancellationToken.None));

        Assert.Equal("matrix_user_inventory_query_failed", ex.Code);

        var user = await fixture.Db.RuntimeStackUsers.AsNoTracking().SingleAsync();
        Assert.Equal("active", user.Status);
        Assert.True(user.IsAdmin);

        var database = await fixture.Db.RuntimeStackDatabases.AsNoTracking().SingleAsync();
        var state = RuntimeStackUserInventoryMetadata.Read(database.MetadataJson);

        Assert.Equal(RuntimeStackUserInventoryStates.Failed, state.Status);
        Assert.Equal("matrix_user_inventory_query_failed", state.ErrorCode);
        Assert.DoesNotContain("super-secret", database.MetadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw SQL", database.MetadataJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeStackUserInventory_creation_policy_blocks_restored_admin_and_duplicate_username()
    {
        var inventory = new RuntimeStackUserInventorySynchronizationResult(
            RuntimeStackId: Guid.NewGuid(),
            Source: "synapse-postgres",
            Accounts:
            [
                Account("@admin:restored.test", "admin", isAdmin: true),
                Account("@alice:restored.test", "alice")
            ],
            InsertedCount: 2,
            UpdatedCount: 0,
            MissingCount: 0,
            SynchronizedAtUtc: DateTimeOffset.UtcNow);

        var duplicate = Assert.Throws<RuntimeStackUserConflictException>(() =>
            RuntimeStackUserCreationPolicy.EnsureAllowed(
                inventory,
                "alice",
                isFirstAdmin: false));
        Assert.Equal("matrix_user_already_exists", duplicate.Code);

        var admin = Assert.Throws<RuntimeStackUserConflictException>(() =>
            RuntimeStackUserCreationPolicy.EnsureAllowed(
                inventory,
                "new-admin",
                isFirstAdmin: true));
        Assert.Equal("matrix_admin_already_exists", admin.Code);
    }

    private static RuntimeStackUserEntity Existing(
        Fixture fixture,
        string username,
        string? matrixUserId,
        string status,
        bool isAdmin = false) => new()
    {
        Id = Guid.NewGuid(),
        RuntimeStackId = fixture.Manifest.StackId,
        MatrixInstanceId = fixture.Manifest.Matrix.InstanceId,
        Username = username,
        MatrixUserId = matrixUserId,
        IsAdmin = isAdmin,
        IsFirstAdmin = false,
        Status = status,
        CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
        MetadataJson = "{\"origin\":\"synapse-discovered\"}"
    };

    private static SynapseUserInventorySnapshot Snapshot(
        params SynapseUserInventoryAccount[] accounts) => new(
        Source: "synapse-postgres",
        Accounts: accounts,
        ReadAtUtc: new DateTimeOffset(2026, 7, 10, 1, 0, 0, TimeSpan.Zero));

    private static SynapseUserInventoryAccount Account(
        string matrixUserId,
        string username,
        bool isAdmin = false,
        bool isDeactivated = false) => new(
        MatrixUserId: matrixUserId,
        Username: username,
        IsAdmin: isAdmin,
        IsDeactivated: isDeactivated,
        CreatedAtUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private Fixture(
            string databasePath,
            MemDbContext db,
            RuntimeStackManifest manifest,
            FakeInventoryReader reader,
            RuntimeStackUserInventoryReconciliationService service)
        {
            _databasePath = databasePath;
            Db = db;
            Manifest = manifest;
            Reader = reader;
            Service = service;
        }

        public MemDbContext Db { get; }
        public RuntimeStackManifest Manifest { get; }
        public FakeInventoryReader Reader { get; }
        public RuntimeStackUserInventoryReconciliationService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-runtime-user-inventory-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var stackId = Guid.NewGuid();
            var matrixInstanceId = Guid.NewGuid();
            var manifest = new RuntimeStackManifest(
                Source: "control-plane",
                StackId: stackId,
                Slug: "restored-stack",
                LastVerifiedStatus: "passed",
                LastVerifiedAtUtc: DateTimeOffset.UtcNow,
                Matrix: new RuntimeStackServiceManifest(
                    InstanceId: matrixInstanceId,
                    ServiceKey: "matrix",
                    ContainerId: "matrix-container",
                    ContainerName: "mem-matrix-restored-stack",
                    HostPort: 0,
                    DataPath: "/tmp/matrix",
                    ServerName: "restored.test",
                    PublicHost: "matrix.restored.test",
                    PublicBaseUrl: "https://matrix.restored.test",
                    InternalHost: "mem-matrix-restored-stack",
                    InternalBaseUrl: "http://mem-matrix-restored-stack:8008",
                    PublicRouteId: null,
                    InternalRouteId: null,
                    NpmCertificateId: null,
                    RuntimeMetadata: new Dictionary<string, string?>()),
                Element: null,
                Warnings: [],
                Metadata: new Dictionary<string, string?>());

            db.RuntimeStacks.Add(new RuntimeStackEntity
            {
                Id = stackId,
                Slug = manifest.Slug,
                DisplayName = manifest.Slug,
                Status = "passed",
                LastVerifiedStatus = "passed",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                MatrixInstanceId = matrixInstanceId,
                MatrixPublicBaseUrl = manifest.Matrix.PublicBaseUrl
            });
            db.RuntimeStackDatabases.Add(new RuntimeStackDatabaseEntity
            {
                Id = Guid.NewGuid(),
                RuntimeStackId = stackId,
                DatabaseEngine = "postgres",
                DatabaseHost = "mem-postgres",
                DatabasePort = 5432,
                DatabaseName = "matrix_restored_stack_12345678",
                DatabaseUsername = "mxu_restored_stack_12345678",
                PasswordSecretKind = "matrix_postgres_password",
                Status = "active",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                MetadataJson = "{\"purpose\":\"synapse-database\",\"ownership\":\"one-postgres-database-per-runtime-stack\"}"
            });
            await db.SaveChangesAsync();

            var reader = new FakeInventoryReader();
            var service = new RuntimeStackUserInventoryReconciliationService(
                db,
                reader,
                NullLogger<RuntimeStackUserInventoryReconciliationService>.Instance);

            return new Fixture(databasePath, db, manifest, reader, service);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();

            foreach (var path in new[]
                     {
                         _databasePath,
                         _databasePath + "-shm",
                         _databasePath + "-wal"
                     })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private sealed class FakeInventoryReader : ISynapseUserInventoryReader
    {
        public SynapseUserInventorySnapshot Snapshot { get; set; } =
            RuntimeStackUserInventoryPersistenceTests.Snapshot();
        public RuntimeStackUserInventoryException? Exception { get; set; }

        public Task<SynapseUserInventorySnapshot> ReadAsync(
            RuntimeStackUserInventoryTarget target,
            CancellationToken ct)
        {
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Snapshot);
        }
    }
}
