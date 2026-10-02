using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;

namespace Api.IntegrationTests.Setup;

public sealed class FirstTimeSetupAuthorityTests
{
    [Fact]
    public async Task STARTUP_INSTALL_REL_01A_begin_creates_one_durable_draft_and_reuses_it()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Service.BeginSetupAsync(CancellationToken.None);
        var second = await fixture.Service.BeginSetupAsync(CancellationToken.None);

        Assert.Equal(InstallationStatuses.Draft, first.Status);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(
            1,
            await fixture.Db.Installations.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01A_legacy_create_contract_is_idempotent_too()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Service.CreateAsync(CancellationToken.None);
        var second = await fixture.Service.CreateAsync(CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(
            1,
            await fixture.Db.Installations.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01A_begin_reuses_existing_ready_authority()
    {
        await using var fixture = await Fixture.CreateAsync();
        var readyId = Guid.NewGuid();
        fixture.Db.Installations.Add(new Infrastructure.Data.Entities.InstallationEntity
        {
            Id = readyId,
            Status = InstallationStatuses.Ready,
            ConfigJson = null,
            FrozenConfigJson = null,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.Service.BeginSetupAsync(CancellationToken.None);

        Assert.Equal(readyId, response.Id);
        Assert.Equal(InstallationStatuses.Ready, response.Status);
        Assert.Equal(
            1,
            await fixture.Db.Installations.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01A_service_refuses_new_authority_after_completed_installation()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.Installations.Add(new Infrastructure.Data.Entities.InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = InstallationStatuses.Succeeded,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await fixture.Db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.BeginSetupAsync(CancellationToken.None));

        Assert.Contains("already completed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            1,
            await fixture.Db.Installations.CountAsync(CancellationToken.None));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;

        private Fixture(
            string databasePath,
            MemDbContext db,
            ServiceProvider serviceProvider)
        {
            _databasePath = databasePath;
            _serviceProvider = serviceProvider;
            Db = db;
            Service = new InstallPlanService(
                db,
                NullLogger<InstallPlanService>.Instance,
                new RecordingInstallRunCoordinator());
        }

        public MemDbContext Db { get; }
        public InstallPlanService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-startup-install-rel-01a-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var serviceProvider = new ServiceCollection().BuildServiceProvider();
            return new Fixture(databasePath, db, serviceProvider);
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
}
