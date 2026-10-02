using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationSessionLifecycleInterceptorTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 7, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Initializes_and_touches_the_root_lifecycle_spine()
    {
        await using var connection =
            new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var timeProvider = new MutableTimeProvider(InitialTime);
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(
                new MigrationSessionLifecycleInterceptor(timeProvider))
            .Options;

        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_lifecycle_interceptor",
            DisplayName = "Lifecycle interceptor",
            CreatedAtUtc = InitialTime.UtcDateTime,
        };
        db.MigrationIntakes.Add(intake);
        await db.SaveChangesAsync();

        Assert.Equal(MigrationSessionLifecycleStatuses.Active, intake.LifecycleStatus);
        Assert.Equal(InitialTime.UtcDateTime, intake.UpdatedAtUtc);
        Assert.Equal(1, intake.StateVersion);

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        db.MigrationSources.Add(new MigrationSourceEntity
        {
            Id = Guid.NewGuid(),
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            SourceId = "source-lifecycle-touch",
            SourceKind = "migration-package",
            Product = "MatrixEasyMode",
            ProductVersion = "0.1.0",
            SourceFingerprint = new string('a', 64),
            CapturedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
        });
        await db.SaveChangesAsync();

        Assert.Equal(timeProvider.GetUtcNow().UtcDateTime, intake.UpdatedAtUtc);
        Assert.Equal(2, intake.StateVersion);

        timeProvider.Advance(TimeSpan.FromMinutes(5));
        intake.LifecycleStatus = MigrationSessionLifecycleStatuses.Cancelled;
        intake.CancelledAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        intake.CancelledBy = "operator";
        await db.SaveChangesAsync();

        Assert.Equal(
            MigrationSessionLifecycleStatuses.Cancelled,
            intake.LifecycleStatus);
        Assert.Equal(intake.CancelledAtUtc, intake.ClosedAtUtc);
        Assert.Equal("cancelled", intake.ClosureKind);
        Assert.Equal(3, intake.StateVersion);
    }

    private sealed class MutableTimeProvider(
        DateTimeOffset value) : TimeProvider
    {
        private DateTimeOffset _value = value;

        public override DateTimeOffset GetUtcNow() => _value;

        public void Advance(TimeSpan interval) => _value += interval;
    }
}
