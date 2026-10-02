using System.Text.Json;
using System.Text.Json.Nodes;
using Api.IntegrationTests.Runtime;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Observability;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.IntegrationTests.Workspace;

/// <summary>
/// Regression coverage for restore support-report release provenance. Restore
/// evidence must use the exact server-owned runtime identity rather than the
/// product-line version from appsettings.
/// </summary>
public sealed class RestoreSupportReportRuntimeIdentityPersistenceTests
{
    [Fact]
    public async Task Generated_report_preserves_exact_runtime_version_and_commit_and_old_reports_remain_readable()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-restore-support-runtime-{Guid.NewGuid():N}");
        var dataRoot = Path.Combine(root, "mem-data");
        var runtimeRoot = Path.Combine(root, "runtime");
        var databasePath = Path.Combine(root, "control-plane.db");
        Directory.CreateDirectory(root);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot,
                    // Deliberately stale/product-line-only. This value caused
                    // prerelease support reports to collapse to "0.2.0".
                    ["Product:Version"] = "0.2.0"
                })
                .Build();

            await using var db = new MemDbContext(new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options);
            await db.Database.MigrateAsync();

            var workspaceStore = new RestoreAttemptWorkspaceStore(configuration);
            var logs = new RestoreStructuredLogService(
                db,
                workspaceStore,
                NullLogger<RestoreStructuredLogService>.Instance);
            var runtimeContext = TestRuntimeContext.Create(
                runtimeRoot,
                version: "0.2.0-rc.5.qa.5",
                commit: "0123456789abcdef0123456789abcdef01234567");
            var service = new RestoreSupportReportService(
                db,
                logs,
                workspaceStore,
                runtimeContext,
                NullLogger<RestoreSupportReportService>.Instance);

            var restoreSessionId = $"rst_support_{Guid.NewGuid():N}";
            var sessionDirectory = workspaceStore.GetSessionDirectoryPath(restoreSessionId);
            var now = DateTime.UtcNow;
            db.RestoreAttempts.Add(new RestoreAttemptEntity
            {
                Id = Guid.NewGuid(),
                RestoreSessionId = restoreSessionId,
                SourceKind = "backup-catalog",
                SourceKey = $"backup-catalog:bkp_{Guid.NewGuid():N}",
                ActiveSourceKey = null,
                SourceCatalogEntryIdSnapshot = $"bkp_{Guid.NewGuid():N}",
                SourceDisplayNameSnapshot = "Support report runtime identity fixture",
                SourceOriginKindSnapshot = "local-captured",
                SourceStackSlugSnapshot = "support-source",
                SourceBackupIdSnapshot = "backup-001",
                BackupCatalogEntryId = null,
                Status = "completed",
                CurrentStage = "completed",
                CreatedAtUtc = now.AddMinutes(-1),
                UpdatedAtUtc = now,
                TerminalAtUtc = now,
                WarningCount = 0,
                ErrorCount = 0,
                SessionDirectoryPath = sessionDirectory,
                LogDirectoryPath = Path.Combine(sessionDirectory, "logs")
            });
            await db.SaveChangesAsync();

            var generated = await service.GenerateAsync(restoreSessionId, CancellationToken.None);

            Assert.NotNull(generated);
            Assert.Equal("0.2.0-rc.5.qa.5", generated!.Report.MemVersion);
            Assert.Equal(
                "0123456789abcdef0123456789abcdef01234567",
                generated.Report.MemCommit);

            var json = await File.ReadAllTextAsync(generated.ReportPath);
            using (var document = JsonDocument.Parse(json))
            {
                Assert.Equal(
                    "0.2.0-rc.5.qa.5",
                    document.RootElement.GetProperty("memVersion").GetString());
                Assert.Equal(
                    "0123456789abcdef0123456789abcdef01234567",
                    document.RootElement.GetProperty("memCommit").GetString());
            }

            // Additive provenance must not make previously generated v1 reports
            // unreadable. Simulate an older report by removing memCommit.
            var legacyDocument = JsonNode.Parse(json)?.AsObject()
                ?? throw new InvalidOperationException("Generated support report was not a JSON object.");
            Assert.True(legacyDocument.Remove("memCommit"));
            await File.WriteAllTextAsync(generated.ReportPath, legacyDocument.ToJsonString());

            var legacy = await service.GetAsync(restoreSessionId, CancellationToken.None);
            Assert.NotNull(legacy);
            Assert.Equal("0.2.0-rc.5.qa.5", legacy!.MemVersion);
            Assert.Null(legacy.MemCommit);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
