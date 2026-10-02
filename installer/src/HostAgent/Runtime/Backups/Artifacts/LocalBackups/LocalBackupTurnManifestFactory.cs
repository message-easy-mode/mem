using System.Security.Cryptography;
using System.Text;
using HostAgent.Runtime.Stacks.Turn;

namespace HostAgent.Runtime.Backups.Artifacts.LocalBackups;

/// <summary>
/// Creates the safe TURN section written into a local backup manifest. The
/// effective homeserver.yaml remains the payload source of truth; runtime
/// metadata is trusted only when its recorded configuration hash matches the
/// captured file.
/// </summary>
public static class LocalBackupTurnManifestFactory
{
    public static LocalBackupCoturnManifest? Create(
        string? homeserverPath,
        IReadOnlyDictionary<string, string?> runtimeMetadata,
        ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(homeserverPath) || !File.Exists(homeserverPath))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(homeserverPath);
            var yaml = new UTF8Encoding(false, true).GetString(bytes);
            var turnUris = ReadYamlList(yaml, "turn_uris");
            var sharedSecret = ReadYamlScalar(yaml, "turn_shared_secret");
            var sharedSecretPath = ReadYamlScalar(yaml, "turn_shared_secret_path");
            var userLifetime = ReadYamlScalar(yaml, "turn_user_lifetime");
            var allowGuestsValue = ReadYamlScalar(yaml, "turn_allow_guests");
            var publicHost = turnUris
                .Select(TryGetTurnHost)
                .FirstOrDefault(host => !string.IsNullOrWhiteSpace(host));
            var configured = turnUris.Count > 0;
            var sharedSecretPresent =
                !string.IsNullOrWhiteSpace(sharedSecret) ||
                !string.IsNullOrWhiteSpace(sharedSecretPath);
            var configurationSha256 = Hash(bytes);
            var metadataHash = ReadMetadata(runtimeMetadata, "turnConfigurationSha256");
            var metadataMatches =
                !string.IsNullOrWhiteSpace(metadataHash) &&
                string.Equals(metadataHash, configurationSha256, StringComparison.Ordinal);
            var markerPresent = yaml.Contains(
                "# MEM-managed TURN configuration for Matrix voice/video calls.",
                StringComparison.Ordinal);

            var management = configured
                ? metadataMatches
                    ? NormalizeManagement(ReadMetadata(runtimeMetadata, "turnManagement"), markerPresent)
                    : markerPresent
                        ? RuntimeStackTurnManagementKinds.MemManaged
                        : RuntimeStackTurnManagementKinds.ExternalObserved
                : RuntimeStackTurnManagementKinds.None;

            var state = configured
                ? string.Equals(
                    management,
                    RuntimeStackTurnManagementKinds.MemManaged,
                    StringComparison.Ordinal)
                    ? RuntimeStackTurnStates.Connected
                    : RuntimeStackTurnStates.External
                : RuntimeStackTurnStates.NotConnected;

            var configurationSource = metadataMatches
                ? ReadMetadata(runtimeMetadata, "turnConfigurationSource")
                : configured
                    ? "backup-observed"
                    : "backup-disconnected";

            if (configured && !sharedSecretPresent)
            {
                warnings.Add(
                    "TURN URIs were found in homeserver.yaml but no supported TURN shared-secret setting was found.");
            }

            if (runtimeMetadata.ContainsKey("turnConfigurationSha256") && !metadataMatches)
            {
                warnings.Add(
                    "Recorded TURN metadata did not match the captured homeserver.yaml. The backup recorded the effective file state instead.");
            }

            return new LocalBackupCoturnManifest(
                Configured: configured,
                PublicHost: publicHost,
                Realm: ReadTurnRealmComment(yaml),
                TurnUris: turnUris,
                SharedSecretPresent: sharedSecretPresent,
                UserLifetime: userLifetime,
                AllowGuests: TryParseYamlBoolean(allowGuestsValue),
                State: state,
                Management: management,
                ConfigurationSource: configurationSource,
                ConfigurationSha256: configurationSha256);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            DecoderFallbackException)
        {
            warnings.Add($"TURN metadata could not be read from homeserver.yaml: {ex.Message}");
            return null;
        }
    }

    private static string NormalizeManagement(
        string? value,
        bool markerPresent) =>
        value?.Trim().ToLowerInvariant() switch
        {
            RuntimeStackTurnManagementKinds.MemManaged =>
                RuntimeStackTurnManagementKinds.MemManaged,
            RuntimeStackTurnManagementKinds.ExternalObserved =>
                RuntimeStackTurnManagementKinds.ExternalObserved,
            RuntimeStackTurnManagementKinds.None =>
                RuntimeStackTurnManagementKinds.None,
            _ => markerPresent
                ? RuntimeStackTurnManagementKinds.MemManaged
                : RuntimeStackTurnManagementKinds.ExternalObserved
        };

    private static string? ReadMetadata(
        IReadOnlyDictionary<string, string?> metadata,
        string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static IReadOnlyList<string> ReadYamlList(string yaml, string key)
    {
        var values = new List<string>();
        var lines = yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var prefix = key + ":";
        var reading = false;

        foreach (var line in lines)
        {
            if (!reading)
            {
                if (!line.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                reading = true;
                var inline = TrimYamlValue(line[prefix.Length..]);
                if (!string.IsNullOrWhiteSpace(inline))
                {
                    return inline.Trim('[', ']')
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(item => item.Trim().Trim('"', '\''))
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .ToArray();
                }

                continue;
            }

            if (line.Length == 0)
            {
                break;
            }

            if (!char.IsWhiteSpace(line[0]))
            {
                break;
            }

            var trimmed = line.Trim();
            if (!trimmed.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var value = trimmed[1..].Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string? ReadTurnRealmComment(string yaml)
    {
        const string prefix = "# TURN realm:";
        foreach (var line in yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var realm = trimmed[prefix.Length..].Trim().Trim('"', '\'');
                return string.IsNullOrWhiteSpace(realm) ? null : realm;
            }
        }

        return null;
    }

    private static string? ReadYamlScalar(string yaml, string key)
    {
        var prefix = key + ":";
        foreach (var line in yaml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return TrimYamlValue(line[prefix.Length..]);
            }
        }

        return null;
    }

    private static string? TrimYamlValue(string value)
    {
        var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
        var withoutComment = commentIndex >= 0 ? value[..commentIndex] : value;
        var trimmed = withoutComment.Trim().Trim('"', '\'');
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool? TryParseYamlBoolean(string? value) =>
        bool.TryParse(value, out var parsed) ? parsed : null;

    private static string? TryGetTurnHost(string? turnUri)
    {
        if (string.IsNullOrWhiteSpace(turnUri))
        {
            return null;
        }

        var value = turnUri.Trim();
        if (value.StartsWith("turn:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[5..];
        }
        else if (value.StartsWith("turns:", StringComparison.OrdinalIgnoreCase))
        {
            value = value[6..];
        }

        var withoutQuery = value.Split('?', 2)[0].Trim();
        if (withoutQuery.StartsWith("[", StringComparison.Ordinal))
        {
            var closing = withoutQuery.IndexOf(']');
            return closing > 1 ? withoutQuery[1..closing] : null;
        }

        var colon = withoutQuery.LastIndexOf(':');
        return colon > 0 ? withoutQuery[..colon] : withoutQuery;
    }
}
