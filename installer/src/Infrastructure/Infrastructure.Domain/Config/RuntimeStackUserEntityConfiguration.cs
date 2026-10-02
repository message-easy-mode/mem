using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class RuntimeStackUserEntityConfiguration : IEntityTypeConfiguration<RuntimeStackUserEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeStackUserEntity> builder)
    {
        builder.ToTable("RuntimeStackUsers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Username)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(x => x.MatrixUserId)
            .HasMaxLength(512);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.DisplayName)
            .HasMaxLength(255);

        builder.Property(x => x.Email)
            .HasMaxLength(320);

        builder.Property(x => x.LastError)
            .HasMaxLength(4000);

        builder.HasIndex(x => new
        {
            x.RuntimeStackId,
            x.Username
        }).IsUnique();

        builder.HasIndex(x => new
        {
            x.RuntimeStackId,
            x.MatrixUserId
        });

        builder.HasIndex(x => new
        {
            x.RuntimeStackId,
            x.IsFirstAdmin
        });

        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.RuntimeStack)
            .WithMany()
            .HasForeignKey(x => x.RuntimeStackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
