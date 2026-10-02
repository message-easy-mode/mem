using Infrastructure.Data.Entities.ControlPlane;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.ControlPlane;

public sealed class ControlPlaneUserRoleEntityConfiguration : IEntityTypeConfiguration<ControlPlaneUserRoleEntity>
{
    public void Configure(EntityTypeBuilder<ControlPlaneUserRoleEntity> builder)
    {
        builder.ToTable("ControlPlaneUserRoles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.ControlPlaneUserId)
            .IsRequired();

        builder.Property(x => x.Role)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(x => new
        {
            x.ControlPlaneUserId,
            x.Role
        }).IsUnique();

        builder.HasIndex(x => x.Role);
    }
}
