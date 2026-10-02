using Infrastructure.Data.Entities.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Migrations;

public sealed class MigrationAcceptanceEntityConfiguration : IEntityTypeConfiguration<MigrationAcceptanceEntity>
{
    public void Configure(EntityTypeBuilder<MigrationAcceptanceEntity> builder)
    {
        builder.ToTable("MigrationAcceptances");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AcceptanceId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ExecutionId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.CandidateArtifactId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.StagingRunId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.PublicVerificationStatus).IsRequired().HasMaxLength(40);
        builder.Property(x => x.PublicVerificationEvidenceJson).IsRequired();
        builder.Property(x => x.PublicVerificationEvidenceSha256).IsRequired().HasMaxLength(64);
        builder.Property(x => x.AcceptedBy).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.HasIndex(x => x.AcceptanceId).IsUnique();
        builder.HasIndex(x => x.MigrationIntakeEntityId).IsUnique();
        builder.HasIndex(x => x.ExecutionId).IsUnique();
        builder.HasOne(x => x.MigrationIntake).WithOne(x => x.Acceptance)
            .HasForeignKey<MigrationAcceptanceEntity>(x => x.MigrationIntakeEntityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
