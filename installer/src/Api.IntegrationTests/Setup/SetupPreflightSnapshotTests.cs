using System.Text.Json;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.HostChecks;
using Modules.Setup.InstallPlans;

namespace Api.IntegrationTests.Setup;

public sealed class SetupPreflightSnapshotTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task STARTUP_INSTALL_REL_01C_compact_host_check_snapshot_is_persisted_into_Draft_plan()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-startup-install-rel-01c-preflight-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var service = new InstallPlanService(
                db,
                NullLogger<InstallPlanService>.Instance,
                new RecordingInstallRunCoordinator());
            var setup = await service.BeginSetupAsync(CancellationToken.None);

            var run = new HostCheckRunResponse(
                Id: "preflight-01c",
                Status: HostCheckRunStatus.SucceededWithWarnings,
                StartedAtUtc: DateTimeOffset.UtcNow.AddSeconds(-3),
                CompletedAtUtc: DateTimeOffset.UtcNow,
                Summary: new HostCheckSummaryDto(
                    Passed: 8,
                    Warnings: 1,
                    Failed: 0,
                    Skipped: 2,
                    Unavailable: 2,
                    Unknown: 0),
                Groups:
                [
                    new HostCheckGroupDto(
                        "host",
                        "Host",
                        "Host facts",
                        [
                            Result("memory", HostCheckStatus.Pass),
                            Result("storage", HostCheckStatus.Warning),
                            Result("ports", HostCheckStatus.Unavailable)
                        ])
                ]);

            var persisted = await service.RecordPreflightSnapshotAsync(
                run,
                CancellationToken.None);

            Assert.True(persisted);

            var saved = await service.GetAsync(setup.Id, CancellationToken.None);
            var config = JsonSerializer.Deserialize<InstallPlan>(saved!.ConfigJson!, JsonOptions);

            Assert.NotNull(config);
            Assert.NotNull(config!.Preflight);
            Assert.Equal("preflight-01c", config.Preflight!.RunId);
            Assert.Equal(8, config.Preflight.Passed);
            Assert.Equal(1, config.Preflight.Warnings);
            Assert.Equal(2, config.Preflight.Unavailable);
            Assert.Contains("storage", config.Preflight.WarningCheckKeys);
            Assert.Contains("ports", config.Preflight.UnavailableCheckKeys);
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    private static HostCheckResultDto Result(string key, HostCheckStatus status) =>
        new(
            Key: key,
            Title: key,
            Status: status,
            Blocking: false,
            Summary: key,
            Details: null,
            WhyItMatters: key,
            RecommendedAction: null,
            Evidence: []);

    private static void DeleteSqliteArtifacts(string databasePath)
    {
        foreach (var path in new[]
                 {
                     databasePath,
                     databasePath + "-shm",
                     databasePath + "-wal"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
