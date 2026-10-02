using System.Text.Json;
using System.Text.Json.Serialization;
using Mem.Localization;

namespace Mem.Cli.Config;

/// <summary>
/// Persists non-secret local CLI preferences and profiles. This store must
/// never contain installer tokens, cookies, named-device credentials,
/// passwords, TOTP data, recovery codes, raw control-plane responses, or
/// runtime/session metadata.
/// </summary>
public interface ICliPreferencesStore
{
    CliPreferencesReadResult Read();

    CliPreferencesWriteResult SaveLanguage(MemLanguage language);

    CliPreferencesWriteResult CreateProfile(CliProfile profile);

    CliPreferencesWriteResult SetDefaultProfile(string profileName);

    CliPreferencesWriteResult RemoveProfile(string profileName);
}

/// <summary>
/// A local, non-secret control-plane profile. Credentials are deliberately
/// absent; later device-session work uses a separate secure credential store.
/// </summary>
public sealed record CliProfile(
    string Name,
    string ServerUrl,
    MemLanguage? Language);

public sealed record CliPreferences
{
    public CliPreferences(
        MemLanguage? language,
        string? defaultProfile = null,
        IReadOnlyList<CliProfile>? profiles = null)
    {
        Language = language;
        DefaultProfile = defaultProfile;
        Profiles = (profiles ?? Array.Empty<CliProfile>()).ToArray();
    }

    public MemLanguage? Language { get; }

    public string? DefaultProfile { get; }

    public IReadOnlyList<CliProfile> Profiles { get; }

    public static CliPreferences Empty { get; } =
        new(language: null);

    public CliProfile? FindProfile(string profileName) =>
        Profiles.SingleOrDefault(
            profile => string.Equals(
                profile.Name,
                profileName,
                StringComparison.OrdinalIgnoreCase));
}

public sealed record CliPreferencesReadResult(
    CliPreferences Preferences,
    CliPreferencesError? Error)
{
    public bool Succeeded => Error is null;

    public static CliPreferencesReadResult Success(
        CliPreferences preferences) =>
        new(preferences, null);

    public static CliPreferencesReadResult Failure(
        CliPreferencesErrorKind errorKind) =>
        new(CliPreferences.Empty, new CliPreferencesError(errorKind));
}

public sealed record CliPreferencesWriteResult(
    CliPreferencesError? Error)
{
    public bool Succeeded => Error is null;

    public static CliPreferencesWriteResult Success() =>
        new((CliPreferencesError?)null);

    public static CliPreferencesWriteResult Failure(
        CliPreferencesErrorKind errorKind) =>
        new(new CliPreferencesError(errorKind));
}

public sealed record CliPreferencesError(
    CliPreferencesErrorKind Kind);

public enum CliPreferencesErrorKind
{
    Unavailable,
    InvalidFormat,
    UnsupportedVersion,
    InvalidLanguage,
    InvalidProfile,
    InvalidProfileName,
    InvalidServerUrl,
    ProfileAlreadyExists,
    ProfileNotFound,
    UnsafePath
}

/// <summary>
/// Shared validation for local profile names and server URLs. Profiles never
/// carry credentials. A server address must be HTTPS, except for an explicit
/// loopback HTTP development endpoint.
/// </summary>
public static class CliProfileValidator
{
    public const int MaximumProfileNameLength = 32;

    public const string ProfileNameRulesDescription =
        "MEM CLI profile names must be 1-32 characters and use only letters, " +
        "numbers, and hyphens.";

    public static bool TryNormalizeName(
        string? value,
        out string normalizedName)
    {
        normalizedName = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim().ToLowerInvariant();

        if (candidate.Length > MaximumProfileNameLength ||
            !IsAlphaNumeric(candidate[0]) ||
            !IsAlphaNumeric(candidate[^1]))
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!IsAlphaNumeric(character) &&
                character != '-')
            {
                return false;
            }
        }

        normalizedName = candidate;
        return true;
    }

    public static bool TryNormalizeServerUrl(
        string? value,
        out string normalizedServerUrl)
    {
        normalizedServerUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(
                value.Trim(),
                UriKind.Absolute,
                out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(
                uri.AbsolutePath,
                "/",
                StringComparison.Ordinal))
        {
            return false;
        }

        var isHttps = string.Equals(
            uri.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);

        var isLoopbackHttp = string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) &&
            uri.IsLoopback;

        if (!isHttps && !isLoopbackHttp)
        {
            return false;
        }

        normalizedServerUrl = uri.GetLeftPart(
            UriPartial.Authority);

        return true;
    }

    private static bool IsAlphaNumeric(char value) =>
        value is >= 'a' and <= 'z' ||
        value is >= '0' and <= '9';
}

/// <summary>
/// JSON-backed local preference store. The file contains only language/profile
/// data, is written atomically, and receives restrictive Unix file modes where
/// the platform supports them.
/// </summary>
public sealed class CliPreferencesStore : ICliPreferencesStore
{
    private const int LegacySchemaVersion = 1;
    private const int CurrentSchemaVersion = 2;
    private const string FileName = "config.json";
    private const int MaximumProfileCount = 32;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _directoryPath;
    private readonly string _filePath;

    public CliPreferencesStore(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException(
                "A local CLI configuration directory is required.",
                nameof(directoryPath));
        }

        _directoryPath = Path.GetFullPath(directoryPath);
        _filePath = Path.Combine(
            _directoryPath,
            FileName);
    }

    public string DirectoryPath => _directoryPath;

    public string FilePath => _filePath;

    public static CliPreferencesStore CreateDefault() =>
        new(ResolveDefaultDirectoryPath());

    public CliPreferencesReadResult Read()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return CliPreferencesReadResult.Success(
                    CliPreferences.Empty);
            }

            if (IsSymbolicLink(new FileInfo(_filePath)))
            {
                return CliPreferencesReadResult.Failure(
                    CliPreferencesErrorKind.UnsafePath);
            }

            using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            var document = JsonSerializer.Deserialize<CliPreferencesDocument>(
                stream,
                SerializerOptions);

            if (document is null)
            {
                return CliPreferencesReadResult.Failure(
                    CliPreferencesErrorKind.InvalidFormat);
            }

            return ReadDocument(document);
        }
        catch (JsonException)
        {
            return CliPreferencesReadResult.Failure(
                CliPreferencesErrorKind.InvalidFormat);
        }
        catch (IOException)
        {
            return CliPreferencesReadResult.Failure(
                CliPreferencesErrorKind.Unavailable);
        }
        catch (UnauthorizedAccessException)
        {
            return CliPreferencesReadResult.Failure(
                CliPreferencesErrorKind.Unavailable);
        }
        catch (NotSupportedException)
        {
            return CliPreferencesReadResult.Failure(
                CliPreferencesErrorKind.Unavailable);
        }
    }

    public CliPreferencesWriteResult SaveLanguage(
        MemLanguage language)
    {
        var existing = Read();

        if (!existing.Succeeded)
        {
            return CliPreferencesWriteResult.Failure(
                existing.Error!.Kind);
        }

        return Write(
            new CliPreferences(
                language,
                existing.Preferences.DefaultProfile,
                existing.Preferences.Profiles));
    }

    public CliPreferencesWriteResult CreateProfile(
        CliProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (!CliProfileValidator.TryNormalizeName(
                profile.Name,
                out var normalizedName))
        {
            return CliPreferencesWriteResult.Failure(
                CliPreferencesErrorKind.InvalidProfileName);
        }

        if (!CliProfileValidator.TryNormalizeServerUrl(
                profile.ServerUrl,
                out var normalizedServerUrl))
        {
            return CliPreferencesWriteResult.Failure(
                CliPreferencesErrorKind.InvalidServerUrl);
        }

        var existing = Read();

        if (!existing.Succeeded)
        {
            return CliPreferencesWriteResult.Failure(
                existing.Error!.Kind);
        }

        if (existing.Preferences.FindProfile(
                normalizedName) is not null)
        {
            return CliPreferencesWriteResult.Failure(
                CliPreferencesErrorKind.ProfileAlreadyExists);
        }

        var profiles = existing.Preferences.Profiles
            .Append(new CliProfile(
                normalizedName,
                normalizedServerUrl,
                profile.Language))
            .OrderBy(
                value => value.Name,
                StringComparer.Ordinal)
            .ToArray();

        return Write(
            new CliPreferences(
                existing.Preferences.Language,
                existing.Preferences.DefaultProfile,
                profiles));
    }

    public CliPreferencesWriteResult SetDefaultProfile(
        string profileName)
    {
        if (!CliProfileValidator.TryNormalizeName(
                profileName,
                out var normalizedName))
        {
            return CliPreferencesWriteResult.Failure(
                CliPreferencesErrorKind.InvalidProfileName);
        }

        var existing = Read();

        if (!existing.Succeeded)
        {
            return CliPreferencesWriteResult.Failure(
                existing.Error!.Kind);
        }

        if (existing.Preferences.FindProfile(
                normalizedName) is null)
        {
            return CliPreferencesWriteResult.Failure(
                CliPreferencesErrorKind.ProfileNotFound);
        }

        return Write(
            new CliPreferences(
                existing.Preferences.Language,
                normalizedName,
                existing.Preferences.Profiles));
    }

    public CliPreferencesWriteResult RemoveProfile(
        string profileName)
    {
        if (!CliProfileValidator.TryNormalizeName(
                profileName,
                out var normalizedName))
        {
            return CliPreferencesWriteResult.Failure(
                CliPreferencesErrorKind.InvalidProfileName);
        }

        var existing = Read();

        if (!existing.Succeeded)
        {
            return CliPreferencesWriteResult.Failure(
                existing.Error!.Kind);
        }

        if (existing.Preferences.FindProfile(
                normalizedName) is null)
        {
            return CliPreferencesWriteResult.Failure(
                CliPreferencesErrorKind.ProfileNotFound);
        }

        var profiles = existing.Preferences.Profiles
            .Where(profile => !string.Equals(
                profile.Name,
                normalizedName,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var defaultProfile = string.Equals(
            existing.Preferences.DefaultProfile,
            normalizedName,
            StringComparison.OrdinalIgnoreCase)
            ? null
            : existing.Preferences.DefaultProfile;

        return Write(
            new CliPreferences(
                existing.Preferences.Language,
                defaultProfile,
                profiles));
    }

    private CliPreferencesReadResult ReadDocument(
        CliPreferencesDocument document)
    {
        if (document.SchemaVersion != LegacySchemaVersion &&
            document.SchemaVersion != CurrentSchemaVersion)
        {
            return CliPreferencesReadResult.Failure(
                CliPreferencesErrorKind.UnsupportedVersion);
        }

        if (!TryReadLanguage(
                document.Language,
                out var language))
        {
            return CliPreferencesReadResult.Failure(
                CliPreferencesErrorKind.InvalidLanguage);
        }

        if (document.SchemaVersion == LegacySchemaVersion)
        {
            return CliPreferencesReadResult.Success(
                new CliPreferences(language));
        }

        var profiles = new List<CliProfile>();

        if (document.Profiles is not null)
        {
            if (document.Profiles.Count > MaximumProfileCount)
            {
                return CliPreferencesReadResult.Failure(
                    CliPreferencesErrorKind.InvalidProfile);
            }

            foreach (var profile in document.Profiles)
            {
                if (profile is null ||
                    !CliProfileValidator.TryNormalizeName(
                        profile.Name,
                        out var normalizedName) ||
                    !CliProfileValidator.TryNormalizeServerUrl(
                        profile.ServerUrl,
                        out var normalizedServerUrl) ||
                    !TryReadLanguage(
                        profile.Language,
                        out var profileLanguage) ||
                    profiles.Any(existing => string.Equals(
                        existing.Name,
                        normalizedName,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return CliPreferencesReadResult.Failure(
                        CliPreferencesErrorKind.InvalidProfile);
                }

                profiles.Add(
                    new CliProfile(
                        normalizedName,
                        normalizedServerUrl,
                        profileLanguage));
            }
        }

        string? defaultProfile = null;

        if (!string.IsNullOrWhiteSpace(
                document.DefaultProfile))
        {
            if (!CliProfileValidator.TryNormalizeName(
                    document.DefaultProfile,
                    out var normalizedDefaultProfile) ||
                !profiles.Any(profile => string.Equals(
                    profile.Name,
                    normalizedDefaultProfile,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return CliPreferencesReadResult.Failure(
                    CliPreferencesErrorKind.InvalidProfile);
            }

            defaultProfile = normalizedDefaultProfile;
        }

        return CliPreferencesReadResult.Success(
            new CliPreferences(
                language,
                defaultProfile,
                profiles));
    }

    private CliPreferencesWriteResult Write(
        CliPreferences preferences)
    {
        var temporaryFilePath = Path.Combine(
            _directoryPath,
            $".{FileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            if (Directory.Exists(_directoryPath) &&
                IsSymbolicLink(new DirectoryInfo(
                    _directoryPath)))
            {
                return CliPreferencesWriteResult.Failure(
                    CliPreferencesErrorKind.UnsafePath);
            }

            if (File.Exists(_filePath) &&
                IsSymbolicLink(new FileInfo(_filePath)))
            {
                return CliPreferencesWriteResult.Failure(
                    CliPreferencesErrorKind.UnsafePath);
            }

            Directory.CreateDirectory(_directoryPath);
            SetPrivateUnixDirectoryMode(_directoryPath);

            var document = new CliPreferencesDocument(
                SchemaVersion: CurrentSchemaVersion,
                Language: preferences.Language.HasValue
                    ? MemLanguageResolver.GetCode(
                        preferences.Language.Value)
                    : null,
                DefaultProfile: preferences.DefaultProfile,
                Profiles: preferences.Profiles
                    .OrderBy(
                        profile => profile.Name,
                        StringComparer.Ordinal)
                    .Select(profile => new CliProfileDocument(
                        profile.Name,
                        profile.ServerUrl,
                        profile.Language.HasValue
                            ? MemLanguageResolver.GetCode(
                                profile.Language.Value)
                            : null))
                    .ToList());

            using (var stream = new FileStream(
                       temporaryFilePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(
                    stream,
                    document,
                    SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            SetPrivateUnixFileMode(temporaryFilePath);

            File.Move(
                temporaryFilePath,
                _filePath,
                overwrite: true);

            SetPrivateUnixFileMode(_filePath);

            return CliPreferencesWriteResult.Success();
        }
        catch (IOException)
        {
            return CliPreferencesWriteResult.Failure(
                CliPreferencesErrorKind.Unavailable);
        }
        catch (UnauthorizedAccessException)
        {
            return CliPreferencesWriteResult.Failure(
                CliPreferencesErrorKind.Unavailable);
        }
        catch (NotSupportedException)
        {
            return CliPreferencesWriteResult.Failure(
                CliPreferencesErrorKind.Unavailable);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryFilePath))
                {
                    File.Delete(temporaryFilePath);
                }
            }
            catch (IOException)
            {
                // Never surface filesystem exception text to CLI output.
            }
            catch (UnauthorizedAccessException)
            {
                // Never surface filesystem exception text to CLI output.
            }
        }
    }

    private static bool TryReadLanguage(
        string? value,
        out MemLanguage? language)
    {
        language = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (!MemLanguageResolver.TryParse(
                value,
                out var resolvedLanguage))
        {
            return false;
        }

        language = resolvedLanguage;
        return true;
    }

    private static string ResolveDefaultDirectoryPath()
    {
        var xdgConfigHome = Environment.GetEnvironmentVariable(
            "XDG_CONFIG_HOME");

        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            return Path.Combine(
                xdgConfigHome,
                "mem");
        }

        var applicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);

        if (!string.IsNullOrWhiteSpace(applicationData))
        {
            return Path.Combine(
                applicationData,
                "mem");
        }

        var userProfile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(
                userProfile,
                ".config",
                "mem");
        }

        throw new InvalidOperationException(
            "A local CLI configuration directory could not be resolved.");
    }

    private static bool IsSymbolicLink(
        FileSystemInfo fileSystemInfo) =>
        fileSystemInfo.LinkTarget is not null;

    private static void SetPrivateUnixDirectoryMode(
        string directoryPath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            directoryPath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
    }

    private static void SetPrivateUnixFileMode(
        string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            filePath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite);
    }

    private sealed record CliPreferencesDocument(
        int SchemaVersion,
        string? Language,
        string? DefaultProfile,
        List<CliProfileDocument>? Profiles);

    private sealed record CliProfileDocument(
        string? Name,
        string? ServerUrl,
        string? Language);
}
