using Mem.Localization;

namespace Mem.Cli.Config;

public sealed record CliOptions(
    string HostAgentUrl,
    string? InstallerToken,
    bool Json,
    MemLanguage Language = MemLanguage.English,
    CliLanguageError? LanguageError = null,
    bool RetiredAgentSecretOptionSpecified = false,
    bool RetiredInstallerTokenOptionSpecified = false,
    bool SecretMaterialOptionSpecified = false,
    string? ProfileName = null,
    string? DeviceCredential = null)
{
    /// <summary>
    /// Builds one invocation's options. --server/MEM_SERVER_URL are the
    /// operator-facing names. --host-agent-url/MEM_HOST_AGENT_URL remain
    /// temporary server-address compatibility aliases only. Installer-token
    /// operator authority is retired: normal control-plane commands use the
    /// stored named CLI device session established by mem login --device.
    /// </summary>
    public static CliOptions FromArgs(
        string[] args,
        MemLanguage? savedLanguage = null,
        string? environmentLanguageOverride = null,
        string? profileServerUrl = null,
        string? profileName = null,
        string? environmentServerOverride = null)
    {
        ArgumentNullException.ThrowIfNull(args);

        var hostAgentUrl = ResolveServerUrl(
            args,
            profileServerUrl,
            environmentServerOverride);

        var retiredInstallerTokenOptionSpecified = FindOptionIndex(
                args,
                "--installer-token") >= 0;

        // MEM_INSTALLER_TOKEN is intentionally ignored after the device-session
        // cutover. An ambient legacy environment variable must not regain
        // authority or break an otherwise valid named-device invocation.
        string? installerToken = null;

        var json = args.Any(argument =>
            string.Equals(
                argument,
                "--json",
                StringComparison.OrdinalIgnoreCase));

        var languageSelection = ResolveLanguage(
            args,
            savedLanguage,
            environmentLanguageOverride);

        return new CliOptions(
            HostAgentUrl: hostAgentUrl,
            InstallerToken: installerToken,
            Json: json,
            Language: languageSelection.Language,
            LanguageError: languageSelection.Error,
            RetiredAgentSecretOptionSpecified: FindOptionIndex(
                args,
                "--agent-secret") >= 0,
            RetiredInstallerTokenOptionSpecified:
                retiredInstallerTokenOptionSpecified,
            SecretMaterialOptionSpecified: ContainsSecretMaterialOption(args),
            ProfileName: profileName);
    }

    public static string? GetOptionValue(
        string[] args,
        string name)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(
                    args[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static string ResolveServerUrl(
        string[] args,
        string? profileServerUrl,
        string? environmentServerOverride)
    {
        var explicitServerUrl =
            GetOptionValue(args, "--server")
            ?? GetOptionValue(args, "--host-agent-url");

        if (!string.IsNullOrWhiteSpace(
                explicitServerUrl))
        {
            return explicitServerUrl;
        }

        var environmentServerUrl = environmentServerOverride
            ?? Environment.GetEnvironmentVariable("MEM_SERVER_URL")
            ?? Environment.GetEnvironmentVariable("MEM_HOST_AGENT_URL");

        if (!string.IsNullOrWhiteSpace(
                environmentServerUrl))
        {
            return environmentServerUrl;
        }

        if (!string.IsNullOrWhiteSpace(
                profileServerUrl))
        {
            return profileServerUrl;
        }

        return "http://localhost:7105";
    }

    private static CliLanguageSelection ResolveLanguage(
        string[] args,
        MemLanguage? savedLanguage,
        string? environmentLanguageOverride)
    {
        var languageOptionIndex = FindOptionIndex(
            args,
            "--language");

        if (languageOptionIndex >= 0)
        {
            if (languageOptionIndex == args.Length - 1 ||
                args[languageOptionIndex + 1].StartsWith(
                    "--",
                    StringComparison.Ordinal))
            {
                return new CliLanguageSelection(
                    MemLanguage.English,
                    new CliLanguageError(
                        CliLanguageErrorKind.MissingValue,
                        null));
            }

            return ResolveExplicitLanguage(
                args[languageOptionIndex + 1]);
        }

        var environmentLanguage = environmentLanguageOverride
            ?? Environment.GetEnvironmentVariable(
                "MEM_CLI_LANGUAGE");

        if (!string.IsNullOrWhiteSpace(
                environmentLanguage))
        {
            return ResolveExplicitLanguage(
                environmentLanguage);
        }

        if (savedLanguage.HasValue)
        {
            return new CliLanguageSelection(
                savedLanguage.Value,
                null);
        }

        return new CliLanguageSelection(
            MemLanguageResolver.ResolveSystemDefault(),
            null);
    }

    private static CliLanguageSelection ResolveExplicitLanguage(
        string value)
    {
        return MemLanguageResolver.TryParse(
            value,
            out var language)
            ? new CliLanguageSelection(
                language,
                null)
            : new CliLanguageSelection(
                MemLanguage.English,
                new CliLanguageError(
                    CliLanguageErrorKind.UnsupportedValue,
                    value));
    }

    private static bool ContainsSecretMaterialOption(string[] args)
    {
        string[] prohibitedOptions =
        [
            "--password",
            "--totp",
            "--totp-code",
            "--recovery-code",
            "--device-credential",
            "--bearer-token"
        ];

        return prohibitedOptions.Any(option =>
            FindOptionIndex(args, option) >= 0);
    }

    private static int FindOptionIndex(
        string[] args,
        string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(
                    args[index],
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private sealed record CliLanguageSelection(
        MemLanguage Language,
        CliLanguageError? Error);
}

public sealed record CliLanguageError(
    CliLanguageErrorKind Kind,
    string? Value);

public enum CliLanguageErrorKind
{
    MissingValue,
    UnsupportedValue
}
