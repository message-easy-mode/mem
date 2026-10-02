using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationCandidateArtifactEntityConfiguration :
    IEntityTypeConfiguration<MigrationCandidateArtifactEntity>
{
    public void Configure(EntityTypeBuilder<MigrationCandidateArtifactEntity> builder)
    {
        builder.ToTable("MigrationCandidateArtifacts");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.CandidateArtifactId)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.ArtifactKind)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.ArtifactSchemaVersion)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(x => x.SourcePackageSha256)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.ArtifactSha256)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.ManifestSha256)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.ChecksumsSha256)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(x => x.ProvenanceJson)
            .IsRequired();

        builder.Property(x => x.VerificationStatus)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(x => x.RetentionState)
            .IsRequired()
            .HasMaxLength(40);

        builder.Property(x => x.StorageKind)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.ArtifactPath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(x => x.VerificationReportPath)
            .HasMaxLength(1000);

        builder.HasIndex(x => x.CandidateArtifactId)
            .IsUnique();

        builder.HasIndex(x => x.MigrationConversionAttemptEntityId)
            .IsUnique();

        builder.HasIndex(x => x.VerificationStatus);
        builder.HasIndex(x => x.RetentionState);
    }
}
