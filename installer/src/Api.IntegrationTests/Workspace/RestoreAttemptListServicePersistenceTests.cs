using System.Text.Json;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.RestoreAttempts.List;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Workspace;

/// <summary>
/// Regression coverage for the canonical restore-attempt list contract. The
/// list must be driven only by durable SQLite attempts and safe catalog/source
/// snapshots; it must never rehydrate a validation-directory ledger.
/// </summary>
public sealed class RestoreAttemptListServicePersistenceTests
{
    [Fact]
    public async Task List_reads_durable_attempts_not_validation_history_and_never_emits_validation_id()
    {
        await using var fixture = await Fixture.CreateAsync();

        await fixture.AddCatalogAttemptAsync(
            restoreSessionId: "restore-catalog-only",
            catalogEntryId: "bkp_catalog_only",
            displayName: "Catalog-only capture",
            status: RestoreAttemptStatuses.Ready,
            currentStage: RestoreAttemptStages.BackupReady,
            targetStackSlug: null);

        // A legacy-looking validation directory must be irrelevant to this list.
        var legacyDirectory = Path.Combine(fixture.DataRoot, "imports", "uploads", "validation-ledger-only");
        Directory.CreateDirectory(legacyDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(legacyDirectory, "validation.json"),
            "{\"validationId\":\"validation-ledger-only\"}");

        var response = await fixture.Service.ListAsync(
            Query(),
            CancellationToken.None);

        var row = Assert.Single(response.Sessions);
        Assert.Equal("restore-catalog-only", row.RestoreSessionId);
        Assert.Equal("bkp_catalog_only", row.CatalogEntryId);
        Assert.Equal("Catalog-only capture", row.SourceLabel);
        Assert.False(row.SourceDeleted);

        var json = JsonSerializer.Serialize(response);
        Assert.DoesNotContain("validationId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("validation-ledger-only", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("in-progress", "restore-ready")]
    [InlineData("completed", "restore-completed")]
    [InlineData("failed", "restore-needs-attention")]
    [InlineData("cancelled", "restore-cancelled")]
    [InlineData("warnings", "restore-warning")]
    public async Task Lifecycle_filters_return_only_matching_attempts(
        string status,
        string expectedRestoreSessionId)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCatalogAttemptAsync("restore-ready", "bkp_ready", "Ready backup", RestoreAttemptStatuses.Ready, RestoreAttemptStages.BackupReady, null);
        await fixture.AddCatalogAttemptAsync("restore-completed", "bkp_completed", "Completed backup", RestoreAttemptStatuses.Completed, RestoreAttemptStages.PublicVerification, "completed-stack");
        await fixture.AddCatalogAttemptAsync("restore-needs-attention", "bkp_attention", "Needs attention backup", RestoreAttemptStatuses.NeedsAttention, RestoreAttemptStages.NeedsAttention, null, errorCount: 1);
        await fixture.AddCatalogAttemptAsync("restore-cancelled", "bkp_cancelled", "Cancelled backup", RestoreAttemptStatuses.Cancelled, RestoreAttemptStages.Cancelled, null);
        await fixture.AddCatalogAttemptAsync("restore-warning", "bkp_warning", "Warning backup", RestoreAttemptStatuses.Ready, RestoreAttemptStages.BackupReady, null, warningCount: 1);

        var response = await fixture.Service.ListAsync(
            Query(status: status),
            CancellationToken.None);

        Assert.Contains(response.Sessions, row => row.RestoreSessionId == expectedRestoreSessionId);
        Assert.All(response.Sessions, row =>
        {
            if (status == "warnings")
            {
                Assert.True(row.WarningCount > 0);
            }
            else if (status == "failed")
            {
                Assert.True(row.Status is RestoreAttemptStatuses.NeedsAttention or RestoreAttemptStatuses.Abandoned);
            }
            else if (status == "in-progress")
            {
                Assert.False(RestoreAttemptStatuses.IsTerminal(row.Status));
                Assert.NotEqual(RestoreAttemptStatuses.NeedsAttention, row.Status);
            }
            else
            {
                Assert.Equal(status, row.Status);
            }
        });
    }

    [Theory]
    [InlineData("restore-search-session", "restore-search-session")]
    [InlineData("Searchable backup display", "restore-search-session")]
    [InlineData("bkp_searchable", "restore-search-session")]
    [InlineData("search-target-stack", "restore-search-session")]
    public async Task Search_matches_session_backup_catalog_and_target_stack(
        string search,
        string expectedRestoreSessionId)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCatalogAttemptAsync(
            restoreSessionId: "restore-search-session",
            catalogEntryId: "bkp_searchable",
            displayName: "Searchable backup display",
            status: RestoreAttemptStatuses.Verifying,
            currentStage: RestoreAttemptStages.PublicVerification,
            targetStackSlug: "search-target-stack");

        var response = await fixture.Service.ListAsync(
            Query(search: search),
            CancellationToken.None);

        Assert.Equal(expectedRestoreSessionId, Assert.Single(response.Sessions).RestoreSessionId);
    }

    [Fact]
    public async Task Target_choices_sort_paging_and_summary_are_stable_and_respect_filters()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCatalogAttemptAsync("restore-a", "bkp_a", "A backup", RestoreAttemptStatuses.Completed, RestoreAttemptStages.PublicVerification, "alpha-stack", updatedOffsetMinutes: 1);
        await fixture.AddCatalogAttemptAsync("restore-b", "bkp_b", "B backup", RestoreAttemptStatuses.Ready, RestoreAttemptStages.BackupReady, "beta-stack", updatedOffsetMinutes: 2);
        await fixture.AddCatalogAttemptAsync("restore-c", "bkp_c", "C backup", RestoreAttemptStatuses.NeedsAttention, RestoreAttemptStages.NeedsAttention, "beta-stack", errorCount: 1, updatedOffsetMinutes: 3);

        var firstPage = await fixture.Service.ListAsync(
            Query(page: 1, pageSize: 1, sortBy: "session", sortDirection: "asc"),
            CancellationToken.None);
        var secondPage = await fixture.Service.ListAsync(
            Query(page: 2, pageSize: 1, sortBy: "session", sortDirection: "asc"),
            CancellationToken.None);
        var betaOnly = await fixture.Service.ListAsync(
            Query(targetStack: "beta-stack"),
            CancellationToken.None);

        Assert.Equal("restore-a", Assert.Single(firstPage.Sessions).RestoreSessionId);
        Assert.Equal("restore-b", Assert.Single(secondPage.Sessions).RestoreSessionId);
        Assert.True(firstPage.HasNextPage);
        Assert.True(secondPage.HasPreviousPage);
        Assert.Equal(3, firstPage.TotalSessions);
        Assert.Equal(new[] { "alpha-stack", "beta-stack" }, firstPage.TargetStacks);

        Assert.Equal(2, betaOnly.Summary.TotalSessions);
        Assert.Equal(1, betaOnly.Summary.NeedsActionCount);
        Assert.Equal(0, betaOnly.Summary.PubliclyVerifiedCount);
        Assert.Equal(new[] { "beta-stack" }, betaOnly.Sessions.Select(row => row.TargetStackSlug).Distinct());
    }

    [Fact]
    public async Task Detached_terminal_history_uses_snapshots_displays_deleted_backup_and_remains_workspace_linkable()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddDetachedAttemptAsync(
            restoreSessionId: "restore-deleted-source",
            catalogEntryIdSnapshot: "bkp_deleted_source",
            displayNameSnapshot: "Original deleted backup",
            status: RestoreAttemptStatuses.Cancelled,
            currentStage: RestoreAttemptStages.Cancelled);

        var response = await fixture.Service.ListAsync(
            Query(search: "bkp_deleted_source"),
            CancellationToken.None);

        var row = Assert.Single(response.Sessions);
        Assert.Equal("restore-deleted-source", row.RestoreSessionId);
        Assert.True(row.WorkspaceAvailable);
        Assert.True(row.SourceDeleted);
        Assert.Null(row.CatalogEntryId);
        Assert.Equal("Deleted backup", row.SourceLabel);
        Assert.Equal("Cancelled", row.StatusLabel);
        Assert.Null(row.ProgressPercent);
    }

    private static RestoreAttemptListQuery Query(
        int page = 1,
        int pageSize = 20,
        string? search = null,
        string? status = null,
        string? targetStack = null,
        string? sortBy = null,
        string? sortDirection = null) =>
        new(
            Page: page,
            PageSize: pageSize,
            Search: search,
            Status: status,
            TargetStack: targetStack,
            SortBy: sortBy,
            SortDirection: sortDirection);

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(string dataRoot, string databasePath, MemDbContext db)
        {
            DataRoot = dataRoot;
            _databasePath = databasePath;
            Db = db;
            Service = new RestoreAttemptListService(db);
        }

        private readonly string _databasePath;

        public string DataRoot { get; }
        public MemDbContext Db { get; }
        public RestoreAttemptListService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), $"mem-restore-list-{Guid.NewGuid():N}");
            var databasePath = Path.Combine(Path.GetTempPath(), $"mem-restore-list-{Guid.NewGuid():N}.db");
            var db = new MemDbContext(new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options);
            await db.Database.MigrateAsync();

            return new Fixture(dataRoot, databasePath, db);
        }

        public async Task AddCatalogAttemptAsync(
            string restoreSessionId,
            string catalogEntryId,
            string displayName,
            string status,
            string currentStage,
            string? targetStackSlug,
            int warningCount = 0,
            int errorCount = 0,
            int updatedOffsetMinutes = 0)
        {
            var entryId = Guid.NewGuid();
            var now = DateTime.UtcNow.AddMinutes(updatedOffsetMinutes);
            var catalog = new BackupCatalogEntryEntity
            {
                Id = entryId,
                CatalogEntryId = catalogEntryId,
                OriginKind = BackupCatalogOriginKinds.LocalCaptured,
                DisplayName = displayName,
                PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
                PayloadDirectoryPath = Path.Combine(DataRoot, "catalog", catalogEntryId),
                SourceStackSlug = "source-stack",
                SourceBackupId = $"backup-{catalogEntryId}",
                IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
                IntegritySummary = "Test catalog payload is available.",
                WarningCount = 0,
                CreatedAtUtc = now
            };

            var attempt = CreateAttempt(
                restoreSessionId,
                catalog,
                status,
                currentStage,
                now,
                warningCount,
                errorCount);

            Db.BackupCatalogEntries.Add(catalog);
            Db.RestoreAttempts.Add(attempt);

            if (!string.IsNullOrWhiteSpace(targetStackSlug))
            {
                Db.RestoreTargetClaims.Add(new RestoreTargetClaimEntity
                {
                    Id = Guid.NewGuid(),
                    RestoreAttemptId = attempt.Id,
                    ResourceType = RestoreTargetResourceTypes.StackSlug,
                    ResourceValue = targetStackSlug,
                    ActiveClaimKey = null,
                    ClaimedAtUtc = now,
                    ReleasedAtUtc = now,
                    ReleaseReason = "test-list-projection"
                });
            }

            await Db.SaveChangesAsync();
        }

        public async Task AddDetachedAttemptAsync(
            string restoreSessionId,
            string catalogEntryIdSnapshot,
            string displayNameSnapshot,
            string status,
            string currentStage)
        {
            var now = DateTime.UtcNow;
            Db.RestoreAttempts.Add(new RestoreAttemptEntity
            {
                Id = Guid.NewGuid(),
                RestoreSessionId = restoreSessionId,
                SourceKind = "backup-catalog",
                SourceKey = $"backup-catalog:{catalogEntryIdSnapshot}",
                ActiveSourceKey = null,
                SourceCatalogEntryIdSnapshot = catalogEntryIdSnapshot,
                SourceDisplayNameSnapshot = displayNameSnapshot,
                SourceOriginKindSnapshot = BackupCatalogOriginKinds.ImportedZip,
                SourceStackSlugSnapshot = "deleted-source-stack",
                SourceBackupIdSnapshot = "deleted-source-backup",
                BackupCatalogEntryId = null,
                Status = status,
                CurrentStage = currentStage,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                TerminalAtUtc = now,
                SessionDirectoryPath = Path.Combine(DataRoot, "restore-sessions", restoreSessionId),
                WarningCount = 0,
                ErrorCount = 0
            });
            await Db.SaveChangesAsync();
        }

        private RestoreAttemptEntity CreateAttempt(
            string restoreSessionId,
            BackupCatalogEntryEntity catalog,
            string status,
            string currentStage,
            DateTime now,
            int warningCount,
            int errorCount) =>
            new()
            {
                Id = Guid.NewGuid(),
                RestoreSessionId = restoreSessionId,
                SourceKind = "backup-catalog",
                SourceKey = $"backup-catalog:{catalog.CatalogEntryId}",
                ActiveSourceKey = RestoreAttemptStatuses.IsTerminal(status)
                    ? null
                    : $"backup-catalog:{catalog.CatalogEntryId}",
                SourceCatalogEntryIdSnapshot = catalog.CatalogEntryId,
                SourceDisplayNameSnapshot = catalog.DisplayName,
                SourceOriginKindSnapshot = catalog.OriginKind,
                SourceStackSlugSnapshot = catalog.SourceStackSlug,
                SourceBackupIdSnapshot = catalog.SourceBackupId,
                BackupCatalogEntryId = catalog.Id,
                Status = status,
                CurrentStage = currentStage,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                TerminalAtUtc = RestoreAttemptStatuses.IsTerminal(status) ? now : null,
                SessionDirectoryPath = Path.Combine(DataRoot, "restore-sessions", restoreSessionId),
                WarningCount = warningCount,
                ErrorCount = errorCount
            };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();

            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, recursive: true);
            }

            foreach (var path in new[] { _databasePath, _databasePath + "-shm", _databasePath + "-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
