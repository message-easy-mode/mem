using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationProductionAuthorityEntityConfiguration
    : IEntityTypeConfiguration<MigrationProductionAuthorityEntity>
{
    public void Configure(EntityTypeBuilder<MigrationProductionAuthorityEntity> builder)
    {
        builder.ToTable("MigrationProductionAuthorities");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ProductionAuthorityId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.AuthorityType).IsRequired().HasMaxLength(60);
        builder.Property(x => x.Status).IsRequired().HasMaxLength(40);
        builder.Property(x => x.ActiveMigrationKey).HasMaxLength(100);

        builder.Property(x => x.EncryptedPackageSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.DecryptedArchiveSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.SourceMigrationId).IsRequired().HasMaxLength(160);
        builder.Property(x => x.SourceFingerprint).IsRequired().HasMaxLength(64);
        builder.Property(x => x.MatrixServerName).IsRequired().HasMaxLength(255);
        builder.Property(x => x.SigningKeyIdentitySha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.CaptureKind).IsRequired().HasMaxLength(40);

        builder.Property(x => x.EvidenceSchemaVersion).IsRequired().HasMaxLength(80);
        builder.Property(x => x.EvidenceJson).IsRequired();
        builder.Property(x => x.EvidenceSha256).IsRequired().HasMaxLength(64);

        builder.Property(x => x.AcknowledgementsSchemaVersion).IsRequired().HasMaxLength(80);
        builder.Property(x => x.AcknowledgementsJson).IsRequired();
        builder.Property(x => x.AcknowledgementsSha256).IsRequired().HasMaxLength(64);

        builder.Property(x => x.SupersessionReason).HasMaxLength(500);
        builder.Property(x => x.RevocationReason).HasMaxLength(500);

        builder.HasIndex(x => x.ProductionAuthorityId).IsUnique();
        builder.HasIndex(x => x.ActiveMigrationKey).IsUnique();
        builder.HasIndex(x => x.MigrationIntakeEntityId);
        builder.HasIndex(x => x.MigrationPackageRevisionEntityId);
        builder.HasIndex(x => x.MigrationCandidateArtifactEntityId);
        builder.HasIndex(x => x.MigrationStagingRunEntityId);
        builder.HasIndex(x => x.AuthorityType);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAtUtc);

        builder.HasOne(x => x.MigrationIntake)
            .WithMany(x => x.ProductionAuthorities)
            .HasForeignKey(x => x.MigrationIntakeEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PackageRevision)
            .WithMany()
            .HasForeignKey(x => x.MigrationPackageRevisionEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CandidateArtifact)
            .WithMany()
            .HasForeignKey(x => x.MigrationCandidateArtifactEntityId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.StagingRun)
            .WithMany()
            .HasForeignKey(x => x.MigrationStagingRunEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
