using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Infrastructure.Persistence;

namespace Mem.Migrate.IntegrationTests;

public sealed class SqliteSourceRestorationJournalTests
{
    [Fact]
    public async Task Persists_completed_source_restoration_evidence()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-source-restoration-journal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var journal = new SqliteSourceRestorationJournal(root);
            await journal.InitializeAsync(CancellationToken.None);
            var started = DateTimeOffset.UtcNow;
            await journal.StartAsync(
                "mm01cb-proof",
                started,
                "mpsh-proof",
                new string('a', 64),
                "mm06b-proof",
                CancellationToken.None);

            var report = new SourceRestorationReport(
                Schema: "mem-cutover-source-restoration-report",
                SchemaVersion: 1,
                Status: "Completed",
                RestorationAttemptId: "mm01cb-proof",
                SourceHandoffId: "mpsh-proof",
                SourceHandoffSha256: new string('a', 64),
                MigrationId: "mig-proof",
                SourceMigrationId: "source-migration-proof",
                TargetRollbackExecutionId: "mpre-proof",
                FreezeAttemptId: "mm06b-proof",
                FreezePlanId: "mm06a-proof",
                FreezePlanHash: new string('b', 64),
                SourceFingerprint: new string('c', 64),
                SourceStackSlug: "legacy-stack",
                MatrixServerName: "matrix.example.test",
                StartedAtUtc: started,
                CompletedAtUtc: DateTimeOffset.UtcNow,
                SourceRestored: true,
                RestartPoliciesRestored: true,
                OriginalRunningStatesRestored: true,
                MatrixVerified: true,
                ElementVerified: true,
                TargetRollbackAuthorityVerified: true,
                DevelopmentExternalControlPlane: false,
                Containers: [],
                TargetRouteEvidence: [],
                CompletionEvidencePath: "completion.json",
                CompletionEvidenceSha256: new string('d', 64),
                JsonPath: "report.json",
                MarkdownPath: "report.md",
                Warnings: [],
                NextSteps: []);
            await journal.CompleteAsync(
                report,
                "{\"status\":\"Completed\"}",
                CancellationToken.None);

            var stored = await journal.GetAsync(
                "mm01cb-proof",
                CancellationToken.None);

            Assert.NotNull(stored);
            Assert.Equal("Completed", stored.Status);
            Assert.Equal("mpsh-proof", stored.SourceHandoffId);
            Assert.Equal("mm06b-proof", stored.FreezeAttemptId);
            Assert.Contains("Completed", stored.ReportJson!);
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
