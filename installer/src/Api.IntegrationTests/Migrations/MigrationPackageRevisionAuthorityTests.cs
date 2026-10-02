using Infrastructure.Data.Entities.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationPackageRevisionAuthorityTests
{
    [Fact]
    public void Validated_final_revision_is_authoritative_without_root_package_mirror()
    {
        var preview = Revision(1, "preview", "package-validated", active: true);
        var final = Revision(2, "final", "package-validated", active: true);

        var authority = MigrationPackageRevisionAuthority.ResolveAuthoritativeValidated(
            new[] { preview, final });

        Assert.Same(final, authority);
    }

    [Fact]
    public void Awaiting_final_revision_keeps_validated_preview_authoritative()
    {
        var preview = Revision(1, "preview", "package-validated", active: true);
        var final = Revision(2, "final", "awaiting-package", active: true);

        var authority = MigrationPackageRevisionAuthority.ResolveAuthoritativeValidated(
            new[] { preview, final });

        Assert.Same(preview, authority);
        Assert.Same(
            preview,
            MigrationPackageRevisionAuthority.ResolveCurrentPackage(
                new[] { preview, final }));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("cancelled")]
    [InlineData("invalid")]
    public void Terminal_or_invalid_active_final_revision_blocks_preview_fallback(
        string finalStatus)
    {
        var preview = Revision(1, "preview", "package-validated", active: true);
        var final = Revision(2, "final", finalStatus, active: true);

        Assert.Null(
            MigrationPackageRevisionAuthority.ResolveAuthoritativeValidated(
                new[] { preview, final }));
    }

    [Fact]
    public void Effective_status_uses_active_revision_expiry_not_session_root()
    {
        var now = new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc);
        var revision = Revision(1, "preview", "awaiting-package", active: true);
        revision.ExpiresAtUtc = now.AddMinutes(-1);

        var intake = new MigrationIntakeEntity
        {
            Id = revision.MigrationIntakeEntityId,
            IntakeId = "mig_authority_expiry",
            DisplayName = "Authority expiry",
            CreatedAtUtc = now.AddHours(-1),
            UpdatedAtUtc = now.AddHours(-1),
            LifecycleStatus = MigrationSessionLifecycleStatuses.Active,
            StateVersion = 1,
            PackageRevisions = new List<MigrationPackageRevisionEntity> { revision },
        };
        revision.MigrationIntake = intake;

        Assert.Equal(
            "expired",
            MigrationPackageRevisionAuthority.ResolveEffectiveSessionStatus(
                intake,
                now));
    }

    [Fact]
    public void Protected_identity_is_not_required_for_validated_authority()
    {
        var revision = Revision(1, "preview", "package-validated", active: true);
        revision.ProtectedAgeIdentity = null;

        Assert.Same(
            revision,
            MigrationPackageRevisionAuthority.ResolveAuthoritativeValidated(
                new[] { revision }));
    }

    private static MigrationPackageRevisionEntity Revision(
        int number,
        string purpose,
        string status,
        bool active)
    {
        var isFinal = purpose == "final";
        return new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = $"mpr_authority_{number}",
            MigrationIntakeEntityId = Guid.NewGuid(),
            RevisionNumber = number,
            Purpose = purpose,
            Status = status,
            RetentionState = active ? "active" : "retired",
            ActivePurposeKey = active ? $"mig_authority:{purpose}" : null,
            CreatedAtUtc = new DateTime(2026, 7, 29, number, 0, 0, DateTimeKind.Utc),
            DecryptedArchiveSha256 = new string(isFinal ? 'f' : 'p', 64),
            CaptureKind = purpose,
            SourceFrozen = isFinal,
            RehearsalOnly = !isFinal,
        };
    }
}
