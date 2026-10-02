using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.HostChecks;
using Modules.Setup.HostChecks.Checks;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;

namespace Api.IntegrationTests.Setup;

public sealed class HostChecksGroupingTests
{
    [Fact]
    public async Task CORR_01_groups_checks_by_group_key_even_when_check_descriptions_differ()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-host-check-grouping-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var installPlans = new InstallPlanService(
                db,
                NullLogger<InstallPlanService>.Instance,
                new NoopInstallRunCoordinator());
            await installPlans.BeginSetupAsync(CancellationToken.None);

            var service = new HostChecksService(
                [
                    new StubHostCheck(
                        key: "docker-daemon",
                        groupKey: "docker",
                        groupTitle: "Docker",
                        groupDescription: "Docker daemon reachability.",
                        title: "Docker daemon"),
                    new StubHostCheck(
                        key: "docker-data-root",
                        groupKey: "docker",
                        groupTitle: "Docker",
                        groupDescription: "Docker data-root visibility.",
                        title: "Docker data root")
                ],
                new HostCheckRunStore(),
                installPlans,
                NullLogger<HostChecksService>.Instance);

            var run = await service.RunAsync(CancellationToken.None);

            var docker = Assert.Single(run.Groups);
            Assert.Equal("docker", docker.Key);
            Assert.Equal("Docker", docker.Title);
            Assert.Equal(2, docker.Checks.Count);
            Assert.Contains(docker.Checks, check => check.Key == "docker-daemon");
            Assert.Contains(docker.Checks, check => check.Key == "docker-data-root");
        }
        finally
        {
            try { File.Delete(databasePath); } catch { }
        }
    }

    private sealed class StubHostCheck(
        string key,
        string groupKey,
        string groupTitle,
        string groupDescription,
        string title) : IHostCheck
    {
        public string Key => key;
        public string GroupKey => groupKey;
        public string GroupTitle => groupTitle;
        public string GroupDescription => groupDescription;

        public Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new HostCheckResultDto(
                Key: key,
                Title: title,
                Status: HostCheckStatus.Pass,
                Blocking: false,
                Summary: "Pass",
                Details: null,
                WhyItMatters: "Test",
                RecommendedAction: null,
                Evidence: []));
    }

    private sealed class NoopInstallRunCoordinator : IInstallRunCoordinator
    {
        public InstallRunQueueResult Queue(Guid installationId) =>
            new(installationId, Queued: false, AlreadyOwned: false);
    }
}
