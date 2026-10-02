using System.Text.RegularExpressions;

namespace HostAgent.Runtime.Backups.Observability;

public static class RestoreLogSeverities
{
    public const string Trace = "trace";
    public const string Information = "information";
    public const string Warning = "warning";
    public const string Error = "error";
    public const string Critical = "critical";

    public static bool IsKnown(string value) => value is
        Trace or Information or Warning or Error or Critical;

    public static bool IsError(string value) => value is Error or Critical;
}

/// <summary>
/// Append-only, safe-to-render event stored as one NDJSON line beneath the
/// restore attempt workspace. Events must never contain credentials, private
/// keys, access tokens, connection strings, or raw command output.
/// </summary>
public sealed record RestoreLogEvent(
    int SchemaVersion,
    string EventId,
    DateTimeOffset TimestampUtc,
    string RestoreSessionId,
    Guid? OperationId,
    string Stage,
    string Severity,
    string EventCode,
    string Message,
    Dictionary<string, string>? Details);

public sealed record RestoreLogQuery(
    int Page,
    int PageSize,
    string? Severity,
    string? Stage,
    string? Search);

public sealed record RestoreLogSummary(
    int TotalEvents,
    int WarningCount,
    int ErrorCount,
    RestoreLogEvent? LatestEvent,
    RestoreLogEvent? LatestWarningOrError);

public sealed record RestoreLogPage(
    string RestoreSessionId,
    int Page,
    int PageSize,
    int TotalEvents,
    int TotalPages,
    RestoreLogSummary Summary,
    IReadOnlyList<RestoreLogEvent> Events,
    IReadOnlyList<string> Warnings);

public sealed record RestoreSupportReportSource(
    string SourceKind,
    string SourceKey,
    string CatalogEntryId,
    string SourceDisplayName,
    string SourceOriginKind,
    string? SourceStackSlug,
    string? SourceBackupId,
    bool SourceDeleted);

public sealed record RestoreSupportReportTarget(
    string? TargetStackSlug,
    string? MatrixHost,
    string? ElementHost);

public sealed record RestoreSupportReportAttempt(
    string Status,
    string CurrentStage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? TerminalAtUtc,
    string? LastErrorCode,
    string? LastErrorSummary,
    int WarningCount,
    int ErrorCount);

public sealed record RestoreSupportReportOperation(
    Guid OperationId,
    string Operation,
    string Status,
    string? CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastError);

/// <summary>
/// Curated redacted support payload. It deliberately excludes database dumps,
/// configuration files, credentials, private keys, and unrestricted raw logs.
/// </summary>
public sealed record RestoreSupportReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string MemVersion,
    string RestoreSessionId,
    RestoreSupportReportAttempt Attempt,
    RestoreSupportReportSource Source,
    RestoreSupportReportTarget Target,
    RestoreLogSummary Logs,
    IReadOnlyList<RestoreSupportReportOperation> Operations,
    IReadOnlyList<RestoreLogEvent> RecentEvents,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Exact source revision of the Control Plane process that generated this
    /// report when release/runtime provenance provides one. Kept optional so
    /// support reports generated before this field existed remain readable.
    /// </summary>
    public string? MemCommit { get; init; }
}

public sealed record RestoreSupportReportGenerationResult(
    string RestoreSessionId,
    string ReportPath,
    RestoreSupportReport Report);

/// <summary>
/// Applies conservative, repeatable redaction both when an event is recorded
/// and again when a support report is generated. Prefer allowlisted fields at
/// call sites; this is a final safety net, not a licence to log raw output.
/// </summary>
public static class RestoreDiagnosticRedactor
{
    private static readonly string[] SensitiveKeyFragments =
    [
        "password", "passwd", "secret", "token", "authorization", "credential",
        "connectionstring", "connection-string", "privatekey", "private-key",
        "signingkey", "signing-key", "sharedkey", "shared-key"
    ];

    private static readonly Regex SensitiveAssignment = new(
        @"(?i)\b(password|passwd|secret|token|authorization|credential|private[_-]?key|signing[_-]?key)\b\s*[:=]\s*[^\s,;]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex UriWithCredentials = new(
        @"(?i)\b[a-z][a-z0-9+.-]*://[^\s/@:]+:[^\s/@]+@",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static Dictionary<string, string>? RedactDetails(
        IReadOnlyDictionary<string, string?>? details)
    {
        if (details is null || details.Count == 0)
        {
            return null;
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in details.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var key = SanitizeKey(pair.Key);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            result[key] = IsSensitiveKey(key)
                ? "[redacted]"
                : RedactText(pair.Value, 1500);
        }

        return result.Count == 0 ? null : result;
    }

    public static RestoreLogEvent RedactEvent(RestoreLogEvent source) =>
        source with
        {
            EventId = Trim(source.EventId, 80),
            RestoreSessionId = Trim(source.RestoreSessionId, 160),
            Stage = Trim(source.Stage, 120),
            Severity = Trim(source.Severity, 32),
            EventCode = Trim(source.EventCode, 200),
            Message = RedactText(source.Message, 1500),
            Details = RedactDetails(source.Details)
        };

    public static string RedactText(string? value, int maxLength = 1500)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = new string(value
            .Where(ch => !char.IsControl(ch) || ch is '\r' or '\n' or '\t')
            .ToArray())
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();

        normalized = SensitiveAssignment.Replace(normalized, "[redacted]");
        normalized = UriWithCredentials.Replace(normalized, "[redacted-uri]@");

        return Trim(normalized, maxLength);
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return SensitiveKeyFragments.Any(fragment =>
            normalized.Contains(fragment.Replace("_", string.Empty).Replace("-", string.Empty), StringComparison.Ordinal));
    }

    private static string SanitizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Trim()
            .Where(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')
            .Take(100)
            .ToArray();

        return new string(chars);
    }

    private static string Trim(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
