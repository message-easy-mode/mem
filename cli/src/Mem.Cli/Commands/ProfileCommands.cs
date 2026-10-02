using Mem.Cli.Config;
using Mem.Cli.Output;
using Mem.Localization;

namespace Mem.Cli.Commands;

/// <summary>
/// Handles local non-secret profile management. This command group never
/// constructs the normal HTTP client and does not authenticate a CLI session.
/// </summary>
public static class ProfileCommands
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
                "create" => Create(
                    args,
                    options,
                    output,
                    preferencesStore),
                "list" => List(
                    options,
                    output,
                    preferencesStore),
                "select" => Select(
                    args,
                    options,
                    output,
                    preferencesStore),
                "remove" => Remove(
                    args,
                    options,
                    output,
                    preferencesStore),
                _ => WriteUsage(output)
            });
    }

    private static int Create(
        string[] args,
        CliOptions options,
        CliOutput output,
        ICliPreferencesStore preferencesStore)
    {
        var profileName = GetRequiredArgument(
            args,
            index: 2);
        var serverUrl = GetRequiredOptionValue(
            args,
            "--server");

        if (!CliProfileValidator.TryNormalizeName(
                profileName,
                out var normalizedName))
        {
            return WriteError(
                output,
                "cli_profile_invalid_name",
                CliProfileValidator.ProfileNameRulesDescription);
        }

        if (!CliProfileValidator.TryNormalizeServerUrl(
                serverUrl,
                out var normalizedServerUrl))
        {
            return WriteError(
                output,
                "cli_profile_invalid_server",
                "Profile server must be an HTTPS control-plane URL, or a loopback HTTP development URL.");
        }

        var profileLanguage = GetProfileLanguage(
            args,
            out var invalidLanguage);

        if (invalidLanguage)
        {
            return WriteError(
                output,
                "cli_profile_invalid_language",
                "Supported language values are en and de.");
        }

        var result = preferencesStore.CreateProfile(
            new CliProfile(
                normalizedName,
                normalizedServerUrl,
                profileLanguage));

        if (!result.Succeeded)
        {
            return WritePreferencesError(
                output,
                result.Error!.Kind);
        }

        if (options.Json)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "ok",
                action = "created",
                profile = normalizedName,
                server = normalizedServerUrl,
                language = profileLanguage.HasValue
                    ? MemLanguageResolver.GetCode(
                        profileLanguage.Value)
                    : null,
                scope = "local"
            });
        }
        else
        {
            output.WriteHumanLine(
                $"MEM CLI profile created: {normalizedName}");
            output.WriteHumanLine(
                $"Server: {normalizedServerUrl}");
            output.WriteHumanLine(
                $"Language: {(profileLanguage.HasValue
                    ? MemLanguageResolver.GetCode(
                        profileLanguage.Value)
                    : "inherit local default")}");
            output.WriteHumanLine(
                "No credential or session was stored.");
        }

        return 0;
    }

    private static int List(
        CliOptions options,
        CliOutput output,
        ICliPreferencesStore preferencesStore)
    {
        var result = preferencesStore.Read();

        if (!result.Succeeded)
        {
            return WritePreferencesError(
                output,
                result.Error!.Kind);
        }

        var profiles = result.Preferences.Profiles
            .OrderBy(
                profile => profile.Name,
                StringComparer.Ordinal)
            .Select(profile => new
            {
                name = profile.Name,
                server = profile.ServerUrl,
                language = profile.Language.HasValue
                    ? MemLanguageResolver.GetCode(
                        profile.Language.Value)
                    : null,
                isDefault = string.Equals(
                    result.Preferences.DefaultProfile,
                    profile.Name,
                    StringComparison.OrdinalIgnoreCase)
            })
            .ToArray();

        if (options.Json)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "ok",
                defaultProfile = result.Preferences.DefaultProfile,
                profiles,
                scope = "local"
            });
        }
        else if (profiles.Length == 0)
        {
            output.WriteHumanLine(
                "No local MEM CLI profiles are configured.");
        }
        else
        {
            output.WriteHumanLine("MEM CLI local profiles");

            foreach (var profile in profiles)
            {
                output.WriteHumanLine(
                    $"{(profile.isDefault ? "* " : "  ")}{profile.name}");
                output.WriteHumanLine(
                    $"  Server: {profile.server}");
                output.WriteHumanLine(
                    $"  Language: {profile.language ?? "inherit local default"}");
            }
        }

        return 0;
    }

    private static int Select(
        string[] args,
        CliOptions options,
        CliOutput output,
        ICliPreferencesStore preferencesStore)
    {
        var profileName = GetRequiredArgument(
            args,
            index: 2);

        if (!CliProfileValidator.TryNormalizeName(
                profileName,
                out var normalizedName))
        {
            return WriteError(
                output,
                "cli_profile_invalid_name",
                CliProfileValidator.ProfileNameRulesDescription);
        }

        var result = preferencesStore.SetDefaultProfile(
            normalizedName);

        if (!result.Succeeded)
        {
            return WritePreferencesError(
                output,
                result.Error!.Kind);
        }

        if (options.Json)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "ok",
                action = "selected",
                profile = normalizedName,
                scope = "local"
            });
        }
        else
        {
            output.WriteHumanLine(
                $"MEM CLI default profile selected: {normalizedName}");
        }

        return 0;
    }

    private static int Remove(
        string[] args,
        CliOptions options,
        CliOutput output,
        ICliPreferencesStore preferencesStore)
    {
        var profileName = GetRequiredArgument(
            args,
            index: 2);

        if (!CliProfileValidator.TryNormalizeName(
                profileName,
                out var normalizedName))
        {
            return WriteError(
                output,
                "cli_profile_invalid_name",
                CliProfileValidator.ProfileNameRulesDescription);
        }

        var result = preferencesStore.RemoveProfile(
            normalizedName);

        if (!result.Succeeded)
        {
            return WritePreferencesError(
                output,
                result.Error!.Kind);
        }

        if (options.Json)
        {
            output.WriteJson(new
            {
                source = "mem-cli",
                status = "ok",
                action = "removed",
                profile = normalizedName,
                scope = "local"
            });
        }
        else
        {
            output.WriteHumanLine(
                $"MEM CLI profile removed: {normalizedName}");
            output.WriteHumanLine(
                "No credential or session was removed because profiles do not store them in this build.");
        }

        return 0;
    }

    private static MemLanguage? GetProfileLanguage(
        string[] args,
        out bool invalidLanguage)
    {
        invalidLanguage = false;

        var languageValue = GetRequiredOptionValue(
            args,
            "--language");

        if (languageValue is null)
        {
            return null;
        }

        if (!MemLanguageResolver.TryParse(
                languageValue,
                out var language))
        {
            invalidLanguage = true;
            return null;
        }

        return language;
    }

    private static string? GetRequiredArgument(
        string[] args,
        int index) =>
        args.Length > index &&
        !args[index].StartsWith(
            "--",
            StringComparison.Ordinal)
            ? args[index]
            : null;

    private static string? GetRequiredOptionValue(
        string[] args,
        string optionName)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(
                    args[index],
                    optionName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return index + 1 < args.Length &&
                !args[index + 1].StartsWith(
                    "--",
                    StringComparison.Ordinal)
                ? args[index + 1]
                : null;
        }

        return null;
    }

    private static int WritePreferencesError(
        CliOutput output,
        CliPreferencesErrorKind errorKind)
    {
        return errorKind switch
        {
            CliPreferencesErrorKind.InvalidProfileName =>
                WriteError(
                    output,
                    "cli_profile_invalid_name",
                    CliProfileValidator.ProfileNameRulesDescription),
            CliPreferencesErrorKind.InvalidServerUrl =>
                WriteError(
                    output,
                    "cli_profile_invalid_server",
                    "Profile server must be an HTTPS control-plane URL, or a loopback HTTP development URL."),
            CliPreferencesErrorKind.ProfileAlreadyExists =>
                WriteError(
                    output,
                    "cli_profile_exists",
                    "A local MEM CLI profile with that name already exists."),
            CliPreferencesErrorKind.ProfileNotFound =>
                WriteError(
                    output,
                    "cli_profile_not_found",
                    "The requested local MEM CLI profile was not found."),
            _ => WriteError(
                output,
                error: errorKind == CliPreferencesErrorKind.UnsupportedVersion
                    ? "cli_config_unsupported_version"
                    : "cli_config_unavailable",
                humanMessage:
                    "MEM CLI local configuration could not be read or written safely.")
        };
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
            output.WriteHumanErrorLine(humanMessage);
        }

        return 1;
    }

    private static int WriteUsage(
        CliOutput output)
    {
        output.WriteHumanErrorLine("Usage:");
        output.WriteHumanErrorLine(
            "  mem profile create <name> --server <url> [--language <en|de>] [--json]");
        output.WriteHumanErrorLine(
            "  mem profile list [--json]");
        output.WriteHumanErrorLine(
            "  mem profile select <name> [--json]");
        output.WriteHumanErrorLine(
            "  mem profile remove <name> [--json]");

        return 1;
    }
}
