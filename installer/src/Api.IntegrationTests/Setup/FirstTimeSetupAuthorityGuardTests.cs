using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.HostChecks.Runtime;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Modules.Setup.Lifecycle;
using Modules.Setup.Start;

namespace Api.IntegrationTests.Setup;

public sealed class FirstTimeSetupAuthorityGuardTests
{
    [Fact]
    public async Task STARTUP_INSTALL_REL_01A_fresh_start_classification_can_create_authority()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Authority.BeginOrResumeAsync(CancellationToken.None);

        Assert.True(result.Allowed);
        Assert.NotNull(result.Installation);
        Assert.Equal(InstallationStatuses.Draft, result.Installation!.Status);
        Assert.Equal(1, await fixture.Db.Installations.CountAsync());
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01A_legacy_source_classification_cannot_create_authority()
    {
        await using var fixture = await Fixture.CreateAsync(
            new SetupDockerContainer(
                Id: "legacy-api",
                Name: "mem-api",
                Image: "mem-api:0.1.0",
                State: "running",
                Status: "Up",
                Labels: new Dictionary<string, string>()));

        var result = await fixture.Authority.BeginOrResumeAsync(CancellationToken.None);

        Assert.False(result.Allowed);
        Assert.Equal("setup-not-available", result.ReasonCode);
        Assert.Null(result.Installation);
        Assert.Equal(0, await fixture.Db.Installations.CountAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;

        private Fixture(
            string databasePath,
            MemDbContext db,
            ServiceProvider serviceProvider,
            FirstTimeSetupAuthorityService authority)
        {
            _databasePath = databasePath;
            _serviceProvider = serviceProvider;
            Db = db;
            Authority = authority;
        }

        public MemDbContext Db { get; }
        public FirstTimeSetupAuthorityService Authority { get; }

        public static async Task<Fixture> CreateAsync(
            params SetupDockerContainer[] containers)
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-startup-install-rel-01a-guard-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var serviceProvider = new ServiceCollection().BuildServiceProvider();
            var installPlans = new InstallPlanService(
                db,
                NullLogger<InstallPlanService>.Instance,
                new RecordingInstallRunCoordinator());
            var setupStart = new SetupStartService(
                new StaticDockerProbe(containers),
                db,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Product:Version"] = "0.2.0"
                    })
                    .Build(),
                NullLogger<SetupStartService>.Instance);
            var authority = new FirstTimeSetupAuthorityService(
                setupStart,
                installPlans,
                NullLogger<FirstTimeSetupAuthorityService>.Instance);

            return new Fixture(databasePath, db, serviceProvider, authority);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _serviceProvider.DisposeAsync();

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

    private sealed class StaticDockerProbe(
        IReadOnlyList<SetupDockerContainer> containers)
        : ISetupDockerRuntimeProbe
    {
        public Task<SetupDockerSystemInfo> GetSystemInfoAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new SetupDockerSystemInfo(
                ServerVersion: "28.0.2",
                DockerRootDir: "/var/lib/docker",
                OperatingSystem: "Ubuntu",
                Architecture: "x86_64",
                MemoryBytes: 8L * 1024 * 1024 * 1024,
                CpuCount: 4,
                ContainerCount: containers.Count,
                ImageCount: 1));

        public Task<IReadOnlyList<SetupDockerContainer>> ListContainersAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SetupDockerContainer>>(containers);

        public Task<IReadOnlyList<SetupDockerVolume>> ListVolumesAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SetupDockerVolume>>([]);

        public Task<IReadOnlyList<SetupDockerNetwork>> ListNetworksAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SetupDockerNetwork>>([]);
    }
}
