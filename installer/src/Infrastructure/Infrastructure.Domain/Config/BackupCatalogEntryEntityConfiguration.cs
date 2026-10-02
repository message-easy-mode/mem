using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class BackupCatalogEntryEntityConfiguration : IEntityTypeConfiguration<BackupCatalogEntryEntity>
{
    public void Configure(EntityTypeBuilder<BackupCatalogEntryEntity> builder)
    {
        builder.ToTable("BackupCatalogEntries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CatalogEntryId)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.OriginKind)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(x => x.DisplayName)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(x => x.PayloadState)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(x => x.PayloadStorageKind)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.PayloadDirectoryPath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.SourceStackSlug)
            .HasMaxLength(150);

        builder.Property(x => x.SourceBackupId)
            .HasMaxLength(200);

        builder.Property(x => x.ValidationId)
            .HasMaxLength(120);

        builder.Property(x => x.MemVersion)
            .HasMaxLength(100);

        builder.Property(x => x.MatrixServerName)
            .HasMaxLength(300);

        builder.Property(x => x.MatrixHost)
            .HasMaxLength(300);

        builder.Property(x => x.ElementHost)
            .HasMaxLength(300);

        builder.Property(x => x.IntegrityStatus)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(x => x.IntegritySummary)
            .HasMaxLength(1000);

        builder.Property(x => x.PayloadRemovedBy)
            .HasMaxLength(100);

        builder.HasIndex(x => x.CatalogEntryId)
            .IsUnique();

        // A successful materialisation is linked to at most one validation
        // receipt. SQLite permits multiple NULL values in a unique index, so
        // local-captured entries remain unaffected.
        builder.HasIndex(x => x.ValidationId)
            .IsUnique();

        builder.HasIndex(x => x.OriginKind);
        builder.HasIndex(x => x.PayloadState);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => new { x.SourceStackSlug, x.SourceBackupId });

        builder.HasMany(x => x.RestoreAttempts)
            .WithOne(x => x.BackupCatalogEntry)
            .HasForeignKey(x => x.BackupCatalogEntryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
