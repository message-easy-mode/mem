using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Security;

public sealed class MemCliDeviceInstallationBindingServiceTests
{
    [Fact]
    public async Task CLI_AUTH_02B_binds_device_authorizations_to_the_latest_completed_installation_only()
    {
        await using var fixture = await InstallationBindingFixture.CreateAsync();

        var olderCompleted = new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "Succeeded",
            CreatedAtUtc = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2026, 7, 1, 1, 0, 0, DateTimeKind.Utc)
        };
        var latestCompleted = new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "Succeeded",
            CreatedAtUtc = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 7, 2, 1, 0, 0, DateTimeKind.Utc),
            CompletedAtUtc = new DateTime(2026, 7, 2, 1, 0, 0, DateTimeKind.Utc)
        };
        var newerIncomplete = new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "Running",
            CreatedAtUtc = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAtUtc = new DateTime(2026, 7, 3, 1, 0, 0, DateTimeKind.Utc),
            CompletedAtUtc = null
        };

        await fixture.Db.Installations.AddRangeAsync(
            olderCompleted,
            latestCompleted,
            newerIncomplete);
        await fixture.Db.SaveChangesAsync();

        var service = new MemCliDeviceInstallationBindingService(fixture.Db);

        Assert.Equal(
            latestCompleted.Id,
            await service.GetCurrentInstallationIdAsync());
    }

    [Fact]
    public async Task CLI_AUTH_02B_refuses_to_bind_a_device_authorization_when_no_completed_installation_exists()
    {
        await using var fixture = await InstallationBindingFixture.CreateAsync();

        await fixture.Db.Installations.AddAsync(new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "Running",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var service = new MemCliDeviceInstallationBindingService(fixture.Db);

        Assert.Null(await service.GetCurrentInstallationIdAsync());
    }

    private sealed class InstallationBindingFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private InstallationBindingFixture(string databasePath, MemDbContext db)
        {
            _databasePath = databasePath;
            Db = db;
        }

        public MemDbContext Db { get; }

        public static async Task<InstallationBindingFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-cli-auth-02b-installation-binding-{Guid.NewGuid():N}.db");

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            return new InstallationBindingFixture(databasePath, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();

            try
            {
                File.Delete(_databasePath);
            }
            catch (IOException)
            {
                // A failed temporary-file cleanup must not hide a test result.
            }
        }
    }
}
