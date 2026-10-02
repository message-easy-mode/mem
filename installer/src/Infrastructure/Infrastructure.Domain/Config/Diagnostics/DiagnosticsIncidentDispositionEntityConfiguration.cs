using Infrastructure.Data.Entities.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Diagnostics;

public sealed class DiagnosticsIncidentDispositionEntityConfiguration
    : IEntityTypeConfiguration<DiagnosticsIncidentDispositionEntity>
{
    public void Configure(EntityTypeBuilder<DiagnosticsIncidentDispositionEntity> builder)
    {
        builder.ToTable("DiagnosticsIncidentDispositions");

        builder.HasKey(x => x.IncidentId);

        builder.Property(x => x.IncidentId)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.Disposition)
            .IsRequired()
            .HasMaxLength(32);

        builder.Property(x => x.UpdatedAtUtc)
            .IsRequired();

        builder.Property(x => x.UpdatedByOperatorId)
            .IsRequired();

        builder.Property(x => x.ObservedThroughAtUtc)
            .IsRequired();

        builder.Property(x => x.ObservedThroughEventId)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.SnoozedUntilUtc)
            .IsRequired(false);

        builder.Property(x => x.ResolutionCode)
            .IsRequired(false)
            .HasMaxLength(40);

        builder.Property(x => x.Revision)
            .IsRequired()
            .IsConcurrencyToken()
            .HasDefaultValue(1);

        builder.HasIndex(x => x.Disposition);
        builder.HasIndex(x => x.UpdatedAtUtc);
        builder.HasIndex(x => x.SnoozedUntilUtc);
    }
}
