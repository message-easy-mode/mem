using Infrastructure.Data.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Config.Identity;

public sealed class MemOperatorEntityConfiguration : IEntityTypeConfiguration<MemOperator>
{
    public void Configure(EntityTypeBuilder<MemOperator> builder)
    {
        // IdentityDbContext maps this type to AspNetUsers. Keep the standard
        // Identity table contract so future OIDC/passkey work has a familiar base.
        builder.Property(x => x.IsEnabled)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.IsBootstrapProvisioning)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.Property(x => x.LastLoginAtUtc)
            .IsRequired(false);

        builder.Property(x => x.LastStepUpAtUtc)
            .IsRequired(false);
    }
}
