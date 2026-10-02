using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace HostAgent.Runtime.Coturn;

public static class CoturnDiagnosticsPolicy
{
    public const int DefaultLogTail = 100;
    public const int MinimumLogTail = 20;
    public const int MaximumLogTail = 200;
    public const int MaximumDiagnosticLines = 200;
    public const int MaximumDiagnosticCharacters = 12_000;
    public const int FunctionalCheckFreshForSeconds = 30 * 60;

    private static readonly Regex SecretAssignmentPattern = new(
        @"\b(static-auth-secret|password|passwd|token|authorization|credential)\b(\s*[=:]\s*)([^\r\n]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex TurnRestUsernamePattern = new(
        @"\b\d{10,}:[A-Za-z0-9._@-]+\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex CommandCredentialPattern = new(
        @"(?<!\S)(-[uUwW])\s+([^\s]+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DockerMultiplexHeaderPattern = new(
        @"[\u0001\u0002]\u0000{3}[\s\S]{4}",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AnsiEscapePattern = new(
        @"\x1B(?:\[[0-?]*[ -/]*[@-~]|\][^\x07]*(?:\x07|\x1B\\))",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static int NormalizeLogTail(int? value) =>
        Math.Clamp(
            value ?? DefaultLogTail,
            MinimumLogTail,
            MaximumLogTail);

    public static CoturnTemporaryCredential CreateTemporaryCredential(
        string sharedSecret,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(sharedSecret))
        {
            throw new InvalidOperationException(
                "Coturn allocation probing requires the protected platform shared secret.");
        }

        var expiresAt = now.AddMinutes(2);
        var username = $"{expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}:mem-platform-check";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(sharedSecret));
        var password = Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(username)));

        return new CoturnTemporaryCredential(
            Username: username,
            Password: password,
            ExpiresAtUtc: expiresAt);
    }

    public static CoturnSanitizedText SanitizeDiagnosticText(
        string? value,
        params string?[] exactSensitiveValues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new CoturnSanitizedText(
                Content: string.Empty,
                ReturnedLines: 0,
                Truncated: false);
        }

        var safe = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        safe = DockerMultiplexHeaderPattern.Replace(safe, string.Empty);
        safe = AnsiEscapePattern.Replace(safe, string.Empty);
        safe = string.Concat(
            safe.Where(character =>
                character is '\n' or '\t' ||
                (!char.IsControl(character) && character != '\uFFFD')));

        foreach (var sensitiveValue in exactSensitiveValues
                     .Where(item => !string.IsNullOrWhiteSpace(item))
                     .Distinct(StringComparer.Ordinal))
        {
            safe = safe.Replace(
                sensitiveValue!,
                "[redacted]",
                StringComparison.Ordinal);
        }

        safe = SecretAssignmentPattern.Replace(
            safe,
            match => $"{match.Groups[1].Value}{match.Groups[2].Value}[redacted]");
        safe = CommandCredentialPattern.Replace(
            safe,
            match => $"{match.Groups[1].Value} [redacted]");
        safe = TurnRestUsernamePattern.Replace(safe, "[redacted-turn-user]");

        var lines = safe.Split('\n');
        var truncated = lines.Length > MaximumDiagnosticLines;
        if (truncated)
        {
            lines = lines[^MaximumDiagnosticLines..];
        }

        safe = string.Join("\n", lines).Trim();
        if (safe.Length > MaximumDiagnosticCharacters)
        {
            safe = safe[^MaximumDiagnosticCharacters..];
            truncated = true;
        }

        var returnedLines = string.IsNullOrEmpty(safe)
            ? 0
            : safe.Count(character => character == '\n') + 1;

        return new CoturnSanitizedText(
            Content: safe,
            ReturnedLines: returnedLines,
            Truncated: truncated);
    }

    public static CoturnCheckFreshnessEvaluation EvaluateFreshness(
        CoturnCheckResponse? result,
        DateTimeOffset now,
        bool evidenceUnavailable = false,
        bool runtimeChanged = false)
    {
        if (evidenceUnavailable)
        {
            return new CoturnCheckFreshnessEvaluation(
                CoturnCheckFreshnessStatuses.Unavailable,
                Fresh: false,
                FreshUntilUtc: null);
        }

        if (result is null)
        {
            return new CoturnCheckFreshnessEvaluation(
                CoturnCheckFreshnessStatuses.NotChecked,
                Fresh: false,
                FreshUntilUtc: null);
        }

        if (runtimeChanged)
        {
            return new CoturnCheckFreshnessEvaluation(
                CoturnCheckFreshnessStatuses.RuntimeChanged,
                Fresh: false,
                FreshUntilUtc: result.CheckedAtUtc.AddSeconds(
                    FunctionalCheckFreshForSeconds));
        }

        var freshUntilUtc = result.CheckedAtUtc.AddSeconds(
            FunctionalCheckFreshForSeconds);
        // Older checks only verified the configured mode, not the address that
        // Coturn actually advertised. Do not reuse their green result after upgrade.
        var fresh = now <= freshUntilUtc &&
                    CoturnRelayAddressPolicy.HasCurrentEvidence(result);

        return new CoturnCheckFreshnessEvaluation(
            fresh
                ? CoturnCheckFreshnessStatuses.Fresh
                : CoturnCheckFreshnessStatuses.Stale,
            Fresh: fresh,
            FreshUntilUtc: freshUntilUtc);
    }


    public static CoturnFunctionalCheckDiagnosticPlan PlanFunctionalCheckDiagnostic(
        CoturnCheckResponse current,
        CoturnCheckResponse? previous)
    {
        ArgumentNullException.ThrowIfNull(current);

        var currentFailed = string.Equals(
            current.Status,
            CoturnCheckStatuses.Failed,
            StringComparison.Ordinal);
        var currentWarning = string.Equals(
            current.Status,
            CoturnCheckStatuses.Warning,
            StringComparison.Ordinal);
        var currentPassed = string.Equals(
            current.Status,
            CoturnCheckStatuses.Passed,
            StringComparison.Ordinal);
        var previousFailed = string.Equals(
            previous?.Status,
            CoturnCheckStatuses.Failed,
            StringComparison.Ordinal);
        var previousWarning = string.Equals(
            previous?.Status,
            CoturnCheckStatuses.Warning,
            StringComparison.Ordinal);
        var previousIncidentId = string.IsNullOrWhiteSpace(previous?.IncidentId)
            ? null
            : previous!.IncidentId;
        var previousFailureHasIncident =
            previousFailed &&
            previousIncidentId is not null;

        // A warning-level Coturn result is still usable service evidence when
        // allocation succeeded but another meaningful verification is incomplete.
        // It must not inherit or reopen a previous failure Incident merely
        // because it follows that failure. Only a continuing failed check reuses
        // the prior failure Incident; a fully passed check records recovery.
        var recovered = currentPassed &&
                        previousFailureHasIncident;
        var relatedIncidentId =
            (currentFailed && previousFailureHasIncident) ||
            recovered
                ? previousIncidentId
                : null;

        if (currentFailed)
        {
            return new CoturnFunctionalCheckDiagnosticPlan(
                EventCode: CoturnFunctionalCheckEventCodes.Failed,
                Severity: "error",
                IncidentId: relatedIncidentId,
                CreateIncident: relatedIncidentId is null,
                Recovered: false);
        }

        if (recovered)
        {
            return new CoturnFunctionalCheckDiagnosticPlan(
                EventCode: CoturnFunctionalCheckEventCodes.Recovered,
                Severity: "information",
                IncidentId: relatedIncidentId,
                CreateIncident: false,
                Recovered: true);
        }

        if (currentWarning)
        {
            return new CoturnFunctionalCheckDiagnosticPlan(
                EventCode: CoturnFunctionalCheckEventCodes.Warning,
                Severity: "warning",
                IncidentId: relatedIncidentId,
                CreateIncident: false,
                Recovered: false);
        }

        return new CoturnFunctionalCheckDiagnosticPlan(
            EventCode: CoturnFunctionalCheckEventCodes.Passed,
            Severity: "information",
            IncidentId: null,
            CreateIncident: false,
            Recovered: false);
    }

    public static string SummarizeStatus(
        IEnumerable<CoturnCheckItem> checks,
        CoturnAllocationProbeResponse allocation)
    {
        var statuses = checks
            .Select(check => check.Status)
            .Append(allocation.Status)
            .ToArray();

        if (statuses.Contains(CoturnCheckStatuses.Failed, StringComparer.Ordinal))
        {
            return CoturnCheckStatuses.Failed;
        }

        if (statuses.Any(status =>
                string.Equals(status, CoturnCheckStatuses.Warning, StringComparison.Ordinal) ||
                string.Equals(status, CoturnCheckStatuses.NotRun, StringComparison.Ordinal)))
        {
            return CoturnCheckStatuses.Warning;
        }

        return CoturnCheckStatuses.Passed;
    }
}

public static class CoturnFunctionalCheckEventCodes
{
    public const string Passed = "coturn.functional_check.passed";
    public const string Warning = "coturn.functional_check.warning";
    public const string Failed = "coturn.functional_check.failed";
    public const string Recovered = "coturn.functional_check.recovered";
}

public sealed record CoturnFunctionalCheckDiagnosticPlan(
    string EventCode,
    string Severity,
    string? IncidentId,
    bool CreateIncident,
    bool Recovered);

public sealed record CoturnTemporaryCredential(
    string Username,
    string Password,
    DateTimeOffset ExpiresAtUtc);

public sealed record CoturnSanitizedText(
    string Content,
    int ReturnedLines,
    bool Truncated);
