using Mem.Cli.Clients;
using Mem.Cli.Commands;
using Mem.Cli.Config;
using Mem.Cli.Output;
using Mem.Localization;

namespace Mem.Cli;

public sealed class CliApplication
{
    private readonly Func<CliOptions, HostAgentClient> _hostAgentClientFactory;
    private readonly Func<CliOptions, ICliDeviceAuthorizationClient>
        _deviceAuthorizationClientFactory;
    private readonly Func<ICliDeviceCredentialStore>
        _deviceCredentialStoreFactory;
    private readonly Func<CliOptions, ICliDeviceSessionClient>
        _deviceSessionClientFactory;
    private readonly ICliPreferencesStore? _preferencesStore;

    public CliApplication(
        Func<CliOptions, HostAgentClient>? hostAgentClientFactory = null,
        ICliPreferencesStore? preferencesStore = null,
        Func<CliOptions, ICliDeviceAuthorizationClient>?
            deviceAuthorizationClientFactory = null,
        Func<ICliDeviceCredentialStore>?
            deviceCredentialStoreFactory = null,
        Func<CliOptions, ICliDeviceSessionClient>?
            deviceSessionClientFactory = null)
    {
        _hostAgentClientFactory = hostAgentClientFactory ??
            (static options => new HostAgentClient(options));
        _deviceAuthorizationClientFactory =
            deviceAuthorizationClientFactory ??
            (static options => new CliDeviceAuthorizationClient(options));
        _deviceCredentialStoreFactory =
            deviceCredentialStoreFactory ??
            CliDeviceCredentialStore.CreateDefault;
        _deviceSessionClientFactory =
            deviceSessionClientFactory ??
            (static options => new CliDeviceSessionClient(options));
        _preferencesStore = preferencesStore;
    }

    public async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var command = args.Length > 0
            ? args[0].ToLowerInvariant()
            : string.Empty;
        var subcommand = args.Length > 1
            ? args[1].ToLowerInvariant()
            : string.Empty;

        if (command is "--version" or "version")
        {
            CliVersion.Write(Console.Out);
            return 0;
        }

        // Auth is deliberately independent of local CLI profile/configuration.
        // Future recovery arming must remain usable even when ordinary local
        // preference state is missing or unreadable, and it must never fall
        // through to the normal HTTP client.
        if (command == "auth")
        {
            var authOptions = CliOptions.FromArgs(args);
            var authOutput = CliOutput.Create(authOptions);

            if (TryWriteOptionError(
                    authOptions,
                    authOutput,
                    out var authExitCode))
            {
                return authExitCode;
            }

            return await AuthCommands.RunAsync(
                args,
                subcommand,
                authOptions,
                authOutput);
        }

        var preferencesStore = _preferencesStore ??
            CliPreferencesStore.CreateDefault();
        var preferences = preferencesStore.Read();

        // Configuration and profile operations use only the global local
        // language preference. They must not require a selected profile and
        // must never construct the normal HTTP client.
        var localOptions = CliOptions.FromArgs(
            args,
            preferences.Preferences.Language);
        var localOutput = CliOutput.Create(localOptions);

        if (TryWriteOptionError(
                localOptions,
                localOutput,
                out var localOptionExitCode))
        {
            return localOptionExitCode;
        }

        if (!preferences.Succeeded)
        {
            return WriteConfigurationError(
                localOutput,
                preferences.Error!.Kind);
        }

        if (command == "config")
        {
            return await ConfigCommands.RunAsync(
                args,
                subcommand,
                localOptions,
                localOutput,
                preferencesStore);
        }

        if (command == "profile")
        {
            return await ProfileCommands.RunAsync(
                args,
                subcommand,
                localOptions,
                localOutput,
                preferencesStore);
        }

        var profileSelection = CliProfileSelection.Resolve(
            args,
            preferences.Preferences);

        if (!profileSelection.Succeeded)
        {
            return WriteProfileSelectionError(
                localOutput,
                profileSelection.Error!.Kind);
        }

        // Active profile language takes precedence over the global local
        // preference. CliOptions then applies explicit --language and
        // MEM_CLI_LANGUAGE overrides above this value. Resolve this before
        // root help as well, so a selected/default profile affects the
        // operator's human-facing CLI experience consistently.
        var selectedProfile = profileSelection.Profile;
        var options = CliOptions.FromArgs(
            args,
            selectedProfile?.Language ??
                preferences.Preferences.Language,
            profileServerUrl: selectedProfile?.ServerUrl,
            profileName: selectedProfile?.Name);
        var output = CliOutput.Create(options);

        if (TryWriteOptionError(
                options,
                output,
                out var optionExitCode))
        {
            return optionExitCode;
        }

        if (command == "login")
        {
            return await LoginCommands.RunAsync(
                args,
                options,
                output,
                _deviceAuthorizationClientFactory,
                _deviceCredentialStoreFactory());
        }

        if (command == "account")
        {
            return await AccountCommands.RunAccountAsync(
                args,
                subcommand,
                options,
                output,
                _deviceSessionClientFactory,
                _deviceCredentialStoreFactory());
        }

        if (command == "logout")
        {
            return await AccountCommands.RunLogoutAsync(
                options,
                output,
                _deviceSessionClientFactory,
                _deviceCredentialStoreFactory());
        }

        if (command is not "host" and
            not "stack" and
            not "backup" and
            not "backups" and
            not "restore" and
            not "restores" and
            not "maintenance")
        {
            output.WriteHelp();
            return 1;
        }

        var credentialSelection = await ResolveOperationalCredentialAsync(
            options,
            output,
            _deviceCredentialStoreFactory);

        if (!credentialSelection.Succeeded)
        {
            return 1;
        }

        try
        {
            using var hostAgentClient = _hostAgentClientFactory(
                options with
                {
                    DeviceCredential = credentialSelection.Credential
                });

            if (command == "host")
            {
                return await HostCommands.RunAsync(
                    subcommand,
                    options,
                    hostAgentClient);
            }

            if (command == "stack")
            {
                return await StackCommands.RunAsync(
                    args,
                    subcommand,
                    options,
                    hostAgentClient);
            }

            if (command is "backup" or "backups")
            {
                return await BackupCommands.RunAsync(
                    args,
                    subcommand,
                    options,
                    hostAgentClient);
            }


            if (command == "maintenance")
            {
                return await MaintenanceCommands.RunAsync(
                    args,
                    subcommand,
                    options,
                    hostAgentClient);
            }

            return await RestoreCommands.RunAsync(
                args,
                subcommand,
                options,
                hostAgentClient);
        }
        catch (InstallerAuthenticationException exception)
        {
            var errorCode = exception.FailureKind switch
            {
                InstallerAuthenticationFailureKind.MissingInstallerToken =>
                    "installer_token_required",
                InstallerAuthenticationFailureKind.InstallerTokenRejected =>
                    "installer_token_rejected",
                _ => throw new ArgumentOutOfRangeException()
            };

            if (options.Json)
            {
                output.WriteJson(new
                {
                    source = "mem-cli",
                    status = "error",
                    error = errorCode
                });
            }
            else
            {
                output.WriteLocalizedErrorLine(
                    exception.FailureKind switch
                    {
                        InstallerAuthenticationFailureKind.MissingInstallerToken =>
                            CliMessageKeys.ErrorInstallerTokenRequired,
                        InstallerAuthenticationFailureKind.InstallerTokenRejected =>
                            CliMessageKeys.ErrorInstallerTokenRejected,
                        _ => throw new ArgumentOutOfRangeException()
                    });
            }

            return 1;
        }
    }

    private static async Task<OperationalCredentialSelection>
        ResolveOperationalCredentialAsync(
            CliOptions options,
            CliOutput output,
            Func<ICliDeviceCredentialStore> credentialStoreFactory)
    {
        ArgumentNullException.ThrowIfNull(credentialStoreFactory);

        if (!CliDeviceCredentialKey.TryCreate(
                options.ProfileName,
                options.HostAgentUrl,
                out var key))
        {
            WriteOperationalCredentialError(
                output,
                "cli_profile_required",
                options.ProfileName,
                options.HostAgentUrl,
                "A named MEM CLI profile is required for control-plane commands.",
                "Run: mem profile create <name> --server <url>");
            return OperationalCredentialSelection.Failed();
        }

        var credentialStore = credentialStoreFactory();
        var readResult = await credentialStore.ReadAsync(key!);

        if (readResult.Succeeded)
        {
            return OperationalCredentialSelection.Success(
                readResult.Credential!);
        }

        var (error, message, hint) = readResult.Status switch
        {
            CliDeviceCredentialStoreStatus.NotFound => (
                "cli_device_login_required",
                $"No MEM CLI device session is stored for profile: {key!.ProfileName}",
                $"Run: mem login --device --profile {key.ProfileName}"),
            CliDeviceCredentialStoreStatus.Unavailable => (
                "cli_secret_store_unavailable",
                "The OS secret store is unavailable. MEM CLI cannot read the stored device session.",
                "Install and unlock a supported Secret Service keyring, then run mem login --device again."),
            CliDeviceCredentialStoreStatus.InvalidCredential => (
                "cli_device_credential_invalid",
                $"The stored MEM CLI device session for profile {key!.ProfileName} is invalid.",
                $"Run: mem logout --profile {key.ProfileName}, then mem login --device --profile {key.ProfileName}."),
            _ => (
                "cli_device_login_required",
                $"No MEM CLI device session is stored for profile: {key!.ProfileName}",
                $"Run: mem login --device --profile {key.ProfileName}")
        };

        WriteOperationalCredentialError(
            output,
            error,
            key!.ProfileName,
            key.ServerUrl,
            message,
            hint);

        return OperationalCredentialSelection.Failed();
    }

    private static void WriteOperationalCredentialError(
        CliOutput output,
        string error,
        string? profileName,
        string serverUrl,
        string message,
        string hint)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "error",
                error,
                profile = profileName,
                server = serverUrl
            });
            return;
        }

        output.WriteHumanErrorLine(message);
        output.WriteHumanErrorLine($"Server: {serverUrl}");
        output.WriteHumanErrorLine(hint);
    }

    private sealed record OperationalCredentialSelection(
        bool Succeeded,
        string? Credential)
    {
        public static OperationalCredentialSelection Success(
            string credential) =>
            new(true, credential);

        public static OperationalCredentialSelection Failed() =>
            new(false, null);
    }

    private static bool TryWriteOptionError(
        CliOptions options,
        CliOutput output,
        out int exitCode)
    {
        if (options.LanguageError is not null)
        {
            switch (options.LanguageError.Kind)
            {
                case CliLanguageErrorKind.MissingValue:
                    output.WriteLocalizedErrorLine(
                        CliMessageKeys.ErrorMissingLanguageValue);
                    break;

                case CliLanguageErrorKind.UnsupportedValue:
                    output.WriteLocalizedErrorLine(
                        CliMessageKeys.ErrorUnsupportedLanguage,
                        new Dictionary<string, object?>
                        {
                            ["language"] = options.LanguageError.Value ?? string.Empty
                        });
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            exitCode = 1;
            return true;
        }

        if (options.RetiredAgentSecretOptionSpecified)
        {
            if (options.Json)
            {
                output.WriteJson(new
                {
                    source = "mem-cli",
                    status = "error",
                    error = "agent_secret_retired"
                });
            }
            else
            {
                output.WriteLocalizedErrorLine(
                    CliMessageKeys.ErrorAgentSecretRetired);
            }

            exitCode = 1;
            return true;
        }

        if (options.RetiredInstallerTokenOptionSpecified)
        {
            if (options.Json)
            {
                output.WriteJson(new
                {
                    source = "mem-cli",
                    status = "error",
                    error = "installer_token_retired"
                });
            }
            else
            {
                output.WriteHumanErrorLine(
                    "The --installer-token option is retired for MEM CLI control-plane commands.");
                output.WriteHumanErrorLine(
                    "Run: mem login --device --profile <name>");
            }

            exitCode = 1;
            return true;
        }

        if (options.SecretMaterialOptionSpecified)
        {
            if (options.Json)
            {
                output.WriteJson(new
                {
                    source = "mem-cli",
                    status = "error",
                    error = "cli_secret_material_rejected"
                });
            }
            else
            {
                output.WriteHumanErrorLine(
                    "MEM CLI does not accept passwords, TOTP codes, recovery codes, bearer tokens, or device credentials through command-line options.");
                output.WriteHumanErrorLine(
                    "Use mem login --device for normal CLI authority. High-risk step-up remains server-controlled and must fail closed when no approved interactive flow is available.");
            }

            exitCode = 1;
            return true;
        }

        exitCode = 0;
        return false;
    }

    private static int WriteProfileSelectionError(
        CliOutput output,
        CliProfileSelectionErrorKind errorKind)
    {
        var error = errorKind switch
        {
            CliProfileSelectionErrorKind.MissingValue =>
                "cli_profile_missing_value",
            CliProfileSelectionErrorKind.InvalidName =>
                "cli_profile_invalid_name",
            CliProfileSelectionErrorKind.ProfileNotFound =>
                "cli_profile_not_found",
            _ => throw new ArgumentOutOfRangeException(
                nameof(errorKind),
                errorKind,
                null)
        };

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "error",
                error
            });
        }
        else
        {
            output.WriteHumanErrorLine(
                error switch
                {
                    "cli_profile_missing_value" =>
                        "A local MEM CLI profile name is required after --profile.",
                    "cli_profile_invalid_name" =>
                        CliProfileValidator.ProfileNameRulesDescription,
                    _ =>
                        "The requested local MEM CLI profile was not found."
                });
        }

        return 1;
    }

    private static int WriteConfigurationError(
        CliOutput output,
        CliPreferencesErrorKind errorKind)
    {
        var error = errorKind switch
        {
            CliPreferencesErrorKind.InvalidFormat =>
                "cli_config_invalid",
            CliPreferencesErrorKind.UnsupportedVersion =>
                "cli_config_unsupported_version",
            CliPreferencesErrorKind.InvalidLanguage =>
                "cli_config_invalid",
            CliPreferencesErrorKind.InvalidProfile =>
                "cli_config_invalid",
            CliPreferencesErrorKind.InvalidProfileName =>
                "cli_profile_invalid_name",
            CliPreferencesErrorKind.InvalidServerUrl =>
                "cli_profile_invalid_server",
            CliPreferencesErrorKind.ProfileAlreadyExists =>
                "cli_profile_exists",
            CliPreferencesErrorKind.ProfileNotFound =>
                "cli_profile_not_found",
            CliPreferencesErrorKind.UnsafePath =>
                "cli_config_unavailable",
            CliPreferencesErrorKind.Unavailable =>
                "cli_config_unavailable",
            _ => throw new ArgumentOutOfRangeException(
                nameof(errorKind),
                errorKind,
                null)
        };

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "error",
                error
            });
        }
        else
        {
            output.WriteHumanErrorLine(
                "MEM CLI local configuration could not be read safely.");
        }

        return 1;
    }
}
