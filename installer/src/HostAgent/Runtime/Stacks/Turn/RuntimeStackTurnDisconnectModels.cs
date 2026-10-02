namespace HostAgent.Runtime.Stacks.Turn;

public static class RuntimeStackTurnDisconnectStatuses
{
    public const string Ready = "ready";
    public const string NoChange = "no_change";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string CandidateRejected = "candidate_rejected";
    public const string RolledBack = "rolled_back";
    public const string Failed = "failed";
    public const string Unresolved = "unresolved";
}

public sealed record RuntimeStackTurnDisconnectPlan(
    bool ConfigurationChangeRequired,
    bool RestartRequired,
    string Detail);

public sealed record RuntimeStackTurnDisconnectReviewResponse(
    string Source,
    string Status,
    Guid RuntimeStackId,
    string Slug,
    string MatrixServerName,
    bool ConfigurationChangeRequired,
    bool RestartRequired,
    string? PlatformPublicHost,
    IReadOnlyList<string> TurnUris,
    string CurrentConfigurationSha256,
    string ReviewHash,
    string ConfirmationText,
    IReadOnlyList<string> Consequences);

public sealed record RuntimeStackTurnDisconnectRequest(
    string? ReviewHash,
    string? IdempotencyKey,
    bool ConfirmDisconnectFromPlatformTurn);

public sealed record RuntimeStackTurnDisconnectResponse(
    string Source,
    string Status,
    Guid OperationId,
    Guid RuntimeStackId,
    string Slug,
    bool ConfigurationChanged,
    bool MatrixRestarted,
    bool RollbackAttempted,
    bool? RollbackSucceeded,
    string? StateAfter,
    string? ErrorCode,
    string Detail);
