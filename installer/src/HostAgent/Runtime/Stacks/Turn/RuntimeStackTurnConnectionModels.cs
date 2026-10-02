namespace HostAgent.Runtime.Stacks.Turn;

public static class RuntimeStackTurnConnectModes
{
    public const string Configure = "configure";
    public const string AdoptExisting = "adopt-existing";
    public const string ReplaceExternal = "replace-external";
    public const string NoChange = "no-change";
}

public static class RuntimeStackTurnConnectStatuses
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

public sealed record RuntimeStackTurnConnectPlan(
    string Mode,
    bool ConfigurationChangeRequired,
    bool RestartRequired,
    string Detail);

public sealed record RuntimeStackTurnConnectReviewResponse(
    string Source,
    string Status,
    Guid RuntimeStackId,
    string Slug,
    string MatrixServerName,
    string Mode,
    bool ConfigurationChangeRequired,
    bool RestartRequired,
    string PlatformPublicHost,
    IReadOnlyList<string> TurnUris,
    string UserLifetime,
    bool AllowGuests,
    string CurrentConfigurationSha256,
    string ReviewHash,
    string ConfirmationText,
    IReadOnlyList<string> Consequences);

public sealed record RuntimeStackTurnConnectRequest(
    string? ReviewHash,
    string? IdempotencyKey,
    bool ConfirmConnectToPlatformTurn,
    bool ConfirmReplaceExternalTurn = false);

public sealed record RuntimeStackTurnConnectResponse(
    string Source,
    string Status,
    Guid OperationId,
    Guid RuntimeStackId,
    string Slug,
    string Mode,
    bool ConfigurationChanged,
    bool MatrixRestarted,
    bool RollbackAttempted,
    bool? RollbackSucceeded,
    string? StateAfter,
    string? ErrorCode,
    string Detail);

public sealed class RuntimeStackTurnConnectionException : InvalidOperationException
{
    public RuntimeStackTurnConnectionException(string code, string detail)
        : base(detail)
    {
        Code = code;
    }

    public string Code { get; }
}
