namespace Mem.Cli.Config;

/// <summary>
/// Resolves an explicit --profile value or the configured default profile.
/// Resolution is local-only and never creates a normal HTTP client.
/// </summary>
public sealed record CliProfileSelection(
    CliProfile? Profile,
    CliProfileSelectionError? Error)
{
    public bool Succeeded => Error is null;

    public static CliProfileSelection Resolve(
        string[] args,
        CliPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(preferences);

        var profileOptionIndex = FindOptionIndex(
            args,
            "--profile");

        if (profileOptionIndex >= 0)
        {
            if (profileOptionIndex == args.Length - 1 ||
                args[profileOptionIndex + 1].StartsWith(
                    "--",
                    StringComparison.Ordinal))
            {
                return new CliProfileSelection(
                    null,
                    new CliProfileSelectionError(
                        CliProfileSelectionErrorKind.MissingValue));
            }

            return ResolveNamedProfile(
                args[profileOptionIndex + 1],
                preferences);
        }

        if (string.IsNullOrWhiteSpace(
                preferences.DefaultProfile))
        {
            return new CliProfileSelection(null, null);
        }

        // A persisted default is validated on configuration read. Retain an
        // explicit safe error in case an alternative store returns malformed
        // test or future data.
        var profile = preferences.FindProfile(
            preferences.DefaultProfile);

        return profile is null
            ? new CliProfileSelection(
                null,
                new CliProfileSelectionError(
                    CliProfileSelectionErrorKind.ProfileNotFound))
            : new CliProfileSelection(profile, null);
    }

    private static CliProfileSelection ResolveNamedProfile(
        string value,
        CliPreferences preferences)
    {
        if (!CliProfileValidator.TryNormalizeName(
                value,
                out var normalizedName))
        {
            return new CliProfileSelection(
                null,
                new CliProfileSelectionError(
                    CliProfileSelectionErrorKind.InvalidName));
        }

        var profile = preferences.FindProfile(
            normalizedName);

        return profile is null
            ? new CliProfileSelection(
                null,
                new CliProfileSelectionError(
                    CliProfileSelectionErrorKind.ProfileNotFound))
            : new CliProfileSelection(profile, null);
    }

    private static int FindOptionIndex(
        string[] args,
        string optionName)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (string.Equals(
                    args[index],
                    optionName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}

public sealed record CliProfileSelectionError(
    CliProfileSelectionErrorKind Kind);

public enum CliProfileSelectionErrorKind
{
    MissingValue,
    InvalidName,
    ProfileNotFound
}
