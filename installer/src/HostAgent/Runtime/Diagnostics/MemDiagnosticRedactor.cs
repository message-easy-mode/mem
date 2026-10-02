using System.Text.RegularExpressions;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics;

public sealed class MemDiagnosticRedactor : IMemDiagnosticTextRedactor
{
    private const int MaximumDictionaryEntries = 64;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    private static readonly string[] SensitiveKeyFragments =
    [
        "password", "passwd", "pwd", "secret", "token", "authorization",
        "credential", "connectionstring", "privatekey", "signingkey",
        "sharedkey", "cookie", "apikey", "recoverycode", "accesscode",
        "ageidentity", "setupcode"
    ];

    private static readonly Regex SensitiveAssignment = new(
        "(?i)(?<![A-Za-z0-9_])[\"']?(?<key>[A-Za-z0-9_.-]*(?:password|passwd|pwd|secret|token|authorization|credential|cookie|api[_-]?key|private[_-]?key|signing[_-]?key|shared[_-]?key|recovery[_-]?code|access[_-]?code|age[_-]?identity|setup[_-]?code|connection[_-]?string)[A-Za-z0-9_.-]*)[\"']?\\s*[:=]\\s*(?:\"[^\"]*\"|'[^']*'|[^\\s,;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex SensitiveCommandOption = new(
        "(?i)(?<![A-Za-z0-9_])(?<key>--?[A-Za-z0-9_.-]*(?:password|passwd|pwd|secret|token|authorization|credential|cookie|api[_-]?key|private[_-]?key|signing[_-]?key|shared[_-]?key|recovery[_-]?code|access[_-]?code|age[_-]?identity|setup[_-]?code|connection[_-]?string)[A-Za-z0-9_.-]*)\\s+(?:\"[^\"]*\"|'[^']*'|[^\\s,;]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex UriWithCredentials = new(
        "(?i)\\b([a-z][a-z0-9+.-]*://)[^\\s/@:]+:[^\\s/@]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex AuthorizationValue = new(
        "(?i)\\b(Bearer|Basic)\\s+[A-Za-z0-9+/_=.-]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex PrivateKeyBlock = new(
        "-----BEGIN(?: [A-Z0-9]+)? PRIVATE KEY-----.*?-----END(?: [A-Z0-9]+)? PRIVATE KEY-----",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex AgeSecretKey = new(
        "\\bAGE-SECRET-KEY-[A-Z0-9-]+\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);

    private static readonly Regex JwtLikeValue = new(
        "\\beyJ[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\.[A-Za-z0-9_-]{8,}\\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex UnixSourcePath = new(
        "(?<=\\sin\\s)/(?:[^\\r\\n:]+/)+(?<file>[^/\\r\\n:]+\\.cs)(?=:line\\s+\\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private static readonly Regex WindowsSourcePath = new(
        "(?<=\\sin\\s)[A-Za-z]:\\\\(?:[^\\r\\n:]+\\\\)+(?<file>[^\\\\\\r\\n:]+\\.cs)(?=:line\\s+\\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    private readonly MemDiagnosticsOptions _options;

    public MemDiagnosticRedactor(MemDiagnosticsOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public MemDiagnosticSafeText Project(
        string? value,
        int maximumCharacters,
        bool sanitizeSourcePaths = false)
    {
        if (maximumCharacters < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        }

        var projected = RedactText(
            value,
            maximumCharacters,
            exactSecrets: null,
            sanitizeSourcePaths);
        return new MemDiagnosticSafeText(
            projected.Value,
            projected.Changed,
            projected.Truncated);
    }

    internal MemDiagnosticRedactionResult<string> RedactText(
        string? value,
        int? maximumCharacters = null,
        IReadOnlyCollection<string>? exactSecrets = null,
        bool sanitizeSourcePaths = false)
    {
        var original = value ?? string.Empty;
        var limit = maximumCharacters ?? _options.MaximumDetailCharacters;
        var preRedactionLimit = Math.Clamp((long)limit * 4L, 4096L, 262144L);
        var inputWasTruncated = original.Length > preRedactionLimit;
        var boundedInput = inputWasTruncated
            ? original[..(int)preRedactionLimit]
            : original;
        var normalized = NormalizeText(boundedInput);
        var changed = inputWasTruncated ||
                      !string.Equals(original, normalized, StringComparison.Ordinal);

        foreach (var exactSecret in NormalizeExactSecrets(exactSecrets))
        {
            if (normalized.Contains(exactSecret, StringComparison.Ordinal))
            {
                normalized = normalized.Replace(exactSecret, "[redacted]", StringComparison.Ordinal);
                changed = true;
            }
        }

        normalized = ReplaceAndTrack(AuthorizationValue, normalized, "$1 [redacted]", ref changed);
        normalized = ReplaceAndTrack(SensitiveAssignment, normalized, "${key}=[redacted]", ref changed);
        normalized = ReplaceAndTrack(SensitiveCommandOption, normalized, "${key} [redacted]", ref changed);
        normalized = ReplaceAndTrack(UriWithCredentials, normalized, "$1[redacted]@", ref changed);
        normalized = ReplaceAndTrack(PrivateKeyBlock, normalized, "[redacted-private-key]", ref changed);
        normalized = ReplaceAndTrack(AgeSecretKey, normalized, "[redacted-age-identity]", ref changed);
        normalized = ReplaceAndTrack(JwtLikeValue, normalized, "[redacted-token]", ref changed);

        if (sanitizeSourcePaths)
        {
            normalized = ReplaceAndTrack(UnixSourcePath, normalized, "${file}", ref changed);
            normalized = ReplaceAndTrack(WindowsSourcePath, normalized, "${file}", ref changed);
        }

        var truncated = inputWasTruncated || normalized.Length > limit;
        if (normalized.Length > limit)
        {
            normalized = normalized[..limit];
            changed = true;
        }

        return new MemDiagnosticRedactionResult<string>(normalized, changed, truncated);
    }

    internal MemDiagnosticRedactionResult<IReadOnlyDictionary<string, string>?> RedactDictionary(
        IReadOnlyDictionary<string, string?>? source,
        IReadOnlyCollection<string>? exactSecrets = null,
        int? maximumValueCharacters = null)
    {
        if (source is null || source.Count == 0)
        {
            return new MemDiagnosticRedactionResult<IReadOnlyDictionary<string, string>?>(
                null,
                Changed: false,
                Truncated: false);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var changed = false;
        var truncated = source.Count > MaximumDictionaryEntries;

        foreach (var pair in source
                     .OrderBy(item => item.Key, StringComparer.Ordinal)
                     .Take(MaximumDictionaryEntries))
        {
            var safeKey = SanitizeKey(pair.Key);
            if (string.IsNullOrWhiteSpace(safeKey))
            {
                changed = true;
                continue;
            }

            if (!string.Equals(pair.Key, safeKey, StringComparison.Ordinal))
            {
                changed = true;
            }

            if (IsSensitiveKey(safeKey))
            {
                result[safeKey] = "[redacted]";
                changed = true;
                continue;
            }

            var safeValue = RedactText(
                pair.Value,
                maximumValueCharacters,
                exactSecrets);
            result[safeKey] = safeValue.Value;
            changed |= safeValue.Changed;
            truncated |= safeValue.Truncated;
        }

        return new MemDiagnosticRedactionResult<IReadOnlyDictionary<string, string>?>(
            result.Count == 0 ? null : result,
            changed,
            truncated);
    }

    internal MemDiagnosticRedactionResult<MemDiagnosticResource?> RedactResource(
        MemDiagnosticResource? source,
        IReadOnlyCollection<string>? exactSecrets = null)
    {
        if (source is null)
        {
            return new MemDiagnosticRedactionResult<MemDiagnosticResource?>(null, false, false);
        }

        var kindText = RedactText(source.Kind, 80, exactSecrets);
        var idText = RedactText(source.Id, 200, exactSecrets);
        var displayName = RedactText(source.DisplayName, 200, exactSecrets);
        var stackIdText = RedactText(source.StackId, 100, exactSecrets);
        var stackSlugText = RedactText(source.StackSlug, 120, exactSecrets);
        var serviceText = RedactText(source.Service, 100, exactSecrets);
        var workspacePath = RedactText(source.WorkspacePath, 300, exactSecrets);
        var kind = NormalizeIdentifier(kindText.Value, 80);
        var id = NormalizeIdentifier(idText.Value, 200);
        var stackId = NormalizeIdentifier(stackIdText.Value, 100);
        var stackSlug = NormalizeIdentifier(stackSlugText.Value, 120);
        var service = NormalizeIdentifier(serviceText.Value, 100);

        var changed = kindText.Changed || idText.Changed || displayName.Changed ||
                      stackIdText.Changed || stackSlugText.Changed || serviceText.Changed ||
                      workspacePath.Changed ||
                      !string.Equals(kindText.Value, kind, StringComparison.Ordinal) ||
                      !string.Equals(idText.Value, id, StringComparison.Ordinal) ||
                      !string.Equals(stackIdText.Value, stackId, StringComparison.Ordinal) ||
                      !string.Equals(stackSlugText.Value, stackSlug, StringComparison.Ordinal) ||
                      !string.Equals(serviceText.Value, service, StringComparison.Ordinal);
        var truncated = kindText.Truncated || idText.Truncated || displayName.Truncated ||
                        stackIdText.Truncated || stackSlugText.Truncated || serviceText.Truncated ||
                        workspacePath.Truncated;

        if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(id))
        {
            return new MemDiagnosticRedactionResult<MemDiagnosticResource?>(
                null,
                Changed: true,
                Truncated: truncated);
        }

        return new MemDiagnosticRedactionResult<MemDiagnosticResource?>(
            new MemDiagnosticResource(
                kind,
                id,
                EmptyToNull(displayName.Value),
                EmptyToNull(stackId),
                EmptyToNull(stackSlug),
                EmptyToNull(service),
                EmptyToNull(workspacePath.Value)),
            changed,
            truncated);
    }

    internal MemDiagnosticEvent RedactStoredEvent(MemDiagnosticEvent source)
    {
        var message = RedactText(source.Message);
        var suggestedAction = RedactText(source.SuggestedAction, 1500);
        var resource = RedactResource(source.Resource);
        var expected = RedactDictionary(source.Expected);
        var observed = RedactDictionary(source.Observed);
        var details = RedactDictionary(source.Details);
        var stackBudget = new StackBudget(_options.MaximumStackTraceCharacters);
        var projectedException = source.Exception is null
            ? new StoredExceptionRedactionResult(null, false, false)
            : RedactStoredException(source.Exception, 0, stackBudget);

        return source with
        {
            EventId = NormalizeIdentifier(source.EventId, 80),
            Severity = NormalizeIdentifier(source.Severity, 32).ToLowerInvariant(),
            EventCode = NormalizeIdentifier(source.EventCode, 200),
            Source = NormalizeIdentifier(source.Source, 100),
            Feature = NormalizeIdentifier(source.Feature, 100),
            Stage = EmptyToNull(NormalizeIdentifier(source.Stage, 120)),
            Message = message.Value,
            IncidentId = EmptyToNull(NormalizeIdentifier(source.IncidentId, 80)),
            TraceId = EmptyToNull(NormalizeIdentifier(source.TraceId, 80)),
            SpanId = EmptyToNull(NormalizeIdentifier(source.SpanId, 40)),
            RequestId = EmptyToNull(NormalizeIdentifier(source.RequestId, 160)),
            CorrelationId = EmptyToNull(NormalizeIdentifier(source.CorrelationId, 128)),
            Resource = resource.Value,
            Expected = expected.Value,
            Observed = observed.Value,
            Details = details.Value,
            Exception = projectedException.Exception,
            SuggestedAction = EmptyToNull(suggestedAction.Value),
            RedactionsApplied = true,
            Truncated = source.Truncated || message.Truncated || suggestedAction.Truncated ||
                        resource.Truncated || expected.Truncated || observed.Truncated || details.Truncated ||
                        projectedException.Truncated
        };
    }

    internal static string NormalizeIdentifier(string? value, int maximumCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var characters = value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-' or ':')
            .Take(maximumCharacters)
            .ToArray();
        return new string(characters);
    }

    private StoredExceptionRedactionResult RedactStoredException(
        MemDiagnosticException source,
        int depth,
        StackBudget stackBudget)
    {
        var message = RedactText(source.Message);
        var stack = RedactText(
            source.StackTrace,
            stackBudget.Remaining,
            sanitizeSourcePaths: true);
        stackBudget.Remaining = Math.Max(0, stackBudget.Remaining - stack.Value.Length);

        var changed = message.Changed || stack.Changed;
        var truncated = message.Truncated || stack.Truncated;
        var innerExceptions = new List<MemDiagnosticException>();
        var sourceInners = source.InnerExceptions ?? Array.Empty<MemDiagnosticException>();
        if (depth + 1 < _options.MaximumExceptionDepth)
        {
            var allowedInnerCount = _options.MaximumExceptionDepth - depth - 1;
            foreach (var inner in sourceInners.Take(allowedInnerCount))
            {
                if (inner is null)
                {
                    changed = true;
                    continue;
                }

                var projected = RedactStoredException(inner, depth + 1, stackBudget);
                if (projected.Exception is not null)
                {
                    innerExceptions.Add(projected.Exception);
                }
                changed |= projected.Changed;
                truncated |= projected.Truncated;
            }

            truncated |= sourceInners.Count > allowedInnerCount;
        }
        else
        {
            truncated |= sourceInners.Count > 0;
        }

        return new StoredExceptionRedactionResult(
            new MemDiagnosticException(
                NormalizeIdentifier(source.Type, 300),
                message.Value,
                EmptyToNull(stack.Value),
                innerExceptions),
            changed,
            truncated);
    }

    private static string NormalizeText(string value)
    {
        var characters = value
            .Where(character => !char.IsControl(character) || character is '\r' or '\n' or '\t')
            .ToArray();

        return new string(characters)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static IEnumerable<string> NormalizeExactSecrets(
        IReadOnlyCollection<string>? exactSecrets) =>
        exactSecrets?
            .Where(secret => !string.IsNullOrWhiteSpace(secret))
            .Select(secret => secret.Trim())
            .Select(secret => secret.Length <= 4096 ? secret : secret[..4096])
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(secret => secret.Length)
            .Take(64)
        ?? Enumerable.Empty<string>();

    private static string ReplaceAndTrack(
        Regex regex,
        string value,
        string replacement,
        ref bool changed)
    {
        var replaced = regex.Replace(value, replacement);
        if (!string.Equals(value, replaced, StringComparison.Ordinal))
        {
            changed = true;
        }

        return replaced;
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = new string(key
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

        return SensitiveKeyFragments.Any(fragment =>
            normalized.Contains(fragment, StringComparison.Ordinal));
    }

    private static string SanitizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-')
            .Take(100)
            .ToArray());
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record StoredExceptionRedactionResult(
        MemDiagnosticException? Exception,
        bool Changed,
        bool Truncated);

    private sealed class StackBudget(int remaining)
    {
        public int Remaining { get; set; } = remaining;
    }
}

internal sealed record MemDiagnosticRedactionResult<T>(
    T Value,
    bool Changed,
    bool Truncated);
