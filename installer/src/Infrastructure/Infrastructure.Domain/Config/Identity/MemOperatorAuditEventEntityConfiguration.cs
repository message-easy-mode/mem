using Infrastructure.Data.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Identity;

public sealed class MemOperatorAuditEventEntityConfiguration : IEntityTypeConfiguration<MemOperatorAuditEventEntity>
{
    public void Configure(EntityTypeBuilder<MemOperatorAuditEventEntity> builder)
    {
        builder.ToTable("MemOperatorAuditEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.OccurredAtUtc)
            .IsRequired();

        builder.Property(x => x.EventType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Outcome)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.CorrelationId)
            .IsRequired(false)
            .HasMaxLength(128);

        builder.Property(x => x.ReasonCode)
            .IsRequired(false)
            .HasMaxLength(200);

        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasIndex(x => x.ActorOperatorId);
        builder.HasIndex(x => x.SubjectOperatorId);
        builder.HasIndex(x => new { x.EventType, x.OccurredAtUtc });
    }
}
