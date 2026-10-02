using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class RestoreAttemptEntityConfiguration : IEntityTypeConfiguration<RestoreAttemptEntity>
{
    public void Configure(EntityTypeBuilder<RestoreAttemptEntity> builder)
    {
        builder.ToTable("RestoreAttempts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RestoreSessionId)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.SourceKind)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.SourceKey)
            .IsRequired()
            .HasMaxLength(400);

        builder.Property(x => x.ActiveSourceKey)
            .HasMaxLength(400);

        builder.Property(x => x.SourceCatalogEntryIdSnapshot)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(x => x.SourceDisplayNameSnapshot)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(x => x.SourceOriginKindSnapshot)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.SourceStackSlugSnapshot)
            .HasMaxLength(150);

        builder.Property(x => x.SourceBackupIdSnapshot)
            .HasMaxLength(200);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.CurrentStage)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(x => x.LastErrorCode)
            .HasMaxLength(200);

        builder.Property(x => x.LastErrorSummary)
            .HasMaxLength(4000);

        builder.Property(x => x.SessionDirectoryPath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.LogDirectoryPath)
            .HasMaxLength(1000);

        builder.Property(x => x.SupportReportPath)
            .HasMaxLength(1000);

        builder.HasIndex(x => x.RestoreSessionId)
            .IsUnique();

        builder.HasIndex(x => x.ActiveSourceKey)
            .IsUnique();

        builder.HasIndex(x => x.BackupCatalogEntryId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RuntimeOperationId);
        builder.HasIndex(x => new { x.SourceOriginKindSnapshot, x.SourceStackSlugSnapshot, x.SourceBackupIdSnapshot });

        builder.HasMany(x => x.TargetClaims)
            .WithOne(x => x.RestoreAttempt)
            .HasForeignKey(x => x.RestoreAttemptId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.RuntimeOperations)
            .WithOne(x => x.RestoreAttempt)
            .HasForeignKey(x => x.RestoreAttemptId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
