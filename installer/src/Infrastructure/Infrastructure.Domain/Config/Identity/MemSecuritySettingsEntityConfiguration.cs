using Infrastructure.Data.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Identity;

public sealed class MemSecuritySettingsEntityConfiguration
    : IEntityTypeConfiguration<MemSecuritySettingsEntity>
{
    public void Configure(EntityTypeBuilder<MemSecuritySettingsEntity> builder)
    {
        builder.ToTable("MemSecuritySettings");

        builder.HasKey(entity => entity.Id);

        builder.Property(entity => entity.RequireHighRiskStepUp)
            .IsRequired();

        builder.Property(entity => entity.HighRiskStepUpGrantMinutes)
            .IsRequired();

        builder.Property(entity => entity.CreatedAtUtc)
            .IsRequired();

        builder.Property(entity => entity.UpdatedAtUtc)
            .IsRequired();

        builder.Property(entity => entity.ConcurrencyStamp)
            .HasMaxLength(64)
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasIndex(entity => entity.UpdatedAtUtc);
    }
}
