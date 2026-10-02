using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationConversionAttemptEntityConfiguration :
    IEntityTypeConfiguration<MigrationConversionAttemptEntity>
{
    public void Configure(EntityTypeBuilder<MigrationConversionAttemptEntity> builder)
    {
        builder.ToTable("MigrationConversionAttempts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ConversionAttemptId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.ActiveMigrationKey)
            .HasMaxLength(100);

        builder.Property(x => x.SourcePackageSha256)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.SourceAdapterId)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.SourceAdapterVersion)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.ConverterId)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(x => x.ConverterVersion)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(x => x.CurrentStep)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(x => x.ResultCode)
            .HasMaxLength(120);

        builder.Property(x => x.FailureCode)
            .HasMaxLength(200);

        builder.Property(x => x.FailureSummary)
            .HasMaxLength(4000);

        builder.Property(x => x.WorkspacePath)
            .HasMaxLength(1000);

        builder.Property(x => x.EvidenceDirectoryPath)
            .HasMaxLength(1000);

        builder.Property(x => x.LogDirectoryPath)
            .HasMaxLength(1000);

        builder.Property(x => x.CompletionReportPath)
            .HasMaxLength(1000);

        builder.HasIndex(x => x.ConversionAttemptId)
            .IsUnique();

        builder.HasIndex(x => x.ActiveMigrationKey)
            .IsUnique();

        builder.HasIndex(x => new { x.MigrationIntakeEntityId, x.CreatedAtUtc });
        builder.HasIndex(x => x.MigrationPackageRevisionEntityId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RetryOfConversionAttemptEntityId);

        builder.HasOne(x => x.MigrationIntake)
            .WithMany(x => x.ConversionAttempts)
            .HasForeignKey(x => x.MigrationIntakeEntityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.PackageRevision)
            .WithMany(x => x.ConversionAttempts)
            .HasForeignKey(x => x.MigrationPackageRevisionEntityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.RetryOfConversionAttempt)
            .WithMany(x => x.RetryAttempts)
            .HasForeignKey(x => x.RetryOfConversionAttemptEntityId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CandidateArtifact)
            .WithOne(x => x.ConversionAttempt)
            .HasForeignKey<MigrationCandidateArtifactEntity>(x => x.MigrationConversionAttemptEntityId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
