namespace HostAgent.Matrix.Federation.PrivateNetwork;

public static class PrivateNetworkExceptionActions
{
    public const string Add = "add";
    public const string Remove = "remove";
}

public static class PrivateNetworkConfigurationStates
{
    public const string Healthy = "healthy";
    public const string CustomUnsupported = "custom_unsupported";
    public const string Unavailable = "unavailable";
}

public sealed record PrivateNetworkFederationStackStateResponse(
    Guid RuntimeStackId,
    string Slug,
    string MatrixServerName,
    string FederationMode,
    string FederationConfigurationState,
    string ConfigurationState,
    IReadOnlyList<string> CurrentExceptions,
    bool MatrixContainerRunning,
    Guid? LatestOperationId,
    string? LatestOperationStatus,
    string? LatestOperationStep,
    string? ProblemCode,
    string? Detail);

public sealed record PrivateNetworkFederationInventoryResponse(
    string Source,
    string Status,
    IReadOnlyList<PrivateNetworkFederationStackStateResponse> Stacks);

public sealed record PrivateNetworkFederationReviewRequest(
    string? Address,
    string? Action);

public sealed record PrivateNetworkFederationReviewResponse(
    string Source,
    string Status,
    Guid RuntimeStackId,
    string Slug,
    string MatrixServerName,
    string Action,
    string CanonicalAddress,
    string CanonicalCidr,
    IReadOnlyList<string> CurrentExceptions,
    IReadOnlyList<string> ProposedExceptions,
    bool RestartRequired,
    bool NoChange,
    string ReviewHash,
    string ConfirmationText);

public sealed record PrivateNetworkFederationApplyRequest(
    string? Address,
    string? Action,
    string? ReviewHash,
    string? IdempotencyKey);

public sealed record PrivateNetworkFederationApplyResponse(
    string Source,
    string Status,
    Guid OperationId,
    Guid RuntimeStackId,
    string Slug,
    string Action,
    string CanonicalCidr,
    IReadOnlyList<string> ObservedExceptions,
    bool RollbackAttempted,
    bool? RollbackSucceeded,
    string? ErrorCode,
    string Detail);

public sealed class PrivateNetworkFederationException : InvalidOperationException
{
    public PrivateNetworkFederationException(string code, string detail)
        : base(detail)
    {
        Code = code;
    }

    public string Code { get; }
}
