using Mem.Migrate.Cli;
using Mem.Migrate.Core.Capture;

namespace Mem.Migrate.UnitTests;

public sealed class PackageForIntakeCommandLineTests
{
    [Fact]
    public void Parser_accepts_normal_installed_package_for_intake_command_without_paths()
    {
        var recipient =
            "age1r5cmtjs7qft4w44jqh0y4w5w23jlcccztfrlqq8x3r2g8r60vqkqnkvu6f";
        var fingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(recipient);

        var parsed = CommandLineParser.Parse([
            "source", "package-for-intake",
            "--intake-id", "mig_20260715-example",
            "--age-recipient", recipient,
            "--recipient-fingerprint", fingerprint,
            "--json"
        ]);

        Assert.Equal("source-package-for-intake", parsed.Name);
        Assert.False(parsed.ShowHelp);
        var options = Assert.IsType<PackageForIntakeOptions>(
            parsed.PackageForIntakeOptions);
        Assert.Equal("mig_20260715-example", options.IntakeId);
        Assert.Equal(recipient, options.AgeRecipient);
        Assert.Equal(fingerprint, options.RecipientFingerprint);
        Assert.Equal(string.Empty, options.ArchivePath);
        Assert.Equal(".", options.OutputDirectory);
        Assert.Equal(string.Empty, options.WorkspacePath);
        Assert.True(options.JsonConsoleOutput);
    }

    [Fact]
    public void Parser_accepts_final_frozen_package_revision_handoff()
    {
        var recipient =
            "age1r5cmtjs7qft4w44jqh0y4w5w23jlcccztfrlqq8x3r2g8r60vqkqnkvu6f";
        var fingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(recipient);

        var parsed = CommandLineParser.Parse([
            "source", "package-for-intake",
            "--intake-id", "mig_20260715-example",
            "--package-revision-id", "mpr_20260718-final",
            "--age-recipient", recipient,
            "--recipient-fingerprint", fingerprint,
            "--require-final-frozen"
        ]);

        var options = Assert.IsType<PackageForIntakeOptions>(parsed.PackageForIntakeOptions);
        Assert.Equal("mpr_20260718-final", options.PackageRevisionId);
        Assert.True(options.RequireFinalFrozen);
    }

    [Fact]
    public void Parser_retains_advanced_archive_output_and_workspace_overrides()
    {
        var recipient =
            "age1r5cmtjs7qft4w44jqh0y4w5w23jlcccztfrlqq8x3r2g8r60vqkqnkvu6f";
        var fingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(recipient);

        var parsed = CommandLineParser.Parse([
            "source", "package-for-intake",
            "--intake-id", "mig_20260715-example",
            "--age-recipient", recipient,
            "--recipient-fingerprint", fingerprint,
            "--archive", "final.memmigration.zip",
            "--output-directory", "output",
            "--workspace", "work",
            "--age-command", "/usr/bin/age"
        ]);

        var options = Assert.IsType<PackageForIntakeOptions>(
            parsed.PackageForIntakeOptions);
        Assert.Equal("final.memmigration.zip", options.ArchivePath);
        Assert.Equal("output", options.OutputDirectory);
        Assert.Equal("work", options.WorkspacePath);
        Assert.Equal("/usr/bin/age", options.AgeCommand);
    }
}
