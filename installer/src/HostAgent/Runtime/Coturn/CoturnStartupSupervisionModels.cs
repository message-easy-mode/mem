using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Coturn;

public static class CoturnStartupSupervisionStatuses
{
    public const string Disabled = "disabled";
    public const string Waiting = "waiting";
    public const string Deferred = "deferred";
    public const string NotInstalled = "not-installed";
    public const string Verified = "verified";
    public const string Recovering = "recovering";
    public const string Recovered = "recovered";
    public const string RepairRequired = "repair-required";
    public const string Conflict = "conflict";
    public const string Cooldown = "cooldown";
    public const string Failed = "failed";
}

public static class CoturnStartupSupervisionDecisions
{
    public const string NotEvaluated = "not-evaluated";
    public const string NotInstalled = "not-installed";
    public const string Ready = "ready";
    public const string StartStopped = "start-stopped";
    public const string CorrectRestartPolicy = "correct-restart-policy";
    public const string RepairRequired = "repair-required";
    public const string Conflict = "conflict";
    public const string Unavailable = "unavailable";
}

public enum CoturnStartupSupervisionExecutionOutcome
{
    Finished = 0,
    DeferredNotExpected = 1,
    DeferredCurrentProcessMutation = 2
}

public interface ICoturnStartupSupervisionProcessor
{
    Task<CoturnStartupSupervisionExecutionOutcome> ExecuteAsync(
        CancellationToken cancellationToken);
}

public static class CoturnStartupSupervisionEventCodes
{
    public const string Started = "coturn.startup_recovery.started";
    public const string Succeeded = "coturn.startup_recovery.succeeded";
    public const string Failed = "coturn.startup_recovery.failed";
    public const string RepairRequired = "coturn.repair.required";
    public const string ContainerStopped = "coturn.container.stopped";
    public const string RestartPolicyDrift = "coturn.restart_policy.drift";
    public const string ForeignContainerConflict = "coturn.foreign_container.conflict";
}

public sealed record CoturnStartupSupervisionResponse(
    string Source,
    string Status,
    string RuntimeMode,
    bool Enabled,
    string Decision,
    bool MutationPerformed,
    bool AutomaticRestartAttempted,
    bool CooldownActive,
    DateTimeOffset? CooldownUntilUtc,
    Guid? OperationId,
    DateTimeOffset ObservedAtUtc,
    string? Detail);

public sealed record CoturnStartupRecoveryMutationResult(
    CoturnRuntimeResponse Runtime,
    bool MutationPerformed,
    bool ContainerStarted,
    bool RestartPolicyCorrected);

public interface ICoturnStartupSupervisionRuntime
{
    Task<CoturnRuntimeResponse> InspectAsync(CancellationToken cancellationToken);

    Task<CoturnStartupRecoveryMutationResult> RecoverStartupAsync(
        string decision,
        string expectedContainerId,
        CancellationToken cancellationToken);

    Task<CoturnRuntimeResponse> RestartOwnedAsync(CancellationToken cancellationToken);

    Task<CoturnCheckResponse> CheckAsync(CancellationToken cancellationToken);

    Task<CoturnLogsResponse> GetRecentLogsAsync(
        int? tail,
        CancellationToken cancellationToken);
}

public static class CoturnStartupSupervisionPolicy
{
    private static readonly HashSet<string> SafeStoppedDrift = new(StringComparer.Ordinal)
    {
        "runtime-state",
        "restart-policy"
    };

    private static readonly HashSet<string> TransientStartupDrift = new(StringComparer.Ordinal)
    {
        "docker-inspection",
        "runtime-state",
        "gateway-network",
        "network-aliases"
    };

    public static bool IsEnabled(MemControlPlaneRuntimeContext runtimeContext) =>
        MemRuntimeModes.IsContainerized(runtimeContext.RuntimeMode) &&
        runtimeContext.MutationsAllowed;

    public static string Evaluate(CoturnRuntimeResponse current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (!current.ContainerExists)
        {
            if (!string.Equals(
                    current.ProtectedEvidenceAccess,
                    CoturnProtectedEvidenceAccess.Available,
                    StringComparison.Ordinal))
            {
                return CoturnStartupSupervisionDecisions.Unavailable;
            }

            return current.SecretPresent || current.ConfigurationPresent
                ? CoturnStartupSupervisionDecisions.RepairRequired
                : CoturnStartupSupervisionDecisions.NotInstalled;
        }

        if (!current.OwnershipVerified)
        {
            return CoturnStartupSupervisionDecisions.Conflict;
        }

        if (!string.Equals(
                current.ProtectedEvidenceAccess,
                CoturnProtectedEvidenceAccess.Available,
                StringComparison.Ordinal))
        {
            return CoturnStartupSupervisionDecisions.Unavailable;
        }

        if (current.DockerRuntime is null ||
            string.Equals(
                current.OperatorStatus,
                CoturnOperatorStatuses.Unknown,
                StringComparison.Ordinal))
        {
            return CoturnStartupSupervisionDecisions.Unavailable;
        }

        if (HasUnsafeRuntimeStateEvidence(current.DockerRuntime))
        {
            return CoturnStartupSupervisionDecisions.Unavailable;
        }

        var exactProtectedAndDockerStructure =
            HasExactProtectedAndDockerStructure(current);

        if (current.Running &&
            current.RuntimeExact &&
            exactProtectedAndDockerStructure &&
            string.Equals(
                current.OperatorStatus,
                CoturnOperatorStatuses.RuntimeReady,
                StringComparison.Ordinal))
        {
            return CoturnStartupSupervisionDecisions.Ready;
        }

        if (!exactProtectedAndDockerStructure)
        {
            return CoturnStartupSupervisionDecisions.RepairRequired;
        }

        if (current.Running &&
            current.RuntimeDrift.Count == 1 &&
            string.Equals(
                current.RuntimeDrift[0],
                "restart-policy",
                StringComparison.Ordinal))
        {
            return CoturnStartupSupervisionDecisions.CorrectRestartPolicy;
        }

        if (!current.Running &&
            IsSafeStoppedState(current) &&
            current.RuntimeDrift.Count > 0 &&
            current.RuntimeDrift.All(SafeStoppedDrift.Contains) &&
            current.RuntimeDrift.Contains("runtime-state", StringComparer.Ordinal))
        {
            return CoturnStartupSupervisionDecisions.StartStopped;
        }

        return CoturnStartupSupervisionDecisions.RepairRequired;
    }

    public static bool CouldBeTransientDuringStartup(CoturnRuntimeResponse current)
    {
        ArgumentNullException.ThrowIfNull(current);

        if (string.Equals(
                current.OperatorStatus,
                CoturnOperatorStatuses.Unknown,
                StringComparison.Ordinal))
        {
            return true;
        }

        if (current.DockerRuntime?.Restarting == true)
        {
            return true;
        }

        if (current.RuntimeDrift.Count == 0)
        {
            return false;
        }

        var hasStartupInfrastructureDrift = current.RuntimeDrift.Any(item =>
            string.Equals(item, "docker-inspection", StringComparison.Ordinal) ||
            string.Equals(item, "gateway-network", StringComparison.Ordinal) ||
            string.Equals(item, "network-aliases", StringComparison.Ordinal));

        return hasStartupInfrastructureDrift &&
            current.RuntimeDrift.All(TransientStartupDrift.Contains);
    }

    private static bool IsSafeStoppedState(CoturnRuntimeResponse current)
    {
        var docker = current.DockerRuntime;
        if (docker is null)
        {
            return false;
        }

        var state = current.DockerState?.Trim();
        var stoppedState =
            string.Equals(state, "exited", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "created", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(state, "stopped", StringComparison.OrdinalIgnoreCase);

        return stoppedState &&
            docker.ExitCode == 0 &&
            !HasUnsafeRuntimeStateEvidence(docker);
    }

    private static bool HasUnsafeRuntimeStateEvidence(
        CoturnDockerRuntimeEvidence docker) =>
        docker.Restarting ||
        docker.Paused ||
        docker.OomKilled ||
        docker.Dead ||
        docker.StateErrorPresent ||
        docker.ExitCode != 0;

    private static bool HasExactProtectedAndDockerStructure(
        CoturnRuntimeResponse current)
    {
        var docker = current.DockerRuntime;
        return docker is not null &&
            current.ImageApproved &&
            current.SecretPresent &&
            current.SecretFilePermissionsApplied &&
            current.ConfigurationExact &&
            !current.DomainDriftDetected &&
            // Docker's list-level Ports projection represents active publication.
            // A cleanly stopped container can therefore report no live published
            // ports even though its immutable HostConfig port bindings remain
            // intact. Do not turn that normal stopped-state observation into
            // destructive Repair. Running Coturn must still prove live publication,
            // and RecoverStartupAsync re-inspects after start and requires full
            // RuntimeExact before recovery can succeed.
            (!current.Running || current.RelayPortsPublished) &&
            current.SecurityPolicyApplied &&
            docker.ExpectedNetworkAttached &&
            docker.NetworkAliasesMatch &&
            docker.ConfigMountPresent &&
            docker.ConfigMountReadOnly &&
            docker.ConfigMountSourceMatches &&
            docker.ConfigMountDestinationMatches &&
            docker.CommandMatches &&
            docker.StartupUserMatches;
    }
}
