using System.Text.Json;
using Api.IntegrationTests.Runtime;
using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using HostAgent.Runtime.Backups.Catalog;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.IntegrationTests.Persistence;

/// <summary>
/// Regression coverage for true Backup Catalog deletion. These tests use a
/// migrated SQLite database and real filesystem artifacts so a Delete action
/// cannot regress into a payload-only tombstone.
/// </summary>
public sealed class BackupCatalogPermanentDeletePersistenceTests
{
    [Fact]
    public async Task Permanent_delete_removes_local_payload_catalog_row_and_generated_export_but_keeps_completed_restore_history()
    {
        var dataRoot = CreateTemporaryDirectory("mem-catalog-hard-delete");
        var databasePath = CreateDatabasePath();
        var payloadDirectory = Path.Combine(dataRoot, "local", "demo-stack", "backup-1");

        try
        {
            Directory.CreateDirectory(payloadDirectory);
            await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "payload.txt"), "payload");

            await using var db = await CreateDatabaseAsync(databasePath);
            var entry = await AddCatalogEntryAsync(
                db,
                catalogEntryId: "bkp_delete_local",
                originKind: BackupCatalogOriginKinds.LocalCaptured,
                payloadDirectoryPath: payloadDirectory,
                sourceStackSlug: "demo-stack",
                validationId: null,
                payloadState: BackupCatalogPayloadStates.Available);

            var attempt = new RestoreAttemptEntity
            {
                Id = Guid.NewGuid(),
                RestoreSessionId = "restore-completed-local-delete",
                SourceKind = "backup-catalog",
                SourceKey = "backup-catalog:bkp_delete_local",
                ActiveSourceKey = null,
                SourceCatalogEntryIdSnapshot = entry.CatalogEntryId,
                SourceDisplayNameSnapshot = entry.DisplayName,
                SourceOriginKindSnapshot = entry.OriginKind,
                SourceStackSlugSnapshot = "demo-stack",
                SourceBackupIdSnapshot = "backup-1",
                BackupCatalogEntryId = entry.Id,
                Status = "completed",
                CurrentStage = "completed",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                TerminalAtUtc = DateTime.UtcNow,
                SessionDirectoryPath = Path.Combine(dataRoot, "restores", "restore-completed-local-delete"),
                WarningCount = 0,
                ErrorCount = 0
            };
            db.RestoreAttempts.Add(attempt);
            await db.SaveChangesAsync();

            var exportPath = CreateGeneratedExportPath(dataRoot, "demo-stack", entry.CatalogEntryId);
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
            await File.WriteAllTextAsync(exportPath, "portable export");

            var result = await CreateLifecycleService(dataRoot, db).DeleteAsync(
                entry.CatalogEntryId,
                "Nigel",
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("deleted", result!.Status);
            Assert.Equal(entry.CatalogEntryId, result.CatalogEntryId);
            Assert.Equal("Nigel", result.DeletedBy);
            Assert.True(result.PayloadDeleted);
            Assert.False(result.OriginalArchiveDeleted);
            Assert.Equal(1, result.PortableExportsDeleted);
            Assert.Equal(1, result.DetachedRestoreAttempts);
            Assert.False(Directory.Exists(payloadDirectory));
            Assert.False(File.Exists(exportPath));
            Assert.False(await db.BackupCatalogEntries.AnyAsync(x => x.Id == entry.Id));

            var retainedAttempt = await db.RestoreAttempts.SingleAsync(x => x.Id == attempt.Id);
            Assert.Equal("backup-catalog", retainedAttempt.SourceKind);
            Assert.Equal("backup-catalog:bkp_delete_local", retainedAttempt.SourceKey);
            Assert.Null(retainedAttempt.BackupCatalogEntryId);
        }
        finally
        {
            DeleteTestFiles(databasePath, dataRoot);
        }
    }

    [Fact]
    public async Task Permanent_delete_removes_imported_zip_transport_archive_and_materialised_payload()
    {
        var dataRoot = CreateTemporaryDirectory("mem-catalog-hard-delete-import");
        var databasePath = CreateDatabasePath();
        var payloadDirectory = Path.Combine(dataRoot, "catalog", "payload");
        const string validationId = "validation-delete-imported-1";

        try
        {
            Directory.CreateDirectory(payloadDirectory);
            await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "payload.txt"), "payload");

            var uploadDirectory = Path.Combine(ResolveImportDataRoot(dataRoot), "imports", "uploads", validationId);
            Directory.CreateDirectory(uploadDirectory);
            var archivePath = Path.Combine(uploadDirectory, "source.zip");
            await File.WriteAllTextAsync(archivePath, "archive");
            await WriteValidationReceiptAsync(uploadDirectory, validationId, archivePath);

            await using var db = await CreateDatabaseAsync(databasePath);
            var entry = await AddCatalogEntryAsync(
                db,
                catalogEntryId: "bkp_delete_imported",
                originKind: BackupCatalogOriginKinds.ImportedZip,
                payloadDirectoryPath: payloadDirectory,
                sourceStackSlug: "imported-stack",
                validationId: validationId,
                payloadState: BackupCatalogPayloadStates.Available);

            var generatedExportPath = CreateGeneratedExportPath(
                dataRoot,
                "manifest-derived-stack",
                entry.CatalogEntryId);
            Directory.CreateDirectory(Path.GetDirectoryName(generatedExportPath)!);
            await File.WriteAllTextAsync(generatedExportPath, "portable export");

            var result = await CreateLifecycleService(dataRoot, db).DeleteAsync(
                entry.CatalogEntryId,
                "Nigel",
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.True(result!.PayloadDeleted);
            Assert.True(result.OriginalArchiveDeleted);
            Assert.Equal(1, result.PortableExportsDeleted);
            Assert.False(Directory.Exists(payloadDirectory));
            Assert.False(File.Exists(archivePath));
            Assert.False(Directory.Exists(uploadDirectory));
            Assert.False(File.Exists(generatedExportPath));
            Assert.False(await db.BackupCatalogEntries.AnyAsync(x => x.Id == entry.Id));
        }
        finally
        {
            DeleteTestFiles(databasePath, dataRoot);
        }
    }

    [Fact]
    public async Task Permanent_delete_retry_completes_from_partially_removed_artifacts_and_terminal_retry_is_not_found()
    {
        var dataRoot = CreateTemporaryDirectory("mem-catalog-hard-delete-retry");
        var databasePath = CreateDatabasePath();
        var payloadDirectory = Path.Combine(dataRoot, "catalog", "retry-payload");
        const string validationId = "validation-delete-retry-1";

        try
        {
            Directory.CreateDirectory(payloadDirectory);
            await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "payload.txt"), "payload");

            var uploadDirectory = Path.Combine(ResolveImportDataRoot(dataRoot), "imports", "uploads", validationId);
            Directory.CreateDirectory(uploadDirectory);
            var archivePath = Path.Combine(uploadDirectory, "source.zip");
            await File.WriteAllTextAsync(archivePath, "archive");
            await WriteValidationReceiptAsync(uploadDirectory, validationId, archivePath);

            await using var db = await CreateDatabaseAsync(databasePath);
            var entry = await AddCatalogEntryAsync(
                db,
                catalogEntryId: "bkp_delete_retry",
                originKind: BackupCatalogOriginKinds.ImportedZip,
                payloadDirectoryPath: payloadDirectory,
                sourceStackSlug: "retry-stack",
                validationId: validationId,
                payloadState: BackupCatalogPayloadStates.Available);

            var generatedExportPath = CreateGeneratedExportPath(
                dataRoot,
                "retry-stack",
                entry.CatalogEntryId);
            Directory.CreateDirectory(Path.GetDirectoryName(generatedExportPath)!);
            await File.WriteAllTextAsync(generatedExportPath, "portable export");

            // Reproduce the durable shape left by an interrupted pre-CORR-05B
            // destructive request: owned filesystem artifacts are already gone,
            // while the authoritative catalog row still exists. A retry must
            // reconcile that partial state rather than fail on missing artifacts.
            Directory.Delete(uploadDirectory, recursive: true);
            File.Delete(generatedExportPath);
            Directory.Delete(payloadDirectory, recursive: true);

            var lifecycle = CreateLifecycleService(dataRoot, db);
            var result = await lifecycle.DeleteAsync(
                entry.CatalogEntryId,
                "operator",
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal("deleted", result!.Status);
            Assert.False(result.PayloadDeleted);
            Assert.False(result.OriginalArchiveDeleted);
            Assert.Equal(0, result.PortableExportsDeleted);
            Assert.False(await db.BackupCatalogEntries.AnyAsync(x => x.Id == entry.Id));

            var terminalRetry = await lifecycle.DeleteAsync(
                entry.CatalogEntryId,
                "operator",
                CancellationToken.None);

            Assert.Null(terminalRetry);
        }
        finally
        {
            DeleteTestFiles(databasePath, dataRoot);
        }
    }

    [Fact]
    public async Task Permanent_delete_is_blocked_while_an_active_restore_references_the_catalog_source()
    {
        var dataRoot = CreateTemporaryDirectory("mem-catalog-hard-delete-active");
        var databasePath = CreateDatabasePath();
        var payloadDirectory = Path.Combine(dataRoot, "payload");

        try
        {
            Directory.CreateDirectory(payloadDirectory);
            await File.WriteAllTextAsync(Path.Combine(payloadDirectory, "payload.txt"), "payload");

            await using var db = await CreateDatabaseAsync(databasePath);
            var entry = await AddCatalogEntryAsync(
                db,
                catalogEntryId: "bkp_delete_active",
                originKind: BackupCatalogOriginKinds.LocalCaptured,
                payloadDirectoryPath: payloadDirectory,
                sourceStackSlug: "active-stack",
                validationId: null,
                payloadState: BackupCatalogPayloadStates.Available);

            db.RestoreAttempts.Add(new RestoreAttemptEntity
            {
                Id = Guid.NewGuid(),
                RestoreSessionId = "restore-active-delete",
                SourceKind = "backup-catalog",
                SourceKey = "backup-catalog:bkp_delete_active",
                ActiveSourceKey = "backup-catalog:bkp_delete_active",
                SourceCatalogEntryIdSnapshot = entry.CatalogEntryId,
                SourceDisplayNameSnapshot = entry.DisplayName,
                SourceOriginKindSnapshot = entry.OriginKind,
                SourceStackSlugSnapshot = "active-stack",
                SourceBackupIdSnapshot = "backup-active",
                BackupCatalogEntryId = entry.Id,
                Status = "ready",
                CurrentStage = "source-ready",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                SessionDirectoryPath = Path.Combine(dataRoot, "restores", "restore-active-delete"),
                WarningCount = 0,
                ErrorCount = 0
            });
            await db.SaveChangesAsync();

            var exception = await Assert.ThrowsAsync<BackupCatalogLifecycleConflictException>(
                () => CreateLifecycleService(dataRoot, db).DeleteAsync(
                    entry.CatalogEntryId,
                    "Nigel",
                    CancellationToken.None));

            Assert.Contains("restore-active-delete", exception.Message, StringComparison.Ordinal);
            Assert.True(Directory.Exists(payloadDirectory));
            Assert.True(await db.BackupCatalogEntries.AnyAsync(x => x.Id == entry.Id));
        }
        finally
        {
            DeleteTestFiles(databasePath, dataRoot);
        }
    }

    [Fact]
    public async Task Legacy_removed_catalog_entry_can_be_permanently_purged()
    {
        var dataRoot = CreateTemporaryDirectory("mem-catalog-hard-delete-purge");
        var databasePath = CreateDatabasePath();
        var removedPayloadDirectory = Path.Combine(dataRoot, "removed-payload");

        try
        {
            await using var db = await CreateDatabaseAsync(databasePath);
            var entry = await AddCatalogEntryAsync(
                db,
                catalogEntryId: "bkp_delete_removed",
                originKind: BackupCatalogOriginKinds.LocalCaptured,
                payloadDirectoryPath: removedPayloadDirectory,
                sourceStackSlug: "removed-stack",
                validationId: null,
                payloadState: BackupCatalogPayloadStates.Removed);

            var result = await CreateLifecycleService(dataRoot, db).DeleteAsync(
                entry.CatalogEntryId,
                null,
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.False(result!.PayloadDeleted);
            Assert.Equal(0, result.PortableExportsDeleted);
            Assert.False(await db.BackupCatalogEntries.AnyAsync(x => x.Id == entry.Id));
        }
        finally
        {
            DeleteTestFiles(databasePath, dataRoot);
        }
    }

    private static BackupCatalogLifecycleService CreateLifecycleService(
        string dataRoot,
        MemDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HostAgent:DataRoot"] = dataRoot
            })
            .Build();

        var catalogStore = new BackupCatalogStore(db);
        var resolver = new BackupCatalogPayloadResolver(catalogStore);
        var portableExports = new CatalogPortableExportService(
            configuration,
            resolver,
            TestRuntimeContext.Create(
                Path.Combine(dataRoot, "runtime-context"),
                version: "0.2.0-delete-test"));
        var imports = new ImportValidationService(configuration);

        return new BackupCatalogLifecycleService(
            db,
            imports,
            portableExports,
            configuration,
            new BackupCatalogDeleteOperationLifetime(
                new FakeHostApplicationLifetime(),
                NullLogger<BackupCatalogDeleteOperationLifetime>.Instance));
    }

    private static async Task<BackupCatalogEntryEntity> AddCatalogEntryAsync(
        MemDbContext db,
        string catalogEntryId,
        string originKind,
        string payloadDirectoryPath,
        string sourceStackSlug,
        string? validationId,
        string payloadState)
    {
        var entry = new BackupCatalogEntryEntity
        {
            Id = Guid.NewGuid(),
            CatalogEntryId = catalogEntryId,
            OriginKind = originKind,
            DisplayName = $"Catalog entry {catalogEntryId}",
            PayloadState = payloadState,
            PayloadStorageKind = originKind == BackupCatalogOriginKinds.LocalCaptured
                ? BackupCatalogPayloadStorageKinds.LocalBackupDirectory
                : BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
            PayloadDirectoryPath = payloadDirectoryPath,
            SourceStackSlug = sourceStackSlug,
            SourceBackupId = "backup-1",
            ValidationId = validationId,
            IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
            IntegritySummary = "Valid",
            WarningCount = 0,
            PayloadBytes = 7,
            CreatedAtUtc = DateTime.UtcNow,
            CapturedAtUtc = DateTime.UtcNow
        };

        db.BackupCatalogEntries.Add(entry);
        await db.SaveChangesAsync();
        return entry;
    }

    private static async Task WriteValidationReceiptAsync(
        string uploadDirectory,
        string validationId,
        string archivePath)
    {
        var receipt = new ImportValidationResponse(
            Source: "control-plane",
            Status: "valid",
            ValidationId: validationId,
            UploadedFileName: "imported-backup.zip",
            StoredZipPath: archivePath,
            ZipBytes: new FileInfo(archivePath).Length,
            ZipEntryCount: 1,
            TotalUncompressedBytes: 1,
            ManifestPresent: true,
            ChecksumsPresent: true,
            Manifest: null,
            Integrity: new ImportValidationIntegritySummary(0, 0, 0, 0, 0),
            Checks: [],
            Warnings: [],
            Errors: [],
            Detail: "Test receipt.");

        await File.WriteAllTextAsync(
            Path.Combine(uploadDirectory, "validation-result.json"),
            JsonSerializer.Serialize(receipt));
    }

    private static string ResolveImportDataRoot(string configuredDataRoot) =>
        Environment.GetEnvironmentVariable("MEM_DATA_ROOT") ?? configuredDataRoot;

    private static string CreateGeneratedExportPath(
        string dataRoot,
        string sourceStackSlug,
        string catalogEntryId) =>
        Path.Combine(
            dataRoot,
            "exports",
            "stacks",
            sourceStackSlug,
            $"mem-stack-catalog-{catalogEntryId}.zip");

    private static async Task<MemDbContext> CreateDatabaseAsync(string databasePath)
    {
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var db = new MemDbContext(options);
        await db.Database.MigrateAsync();
        return db;
    }

    private static string CreateDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        $"mem-catalog-permanent-delete-{Guid.NewGuid():N}.db");

    private static string CreateTemporaryDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTestFiles(string databasePath, string dataRoot)
    {
        if (Directory.Exists(dataRoot))
        {
            Directory.Delete(dataRoot, recursive: true);
        }

        foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
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
