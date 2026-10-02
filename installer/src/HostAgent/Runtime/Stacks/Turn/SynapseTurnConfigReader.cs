using System.Security.Cryptography;
using System.Text;

namespace HostAgent.Runtime.Stacks.Turn;

public sealed class SynapseTurnConfigReader
{
    private static readonly string[] TargetKeys =
    [
        "turn_uris",
        "turn_shared_secret",
        "turn_shared_secret_path",
        "turn_user_lifetime",
        "turn_allow_guests"
    ];

    private const string ManagedMarker = "# MEM-managed TURN configuration for Matrix voice/video calls.";
    private const string PublicHostComment = "# TURN public host:";
    private const string RealmComment = "# TURN realm:";

    public async Task<SynapseTurnConfigReadResult> ReadAsync(
        string path,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Synapse configuration path is required.", nameof(path));
        }

        var bytes = await File.ReadAllBytesAsync(path, ct);
        var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Unsupported(hash, "turn_config_invalid_utf8", "The Synapse configuration is not valid UTF-8 text.");
        }

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var lines = Normalise(text).Split('\n');
        var indexes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var key in TargetKeys)
        {
            indexes[key] = [];
        }

        var malformedTarget = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || line.Length != trimmed.Length)
            {
                continue;
            }

            var raw = StripInlineComment(line).TrimEnd();
            var colon = raw.IndexOf(':');
            if (colon < 0)
            {
                if (TargetKeys.Any(key => LooksLikeTarget(raw, key)))
                {
                    malformedTarget = true;
                }
                continue;
            }

            var rawKey = raw[..colon].Trim();
            var exact = TargetKeys.FirstOrDefault(key => string.Equals(rawKey, key, StringComparison.Ordinal));
            if (exact is not null)
            {
                indexes[exact].Add(index);
            }
            else if (TargetKeys.Any(key => LooksLikeTarget(rawKey, key)) ||
                     (raw.StartsWith("{", StringComparison.Ordinal) && TargetKeys.Any(key => raw.Contains(key, StringComparison.Ordinal))))
            {
                malformedTarget = true;
            }
        }

        if (malformedTarget)
        {
            return Unsupported(hash, "turn_config_custom_unsupported", "TURN settings use a custom YAML key or collection representation that MEM cannot classify safely.");
        }

        var duplicated = indexes.FirstOrDefault(pair => pair.Value.Count > 1);
        if (duplicated.Value is not null)
        {
            return Unsupported(hash, "turn_config_ambiguous", $"The top-level {duplicated.Key} key is duplicated.");
        }

        var uris = ReadList(lines, GetIndex(indexes, "turn_uris"), "turn_uris", hash);
        if (!uris.Supported)
        {
            return uris.Unsupported!;
        }

        var inlineSecret = ReadScalar(lines, GetIndex(indexes, "turn_shared_secret"), "turn_shared_secret", hash);
        if (!inlineSecret.Supported)
        {
            return inlineSecret.Unsupported!;
        }

        var secretPath = ReadScalar(lines, GetIndex(indexes, "turn_shared_secret_path"), "turn_shared_secret_path", hash);
        if (!secretPath.Supported)
        {
            return secretPath.Unsupported!;
        }

        if (!string.IsNullOrWhiteSpace(inlineSecret.Value) && !string.IsNullOrWhiteSpace(secretPath.Value))
        {
            return Unsupported(hash, "turn_config_credential_ambiguous", "Synapse contains both turn_shared_secret and turn_shared_secret_path.");
        }

        var userLifetime = ReadScalar(lines, GetIndex(indexes, "turn_user_lifetime"), "turn_user_lifetime", hash);
        if (!userLifetime.Supported)
        {
            return userLifetime.Unsupported!;
        }

        var allowGuests = ReadScalar(lines, GetIndex(indexes, "turn_allow_guests"), "turn_allow_guests", hash);
        if (!allowGuests.Supported)
        {
            return allowGuests.Unsupported!;
        }

        bool? parsedAllowGuests = null;
        if (!string.IsNullOrWhiteSpace(allowGuests.Value))
        {
            if (!bool.TryParse(allowGuests.Value, out var parsed))
            {
                return Unsupported(hash, "turn_config_allow_guests_invalid", "turn_allow_guests is not a supported boolean value.");
            }
            parsedAllowGuests = parsed;
        }

        var anySettings = indexes.Values.Any(value => value.Count > 0);
        var sharedSecretPresent = !string.IsNullOrWhiteSpace(inlineSecret.Value) || !string.IsNullOrWhiteSpace(secretPath.Value);
        var mechanism = !string.IsNullOrWhiteSpace(inlineSecret.Value)
            ? "inline-shared-secret"
            : !string.IsNullOrWhiteSpace(secretPath.Value)
                ? "shared-secret-path"
                : "none";

        return new SynapseTurnConfigReadResult(
            Supported: true,
            AnyTurnSettings: anySettings,
            MemManagedMarkerPresent: lines.Any(line => string.Equals(line.Trim(), ManagedMarker, StringComparison.Ordinal)),
            TurnUris: uris.Values,
            CredentialMechanism: mechanism,
            SharedSecretPresent: sharedSecretPresent,
            SharedSecretValue: inlineSecret.Value,
            SharedSecretFingerprint: string.IsNullOrWhiteSpace(inlineSecret.Value)
                ? null
                : "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inlineSecret.Value))).ToLowerInvariant(),
            SharedSecretPath: string.IsNullOrWhiteSpace(secretPath.Value) ? null : secretPath.Value,
            UserLifetime: string.IsNullOrWhiteSpace(userLifetime.Value) ? null : userLifetime.Value,
            AllowGuests: parsedAllowGuests,
            CommentPublicHost: ReadComment(lines, PublicHostComment),
            CommentRealm: ReadComment(lines, RealmComment),
            FileSha256: hash,
            ProblemCode: null,
            Detail: null);
    }

    private static ListReadResult ReadList(
        IReadOnlyList<string> lines,
        int index,
        string key,
        string hash)
    {
        if (index < 0)
        {
            return new ListReadResult(true, [], null);
        }

        var line = StripInlineComment(lines[index]).TrimEnd();
        var colon = line.IndexOf(':');
        var inline = line[(colon + 1)..].Trim();
        if (inline == "[]")
        {
            return new ListReadResult(true, [], null);
        }

        if (inline.Length > 0)
        {
            if (!inline.StartsWith("[", StringComparison.Ordinal) || !inline.EndsWith("]", StringComparison.Ordinal))
            {
                return new ListReadResult(false, [], Unsupported(hash, "turn_config_uris_custom_unsupported", $"{key} must use a plain YAML list."));
            }

            try
            {
                var values = inline[1..^1]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(UnquoteSimple)
                    .Where(value => value.Length > 0)
                    .ToArray();
                return new ListReadResult(true, values, null);
            }
            catch (FormatException)
            {
                return new ListReadResult(false, [], Unsupported(hash, "turn_config_uris_custom_unsupported", $"{key} contains an unsupported YAML scalar."));
            }
        }

        var result = new List<string>();
        var meaningful = false;
        for (var child = index + 1; child < lines.Count; child++)
        {
            var candidate = lines[child];
            var trimmed = candidate.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (candidate.Length == trimmed.Length)
            {
                break;
            }

            meaningful = true;
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                return new ListReadResult(false, [], Unsupported(hash, "turn_config_uris_custom_unsupported", $"{key} contains a non-list YAML value."));
            }

            try
            {
                var value = UnquoteSimple(StripInlineComment(trimmed[2..]).Trim());
                if (value.Length == 0)
                {
                    return new ListReadResult(false, [], Unsupported(hash, "turn_config_uris_custom_unsupported", $"{key} contains an empty list item."));
                }
                result.Add(value);
            }
            catch (FormatException)
            {
                return new ListReadResult(false, [], Unsupported(hash, "turn_config_uris_custom_unsupported", $"{key} contains an unsupported YAML scalar."));
            }
        }

        if (!meaningful)
        {
            return new ListReadResult(false, [], Unsupported(hash, "turn_config_uris_custom_unsupported", $"{key} does not contain a supported list."));
        }

        return new ListReadResult(true, result, null);
    }

    private static ScalarReadResult ReadScalar(
        IReadOnlyList<string> lines,
        int index,
        string key,
        string hash)
    {
        if (index < 0)
        {
            return new ScalarReadResult(true, null, null);
        }

        var line = StripInlineComment(lines[index]).TrimEnd();
        var colon = line.IndexOf(':');
        var value = line[(colon + 1)..].Trim();
        if (value.Length == 0)
        {
            return new ScalarReadResult(false, null, Unsupported(hash, "turn_config_scalar_custom_unsupported", $"{key} must use a plain scalar value."));
        }

        try
        {
            return new ScalarReadResult(true, UnquoteSimple(value), null);
        }
        catch (FormatException)
        {
            return new ScalarReadResult(false, null, Unsupported(hash, "turn_config_scalar_custom_unsupported", $"{key} contains an unsupported YAML scalar."));
        }
    }

    private static int GetIndex(
        IReadOnlyDictionary<string, List<int>> indexes,
        string key) =>
        indexes[key].Count == 0 ? -1 : indexes[key][0];

    private static string? ReadComment(IReadOnlyList<string> lines, string prefix)
    {
        var line = lines.FirstOrDefault(candidate => candidate.TrimStart().StartsWith(prefix, StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        var value = line.TrimStart()[prefix.Length..].Trim();
        try
        {
            return value.Length == 0 ? null : UnquoteSimple(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string UnquoteSimple(string value)
    {
        var startsDoubleQuoted = value.StartsWith('"');
        var endsDoubleQuoted = value.EndsWith('"');
        var startsSingleQuoted = value.StartsWith('\'');
        var endsSingleQuoted = value.EndsWith('\'');

        if (startsDoubleQuoted != endsDoubleQuoted ||
            startsSingleQuoted != endsSingleQuoted)
        {
            throw new FormatException("Unbalanced YAML quotes are unsupported.");
        }

        if (value.Length >= 2 &&
            ((startsDoubleQuoted && endsDoubleQuoted) ||
             (startsSingleQuoted && endsSingleQuoted)))
        {
            var inner = value[1..^1];
            if (inner.Contains(value[0]) || inner.Contains('\\'))
            {
                throw new FormatException("Escaped YAML scalars are unsupported.");
            }
            return inner;
        }

        if (value.StartsWith("&", StringComparison.Ordinal) ||
            value.StartsWith("*", StringComparison.Ordinal) ||
            value.StartsWith("[", StringComparison.Ordinal) ||
            value.StartsWith("{", StringComparison.Ordinal))
        {
            throw new FormatException("Custom YAML scalar is unsupported.");
        }

        return value;
    }

    private static bool LooksLikeTarget(string value, string target)
    {
        var candidate = value.Trim();
        return string.Equals(candidate.Trim('"', '\''), target, StringComparison.Ordinal);
    }

    private static string StripInlineComment(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '#' && (index == 0 || char.IsWhiteSpace(value[index - 1])))
            {
                return value[..index];
            }
        }
        return value;
    }

    private static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static SynapseTurnConfigReadResult Unsupported(string hash, string code, string detail) =>
        new(
            Supported: false,
            AnyTurnSettings: false,
            MemManagedMarkerPresent: false,
            TurnUris: [],
            CredentialMechanism: "unknown",
            SharedSecretPresent: false,
            SharedSecretValue: null,
            SharedSecretFingerprint: null,
            SharedSecretPath: null,
            UserLifetime: null,
            AllowGuests: null,
            CommentPublicHost: null,
            CommentRealm: null,
            FileSha256: hash,
            ProblemCode: code,
            Detail: detail);

    private sealed record ListReadResult(
        bool Supported,
        IReadOnlyList<string> Values,
        SynapseTurnConfigReadResult? Unsupported);

    private sealed record ScalarReadResult(
        bool Supported,
        string? Value,
        SynapseTurnConfigReadResult? Unsupported);
}

