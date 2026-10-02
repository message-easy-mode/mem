using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationPackageRevisionEntityConfiguration :
    IEntityTypeConfiguration<MigrationPackageRevisionEntity>
{
    public void Configure(EntityTypeBuilder<MigrationPackageRevisionEntity> builder)
    {
        builder.ToTable("MigrationPackageRevisions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.PackageRevisionId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Purpose)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(x => x.RetentionState)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(x => x.ActivePurposeKey)
            .HasMaxLength(180);

        builder.Property(x => x.AgeRecipient)
            .HasMaxLength(200);

        builder.Property(x => x.ProtectedAgeIdentity);

        builder.Property(x => x.RecipientFingerprint)
            .HasMaxLength(80);

        builder.Property(x => x.PackageFileName)
            .HasMaxLength(300);

        builder.Property(x => x.EncryptedPackageSha256)
            .HasMaxLength(64);

        builder.Property(x => x.DecryptedArchiveSha256)
            .HasMaxLength(64);

        builder.Property(x => x.ArchiveMigrationId)
            .HasMaxLength(120);

        builder.Property(x => x.ArchiveSourceProduct)
            .HasMaxLength(120);

        builder.Property(x => x.ArchiveSourceVersion)
            .HasMaxLength(80);

        builder.Property(x => x.CaptureKind)
            .HasMaxLength(20);

        builder.Property(x => x.ValidationCode)
            .HasMaxLength(120);

        builder.Property(x => x.ValidationSummary)
            .HasMaxLength(2000);

        builder.HasIndex(x => x.PackageRevisionId)
            .IsUnique();

        builder.HasIndex(x => new { x.MigrationIntakeEntityId, x.RevisionNumber })
            .IsUnique();

        builder.HasIndex(x => x.ActivePurposeKey)
            .IsUnique();

        builder.HasIndex(x => new { x.MigrationIntakeEntityId, x.Purpose, x.CreatedAtUtc });
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RetentionState);

        builder.HasOne(x => x.MigrationIntake)
            .WithMany(x => x.PackageRevisions)
            .HasForeignKey(x => x.MigrationIntakeEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
