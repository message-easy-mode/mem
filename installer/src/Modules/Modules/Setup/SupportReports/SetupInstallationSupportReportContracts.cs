using Shared.ControlPlane.Runtime;

namespace Modules.Setup.SupportReports;

public static class SetupSupportReportFormats
{
    public const string Json = "json";
    public const string Text = "text";

    public static bool IsSupported(string? value) =>
        string.Equals(value, Json, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Text, StringComparison.OrdinalIgnoreCase);

    public static string Normalize(string? value) =>
        string.Equals(value, Text, StringComparison.OrdinalIgnoreCase)
            ? Text
            : Json;
}

public sealed record SetupInstallationSupportReportRequest(
    string? TraceId = null,
    bool IncludeDockerEvidence = true,
    string Format = SetupSupportReportFormats.Json);

public sealed record SetupInstallationSupportReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string MemVersion,
    SetupSupportReportSelection Selection,
    MemControlPlaneRuntimeContextProjection RuntimeContext,
    SetupSupportLifecycle? Setup,
    SetupSupportReviewedPlan? ReviewedPlan,
    SetupSupportServerChecks? ServerChecks,
    IReadOnlyList<SetupSupportInstallStep> Steps,
    SetupSupportDiagnostics Diagnostics,
    SetupSupportRecovery Recovery,
    SetupSupportRedaction Redaction,
    bool Truncated,
    IReadOnlyList<string> Warnings);

public sealed record SetupSupportReportSelection(
    string Mode,
    Guid? InstallationId,
    string? TraceId);

public sealed record SetupSupportLifecycle(
    Guid InstallationId,
    string Status,
    string Stage,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string HandoffState,
    string? LastSafeError);

public sealed record SetupSupportReviewedPlan(
    string? Fingerprint,
    DateTimeOffset? ReviewedAtUtc,
    string? BaseDomain,
    string? WildcardCertificate,
    string? DnsZone,
    string? DnsProvider,
    string AcmeEnvironment,
    IReadOnlyList<string> Services,
    IReadOnlyDictionary<string, string> ApprovedImages,
    IReadOnlyList<string> Containers,
    IReadOnlyList<string> Networks,
    IReadOnlyList<string> Volumes,
    IReadOnlyDictionary<string, int> Ports,
    IReadOnlyList<string> SupportTools,
    IReadOnlyList<string> VerificationPlan,
    bool SecretsOmitted);

public sealed record SetupSupportServerChecks(
    string RunId,
    DateTimeOffset CompletedAtUtc,
    string RunStatus,
    int Passed,
    int Warnings,
    int Failed,
    int Skipped,
    int Unavailable,
    int Unknown,
    int BlockingIssueCount,
    IReadOnlyList<string> WarningCheckKeys,
    IReadOnlyList<string> UnavailableCheckKeys);

public sealed record SetupSupportInstallStep(
    int Sequence,
    string Name,
    string Status,
    int AttemptCount,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? SafeMessage,
    string? SafeError,
    bool RedactionsApplied,
    bool Truncated);

public sealed record SetupSupportDiagnostics(
    SetupSupportLoggingHealth LoggingHealth,
    IReadOnlyList<SetupSupportDiagnosticEvent> Events,
    IReadOnlyList<SetupSupportIncident> Incidents,
    SetupSupportDockerEvidence? DockerEvidence);

public sealed record SetupSupportLoggingHealth(
    string Status,
    string? LocalRecorderStatus,
    string SafeEventStoreStatus,
    DateTimeOffset? SafeEventStoreLastWriteAtUtc,
    long SafeEventStoreEventCount,
    IReadOnlyList<string> Warnings);

public sealed record SetupSupportDiagnosticEvent(
    string EventId,
    DateTimeOffset TimestampUtc,
    string Severity,
    string EventCode,
    string Source,
    string Feature,
    string? Stage,
    string Message,
    string? IncidentId,
    string? TraceId,
    string? CorrelationId,
    string? SuggestedAction,
    bool Retryable,
    bool RedactionsApplied,
    bool Truncated);

public sealed record SetupSupportIncident(
    string IncidentId,
    string Severity,
    string EventCode,
    string? Stage,
    DateTimeOffset LastSeenAtUtc,
    string? TraceId,
    bool Retryable);

public sealed record SetupSupportDockerEvidence(
    bool Available,
    string? WarningCode,
    string? ResourceKind,
    string? ResourceId,
    DateTimeOffset? ObservedAtUtc,
    SetupSupportDockerContainer? Container,
    SetupSupportDockerLogTail? LogTail,
    IReadOnlyList<string> Warnings);

public sealed record SetupSupportDockerContainer(
    string LogicalName,
    string ObservedState,
    long? ExitCode,
    string? Health,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    long RestartCount,
    string? Image);

public sealed record SetupSupportDockerLogTail(
    int RequestedLines,
    int ReturnedLines,
    int MaximumCharacters,
    string Content,
    bool Truncated,
    bool RedactionsApplied);

public sealed record SetupSupportRecovery(
    bool RetryAllowed,
    string RetryScope,
    string? RequiredOperatorAction,
    string SuggestedRoute,
    IReadOnlyList<string> ProvenComplete,
    IReadOnlyList<string> StateUncertain);

public sealed record SetupSupportRedaction(
    string PolicyVersion,
    bool RedactionsApplied,
    IReadOnlyList<string> OmittedContent);
