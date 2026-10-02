using Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config;

public sealed class RuntimeStackDatabaseEntityConfiguration : IEntityTypeConfiguration<RuntimeStackDatabaseEntity>
{
    public void Configure(EntityTypeBuilder<RuntimeStackDatabaseEntity> builder)
    {
        builder.ToTable("RuntimeStackDatabases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DatabaseEngine)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.DatabaseHost)
            .IsRequired()
            .HasMaxLength(253);

        builder.Property(x => x.DatabaseName)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(x => x.DatabaseUsername)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(x => x.PasswordSecretKind)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(80);

        builder.HasIndex(x => x.RuntimeStackId)
            .IsUnique();

        builder.HasIndex(x => x.DatabaseName)
            .IsUnique();

        builder.HasIndex(x => x.DatabaseUsername)
            .IsUnique();

        builder.HasIndex(x => x.Status);

        builder.HasOne(x => x.RuntimeStack)
            .WithMany()
            .HasForeignKey(x => x.RuntimeStackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}