using System.Text.Json;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationSourceRequestServiceTests
{
    private const string Recipient =
        "age1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq";

    [Theory]
    [InlineData("preview")]
    [InlineData("final")]
    public async Task Exports_versioned_request_for_active_package_revision(string purpose)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var intake = NewIntake(now, purpose);
        db.MigrationIntakes.Add(intake);
        await db.SaveChangesAsync();

        var result = await new MigrationSourceRequestService(db).CreateAsync(
            intake.IntakeId,
            purpose,
            CancellationToken.None);

        Assert.EndsWith($"-{purpose}.json", result.FileName, StringComparison.Ordinal);
        Assert.Equal("mem-secure-intake-request", result.Request.Schema);
        Assert.Equal(1, result.Request.SchemaVersion);
        Assert.Equal(intake.IntakeId, result.Request.IntakeId);
        Assert.Equal($"mpr-{purpose}", result.Request.PackageRevisionId);
        Assert.Equal(purpose, result.Request.RequestKind);
        Assert.Equal(Recipient, result.Request.AgeRecipient);
        Assert.Equal("1111-2222-3333-4444", result.Request.RecipientFingerprint);
        Assert.Equal("0.2.0", result.Request.TargetControlPlaneVersion);
        Assert.Null(result.Request.SourceStackId);

        using var document = JsonDocument.Parse(result.Contents);
        var root = document.RootElement;
        Assert.Equal("mem-secure-intake-request", root.GetProperty("schema").GetString());
        Assert.Equal(purpose, root.GetProperty("requestKind").GetString());
        Assert.Equal($"mpr-{purpose}", root.GetProperty("packageRevisionId").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("sourceStackId").ValueKind);
        Assert.False(root.TryGetProperty("protectedAgeIdentity", out _));
        Assert.False(root.TryGetProperty("ageIdentity", out _));
        Assert.False(root.TryGetProperty("hostPath", out _));
    }

    [Fact]
    public async Task Blocks_request_after_revision_is_no_longer_awaiting_package()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var intake = NewIntake(DateTime.UtcNow, "preview");
        intake.PackageRevisions.Single().Status = "package-validated";
        db.MigrationIntakes.Add(intake);
        await db.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<MigrationSourceRequestException>(() =>
            new MigrationSourceRequestService(db).CreateAsync(
                intake.IntakeId,
                "preview",
                CancellationToken.None));

        Assert.Equal("migration_source_request_not_awaiting_package", exception.Code);
        Assert.Equal(409, exception.StatusCode);
    }

    private static MigrationIntakeEntity NewIntake(DateTime now, string purpose)
    {
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig-source-request",
            DisplayName = "Source request",
            CreatedAtUtc = now,
        };
        intake.PackageRevisions.Add(new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = $"mpr-{purpose}",
            MigrationIntakeEntityId = intake.Id,
            MigrationIntake = intake,
            RevisionNumber = purpose == "preview" ? 1 : 2,
            Purpose = purpose,
            Status = "awaiting-package",
            RetentionState = "active",
            ActivePurposeKey = $"{intake.IntakeId}:{purpose}",
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddHours(12),
            AgeRecipient = Recipient,
            RecipientFingerprint = "1111-2222-3333-4444",
        });
        return intake;
    }
}
