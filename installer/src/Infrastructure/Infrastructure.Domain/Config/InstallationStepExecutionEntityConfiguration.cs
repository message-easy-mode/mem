using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations;

public sealed class InstallationStepExecutionEntityConfiguration : IEntityTypeConfiguration<InstallationStepExecutionEntity>
{
    public void Configure(EntityTypeBuilder<InstallationStepExecutionEntity> builder)
    {
        builder.ToTable("InstallationStepExecutions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.StepName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Message)
            .HasMaxLength(4000);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(8000);

        builder.Property(x => x.AttemptCount)
            .IsRequired();

        builder.HasIndex(x => new { x.InstallationId, x.Sequence });
    }
}