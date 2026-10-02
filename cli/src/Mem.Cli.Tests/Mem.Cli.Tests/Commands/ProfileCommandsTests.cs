using System.Text.Json;
using Mem.Cli;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;

namespace Mem.Cli.Tests.Commands;

[Collection("Console output")]
public sealed class ProfileCommandsTests
{
    [Fact]
    public async Task Create_list_select_and_remove_profiles_use_only_local_non_secret_configuration()
    {
        using var directory = new TemporaryDirectory();
        var store = new CliPreferencesStore(directory.Path);
        var application = new CliApplication(
            _ => throw new InvalidOperationException(
                "Profiles must not construct the normal HTTP client."),
            store);

        var created = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                [
                    "profile",
                    "create",
                    "home",
                    "--server",
                    "https://mem.example.internal",
                    "--language",
                    "de",
                    "--json"
                ]));

        var selected = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["profile", "select", "home", "--json"]));

        var listed = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["profile", "list", "--json"]));

        var removed = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["profile", "remove", "home", "--json"]));

        Assert.Equal(0, created.ExitCode);
        Assert.Equal(0, selected.ExitCode);
        Assert.Equal(0, listed.ExitCode);
        Assert.Equal(0, removed.ExitCode);

        using var listDocument = JsonDocument.Parse(
            listed.StandardOutput);

        Assert.Equal(
            "home",
            listDocument.RootElement
                .GetProperty("defaultProfile")
                .GetString());
        Assert.Equal(
            "https://mem.example.internal",
            listDocument.RootElement
                .GetProperty("profiles")[0]
                .GetProperty("server")
                .GetString());

        var stored = File.ReadAllText(store.FilePath);

        Assert.DoesNotContain(
            "token",
            stored,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "cookie",
            stored,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "credential",
            stored,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "session",
            stored,
            StringComparison.OrdinalIgnoreCase);

        var finalPreferences = store.Read();

        Assert.True(finalPreferences.Succeeded);
        Assert.Empty(finalPreferences.Preferences.Profiles);
        Assert.Null(finalPreferences.Preferences.DefaultProfile);
    }

    [Fact]
    public async Task Create_rejects_a_non_loopback_http_server_url()
    {
        using var directory = new TemporaryDirectory();
        var application = new CliApplication(
            _ => throw new InvalidOperationException(
                "Profiles must not construct the normal HTTP client."),
            new CliPreferencesStore(directory.Path));

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                [
                    "profile",
                    "create",
                    "home",
                    "--server",
                    "http://mem.example.internal",
                    "--json"
                ]));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.StandardError);

        using var response = JsonDocument.Parse(
            captured.StandardOutput);

        Assert.Equal(
            "cli_profile_invalid_server",
            response.RootElement
                .GetProperty("error")
                .GetString());
    }

    [Fact]
    public async Task Create_rejects_an_over_length_profile_name_with_length_aware_human_message()
    {
        using var directory = new TemporaryDirectory();
        var application = new CliApplication(
            _ => throw new InvalidOperationException(
                "Profiles must not construct the normal HTTP client."),
            new CliPreferencesStore(directory.Path));

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                [
                    "profile",
                    "create",
                    "this-profile-name-is-way-too-long-for-the-limit",
                    "--server",
                    "http://127.0.0.1:7105"
                ]));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.StandardOutput);
        Assert.Contains(
            "MEM CLI profile names must be 1-32 characters and use only letters, numbers, and hyphens.",
            captured.StandardError);
    }

    [Fact]
    public async Task Config_default_profile_requires_an_existing_local_profile()
    {
        using var directory = new TemporaryDirectory();
        var application = new CliApplication(
            _ => throw new InvalidOperationException(
                "Config must not construct the normal HTTP client."),
            new CliPreferencesStore(directory.Path));

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                [
                    "config",
                    "set",
                    "default-profile",
                    "missing",
                    "--json"
                ]));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.StandardError);

        using var response = JsonDocument.Parse(
            captured.StandardOutput);

        Assert.Equal(
            "cli_profile_not_found",
            response.RootElement
                .GetProperty("error")
                .GetString());
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
