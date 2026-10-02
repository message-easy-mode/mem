using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class RuntimeReadinessReportEntityConfiguration : IEntityTypeConfiguration<RuntimeReadinessReportEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeReadinessReportEntity> builder)
    {
        builder.ToTable("RuntimeReadinessReports");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ReportKind)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.Summary)
            .HasMaxLength(1000);

        builder.Property(x => x.TriggeredBy)
            .HasMaxLength(100);

        builder.HasIndex(x => x.RuntimeStackId);

        builder.HasIndex(x => new
        {
            x.RuntimeStackId,
            x.CreatedAtUtc
        });

        builder.HasIndex(x => new
        {
            x.RuntimeStackId,
            x.ReportKind
        });

        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.RuntimeStack)
            .WithMany()
            .HasForeignKey(x => x.RuntimeStackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}