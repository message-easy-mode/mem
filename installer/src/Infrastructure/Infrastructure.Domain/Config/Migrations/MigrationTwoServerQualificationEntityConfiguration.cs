using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationTwoServerQualificationEntityConfiguration :
    IEntityTypeConfiguration<MigrationTwoServerQualificationEntity>
{
    public void Configure(EntityTypeBuilder<MigrationTwoServerQualificationEntity> builder)
    {
        builder.ToTable("MigrationTwoServerQualifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.QualificationId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(40);
        builder.Property(x => x.SourceEvidenceAttemptId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.SourceEvidenceSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.SourceEvidenceJson).IsRequired();
        builder.Property(x => x.QualificationEvidenceSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.QualificationEvidenceJson).IsRequired();
        builder.Property(x => x.SourceMigrationId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.PackageRevisionId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.EncryptedPackageSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.SourceFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(x => x.SourceStackSlug).IsRequired().HasMaxLength(150);
        builder.Property(x => x.MatrixServerName).IsRequired().HasMaxLength(255);
        builder.Property(x => x.FreezeAttemptId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.FreezePlanId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.FreezePlanSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.AdoptionPlanId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ProductionVerificationId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.AcceptanceId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.BaselineBackupHandoffId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.BaselineCatalogEntryId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.SourceMachineIdSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.SourceDockerEngineIdSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.TargetMachineIdSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.TargetDockerEngineIdSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ClosureId).HasMaxLength(100);
        builder.Property(x => x.ClosureStatus).HasMaxLength(40);
        builder.Property(x => x.ClosureEvidenceSha256).HasMaxLength(64);
        builder.Property(x => x.ClosureNote).HasMaxLength(1000);

        builder.HasIndex(x => x.QualificationId).IsUnique();
        builder.HasIndex(x => x.MigrationIntakeEntityId).IsUnique();
        builder.HasIndex(x => x.SourceEvidenceAttemptId).IsUnique();
        builder.HasIndex(x => x.SourceEvidenceSha256).IsUnique();
        builder.HasIndex(x => x.QualificationEvidenceSha256).IsUnique();
        builder.HasIndex(x => x.ClosureId).IsUnique();
        builder.HasIndex(x => x.ClosureStatus);
        builder.HasIndex(x => x.Status);
        builder.HasOne(x => x.MigrationIntake)
            .WithOne(x => x.TwoServerQualification)
            .HasForeignKey<MigrationTwoServerQualificationEntity>(x => x.MigrationIntakeEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
