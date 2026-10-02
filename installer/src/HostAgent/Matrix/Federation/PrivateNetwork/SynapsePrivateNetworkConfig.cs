using System.Security.Cryptography;
using System.Text;

namespace HostAgent.Matrix.Federation.PrivateNetwork;

public sealed record SynapsePrivateNetworkConfigReadResult(
    bool Supported,
    IReadOnlyList<string> Entries,
    string FileSha256,
    string? ProblemCode,
    string? Detail);

public sealed class SynapsePrivateNetworkConfigReader
{
    private const string TargetKey = "ip_range_whitelist";

    public async Task<SynapsePrivateNetworkConfigReadResult> ReadAsync(
        string path,
        CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var hash = Hash(bytes);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Unsupported(hash, "private_network_config_invalid", "The Synapse configuration is not valid UTF-8 text.");
        }

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var lines = Normalise(text).Split('\n');
        var targets = new List<int>();
        var malformed = false;

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
            if (colon >= 0)
            {
                var key = raw[..colon].Trim();
                if (string.Equals(key, TargetKey, StringComparison.Ordinal))
                {
                    targets.Add(index);
                }
                else if (LooksLikeTarget(key) ||
                         (raw.StartsWith("{", StringComparison.Ordinal) && raw.Contains(TargetKey, StringComparison.Ordinal)))
                {
                    malformed = true;
                }
            }
            else if (LooksLikeTarget(raw))
            {
                malformed = true;
            }
        }

        if (targets.Count > 1)
        {
            return Unsupported(hash, "private_network_config_ambiguous", "The top-level ip_range_whitelist key is duplicated.");
        }

        if (malformed)
        {
            return Unsupported(hash, "private_network_config_custom_unsupported", "The ip_range_whitelist setting uses a custom YAML representation that MEM will not rewrite automatically.");
        }

        if (targets.Count == 0)
        {
            return new SynapsePrivateNetworkConfigReadResult(true, [], hash, null, null);
        }

        var target = targets[0];
        var value = StripInlineComment(lines[target][(TargetKey.Length + 1)..]).Trim();
        if (value == "[]")
        {
            return new SynapsePrivateNetworkConfigReadResult(true, [], hash, null, null);
        }

        if (value.Length > 0)
        {
            return Unsupported(hash, "private_network_config_custom_unsupported", "The ip_range_whitelist setting must use a plain YAML block list.");
        }

        var entries = new List<string>();
        var meaningful = false;
        for (var index = target + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Length == trimmed.Length)
            {
                break;
            }

            meaningful = true;
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                return Unsupported(hash, "private_network_config_custom_unsupported", "The ip_range_whitelist block contains a non-list YAML value.");
            }

            var scalar = StripInlineComment(trimmed[2..]).Trim();
            scalar = UnquoteSimple(scalar);
            try
            {
                entries.Add(PrivateNetworkAddressValidator.CanonicaliseCidr(scalar));
            }
            catch (FormatException)
            {
                return Unsupported(hash, "private_network_config_custom_unsupported", "The ip_range_whitelist block contains an unsupported IP range value.");
            }
        }

        if (!meaningful)
        {
            return Unsupported(hash, "private_network_config_custom_unsupported", "The ip_range_whitelist key does not contain a supported block list.");
        }

        return new SynapsePrivateNetworkConfigReadResult(
            true,
            entries.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            hash,
            null,
            null);
    }

    internal static string Normalise(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    internal static string StripInlineComment(string value)
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

    private static string UnquoteSimple(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
        {
            var inner = value[1..^1];
            if (inner.Contains(value[0]) || inner.Contains('\\'))
            {
                throw new FormatException("Quoted scalar escapes are unsupported.");
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

    private static bool LooksLikeTarget(string value)
    {
        var candidate = value.Trim();
        return string.Equals(candidate.Trim('"', '\''), TargetKey, StringComparison.Ordinal);
    }

    private static string Hash(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static SynapsePrivateNetworkConfigReadResult Unsupported(string hash, string code, string detail) =>
        new(false, [], hash, code, detail);
}

public sealed class SynapsePrivateNetworkConfigEditor
{
    private const string TargetKey = "ip_range_whitelist";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public byte[] Render(byte[] currentBytes, string canonicalCidr, bool enabled)
    {
        var text = Utf8.GetString(currentBytes);
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalised = SynapsePrivateNetworkConfigReader.Normalise(text);
        var lines = normalised.Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var current = ReadEntriesAndIndexes(lines);
        var entries = current.Entries.ToHashSet(StringComparer.Ordinal);
        if (enabled)
        {
            entries.Add(canonicalCidr);
        }
        else
        {
            entries.Remove(canonicalCidr);
        }

        var insertionIndex = current.TargetIndex >= 0 ? current.TargetIndex : lines.Count;
        foreach (var index in current.ItemIndexes.OrderByDescending(x => x))
        {
            lines.RemoveAt(index);
        }
        if (current.TargetIndex >= 0)
        {
            lines.RemoveAt(current.TargetIndex);
        }

        if (entries.Count > 0)
        {
            if (current.TargetIndex < 0 && lines.Count > 0 && lines[^1].Length > 0)
            {
                lines.Add(string.Empty);
                insertionIndex = lines.Count;
            }

            var replacement = new List<string> { TargetKey + ":" };
            replacement.AddRange(entries
                .OrderBy(x => x, StringComparer.Ordinal)
                .Select(entry => $"  - \"{entry}\""));
            lines.InsertRange(Math.Min(insertionIndex, lines.Count), replacement);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return Utf8.GetBytes(string.Join(newline, lines) + newline);
    }

    private static (int TargetIndex, IReadOnlyList<int> ItemIndexes, IReadOnlyList<string> Entries)
        ReadEntriesAndIndexes(IReadOnlyList<string> lines)
    {
        var targetIndex = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (line.Length != line.TrimStart().Length)
            {
                continue;
            }

            var raw = SynapsePrivateNetworkConfigReader.StripInlineComment(line).TrimEnd();
            var colon = raw.IndexOf(':');
            if (colon < 0 || !string.Equals(raw[..colon].Trim(), TargetKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (targetIndex >= 0)
            {
                throw new InvalidOperationException("The ip_range_whitelist key is duplicated.");
            }

            targetIndex = index;
        }

        if (targetIndex < 0)
        {
            return (-1, [], []);
        }

        var targetLine = SynapsePrivateNetworkConfigReader.StripInlineComment(lines[targetIndex]).TrimEnd();
        var targetColon = targetLine.IndexOf(':');
        var inline = targetLine[(targetColon + 1)..].Trim();
        if (inline == "[]")
        {
            return (targetIndex, [], []);
        }

        var itemIndexes = new List<int>();
        var entries = new List<string>();
        for (var index = targetIndex + 1; index < lines.Count; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Length == trimmed.Length)
            {
                break;
            }

            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The ip_range_whitelist block contains an unsupported value.");
            }

            var scalar = SynapsePrivateNetworkConfigReader.StripInlineComment(trimmed[2..])
                .Trim()
                .Trim('"', '\'');
            entries.Add(PrivateNetworkAddressValidator.CanonicaliseCidr(scalar));
            itemIndexes.Add(index);
        }

        return (targetIndex, itemIndexes, entries);
    }
}
