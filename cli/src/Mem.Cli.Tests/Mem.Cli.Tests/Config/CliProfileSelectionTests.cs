using Mem.Cli.Config;
using Mem.Localization;

namespace Mem.Cli.Tests.Config;

public sealed class CliProfileSelectionTests
{
    [Fact]
    public void Resolve_uses_the_default_profile_when_no_explicit_profile_is_supplied()
    {
        var preferences = CreatePreferences();

        var selection = CliProfileSelection.Resolve(
            ["backups", "list"],
            preferences);

        Assert.True(selection.Succeeded);
        Assert.NotNull(selection.Profile);
        Assert.Equal("home", selection.Profile!.Name);
        Assert.Equal(
            "https://mem.example.internal",
            selection.Profile.ServerUrl);
        Assert.Equal(
            MemLanguage.German,
            selection.Profile.Language);
    }

    [Fact]
    public void Resolve_prefers_an_explicit_profile_over_the_default_profile()
    {
        var preferences = CreatePreferences();

        var selection = CliProfileSelection.Resolve(
            ["backups", "list", "--profile", "work"],
            preferences);

        Assert.True(selection.Succeeded);
        Assert.Equal("work", selection.Profile!.Name);
        Assert.Equal(
            "https://work.example.internal",
            selection.Profile.ServerUrl);
        Assert.Null(selection.Profile.Language);
    }

    [Fact]
    public void Resolve_rejects_a_missing_or_unknown_explicit_profile_without_constructing_transport()
    {
        var preferences = CreatePreferences();

        var missing = CliProfileSelection.Resolve(
            ["backups", "list", "--profile", "--json"],
            preferences);
        var unknown = CliProfileSelection.Resolve(
            ["backups", "list", "--profile", "missing"],
            preferences);

        Assert.False(missing.Succeeded);
        Assert.Equal(
            CliProfileSelectionErrorKind.MissingValue,
            missing.Error!.Kind);

        Assert.False(unknown.Succeeded);
        Assert.Equal(
            CliProfileSelectionErrorKind.ProfileNotFound,
            unknown.Error!.Kind);
    }

    [Fact]
    public void Resolve_returns_no_profile_when_no_default_or_explicit_profile_exists()
    {
        var selection = CliProfileSelection.Resolve(
            ["backups", "list"],
            CliPreferences.Empty);

        Assert.True(selection.Succeeded);
        Assert.Null(selection.Profile);
    }

    private static CliPreferences CreatePreferences() =>
        new(
            language: MemLanguage.English,
            defaultProfile: "home",
            profiles:
            [
                new CliProfile(
                    "home",
                    "https://mem.example.internal",
                    MemLanguage.German),
                new CliProfile(
                    "work",
                    "https://work.example.internal",
                    null)
            ]);
}
