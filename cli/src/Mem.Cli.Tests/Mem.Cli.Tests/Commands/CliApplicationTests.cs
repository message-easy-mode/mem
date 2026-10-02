using System.Net;
using System.Text.Json;
using Mem.Cli;
using Mem.Cli.Clients;
using Mem.Cli.Config;
using Mem.Cli.Tests.TestSupport;
using Mem.Localization;

namespace Mem.Cli.Tests.Commands;

[Collection("Console output")]
public sealed class CliApplicationTests
{
    [Fact]
    public async Task Version_is_reported_without_reading_profiles_or_constructing_clients()
    {
        var clientFactoryWasCalled = false;
        var application = new CliApplication(
            _ =>
            {
                clientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal HTTP client must not be constructed for version output.");
            },
            new ThrowingCliPreferencesStore());

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(["--version"]));

        Assert.Equal(0, captured.ExitCode);
        Assert.False(clientFactoryWasCalled);
        Assert.Contains("Message Easy Mode CLI", captured.StandardOutput);
        Assert.DoesNotContain("Matrix Easy Mode CLI", captured.StandardOutput);
        Assert.Contains("Version:", captured.StandardOutput);
        Assert.Contains("Command: mem", captured.StandardOutput);
        Assert.DoesNotContain("normal HTTP client", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Root_help_uses_current_message_easy_mode_branding_without_constructing_clients()
    {
        var clientFactoryWasCalled = false;
        var application = new CliApplication(
            _ =>
            {
                clientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal HTTP client must not be constructed for help.");
            },
            new InMemoryCliPreferencesStore());

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(["--help"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(clientFactoryWasCalled);
        Assert.Contains("Message Easy Mode CLI", captured.StandardOutput);
        Assert.DoesNotContain("Matrix Easy Mode CLI", captured.StandardOutput);
        Assert.Contains("Usage:", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Theory]
    [InlineData("host", "mem host status")]
    [InlineData("stack", "mem stack list")]
    [InlineData("backups", "mem backups list")]
    [InlineData("restores", "mem restores list")]
    public async Task Operational_group_help_is_side_effect_free_before_profile_auth_and_network_validation(
        string command,
        string expectedUsage)
    {
        var hostAgentClientFactoryWasCalled = false;
        var credentialStoreFactoryWasCalled = false;
        var application = new CliApplication(
            hostAgentClientFactory: _ =>
            {
                hostAgentClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The Host Agent client must not be constructed for group help.");
            },
            preferencesStore: new InMemoryCliPreferencesStore(),
            deviceCredentialStoreFactory: () =>
            {
                credentialStoreFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The credential store must not be constructed for group help.");
            });

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                [
                    command,
                    "--help",
                    "--profile",
                    "missing-profile",
                    "--server",
                    "https://192.0.2.10:8443"
                ]));

        Assert.Equal(0, captured.ExitCode);
        Assert.False(hostAgentClientFactoryWasCalled);
        Assert.False(credentialStoreFactoryWasCalled);
        Assert.Contains("Message Easy Mode CLI", captured.StandardOutput);
        Assert.Contains("Usage:", captured.StandardOutput);
        Assert.Contains("Commands:", captured.StandardOutput);
        Assert.Contains(expectedUsage, captured.StandardOutput);
        Assert.Contains("Options:", captured.StandardOutput);
        Assert.DoesNotContain("cli_profile_required", captured.StandardOutput);
        Assert.DoesNotContain("missing-profile", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Theory]
    [InlineData("backup", "mem backups list")]
    [InlineData("restore", "mem restores list")]
    public async Task Legacy_singular_group_alias_help_renders_canonical_release_group_without_clients(
        string command,
        string expectedUsage)
    {
        var hostAgentClientFactoryWasCalled = false;
        var credentialStoreFactoryWasCalled = false;
        var application = new CliApplication(
            hostAgentClientFactory: _ =>
            {
                hostAgentClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The Host Agent client must not be constructed for group help.");
            },
            preferencesStore: new InMemoryCliPreferencesStore(),
            deviceCredentialStoreFactory: () =>
            {
                credentialStoreFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The credential store must not be constructed for group help.");
            });

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync([command, "--help"]));

        Assert.Equal(0, captured.ExitCode);
        Assert.False(hostAgentClientFactoryWasCalled);
        Assert.False(credentialStoreFactoryWasCalled);
        Assert.Contains(expectedUsage, captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("help")]
    public async Task Operational_group_help_aliases_are_local_only(string helpToken)
    {
        var hostAgentClientFactoryWasCalled = false;
        var credentialStoreFactoryWasCalled = false;
        var application = new CliApplication(
            hostAgentClientFactory: _ =>
            {
                hostAgentClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The Host Agent client must not be constructed for group help.");
            },
            preferencesStore: new InMemoryCliPreferencesStore(),
            deviceCredentialStoreFactory: () =>
            {
                credentialStoreFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The credential store must not be constructed for group help.");
            });

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(["host", helpToken]));

        Assert.Equal(0, captured.ExitCode);
        Assert.False(hostAgentClientFactoryWasCalled);
        Assert.False(credentialStoreFactoryWasCalled);
        Assert.Contains("mem host status", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Auth_arm_recovery_is_rejected_without_constructing_the_normal_http_client()
    {
        var clientFactoryWasCalled = false;
        var application = new CliApplication(
            _ =>
            {
                clientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal HTTP client must not be constructed for auth.");
            },
            new ThrowingCliPreferencesStore());

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["auth", "arm-recovery", "--json"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(clientFactoryWasCalled);
        Assert.Contains("cli_recovery_arm_not_available", captured.StandardOutput);
        Assert.DoesNotContain("normal HTTP client", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Auth_arm_recovery_rejects_remote_options_without_constructing_the_normal_http_client()
    {
        var clientFactoryWasCalled = false;
        var application = new CliApplication(
            _ =>
            {
                clientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal HTTP client must not be constructed for recovery arming.");
            },
            new ThrowingCliPreferencesStore());

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                [
                    "auth",
                    "arm-recovery",
                    "--server",
                    "https://mem.example.internal",
                    "--json"
                ]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(clientFactoryWasCalled);
        Assert.Contains(
            "cli_recovery_arm_remote_options_rejected",
            captured.StandardOutput);
        Assert.DoesNotContain(
            "https://mem.example.internal",
            captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Secret_material_options_are_rejected_without_constructing_control_plane_clients()
    {
        var hostAgentClientFactoryWasCalled = false;
        var credentialStoreFactoryWasCalled = false;
        var application = new CliApplication(
            hostAgentClientFactory: _ =>
            {
                hostAgentClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The Host Agent client must not be constructed for secret material options.");
            },
            preferencesStore: new InMemoryCliPreferencesStore(),
            deviceCredentialStoreFactory: () =>
            {
                credentialStoreFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The credential store must not be constructed for secret material options.");
            });

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                [
                    "backups",
                    "delete",
                    "catalog-entry-1",
                    "--yes",
                    "--password",
                    "NeverLogThis123!",
                    "--json"
                ]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(hostAgentClientFactoryWasCalled);
        Assert.False(credentialStoreFactoryWasCalled);
        Assert.Contains("cli_secret_material_rejected", captured.StandardOutput);
        Assert.DoesNotContain("NeverLogThis123", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Unknown_root_command_is_rejected_without_constructing_the_normal_http_client()
    {
        var clientFactoryWasCalled = false;
        var application = new CliApplication(
            _ =>
            {
                clientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal HTTP client must not be constructed for help.");
            },
            new InMemoryCliPreferencesStore());

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(["unknown-command"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(clientFactoryWasCalled);
        Assert.Contains("Usage:", captured.StandardOutput);
        Assert.DoesNotContain("normal HTTP client", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Config_set_language_persists_local_preference_without_constructing_the_normal_http_client()
    {
        var clientFactoryWasCalled = false;
        using var directory = new TemporaryDirectory();
        var preferencesStore = new CliPreferencesStore(
            directory.Path);
        var application = new CliApplication(
            _ =>
            {
                clientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal HTTP client must not be constructed for config.");
            },
            preferencesStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["config", "set", "language", "de", "--json"]));

        Assert.Equal(0, captured.ExitCode);
        Assert.False(clientFactoryWasCalled);
        Assert.Empty(captured.StandardError);

        using var response = JsonDocument.Parse(
            captured.StandardOutput);

        Assert.Equal(
            "ok",
            response.RootElement
                .GetProperty("status")
                .GetString());
        Assert.Equal(
            "de",
            response.RootElement
                .GetProperty("language")
                .GetString());

        var preferences = preferencesStore.Read();

        Assert.True(preferences.Succeeded);
        Assert.Equal(
            MemLanguage.German,
            preferences.Preferences.Language);
    }

    [Fact]
    public async Task Profile_commands_are_local_and_do_not_construct_the_normal_http_client()
    {
        var clientFactoryWasCalled = false;
        using var directory = new TemporaryDirectory();
        var preferencesStore = new CliPreferencesStore(
            directory.Path);
        var application = new CliApplication(
            _ =>
            {
                clientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal HTTP client must not be constructed for profiles.");
            },
            preferencesStore);

        var create = await ConsoleOutputCapture.CaptureAsync(
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

        var select = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["profile", "select", "home", "--json"]));

        Assert.Equal(0, create.ExitCode);
        Assert.Equal(0, select.ExitCode);
        Assert.False(clientFactoryWasCalled);
        Assert.Empty(create.StandardError);
        Assert.Empty(select.StandardError);

        var preferences = preferencesStore.Read();

        Assert.True(preferences.Succeeded);
        Assert.Equal(
            "home",
            preferences.Preferences.DefaultProfile);
        Assert.Single(preferences.Preferences.Profiles);
    }

    [Fact]
    public async Task Selected_profile_language_localizes_help_without_constructing_the_normal_http_client()
    {
        var clientFactoryWasCalled = false;
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "home",
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        MemLanguage.German)
                ]));
        var application = new CliApplication(
            _ =>
            {
                clientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal HTTP client must not be constructed for help.");
            },
            preferencesStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(["unknown-command"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(clientFactoryWasCalled);
        Assert.Contains("Verwendung:", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Saved_language_localizes_help_without_constructing_the_normal_http_client()
    {
        var clientFactoryWasCalled = false;
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(MemLanguage.German));
        var application = new CliApplication(
            _ =>
            {
                clientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal HTTP client must not be constructed for help.");
            },
            preferencesStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(["unknown-command"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(clientFactoryWasCalled);
        Assert.Contains("Verwendung:", captured.StandardOutput);
        Assert.Contains("Normale Autorisierung", captured.StandardOutput);
        Assert.Contains("Befehlsnamen, Flags, Umgebungsvariablen", captured.StandardOutput);
        Assert.DoesNotContain("Normal authority:", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Config_set_language_human_output_uses_the_new_language()
    {
        var clientFactoryWasCalled = false;
        using var directory = new TemporaryDirectory();
        var preferencesStore = new CliPreferencesStore(
            directory.Path);
        var application = new CliApplication(
            _ =>
            {
                clientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal HTTP client must not be constructed for config.");
            },
            preferencesStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["config", "set", "language", "de"]));

        Assert.Equal(0, captured.ExitCode);
        Assert.False(clientFactoryWasCalled);
        Assert.Contains("Lokale CLI-Spracheinstellung gespeichert: de", captured.StandardOutput);
        Assert.Contains("JSON-Verträge bleiben Englisch.", captured.StandardOutput);
        Assert.DoesNotContain("Local CLI language preference saved", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }



    [Fact]
    public async Task Operational_commands_report_length_aware_profile_name_rules_for_invalid_profile_option()
    {
        var hostAgentClientFactoryWasCalled = false;
        var credentialStoreFactoryWasCalled = false;
        var application = new CliApplication(
            hostAgentClientFactory: _ =>
            {
                hostAgentClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The Host Agent client must not be constructed for an invalid profile option.");
            },
            preferencesStore: new InMemoryCliPreferencesStore(),
            deviceCredentialStoreFactory: () =>
            {
                credentialStoreFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The credential store must not be constructed for an invalid profile option.");
            });

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                [
                    "host",
                    "status",
                    "--profile",
                    "this-profile-name-is-way-too-long-for-the-limit"
                ]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(hostAgentClientFactoryWasCalled);
        Assert.False(credentialStoreFactoryWasCalled);
        Assert.Contains(
            CliProfileValidator.ProfileNameRulesDescription,
            captured.StandardError);
        Assert.Empty(captured.StandardOutput);
    }

    [Fact]
    public async Task Operational_commands_require_a_named_profile_before_constructing_secret_store_or_host_agent_client()
    {
        var hostAgentClientFactoryWasCalled = false;
        var credentialStoreFactoryWasCalled = false;
        var application = new CliApplication(
            hostAgentClientFactory: _ =>
            {
                hostAgentClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The Host Agent client must not be constructed without a selected profile.");
            },
            preferencesStore: new InMemoryCliPreferencesStore(),
            deviceCredentialStoreFactory: () =>
            {
                credentialStoreFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The credential store must not be constructed without a selected profile.");
            });

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["host", "status", "--json"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(hostAgentClientFactoryWasCalled);
        Assert.False(credentialStoreFactoryWasCalled);
        Assert.Empty(captured.StandardError);

        using var response = JsonDocument.Parse(
            captured.StandardOutput);

        Assert.Equal(
            "mem-cli",
            response.RootElement.GetProperty("source").GetString());
        Assert.Equal(
            "error",
            response.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "cli_profile_required",
            response.RootElement.GetProperty("error").GetString());
        Assert.Equal(
            "http://localhost:7105",
            response.RootElement.GetProperty("server").GetString());
    }

    [Fact]
    public async Task Operational_commands_report_secret_store_unavailable_as_safe_json_without_constructing_host_agent_client()
    {
        var hostAgentClientFactoryWasCalled = false;
        var credentialStore = new ApplicationCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Unavailable()
        };
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "home",
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        null)
                ]));
        var application = new CliApplication(
            hostAgentClientFactory: _ =>
            {
                hostAgentClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The Host Agent client must not be constructed when the secret store is unavailable.");
            },
            preferencesStore: preferencesStore,
            deviceCredentialStoreFactory: () => credentialStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["host", "status", "--json"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(hostAgentClientFactoryWasCalled);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("Host Agent client", captured.StandardOutput);

        using var response = JsonDocument.Parse(
            captured.StandardOutput);

        Assert.Equal(
            "cli_secret_store_unavailable",
            response.RootElement.GetProperty("error").GetString());
        Assert.Equal(
            "home",
            response.RootElement.GetProperty("profile").GetString());
        Assert.Equal(
            "https://mem.example.internal",
            response.RootElement.GetProperty("server").GetString());
    }

    [Fact]
    public async Task Operational_commands_report_invalid_local_device_session_as_safe_json_without_constructing_host_agent_client()
    {
        var hostAgentClientFactoryWasCalled = false;
        var credentialStore = new ApplicationCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.InvalidCredential()
        };
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "home",
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        null)
                ]));
        var application = new CliApplication(
            hostAgentClientFactory: _ =>
            {
                hostAgentClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The Host Agent client must not be constructed when the stored credential is invalid.");
            },
            preferencesStore: preferencesStore,
            deviceCredentialStoreFactory: () => credentialStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["backups", "list", "--json"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(hostAgentClientFactoryWasCalled);
        Assert.Empty(captured.StandardError);

        using var response = JsonDocument.Parse(
            captured.StandardOutput);

        Assert.Equal(
            "cli_device_credential_invalid",
            response.RootElement.GetProperty("error").GetString());
        Assert.Equal(
            "home",
            response.RootElement.GetProperty("profile").GetString());
        Assert.Equal(
            "https://mem.example.internal",
            response.RootElement.GetProperty("server").GetString());
    }

    [Fact]
    public async Task Explicit_installer_token_option_is_rejected_without_constructing_control_plane_clients()
    {
        var hostAgentClientFactoryWasCalled = false;
        var deviceSessionClientFactoryWasCalled = false;
        var credentialStore = new ApplicationCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8")
        };
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "home",
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        null)
                ]));

        var application = new CliApplication(
            hostAgentClientFactory: _ =>
            {
                hostAgentClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The Host Agent client must not be constructed for retired installer-token input.");
            },
            preferencesStore: preferencesStore,
            deviceCredentialStoreFactory: () => credentialStore,
            deviceSessionClientFactory: _ =>
            {
                deviceSessionClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The device session client must not be constructed for retired installer-token input.");
            });

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                [
                    "host",
                    "status",
                    "--installer-token",
                    "not-authority",
                    "--json"
                ]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(hostAgentClientFactoryWasCalled);
        Assert.False(deviceSessionClientFactoryWasCalled);
        Assert.Contains("installer_token_retired", captured.StandardOutput);
        Assert.DoesNotContain("not-authority", captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Operational_commands_use_stored_device_credential_as_bearer_without_installer_unlock()
    {
        const string credential = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

        CliOptions? clientOptions = null;
        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """
                {
                  "source": "control-plane",
                  "hostAgent": "ready",
                  "dockerReachable": true,
                  "runtimeNetworkName": "mem-gateway",
                  "npmReady": true,
                  "status": "ready"
                }
                """));
        var credentialStore = new ApplicationCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                credential)
        };
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "home",
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        null)
                ]));

        var application = new CliApplication(
            hostAgentClientFactory: options =>
            {
                clientOptions = options;
                return new HostAgentClient(
                    options,
                    handler);
            },
            preferencesStore: preferencesStore,
            deviceCredentialStoreFactory: () => credentialStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["host", "status", "--json"]));

        Assert.Equal(0, captured.ExitCode);
        Assert.NotNull(clientOptions);
        Assert.Equal("home", clientOptions!.ProfileName);
        Assert.Equal(
            "https://mem.example.internal",
            clientOptions.HostAgentUrl);
        Assert.Equal(
            credential,
            clientOptions.DeviceCredential);
        Assert.Equal("home", credentialStore.LastReadKey?.ProfileName);
        Assert.Equal(
            "https://mem.example.internal",
            credentialStore.LastReadKey?.ServerUrl);
        Assert.Empty(handler.InstallerUnlockRequests);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/internal/host-agent/status", request.PathAndQuery);
        Assert.Null(request.CookieHeader);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(credential, request.AuthorizationParameter);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Host_status_json_reports_rejected_device_session_without_fabricating_component_health()
    {
        const string credential = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """{ "status": "unauthenticated" }""",
                HttpStatusCode.Unauthorized));
        var credentialStore = new ApplicationCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                credential)
        };
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "home",
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        null)
                ]));

        var application = new CliApplication(
            hostAgentClientFactory: options =>
                new HostAgentClient(
                    options,
                    handler),
            preferencesStore: preferencesStore,
            deviceCredentialStoreFactory: () => credentialStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["host", "status", "--json"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.StandardError);

        using var response = JsonDocument.Parse(
            captured.StandardOutput);
        var root = response.RootElement;

        Assert.Equal(
            "mem-cli",
            root.GetProperty("source").GetString());
        Assert.Equal(
            "unauthenticated",
            root.GetProperty("status").GetString());
        Assert.Equal(
            "cli_device_session_rejected",
            root.GetProperty("error").GetString());
        Assert.Equal(
            "home",
            root.GetProperty("profile").GetString());
        Assert.Equal(
            "https://mem.example.internal",
            root.GetProperty("server").GetString());
        Assert.False(root.TryGetProperty("hostAgent", out _));
        Assert.False(root.TryGetProperty("dockerReachable", out _));
        Assert.False(root.TryGetProperty("runtimeNetworkName", out _));
        Assert.False(root.TryGetProperty("npmReady", out _));
        Assert.DoesNotContain(
            credential,
            captured.StandardOutput);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "/internal/host-agent/status",
            request.PathAndQuery);
        Assert.Equal(
            "Bearer",
            request.AuthorizationScheme);
        Assert.Equal(
            credential,
            request.AuthorizationParameter);
    }

    [Fact]
    public async Task Host_status_human_leads_with_device_session_rejection_instead_of_host_health()
    {
        const string credential = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8";

        var handler = new RecordingHttpMessageHandler(
            RecordingHttpMessageHandler.Json(
                """{ "status": "unauthenticated" }""",
                HttpStatusCode.Unauthorized));
        var credentialStore = new ApplicationCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                credential)
        };
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "home",
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        null)
                ]));

        var application = new CliApplication(
            hostAgentClientFactory: options =>
                new HostAgentClient(
                    options,
                    handler),
            preferencesStore: preferencesStore,
            deviceCredentialStoreFactory: () => credentialStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["host", "status"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.StandardOutput);
        Assert.StartsWith(
            "The control plane rejected the stored MEM CLI device session.",
            captured.StandardError);
        Assert.Contains(
            "Profile: home",
            captured.StandardError);
        Assert.Contains(
            "mem account show --profile home",
            captured.StandardError);
        Assert.Contains(
            "mem login --device --profile home",
            captured.StandardError);
        Assert.DoesNotContain(
            "HostAgent:",
            captured.StandardError);
        Assert.DoesNotContain(
            "Docker:",
            captured.StandardError);
        Assert.DoesNotContain(
            "NPM:",
            captured.StandardError);
        Assert.DoesNotContain(
            credential,
            captured.StandardError);
    }

    [Fact]
    public async Task Operational_commands_require_device_login_without_constructing_host_agent_client()
    {
        var hostAgentClientFactoryWasCalled = false;
        var credentialStore = new ApplicationCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.NotFound()
        };
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "home",
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        null)
                ]));

        var application = new CliApplication(
            hostAgentClientFactory: _ =>
            {
                hostAgentClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The Host Agent client must not be constructed without a stored device session.");
            },
            preferencesStore: preferencesStore,
            deviceCredentialStoreFactory: () => credentialStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["backups", "list", "--json"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.False(hostAgentClientFactoryWasCalled);
        Assert.Contains("cli_device_login_required", captured.StandardOutput);
        Assert.DoesNotContain(
            "The Host Agent client must not be constructed",
            captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }


    [Fact]
    public async Task Account_show_uses_device_session_client_without_constructing_the_normal_host_agent_client()
    {
        var normalClientFactoryWasCalled = false;
        var deviceSessionClientFactoryWasCalled = false;
        var credentialStore = new ApplicationCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8")
        };
        var deviceSessions = new ApplicationDeviceSessionClient
        {
            Result = new CliDeviceSessionStatusResult(
                Status: "authenticated",
                DisplayName: "admin",
                Roles: ["platform_owner"])
        };
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "home",
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        null)
                ]));

        var application = new CliApplication(
            hostAgentClientFactory: _ =>
            {
                normalClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal Host Agent client must not be constructed for account status.");
            },
            preferencesStore: preferencesStore,
            deviceCredentialStoreFactory: () => credentialStore,
            deviceSessionClientFactory: _ =>
            {
                deviceSessionClientFactoryWasCalled = true;
                return deviceSessions;
            });

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(["account", "show"]));

        Assert.Equal(0, captured.ExitCode);
        Assert.False(normalClientFactoryWasCalled);
        Assert.True(deviceSessionClientFactoryWasCalled);
        Assert.Contains("MEM CLI account", captured.StandardOutput);
        Assert.Contains("admin", captured.StandardOutput);
        Assert.DoesNotContain(
            "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8",
            captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Logout_revokes_stored_device_session_without_constructing_normal_host_agent_client()
    {
        var normalClientFactoryWasCalled = false;
        var deviceClientFactoryWasCalled = false;
        var deviceSessionClientFactoryWasCalled = false;
        var deviceSessions = new ApplicationDeviceSessionClient();
        var credentialStore = new ApplicationCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.Found(
                "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8")
        };
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "home",
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        null)
                ]));

        var application = new CliApplication(
            _ =>
            {
                normalClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal Host Agent client must not be constructed for logout.");
            },
            preferencesStore,
            _ =>
            {
                deviceClientFactoryWasCalled = true;
                return new ApplicationDeviceAuthorizationClient();
            },
            () => credentialStore,
            _ =>
            {
                deviceSessionClientFactoryWasCalled = true;
                return deviceSessions;
            });

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(["logout"]));

        Assert.Equal(0, captured.ExitCode);
        Assert.False(normalClientFactoryWasCalled);
        Assert.False(deviceClientFactoryWasCalled);
        Assert.True(deviceSessionClientFactoryWasCalled);
        Assert.True(deviceSessions.Revoked);
        Assert.Equal(
            "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8",
            deviceSessions.LastRevokedCredential);
        Assert.True(credentialStore.Deleted);
        Assert.Contains("Removed local", captured.StandardOutput);
        Assert.Contains("Server-side MEM CLI device session revoked", captured.StandardOutput);
        Assert.DoesNotContain(
            "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8",
            captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }

    [Fact]
    public async Task Device_login_uses_the_device_authorization_client_without_constructing_the_normal_host_agent_client()
    {
        var normalClientFactoryWasCalled = false;
        var deviceClientFactoryWasCalled = false;
        var deviceAuthorizations = new ApplicationDeviceAuthorizationClient
        {
            StartResult = new CliDeviceAuthorizationStartResult(
                Status: "authorization_started",
                AuthorizationId: Guid.NewGuid(),
                UserCode: "ABCD-EFGH",
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddMinutes(5),
                BrowserApprovalUrl: "https://mem.example.internal/cli/authorize"),
            PollResult = new CliDeviceAuthorizationPollResult(
                Status: "authorized",
                DeviceCredential: "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8")
        };
        var credentialStore = new ApplicationCredentialStore();
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "home",
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        null)
                ]));

        var application = new CliApplication(
            _ =>
            {
                normalClientFactoryWasCalled = true;
                throw new InvalidOperationException(
                    "The normal Host Agent client must not be constructed for device login.");
            },
            preferencesStore,
            _ =>
            {
                deviceClientFactoryWasCalled = true;
                return deviceAuthorizations;
            },
            () => credentialStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(["login", "--device"]));

        Assert.Equal(0, captured.ExitCode);
        Assert.False(normalClientFactoryWasCalled);
        Assert.True(deviceClientFactoryWasCalled);
        Assert.True(credentialStore.Stored);
        Assert.Contains("stored securely", captured.StandardOutput);
        Assert.DoesNotContain(
            "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8",
            captured.StandardOutput);
        Assert.Empty(captured.StandardError);
    }


    [Fact]
    public async Task Operational_json_errors_remain_stable_english_under_german_profile()
    {
        var credentialStore = new ApplicationCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.NotFound()
        };
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: "heim",
                profiles:
                [
                    new CliProfile(
                        "heim",
                        "https://mem.example.internal",
                        MemLanguage.German)
                ]));
        var application = new CliApplication(
            hostAgentClientFactory: _ => throw new InvalidOperationException(
                "The Host Agent client must not be constructed without a stored device session."),
            preferencesStore: preferencesStore,
            deviceCredentialStoreFactory: () => credentialStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                ["backups", "list", "--json"]));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.StandardError);
        Assert.DoesNotContain("Geräte", captured.StandardOutput);
        Assert.DoesNotContain("Anmeldung", captured.StandardOutput);
        Assert.DoesNotContain("Run:", captured.StandardOutput);

        using var response = JsonDocument.Parse(
            captured.StandardOutput);

        Assert.Equal(
            "mem-cli",
            response.RootElement.GetProperty("source").GetString());
        Assert.Equal(
            "error",
            response.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "cli_device_login_required",
            response.RootElement.GetProperty("error").GetString());
        Assert.Equal(
            "heim",
            response.RootElement.GetProperty("profile").GetString());
        Assert.Equal(
            "https://mem.example.internal",
            response.RootElement.GetProperty("server").GetString());
    }

    [Fact]
    public async Task Hidden_legacy_server_alias_still_resolves_without_appearing_in_release_help()
    {
        var credentialStore = new ApplicationCredentialStore
        {
            ReadResult = CliDeviceCredentialReadResult.NotFound()
        };
        var preferencesStore = new InMemoryCliPreferencesStore(
            new CliPreferences(
                language: MemLanguage.English,
                defaultProfile: null,
                profiles:
                [
                    new CliProfile(
                        "home",
                        "https://mem.example.internal",
                        null)
                ]));
        var application = new CliApplication(
            hostAgentClientFactory: _ => throw new InvalidOperationException(
                "The Host Agent client must not be constructed without a stored device session."),
            preferencesStore: preferencesStore,
            deviceCredentialStoreFactory: () => credentialStore);

        var captured = await ConsoleOutputCapture.CaptureAsync(
            () => application.RunAsync(
                [
                    "host",
                    "status",
                    "--profile",
                    "home",
                    "--host-agent-url",
                    "http://127.0.0.1:7105",
                    "--json"
                ]));

        Assert.Equal(1, captured.ExitCode);
        Assert.Empty(captured.StandardError);

        using var response = JsonDocument.Parse(
            captured.StandardOutput);

        Assert.Equal(
            "cli_device_login_required",
            response.RootElement.GetProperty("error").GetString());
        Assert.Equal(
            "http://127.0.0.1:7105",
            response.RootElement.GetProperty("server").GetString());
    }

    private sealed class ApplicationDeviceAuthorizationClient :
        ICliDeviceAuthorizationClient
    {
        public CliDeviceAuthorizationStartResult StartResult { get; set; } =
            new("authorization_unavailable");

        public CliDeviceAuthorizationPollResult PollResult { get; set; } =
            new("authorization_unavailable");

        public Task<CliDeviceAuthorizationStartResult> StartAsync(
            string verifierChallenge,
            string deviceLabel,
            CancellationToken ct = default) =>
            Task.FromResult(StartResult);

        public Task<CliDeviceAuthorizationPollResult> PollAsync(
            Guid authorizationId,
            string verifier,
            CancellationToken ct = default) =>
            Task.FromResult(PollResult);

        public void Dispose()
        {
        }
    }

    private sealed class ApplicationDeviceSessionClient :
        ICliDeviceSessionClient
    {
        public CliDeviceSessionStatusResult Result { get; set; } =
            new("unavailable");

        public Task<CliDeviceSessionStatusResult> GetCurrentAsync(
            string deviceCredential,
            CancellationToken ct = default) =>
            Task.FromResult(Result);

        public bool Revoked { get; private set; }

        public string? LastRevokedCredential { get; private set; }

        public Task<CliDeviceSessionRevocationResult> RevokeCurrentAsync(
            string deviceCredential,
            CancellationToken ct = default)
        {
            Revoked = true;
            LastRevokedCredential = deviceCredential;

            return Task.FromResult(
                new CliDeviceSessionRevocationResult("revoked"));
        }

        public void Dispose()
        {
        }
    }

    private sealed class ApplicationCredentialStore :
        ICliDeviceCredentialStore
    {
        public bool Stored { get; private set; }

        public bool Deleted { get; private set; }

        public CliDeviceCredentialReadResult ReadResult { get; set; } =
            CliDeviceCredentialReadResult.NotFound();

        public CliDeviceCredentialKey? LastReadKey { get; private set; }

        public Task<CliDeviceCredentialStoreAvailability> CheckAvailableAsync(
            CancellationToken ct = default) =>
            Task.FromResult(
                CliDeviceCredentialStoreAvailability.AvailableResult());

        public Task<CliDeviceCredentialReadResult> ReadAsync(
            CliDeviceCredentialKey key,
            CancellationToken ct = default)
        {
            LastReadKey = key;

            return Task.FromResult(ReadResult);
        }

        public Task<CliDeviceCredentialWriteResult> StoreAsync(
            CliDeviceCredentialKey key,
            string credential,
            CancellationToken ct = default)
        {
            Stored = true;

            return Task.FromResult(
                CliDeviceCredentialWriteResult.Success());
        }

        public Task<CliDeviceCredentialDeleteResult> DeleteAsync(
            CliDeviceCredentialKey key,
            CancellationToken ct = default)
        {
            Deleted = true;

            return Task.FromResult(
                CliDeviceCredentialDeleteResult.Success());
        }
    }

    private sealed class InMemoryCliPreferencesStore : ICliPreferencesStore
    {
        private CliPreferences _preferences;

        public InMemoryCliPreferencesStore(
            CliPreferences? preferences = null)
        {
            _preferences = preferences ??
                CliPreferences.Empty;
        }

        public CliPreferencesReadResult Read() =>
            CliPreferencesReadResult.Success(
                _preferences);

        public CliPreferencesWriteResult SaveLanguage(
            MemLanguage language)
        {
            _preferences = new CliPreferences(
                language,
                _preferences.DefaultProfile,
                _preferences.Profiles);

            return CliPreferencesWriteResult.Success();
        }

        public CliPreferencesWriteResult CreateProfile(
            CliProfile profile) =>
            throw new InvalidOperationException(
                "This test store does not create profiles.");

        public CliPreferencesWriteResult SetDefaultProfile(
            string profileName) =>
            throw new InvalidOperationException(
                "This test store does not select profiles.");

        public CliPreferencesWriteResult RemoveProfile(
            string profileName) =>
            throw new InvalidOperationException(
                "This test store does not remove profiles.");
    }

    private sealed class ThrowingCliPreferencesStore : ICliPreferencesStore
    {
        public CliPreferencesReadResult Read() =>
            throw new InvalidOperationException(
                "Auth must not read ordinary CLI preferences.");

        public CliPreferencesWriteResult SaveLanguage(
            MemLanguage language) =>
            throw new InvalidOperationException(
                "Auth must not write ordinary CLI preferences.");

        public CliPreferencesWriteResult CreateProfile(
            CliProfile profile) =>
            throw new InvalidOperationException(
                "Auth must not write ordinary CLI profiles.");

        public CliPreferencesWriteResult SetDefaultProfile(
            string profileName) =>
            throw new InvalidOperationException(
                "Auth must not write ordinary CLI profiles.");

        public CliPreferencesWriteResult RemoveProfile(
            string profileName) =>
            throw new InvalidOperationException(
                "Auth must not write ordinary CLI profiles.");
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
