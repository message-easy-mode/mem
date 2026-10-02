using System.Text.Json;
using Mem.Cli.Config;
using Mem.Localization;

namespace Mem.Cli.Tests.Config;

public sealed class CliPreferencesStoreTests
{
    [Fact]
    public void SaveLanguage_migrates_to_the_canonical_non_secret_v2_configuration()
    {
        using var directory = new TemporaryDirectory();
        var store = new CliPreferencesStore(directory.Path);

        var write = store.SaveLanguage(MemLanguage.German);

        Assert.True(write.Succeeded);
        Assert.True(File.Exists(store.FilePath));

        using var document = JsonDocument.Parse(
            File.ReadAllText(store.FilePath));

        Assert.Equal(
            2,
            document.RootElement
                .GetProperty("schemaVersion")
                .GetInt32());
        Assert.Equal(
            "de",
            document.RootElement
                .GetProperty("language")
                .GetString());
        Assert.True(
            document.RootElement
                .GetProperty("profiles")
                .GetArrayLength() == 0);

        AssertContainsNoSecretMaterial(
            File.ReadAllText(store.FilePath));

        AssertPrivateModes(store);
    }

    [Fact]
    public void Read_accepts_the_previous_language_only_schema_without_creating_a_profile()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);

        var path = Path.Combine(
            directory.Path,
            "config.json");

        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 1,
              "language": "de"
            }
            """);

        var store = new CliPreferencesStore(directory.Path);
        var result = store.Read();

        Assert.True(result.Succeeded);
        Assert.Equal(
            MemLanguage.German,
            result.Preferences.Language);
        Assert.Null(result.Preferences.DefaultProfile);
        Assert.Empty(result.Preferences.Profiles);
    }

    [Fact]
    public void CreateProfile_persists_only_canonical_non_secret_profile_data()
    {
        using var directory = new TemporaryDirectory();
        var store = new CliPreferencesStore(directory.Path);

        var write = store.CreateProfile(
            new CliProfile(
                " Home ",
                "https://MEM.example.internal/",
                MemLanguage.German));

        Assert.True(write.Succeeded);

        var read = store.Read();

        Assert.True(read.Succeeded);
        var profile = Assert.Single(
            read.Preferences.Profiles);

        Assert.Equal("home", profile.Name);
        Assert.Equal(
            "https://mem.example.internal",
            profile.ServerUrl);
        Assert.Equal(
            MemLanguage.German,
            profile.Language);

        using var document = JsonDocument.Parse(
            File.ReadAllText(store.FilePath));

        var serializedProfile = document.RootElement
            .GetProperty("profiles")[0];

        Assert.Equal(
            "home",
            serializedProfile
                .GetProperty("name")
                .GetString());
        Assert.Equal(
            "https://mem.example.internal",
            serializedProfile
                .GetProperty("serverUrl")
                .GetString());
        Assert.Equal(
            "de",
            serializedProfile
                .GetProperty("language")
                .GetString());

        AssertContainsNoSecretMaterial(
            File.ReadAllText(store.FilePath));
        AssertPrivateModes(store);
    }

    [Fact]
    public void Select_and_remove_default_profile_keeps_configuration_consistent()
    {
        using var directory = new TemporaryDirectory();
        var store = new CliPreferencesStore(directory.Path);

        Assert.True(store.CreateProfile(
            new CliProfile(
                "home",
                "https://mem.example.internal",
                null)).Succeeded);

        Assert.True(store.SetDefaultProfile(
            "home").Succeeded);

        var selected = store.Read();

        Assert.True(selected.Succeeded);
        Assert.Equal(
            "home",
            selected.Preferences.DefaultProfile);

        Assert.True(store.RemoveProfile(
            "home").Succeeded);

        var removed = store.Read();

        Assert.True(removed.Succeeded);
        Assert.Null(removed.Preferences.DefaultProfile);
        Assert.Empty(removed.Preferences.Profiles);
    }

    [Theory]
    [InlineData("not a profile")]
    [InlineData("contains_underscore")]
    [InlineData("-leading")]
    [InlineData("trailing-")]
    [InlineData("this-profile-name-is-way-too-long-for-the-limit")]
    public void CreateProfile_refuses_invalid_profile_names(
        string name)
    {
        using var directory = new TemporaryDirectory();
        var store = new CliPreferencesStore(directory.Path);

        var result = store.CreateProfile(
            new CliProfile(
                name,
                "https://mem.example.internal",
                null));

        Assert.False(result.Succeeded);
        Assert.Equal(
            CliPreferencesErrorKind.InvalidProfileName,
            result.Error!.Kind);
    }

    [Theory]
    [InlineData("http://mem.example.internal")]
    [InlineData("https://mem.example.internal/control-plane")]
    [InlineData("https://user:password@mem.example.internal")]
    [InlineData("https://mem.example.internal?token=must-not-be-configured")]
    public void CreateProfile_refuses_noncanonical_or_insecure_server_urls(
        string serverUrl)
    {
        using var directory = new TemporaryDirectory();
        var store = new CliPreferencesStore(directory.Path);

        var result = store.CreateProfile(
            new CliProfile(
                "home",
                serverUrl,
                null));

        Assert.False(result.Succeeded);
        Assert.Equal(
            CliPreferencesErrorKind.InvalidServerUrl,
            result.Error!.Kind);
    }

    [Fact]
    public void Read_refuses_an_invalid_or_unknown_language_without_returning_raw_configuration_content()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);

        var path = Path.Combine(
            directory.Path,
            "config.json");

        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 2,
              "language": "not-a-supported-language",
              "profiles": []
            }
            """);

        var store = new CliPreferencesStore(directory.Path);
        var result = store.Read();

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
        Assert.Equal(
            CliPreferencesErrorKind.InvalidLanguage,
            result.Error!.Kind);
        Assert.Null(result.Preferences.Language);
    }

    [Fact]
    public void Read_refuses_a_default_profile_that_is_not_present()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);

        var path = Path.Combine(
            directory.Path,
            "config.json");

        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 2,
              "defaultProfile": "missing",
              "profiles": []
            }
            """);

        var store = new CliPreferencesStore(directory.Path);
        var result = store.Read();

        Assert.False(result.Succeeded);
        Assert.Equal(
            CliPreferencesErrorKind.InvalidProfile,
            result.Error!.Kind);
    }

    [Fact]
    public void Read_treats_a_missing_configuration_file_as_an_empty_preference_store()
    {
        using var directory = new TemporaryDirectory();
        var store = new CliPreferencesStore(directory.Path);

        var result = store.Read();

        Assert.True(result.Succeeded);
        Assert.Null(result.Preferences.Language);
        Assert.Null(result.Preferences.DefaultProfile);
        Assert.Empty(result.Preferences.Profiles);
    }

    private static void AssertContainsNoSecretMaterial(
        string serialized)
    {
        Assert.DoesNotContain(
            "token",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "cookie",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "password",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "totp",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "recovery",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "credential",
            serialized,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "session",
            serialized,
            StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertPrivateModes(
        CliPreferencesStore store)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite,
            File.GetUnixFileMode(store.FilePath));

        Assert.Equal(
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute,
            File.GetUnixFileMode(store.DirectoryPath));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "mem-cli-tests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(
                    Path,
                    recursive: true);
            }
        }
    }
}
