using Mem.Cli.Config;
using Mem.Localization;

namespace Mem.Cli.Tests.Config;

public sealed class CliOptionsTests
{
    [Fact]
    public void FromArgs_uses_an_explicit_supported_language()
    {
        var options = CliOptions.FromArgs(
            ["backups", "list", "--language", "de-DE"],
            environmentLanguageOverride: string.Empty);

        Assert.Equal(MemLanguage.German, options.Language);
        Assert.Null(options.LanguageError);
    }

    [Fact]
    public void FromArgs_rejects_an_unsupported_explicit_language()
    {
        var options = CliOptions.FromArgs(
            ["backups", "list", "--language", "fr"],
            environmentLanguageOverride: string.Empty);

        Assert.Equal(MemLanguage.English, options.Language);
        Assert.NotNull(options.LanguageError);
        Assert.Equal(
            CliLanguageErrorKind.UnsupportedValue,
            options.LanguageError!.Kind);
        Assert.Equal("fr", options.LanguageError.Value);
    }

    [Fact]
    public void FromArgs_rejects_a_missing_explicit_language_value()
    {
        var options = CliOptions.FromArgs(
            ["backups", "list", "--language", "--json"],
            environmentLanguageOverride: string.Empty);

        Assert.Equal(MemLanguage.English, options.Language);
        Assert.NotNull(options.LanguageError);
        Assert.Equal(
            CliLanguageErrorKind.MissingValue,
            options.LanguageError!.Kind);
    }

    [Fact]
    public void FromArgs_marks_the_retired_installer_token_option_for_rejection_without_reading_it()
    {
        var options = CliOptions.FromArgs(
            [
                "backups",
                "list",
                "--installer-token",
                "not-authority"
            ],
            environmentLanguageOverride: string.Empty);

        Assert.True(
            options.RetiredInstallerTokenOptionSpecified);
        Assert.Null(options.InstallerToken);
    }

    [Fact]
    public void FromArgs_ignores_the_retired_ambient_installer_token_environment_variable()
    {
        var previous = Environment.GetEnvironmentVariable("MEM_INSTALLER_TOKEN");

        try
        {
            Environment.SetEnvironmentVariable(
                "MEM_INSTALLER_TOKEN",
                "not-authority");

            var options = CliOptions.FromArgs(
                ["host", "status"],
                environmentLanguageOverride: string.Empty);

            Assert.False(
                options.RetiredInstallerTokenOptionSpecified);
            Assert.Null(options.InstallerToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "MEM_INSTALLER_TOKEN",
                previous);
        }
    }

    [Fact]
    public void FromArgs_marks_the_retired_agent_secret_option_for_rejection_without_reading_it()
    {
        var options = CliOptions.FromArgs(
            [
                "backups",
                "list",
                "--agent-secret",
                "not-authority",
                "--installer-token",
                "also-not-authority"
            ],
            environmentLanguageOverride: string.Empty);

        Assert.True(
            options.RetiredAgentSecretOptionSpecified);
        Assert.True(
            options.RetiredInstallerTokenOptionSpecified);
        Assert.Null(options.InstallerToken);
    }

    [Fact]
    public void FromArgs_uses_the_saved_language_when_no_higher_precedence_value_exists()
    {
        var options = CliOptions.FromArgs(
            ["backups", "list"],
            savedLanguage: MemLanguage.German,
            environmentLanguageOverride: string.Empty);

        Assert.Equal(MemLanguage.German, options.Language);
        Assert.Null(options.LanguageError);
    }

    [Fact]
    public void FromArgs_prefers_environment_language_over_saved_language()
    {
        var options = CliOptions.FromArgs(
            ["backups", "list"],
            savedLanguage: MemLanguage.German,
            environmentLanguageOverride: "en-NZ");

        Assert.Equal(MemLanguage.English, options.Language);
        Assert.Null(options.LanguageError);
    }

    [Fact]
    public void FromArgs_prefers_explicit_language_over_environment_and_saved_language()
    {
        var options = CliOptions.FromArgs(
            ["backups", "list", "--language", "en"],
            savedLanguage: MemLanguage.German,
            environmentLanguageOverride: "de-DE");

        Assert.Equal(MemLanguage.English, options.Language);
        Assert.Null(options.LanguageError);
    }

    [Fact]
    public void FromArgs_uses_the_selected_profile_server_when_no_higher_precedence_server_exists()
    {
        var options = CliOptions.FromArgs(
            ["backups", "list"],
            environmentLanguageOverride: string.Empty,
            profileServerUrl: "https://mem.example.internal",
            profileName: "home",
            environmentServerOverride: string.Empty);

        Assert.Equal(
            "https://mem.example.internal",
            options.HostAgentUrl);
        Assert.Equal("home", options.ProfileName);
    }

    [Fact]
    public void FromArgs_prefers_explicit_server_over_selected_profile_server()
    {
        var options = CliOptions.FromArgs(
            [
                "backups",
                "list",
                "--server",
                "https://override.example.internal"
            ],
            environmentLanguageOverride: string.Empty,
            profileServerUrl: "https://mem.example.internal",
            profileName: "home",
            environmentServerOverride: string.Empty);

        Assert.Equal(
            "https://override.example.internal",
            options.HostAgentUrl);
        Assert.Equal("home", options.ProfileName);
    }

    [Fact]
    public void FromArgs_accepts_the_legacy_host_agent_url_as_an_explicit_compatibility_override()
    {
        var options = CliOptions.FromArgs(
            [
                "backups",
                "list",
                "--host-agent-url",
                "http://localhost:7105"
            ],
            environmentLanguageOverride: string.Empty,
            profileServerUrl: "https://mem.example.internal",
            environmentServerOverride: string.Empty);

        Assert.Equal(
            "http://localhost:7105",
            options.HostAgentUrl);
    }
}
