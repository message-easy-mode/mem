using Mem.Migrate.Core.Capture;

namespace Mem.Migrate.UnitTests;

public sealed class PackageForIntakeOptionsTests
{
    private const string Recipient =
        "age1r5cmtjs7qft4w44jqh0y4w5w23jlcccztfrlqq8x3r2g8r60vqkqnkvu6f";

    [Fact]
    public void Normalize_allows_automatic_archive_discovery_and_current_directory_output()
    {
        var fingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient);
        var options = new PackageForIntakeOptions
        {
            IntakeId = "mig_20260715-example",
            AgeRecipient = Recipient,
            RecipientFingerprint = fingerprint.ToLowerInvariant()
        }.Normalize();

        Assert.Equal("mig_20260715-example", options.IntakeId);
        Assert.Equal(Recipient, options.AgeRecipient);
        Assert.Equal(fingerprint, options.RecipientFingerprint);
        Assert.Equal(string.Empty, options.ArchivePath);
        Assert.Equal(Path.GetFullPath("."), options.OutputDirectory);
        Assert.Equal(string.Empty, options.WorkspacePath);
    }

    [Fact]
    public void Normalize_preserves_advanced_path_overrides()
    {
        var fingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient);
        var options = new PackageForIntakeOptions
        {
            IntakeId = "mig_20260715-example",
            AgeRecipient = Recipient,
            RecipientFingerprint = fingerprint,
            ArchivePath = "source.memmigration.zip",
            OutputDirectory = "output",
            WorkspacePath = "work"
        }.Normalize();

        Assert.True(Path.IsPathFullyQualified(options.ArchivePath));
        Assert.True(Path.IsPathFullyQualified(options.OutputDirectory));
        Assert.True(Path.IsPathFullyQualified(options.WorkspacePath));
    }

    [Fact]
    public void Normalize_requires_package_revision_id_for_final_frozen_handoff()
    {
        var options = new PackageForIntakeOptions
        {
            IntakeId = "mig_20260715-example",
            AgeRecipient = Recipient,
            RecipientFingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient),
            RequireFinalFrozen = true
        };

        var exception = Assert.Throws<ArgumentException>(() => options.Normalize());
        Assert.Contains("package revision ID", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_accepts_final_frozen_handoff_identity()
    {
        var options = new PackageForIntakeOptions
        {
            IntakeId = "mig_20260715-example",
            PackageRevisionId = "mpr_20260718-final",
            AgeRecipient = Recipient,
            RecipientFingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient),
            RequireFinalFrozen = true
        }.Normalize();

        Assert.Equal("mpr_20260718-final", options.PackageRevisionId);
        Assert.True(options.RequireFinalFrozen);
    }

    [Fact]
    public void Normalize_rejects_non_plaintext_migration_archive_override()
    {
        var options = new PackageForIntakeOptions
        {
            IntakeId = "mig_20260715-example",
            AgeRecipient = Recipient,
            RecipientFingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient),
            ArchivePath = "source.memmigration.zip.age"
        };

        var exception = Assert.Throws<ArgumentException>(() => options.Normalize());
        Assert.Contains("plaintext .memmigration.zip", exception.Message, StringComparison.Ordinal);
    }
}
