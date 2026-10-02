namespace HostAgent.Matrix.Federation;

public static class FederationModes
{
    public const string Public = "public";
    public const string Restricted = "restricted";
    public const string LocalOnly = "local_only";
    public const string Unknown = "unknown";
}

public static class FederationConfigurationStates
{
    public const string Healthy = "healthy";
    public const string Incomplete = "incomplete";
    public const string CustomUnsupported = "custom_unsupported";
    public const string Unavailable = "unavailable";
}

public static class FederationIngressModes
{
    public const string Normal = "normal";
    public const string LocalOnly = "local_only";
    public const string Missing = "missing";
    public const string CustomUnsupported = "custom_unsupported";
    public const string Unavailable = "unavailable";
}

public static class FederationCheckStatuses
{
    public const string Passed = "passed";
    public const string Warning = "warning";
    public const string Failed = "failed";
    public const string Unknown = "unknown";
}

public sealed record FederationCheckResponse(
    string Code,
    string Status,
    string Detail);

public sealed record FederationProblemResponse(
    string Code,
    string Detail);

public sealed record RuntimeStackFederationOperationSummaryResponse(
    Guid Id,
    string Status,
    string? CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record RuntimeStackFederationStateResponse(
    string Source,
    string Status,
    Guid RuntimeStackId,
    string Slug,
    string Mode,
    string ConfigurationState,
    IReadOnlyList<string> Allowlist,
    string EnforcementKind,
    bool MatrixContainerRunning,
    bool MatrixDirectHostPortExposed,
    string IngressMode,
    bool ServerWellKnownPublished,
    bool FederationPathsPubliclyForwarded,
    bool SigningKeyPathsPubliclyForwarded,
    bool CanonicalRouteEnabled,
    bool CanonicalRouteTargetsMatrix,
    bool CanonicalCertificatePresent,
    bool AlternateMatrixRouteDetected,
    string? StateFingerprint,
    RuntimeStackFederationOperationSummaryResponse? LatestOperation,
    IReadOnlyList<FederationCheckResponse> Checks,
    IReadOnlyList<FederationProblemResponse> Warnings,
    IReadOnlyList<FederationProblemResponse> Problems);

public sealed record RuntimeStackFederationPolicyRequest(
    string? Mode,
    IReadOnlyList<string>? Allowlist);

public sealed record FederationReviewWarningResponse(
    string Code,
    string Detail);

public sealed record RuntimeStackFederationReviewResponse(
    string Source,
    string Status,
    Guid RuntimeStackId,
    string Slug,
    string CurrentMode,
    string ProposedMode,
    IReadOnlyList<string> CurrentAllowlist,
    IReadOnlyList<string> CanonicalAllowlist,
    IReadOnlyList<string> AddedDomains,
    IReadOnlyList<string> RemovedDomains,
    bool RestartRequired,
    bool IngressChangeRequired,
    bool NoChange,
    IReadOnlyList<FederationReviewWarningResponse> Warnings,
    string ConfirmationText,
    string ReviewHash);

public sealed record RuntimeStackFederationApplyRequest(
    string? Mode,
    IReadOnlyList<string>? Allowlist,
    string? ReviewHash,
    string? IdempotencyKey);

public sealed record RuntimeStackFederationApplyResponse(
    string Source,
    string Status,
    Guid OperationId,
    string PreviousMode,
    string RequestedMode,
    string ObservedMode,
    bool RollbackAttempted,
    bool? RollbackSucceeded,
    IReadOnlyList<FederationCheckResponse> Checks,
    string? ErrorCode,
    string Detail);
