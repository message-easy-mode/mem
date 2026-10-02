using System.Text.Json;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationPackageRevisionProjectionTests
{
    [Fact]
    public async Task Projects_preview_and_final_revision_history_without_private_identity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var intake = new MigrationIntakeEntity
        {
            Id = Guid.NewGuid(),
            IntakeId = "mig_revision_projection",
            DisplayName = "Revision projection",
            CreatedAtUtc = now.AddHours(-2),
        };

        intake.PackageRevisions.Add(CreateRevision(
            intake,
            revisionNumber: 1,
            purpose: "preview",
            active: true,
            protectedIdentity: "PROTECTED-PREVIEW-SECRET",
            now.AddHours(-2)));
        intake.PackageRevisions.Add(CreateRevision(
            intake,
            revisionNumber: 2,
            purpose: "final",
            active: true,
            protectedIdentity: "PROTECTED-FINAL-SECRET",
            now.AddHours(-1)));

        db.MigrationIntakes.Add(intake);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var service = new MigrationSessionProjectionService(db, TimeProvider.System);
        var detail = await service.GetAsync(intake.IntakeId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.NotNull(detail!.Package);
        Assert.Equal("final.memmigration.zip.age", detail.Package!.FileName);
        Assert.Equal(new string('e', 64), detail.Package.DecryptedSha256);
        Assert.Equal("source-final-projection", detail.Package.ArchiveMigrationId);
        Assert.Equal(2, detail.PackageRevisions.Count);

        var preview = Assert.Single(detail.PackageRevisions, x => x.Purpose == "preview");
        Assert.Equal(1, preview.RevisionNumber);
        Assert.True(preview.Active);
        Assert.True(preview.RehearsalOnly);
        Assert.False(preview.SourceFrozen);

        var final = Assert.Single(detail.PackageRevisions, x => x.Purpose == "final");
        Assert.Equal(2, final.RevisionNumber);
        Assert.True(final.Active);
        Assert.False(final.RehearsalOnly);
        Assert.True(final.SourceFrozen);

        var json = JsonSerializer.Serialize(
            detail,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("PROTECTED-PREVIEW-SECRET", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PROTECTED-FINAL-SECRET", json, StringComparison.Ordinal);
        Assert.DoesNotContain("protectedAgeIdentity", json, StringComparison.OrdinalIgnoreCase);
    }

    private static MigrationPackageRevisionEntity CreateRevision(
        MigrationIntakeEntity intake,
        int revisionNumber,
        string purpose,
        bool active,
        string protectedIdentity,
        DateTime createdAtUtc)
    {
        var isFinal = purpose == "final";
        return new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = $"mpr_projection_{revisionNumber}",
            MigrationIntake = intake,
            MigrationIntakeEntityId = intake.Id,
            RevisionNumber = revisionNumber,
            Purpose = purpose,
            Status = "package-validated",
            RetentionState = "active",
            ActivePurposeKey = active ? $"{intake.IntakeId}:{purpose}" : null,
            CreatedAtUtc = createdAtUtc,
            UploadedAtUtc = createdAtUtc.AddMinutes(5),
            ValidatedAtUtc = createdAtUtc.AddMinutes(6),
            AgeRecipient = $"age1{purpose}projectionrecipient",
            ProtectedAgeIdentity = protectedIdentity,
            RecipientFingerprint = isFinal ? "AAAA-BBBB-CCCC-DDDD" : "1111-2222-3333-4444",
            PackageFileName = $"{purpose}.memmigration.zip.age",
            PackageSizeBytes = revisionNumber * 1024,
            EncryptedPackageSha256 = new string(isFinal ? 'd' : 'b', 64),
            DecryptedArchiveSha256 = new string(isFinal ? 'e' : 'c', 64),
            ArchiveMigrationId = $"source-{purpose}-projection",
            ArchiveSourceProduct = "MatrixEasyMode",
            ArchiveSourceVersion = "0.1.0",
            ArchiveStackCount = 1,
            CaptureKind = purpose,
            SourceFrozen = isFinal,
            RehearsalOnly = !isFinal,
            VerifiedFileCount = 12,
            VerifiedExpandedBytes = 2048,
            ValidationCode = "validated",
            ValidationSummary = "Validated package evidence.",
        };
    }
}
