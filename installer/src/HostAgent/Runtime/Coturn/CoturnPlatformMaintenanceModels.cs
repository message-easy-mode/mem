namespace HostAgent.Runtime.Coturn;

public static class CoturnPlatformMaintenanceActions
{
    public const string RestartVerify = "restart-verify";
    public const string Repair = "repair";

    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            RestartVerify => RestartVerify,
            Repair => Repair,
            _ => throw new CoturnPlatformMaintenanceException(
                "coturn_maintenance_action_invalid",
                "Coturn maintenance action must be 'restart-verify' or 'repair'.",
                StatusCodes.Status400BadRequest)
        };
    }
}

public sealed record CoturnPlatformMaintenanceRequest(
    string? Action,
    string? ExternalIp = null,
    string? IdempotencyKey = null);

public sealed record CoturnPlatformMaintenanceAcceptedResponse(
    Guid OperationId,
    string Action,
    string Status,
    string PollUrl,
    bool ReusedExistingOperation);

public sealed record CoturnPlatformMaintenanceOperationResponse(
    Guid OperationId,
    string Action,
    string Status,
    string? CurrentStep,
    DateTimeOffset RequestedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? LastError,
    bool Terminal,
    bool Succeeded);

public sealed record CoturnPlatformMaintenanceActiveResponse(
    string Source,
    bool Active,
    CoturnPlatformMaintenanceOperationResponse? Operation);

public static class CoturnPlatformMaintenancePolicy
{
    public static bool CanRestartAndVerify(CoturnRuntimeResponse current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!current.ContainerExists ||
            !current.Running ||
            !current.OwnershipVerified ||
            string.IsNullOrWhiteSpace(current.ContainerId) ||
            !string.Equals(
                current.ProtectedEvidenceAccess,
                CoturnProtectedEvidenceAccess.Available,
                StringComparison.Ordinal) ||
            !current.ImageApproved ||
            !current.SecretPresent ||
            !current.SecretFilePermissionsApplied ||
            current.DomainDriftDetected ||
            !current.RelayPortsPublished ||
            !current.SecurityPolicyApplied)
        {
            return false;
        }

        if (current.RuntimeExact &&
            string.Equals(
                current.OperatorStatus,
                CoturnOperatorStatuses.RuntimeReady,
                StringComparison.Ordinal))
        {
            return true;
        }

        // Restart-policy drift is the one safe Docker-runtime correction that
        // Restart & Verify may apply in-place before restarting the same owned
        // container. Every other runtime/configuration drift belongs to Repair.
        return current.RuntimeDrift.Count == 1 &&
            string.Equals(
                current.RuntimeDrift[0],
                "restart-policy",
                StringComparison.Ordinal);
    }
}

public sealed class CoturnPlatformMaintenanceException(
    string code,
    string message,
    int statusCode) : InvalidOperationException(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public interface ICoturnPlatformMaintenanceRuntime
{
    Task<CoturnRuntimeResponse> InspectAsync(CancellationToken cancellationToken);

    Task<CoturnRuntimeResponse> RestartOwnedAsync(CancellationToken cancellationToken);

    Task<CoturnRuntimeResponse> RepairAsync(
        string? externalIp,
        CancellationToken cancellationToken);

    Task<CoturnCheckResponse> CheckAsync(CancellationToken cancellationToken);

    Task<CoturnLogsResponse> GetRecentLogsAsync(
        int? tail,
        CancellationToken cancellationToken);
}

public interface ICoturnPlatformMaintenanceDispatcher
{
    bool TryEnqueue(
        Guid operationId,
        CoturnPlatformMaintenanceRequest request);
}
