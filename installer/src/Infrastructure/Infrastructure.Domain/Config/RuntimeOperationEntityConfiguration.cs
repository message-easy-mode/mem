using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class RuntimeOperationEntityConfiguration : IEntityTypeConfiguration<RuntimeOperationEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeOperationEntity> builder)
    {
        builder.ToTable("RuntimeOperations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Operation)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.IdempotencyKey)
            .HasMaxLength(200);

        builder.Property(x => x.RequestedBy)
            .HasMaxLength(100);

        builder.Property(x => x.CurrentStep)
            .HasMaxLength(200);

        builder.Property(x => x.LastError)
            .HasMaxLength(4000);

        builder.Property(x => x.HostMutationLevel)
            .HasMaxLength(100);

        builder.HasIndex(x => x.RuntimeStackId);

        builder.HasIndex(x => x.RestoreAttemptId);

        builder.HasIndex(x => x.DomainId);

        builder.HasIndex(x => x.Operation);

        builder.HasIndex(x => x.Status);

        builder.HasIndex(x => x.RequestedAtUtc);

        builder.HasIndex(x => x.IdempotencyKey);

        builder.HasIndex(x => new
        {
            x.RuntimeStackId,
            x.Operation,
            x.RequestedAtUtc
        });

        builder.HasIndex(x => new
        {
            x.DomainId,
            x.Operation,
            x.RequestedAtUtc
        });

        // Domain-owned operations use a stable cycle idempotency key. The nullable
        // columns preserve existing non-Domain operation behavior while preventing
        // duplicate renewal-cycle rows if two Control Plane processes overlap.
        builder.HasIndex(x => new
        {
            x.DomainId,
            x.Operation,
            x.IdempotencyKey
        })
            .IsUnique();

        builder.HasOne(x => x.RuntimeStack)
            .WithMany()
            .HasForeignKey(x => x.RuntimeStackId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.RestoreAttempt)
            .WithMany(x => x.RuntimeOperations)
            .HasForeignKey(x => x.RestoreAttemptId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Domain)
            .WithMany()
            .HasForeignKey(x => x.DomainId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}