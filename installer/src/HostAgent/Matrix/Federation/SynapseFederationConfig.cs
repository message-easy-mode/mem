using System.Security.Cryptography;
using System.Text;

namespace HostAgent.Matrix.Federation;

public static class SynapseFederationConfigKinds
{
    public const string KeyAbsent = "key_absent";
    public const string EmptyList = "empty_list";
    public const string ExactDomainList = "exact_domain_list";
    public const string CustomUnsupported = "custom_unsupported";
}

public sealed record SynapseFederationConfigReadResult(
    string Kind,
    string Mode,
    IReadOnlyList<string> Allowlist,
    string FileSha256,
    string? ProblemCode,
    string? Detail)
{
    public bool Supported => !string.Equals(
        Kind,
        SynapseFederationConfigKinds.CustomUnsupported,
        StringComparison.Ordinal);
}

public sealed class SynapseFederationConfigReader
{
    private const string TargetKey = "federation_domain_whitelist";
    private readonly FederationDomainValidator _domainValidator;

    public SynapseFederationConfigReader(FederationDomainValidator domainValidator)
    {
        _domainValidator = domainValidator;
    }

    public async Task<SynapseFederationConfigReadResult> ReadAsync(
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
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Unsupported(
                hash,
                "federation_config_invalid",
                "The Synapse configuration is not valid UTF-8 text.");
        }

        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var targetLines = new List<int>();
        var malformedTopLevelTarget = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Length != trimmed.Length)
            {
                continue;
            }

            var uncommented = StripInlineComment(line).TrimEnd();
            var colonIndex = uncommented.IndexOf(':');
            if (colonIndex >= 0)
            {
                var rawKey = uncommented[..colonIndex];
                if (string.Equals(rawKey, TargetKey, StringComparison.Ordinal))
                {
                    targetLines.Add(index);
                }
                else if (IsUnsupportedTargetKey(rawKey) ||
                         (uncommented.StartsWith("{", StringComparison.Ordinal) &&
                          uncommented.Contains(TargetKey, StringComparison.Ordinal)))
                {
                    malformedTopLevelTarget = true;
                }
            }
            else if (IsUnsupportedTargetKey(uncommented) ||
                     (uncommented.StartsWith("?", StringComparison.Ordinal) &&
                      uncommented.Contains(TargetKey, StringComparison.Ordinal)))
            {
                malformedTopLevelTarget = true;
            }
        }

        if (targetLines.Count > 1)
        {
            return Unsupported(
                hash,
                "federation_config_ambiguous",
                "The top-level federation allowlist key is duplicated.");
        }

        if (malformedTopLevelTarget)
        {
            return Unsupported(
                hash,
                "federation_config_custom_unsupported",
                "The federation allowlist uses a custom YAML key or collection representation that MEM will not rewrite automatically.");
        }

        if (targetLines.Count == 0)
        {
            return new SynapseFederationConfigReadResult(
                Kind: SynapseFederationConfigKinds.KeyAbsent,
                Mode: FederationModes.Public,
                Allowlist: [],
                FileSha256: hash,
                ProblemCode: null,
                Detail: null);
        }

        var targetIndex = targetLines[0];
        var targetLine = lines[targetIndex];
        var value = StripInlineComment(targetLine[(TargetKey.Length + 1)..]).Trim();

        if (string.Equals(value, "[]", StringComparison.Ordinal))
        {
            return new SynapseFederationConfigReadResult(
                Kind: SynapseFederationConfigKinds.EmptyList,
                Mode: FederationModes.LocalOnly,
                Allowlist: [],
                FileSha256: hash,
                ProblemCode: null,
                Detail: null);
        }

        if (value.Length > 0)
        {
            return Unsupported(
                hash,
                "federation_config_custom_unsupported",
                "The federation allowlist uses a custom YAML representation that MEM will not rewrite automatically.");
        }

        var domains = new List<string>();
        var foundMeaningfulChild = false;

        for (var index = targetIndex + 1; index < lines.Length; index++)
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

            foundMeaningfulChild = true;
            if (!trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                return Unsupported(
                    hash,
                    "federation_config_custom_unsupported",
                    "The federation allowlist block contains a YAML value that is not a plain exact-domain list item.");
            }

            var domain = StripInlineComment(trimmed[2..]).Trim();
            if (domain.Length == 0 ||
                domain.StartsWith("\"", StringComparison.Ordinal) ||
                domain.StartsWith("'", StringComparison.Ordinal) ||
                domain.StartsWith("&", StringComparison.Ordinal) ||
                domain.StartsWith("*", StringComparison.Ordinal) ||
                domain.StartsWith("[", StringComparison.Ordinal) ||
                domain.StartsWith("{", StringComparison.Ordinal))
            {
                return Unsupported(
                    hash,
                    "federation_config_custom_unsupported",
                    "The federation allowlist contains a custom YAML scalar, anchor, alias, or collection.");
            }

            domains.Add(domain);
        }

        if (!foundMeaningfulChild || domains.Count == 0)
        {
            return Unsupported(
                hash,
                "federation_config_custom_unsupported",
                "The federation allowlist key does not contain a supported non-empty block list.");
        }

        var validation = _domainValidator.ValidateMany(domains, requireAtLeastOne: true);
        if (!validation.Valid)
        {
            return Unsupported(
                hash,
                "federation_config_custom_unsupported",
                validation.Detail ?? "The federation allowlist contains an unsupported domain value.");
        }

        return new SynapseFederationConfigReadResult(
            Kind: SynapseFederationConfigKinds.ExactDomainList,
            Mode: FederationModes.Restricted,
            Allowlist: validation.CanonicalDomains,
            FileSha256: hash,
            ProblemCode: null,
            Detail: null);
    }

    private static bool IsUnsupportedTargetKey(string value)
    {
        var candidate = value.Trim();
        if (string.Equals(candidate, TargetKey, StringComparison.Ordinal))
        {
            return true;
        }

        if (candidate.Length == TargetKey.Length + 2 &&
            ((candidate[0] == '"' && candidate[^1] == '"') ||
             (candidate[0] == '\'' && candidate[^1] == '\'')) &&
            string.Equals(candidate[1..^1], TargetKey, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static string StripInlineComment(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '#')
            {
                continue;
            }

            if (index == 0 || char.IsWhiteSpace(value[index - 1]))
            {
                return value[..index];
            }
        }

        return value;
    }

    private static SynapseFederationConfigReadResult Unsupported(
        string hash,
        string problemCode,
        string detail) =>
        new(
            Kind: SynapseFederationConfigKinds.CustomUnsupported,
            Mode: FederationModes.Unknown,
            Allowlist: [],
            FileSha256: hash,
            ProblemCode: problemCode,
            Detail: detail);
}

public sealed class SynapseFederationConfigEditor
{
    private const string TargetKey = "federation_domain_whitelist";
    private static readonly UTF8Encoding Utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public byte[] Render(
        byte[] currentBytes,
        CanonicalFederationPolicyRequest request)
    {
        ArgumentNullException.ThrowIfNull(currentBytes);
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.Mode, FederationModes.Public, StringComparison.Ordinal) &&
            !string.Equals(request.Mode, FederationModes.Restricted, StringComparison.Ordinal) &&
            !string.Equals(request.Mode, FederationModes.LocalOnly, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The federation config editor accepts only Public, Restricted, or Local-only federation.");
        }

        if (string.Equals(request.Mode, FederationModes.Restricted, StringComparison.Ordinal) &&
            request.Allowlist.Count == 0)
        {
            throw new InvalidOperationException(
                "Restricted federation requires at least one canonical homeserver domain.");
        }

        if (!string.Equals(request.Mode, FederationModes.Restricted, StringComparison.Ordinal) &&
            request.Allowlist.Count > 0)
        {
            throw new InvalidOperationException(
                "Public and Local-only federation require an empty homeserver allowlist.");
        }

        var text = Utf8.GetString(currentBytes);
        if (text.Length > 0 && text[0] == '\uFEFF')
        {
            text = text[1..];
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";
        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n').ToList();

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var targetIndex = FindTargetIndex(lines);
        var insertionIndex = targetIndex >= 0 ? targetIndex : lines.Count;

        if (targetIndex >= 0)
        {
            var itemIndexes = FindSupportedItemIndexes(lines, targetIndex);
            foreach (var index in itemIndexes.OrderByDescending(x => x))
            {
                lines.RemoveAt(index);
            }

            lines.RemoveAt(targetIndex);
        }

        if (string.Equals(request.Mode, FederationModes.Restricted, StringComparison.Ordinal) ||
            string.Equals(request.Mode, FederationModes.LocalOnly, StringComparison.Ordinal))
        {
            if (targetIndex < 0 && lines.Count > 0 && lines[^1].Length > 0)
            {
                lines.Add(string.Empty);
                insertionIndex = lines.Count;
            }

            var replacement = string.Equals(request.Mode, FederationModes.LocalOnly, StringComparison.Ordinal)
                ? new List<string> { TargetKey + ": []" }
                : new List<string> { TargetKey + ":" };
            if (string.Equals(request.Mode, FederationModes.Restricted, StringComparison.Ordinal))
            {
                replacement.AddRange(request.Allowlist.Select(domain => "  - " + domain));
            }

            lines.InsertRange(Math.Min(insertionIndex, lines.Count), replacement);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return Utf8.GetBytes(string.Join(newline, lines) + newline);
    }

    private static int FindTargetIndex(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Length == trimmed.Length &&
                trimmed.StartsWith(TargetKey + ":", StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static IReadOnlyList<int> FindSupportedItemIndexes(
        IReadOnlyList<string> lines,
        int targetIndex)
    {
        var targetLine = lines[targetIndex];
        var value = StripInlineComment(targetLine[(TargetKey.Length + 1)..]).Trim();
        if (value.Length > 0)
        {
            return [];
        }

        var indexes = new List<int>();
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

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static string StripInlineComment(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] == '#' &&
                (index == 0 || char.IsWhiteSpace(value[index - 1])))
            {
                return value[..index];
            }
        }

        return value;
    }
}
