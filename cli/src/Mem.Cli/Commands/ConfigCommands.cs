using Mem.Cli.Config;
using Mem.Cli.Output;
using Mem.Localization;

namespace Mem.Cli.Commands;

/// <summary>
/// Handles non-secret local CLI preferences. This command group must never
/// construct the normal HTTP client or persist authentication material.
/// </summary>
public static class ConfigCommands
{
    public static Task<int> RunAsync(
        string[] args,
        string subcommand,
        CliOptions options,
        CliOutput output,
        ICliPreferencesStore preferencesStore)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(preferencesStore);

        return Task.FromResult(
            subcommand switch
            {
                "get" => Get(
                    args,
                    options,
                    output,
                    preferencesStore),
                "set" => Set(
                    args,
                    options,
                    output,
                    preferencesStore),
                _ => WriteUsage(
                    output)
            });
    }

    private static int Get(
        string[] args,
        CliOptions options,
        CliOutput output,
        ICliPreferencesStore preferencesStore)
    {
        var setting = GetSetting(args);

        return setting switch
        {
            "language" => GetLanguage(
                options,
                output,
                preferencesStore),
            "default-profile" => GetDefaultProfile(
                options,
                output,
                preferencesStore),
            _ => WriteUsage(output)
        };
    }

    private static int Set(
        string[] args,
        CliOptions options,
        CliOutput output,
        ICliPreferencesStore preferencesStore)
    {
        var setting = GetSetting(args);

        return setting switch
        {
            "language" => SetLanguage(
                args,
                options,
                output,
                preferencesStore),
            "default-profile" => SetDefaultProfile(
                args,
                options,
                output,
                preferencesStore),
            _ => WriteUsage(output)
        };
    }

    private static int GetLanguage(
        CliOptions options,
        CliOutput output,
        ICliPreferencesStore preferencesStore)
    {
        var result = preferencesStore.Read();

        if (!result.Succeeded)
        {
            return WriteConfigurationError(
                output,
                result.Error!.Kind);
        }

        var language = result.Preferences.Language;

        if (options.Json)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "ok",
                setting = "language",
                language = language.HasValue
                    ? MemLanguageResolver.GetCode(
                        language.Value)
                    : null,
                configured = language.HasValue,
                scope = "local"
            });
        }
        else
        {
            if (language.HasValue)
            {
                output.UseLanguage(language.Value);
            }

            output.WriteLocalizedLine(
                CliMessageKeys.ConfigLanguageTitle);
            output.WriteLocalizedLine(
                CliMessageKeys.ConfigLanguageValue,
                new Dictionary<string, object?>
                {
                    ["language"] = language.HasValue
                        ? MemLanguageResolver.GetCode(
                            language.Value)
                        : output.FormatLocalized(
                            CliMessageKeys.ConfigLanguageSystemDefault)
                });
        }

        return 0;
    }

    private static int SetLanguage(
        string[] args,
        CliOptions options,
        CliOutput output,
        ICliPreferencesStore preferencesStore)
    {
        var languageValue = GetValue(
            args,
            index: 3);

        if (!MemLanguageResolver.TryParse(
                languageValue,
                out var language))
        {
            return WriteError(
                output,
                "cli_config_invalid_language",
                "Supported language values are en and de.");
        }

        var result = preferencesStore.SaveLanguage(
            language);

        if (!result.Succeeded)
        {
            return WriteConfigurationError(
                output,
                result.Error!.Kind);
        }

        var code = MemLanguageResolver.GetCode(
            language);

        if (options.Json)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "ok",
                setting = "language",
                language = code,
                scope = "local"
            });
        }
        else
        {
            // The operator changed the language for the CLI. Apply the newly
            // saved language immediately so this very command confirms the change
            // in the selected language instead of the previously resolved one.
            output.UseLanguage(language);

            output.WriteLocalizedLine(
                CliMessageKeys.ConfigLanguageSaved,
                new Dictionary<string, object?>
                {
                    ["language"] = code
                });
            output.WriteLocalizedLine(
                CliMessageKeys.ConfigLanguageJsonContracts);
        }

        return 0;
    }

    private static int GetDefaultProfile(
        CliOptions options,
        CliOutput output,
        ICliPreferencesStore preferencesStore)
    {
        var result = preferencesStore.Read();

        if (!result.Succeeded)
        {
            return WriteConfigurationError(
                output,
                result.Error!.Kind);
        }

        var profile = result.Preferences.DefaultProfile;

        if (options.Json)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "ok",
                setting = "default-profile",
                profile,
                configured = profile is not null,
                scope = "local"
            });
        }
        else
        {
            output.WriteHumanLine(
                "MEM CLI default profile");
            output.WriteHumanLine(
                $"Profile: {profile ?? "not configured"}");
        }

        return 0;
    }

    private static int SetDefaultProfile(
        string[] args,
        CliOptions options,
        CliOutput output,
        ICliPreferencesStore preferencesStore)
    {
        var profileValue = GetValue(
            args,
            index: 3);

        if (!CliProfileValidator.TryNormalizeName(
                profileValue,
                out var profileName))
        {
            return WriteError(
                output,
                "cli_profile_invalid_name",
                CliProfileValidator.ProfileNameRulesDescription);
        }

        var result = preferencesStore.SetDefaultProfile(
            profileName);

        if (!result.Succeeded)
        {
            return WriteConfigurationError(
                output,
                result.Error!.Kind);
        }

        if (options.Json)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "ok",
                setting = "default-profile",
                profile = profileName,
                scope = "local"
            });
        }
        else
        {
            output.WriteHumanLine(
                $"MEM CLI default profile saved: {profileName}");
        }

        return 0;
    }

    private static string? GetSetting(
        string[] args) =>
        GetValue(args, index: 2);

    private static string? GetValue(
        string[] args,
        int index) =>
        args.Length > index &&
        !args[index].StartsWith(
            "--",
            StringComparison.Ordinal)
            ? args[index].ToLowerInvariant()
            : null;

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

        return WriteError(
            output,
            error,
            error switch
            {
                "cli_profile_invalid_name" =>
                    CliProfileValidator.ProfileNameRulesDescription,
                "cli_profile_invalid_server" =>
                    "Profile server must be an HTTPS control-plane URL, or a loopback HTTP development URL.",
                "cli_profile_exists" =>
                    "A local MEM CLI profile with that name already exists.",
                "cli_profile_not_found" =>
                    "The requested local MEM CLI profile was not found.",
                _ =>
                    "MEM CLI local configuration could not be read or written safely."
            });
    }

    private static int WriteError(
        CliOutput output,
        string error,
        string humanMessage)
    {
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
                humanMessage);
        }

        return 1;
    }

    private static int WriteUsage(
        CliOutput output)
    {
        output.WriteHumanErrorLine("Usage:");
        output.WriteHumanErrorLine(
            "  mem config get language [--json]");
        output.WriteHumanErrorLine(
            "  mem config set language <en|de> [--json]");
        output.WriteHumanErrorLine(
            "  mem config get default-profile [--json]");
        output.WriteHumanErrorLine(
            "  mem config set default-profile <name> [--json]");

        return 1;
    }
}
