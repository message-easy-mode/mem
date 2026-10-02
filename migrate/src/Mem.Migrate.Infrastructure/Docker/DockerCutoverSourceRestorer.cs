using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Processes;

namespace Mem.Migrate.Infrastructure.Docker;

public sealed class DockerCutoverSourceRestorer(IProcessRunner processRunner)
    : ICutoverSourceRestorer
{
    private static readonly string[] DefaultRestoreOrder =
    [
        "matrix",
        "element",
        "legacy-api",
        "legacy-web"
    ];

    public async Task<SourceRestorationContainerResult[]> RestoreAsync(
        CutoverRollbackCheckpoint checkpoint,
        IReadOnlyList<CutoverFreezeContainerResult> frozenContainers,
        string dockerCommand,
        int commandTimeoutSeconds,
        int readinessTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(frozenContainers);

        var commandTimeout = TimeSpan.FromSeconds(commandTimeoutSeconds);
        var readinessTimeout = TimeSpan.FromSeconds(readinessTimeoutSeconds);
        var ordered = OrderContainers(checkpoint).ToArray();
        ValidateFrozenEvidence(ordered, frozenContainers);

        var observations = new Dictionary<string, ContainerObservation>(StringComparer.Ordinal);
        foreach (var container in ordered)
        {
            var observation = await InspectAsync(
                dockerCommand,
                container.ContainerId,
                commandTimeout,
                cancellationToken);
            ValidateBeforeMutation(container, observation);
            observations.Add(container.ContainerId, observation);
        }

        foreach (var retained in checkpoint.RetainedContainers)
        {
            var observation = await InspectAsync(
                dockerCommand,
                retained.ContainerId,
                commandTimeout,
                cancellationToken);
            ValidateRetainedContainer(retained, observation);
        }

        foreach (var container in ordered)
        {
            var current = observations[container.ContainerId];
            var policy = NormalizeRestartPolicy(container.RestartPolicy);
            if (!RestartPoliciesEqual(current.RestartPolicy, policy))
            {
                await RunRequiredAsync(
                    dockerCommand,
                    ["update", $"--restart={policy}", container.ContainerId],
                    commandTimeout,
                    $"restore restart policy '{policy}' for",
                    container.ContainerName,
                    cancellationToken);
            }
        }

        foreach (var role in GetRestoreOrder(checkpoint))
        {
            foreach (var container in ordered.Where(item =>
                         string.Equals(item.Role, role, StringComparison.Ordinal)))
            {
                if (!container.WasRunning)
                {
                    continue;
                }

                var current = await InspectAsync(
                    dockerCommand,
                    container.ContainerId,
                    commandTimeout,
                    cancellationToken);
                if (!IsRunning(current.State))
                {
                    await RunRequiredAsync(
                        dockerCommand,
                        ["start", container.ContainerId],
                        commandTimeout,
                        "start",
                        container.ContainerName,
                        cancellationToken);
                }

                await WaitForRequiredStateAsync(
                    container,
                    dockerCommand,
                    commandTimeout,
                    readinessTimeout,
                    cancellationToken);
            }
        }

        var results = new List<SourceRestorationContainerResult>();
        foreach (var container in ordered)
        {
            var current = await InspectAsync(
                dockerCommand,
                container.ContainerId,
                commandTimeout,
                cancellationToken);
            var expectedPolicy = NormalizeRestartPolicy(container.RestartPolicy);
            var runningStateRestored = container.WasRunning
                ? IsRunning(current.State)
                : !IsRunningOrRestarting(current.State);
            var policyRestored = RestartPoliciesEqual(current.RestartPolicy, expectedPolicy);
            var serviceVerified = runningStateRestored &&
                (!container.WasRunning ||
                 !IsServiceRole(container.Role) ||
                 IsAcceptableHealth(current.Health));

            if (!runningStateRestored || !policyRestored || !serviceVerified)
            {
                throw new InvalidOperationException(
                    $"Source container '{container.ContainerName}' did not return to its exact pre-freeze operational state.");
            }

            results.Add(new SourceRestorationContainerResult(
                container.Role,
                current.ContainerId,
                current.ContainerName,
                current.ImageId,
                expectedPolicy,
                container.WasRunning,
                current.State,
                NormalizeRestartPolicy(current.RestartPolicy),
                NormalizeHealth(current.Health),
                runningStateRestored,
                policyRestored,
                serviceVerified));
        }

        foreach (var retained in checkpoint.RetainedContainers)
        {
            var current = await InspectAsync(
                dockerCommand,
                retained.ContainerId,
                commandTimeout,
                cancellationToken);
            ValidateRetainedContainer(retained, current);
            var wasRunning = IsRunning(retained.State);
            results.Add(new SourceRestorationContainerResult(
                retained.Role,
                current.ContainerId,
                current.ContainerName,
                current.ImageId,
                NormalizeRestartPolicy(retained.RestartPolicy),
                wasRunning,
                current.State,
                NormalizeRestartPolicy(current.RestartPolicy),
                NormalizeHealth(current.Health),
                RunningStateRestored: true,
                RestartPolicyRestored: true,
                ServiceVerified: true));
        }

        return results.ToArray();
    }

    private async Task WaitForRequiredStateAsync(
        CutoverSourceContainer container,
        string dockerCommand,
        TimeSpan commandTimeout,
        TimeSpan readinessTimeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(readinessTimeout);
        while (true)
        {
            var current = await InspectAsync(
                dockerCommand,
                container.ContainerId,
                commandTimeout,
                cancellationToken);

            if (IsRunning(current.State))
            {
                if (!IsServiceRole(container.Role) || IsAcceptableHealth(current.Health))
                {
                    return;
                }

                if (string.Equals(
                        NormalizeHealth(current.Health),
                        "unhealthy",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Source container '{container.ContainerName}' reported an unhealthy Docker health state.");
                }
            }
            else if (string.Equals(current.State, "dead", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Source container '{container.ContainerName}' entered the Docker dead state during restoration.");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new InvalidOperationException(
                    $"Source container '{container.ContainerName}' did not become ready within {readinessTimeout.TotalSeconds:0} seconds.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task<ContainerObservation> InspectAsync(
        string dockerCommand,
        string containerId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var id = await InspectFieldAsync(
            dockerCommand,
            containerId,
            "{{.Id}}",
            timeout,
            cancellationToken);
        var name = await InspectFieldAsync(
            dockerCommand,
            containerId,
            "{{.Name}}",
            timeout,
            cancellationToken);
        var imageId = await InspectFieldAsync(
            dockerCommand,
            containerId,
            "{{.Image}}",
            timeout,
            cancellationToken);
        var state = await InspectFieldAsync(
            dockerCommand,
            containerId,
            "{{.State.Status}}",
            timeout,
            cancellationToken);
        var restart = await InspectFieldAsync(
            dockerCommand,
            containerId,
            "{{.HostConfig.RestartPolicy.Name}}",
            timeout,
            cancellationToken);
        var health = await InspectFieldAsync(
            dockerCommand,
            containerId,
            "{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}",
            timeout,
            cancellationToken);

        return new ContainerObservation(
            id,
            name.TrimStart('/'),
            imageId,
            state,
            restart,
            health);
    }

    private async Task<string> InspectFieldAsync(
        string dockerCommand,
        string containerId,
        string format,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new ProcessRequest(
                dockerCommand,
                ["inspect", "--format", format, containerId],
                timeout),
            cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Docker could not inspect exact source container '{containerId}'.");
        }

        return result.StandardOutput.Trim();
    }

    private async Task RunRequiredAsync(
        string dockerCommand,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string action,
        string containerName,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            new ProcessRequest(dockerCommand, arguments, timeout),
            cancellationToken);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Docker failed to {action} source container '{containerName}'.");
        }
    }

    private static IEnumerable<CutoverSourceContainer> OrderContainers(
        CutoverRollbackCheckpoint checkpoint)
    {
        var order = GetRestoreOrder(checkpoint).ToArray();
        return checkpoint.SourceContainers
            .OrderBy(container => Array.IndexOf(order, container.Role))
            .ThenBy(container => container.StackId)
            .ThenBy(container => container.ContainerName, StringComparer.Ordinal);
    }

    private static IEnumerable<string> GetRestoreOrder(
        CutoverRollbackCheckpoint checkpoint)
    {
        var order = checkpoint.RestoreOrder.Length == 0
            ? DefaultRestoreOrder
            : checkpoint.RestoreOrder;
        if (order.Any(string.IsNullOrWhiteSpace) ||
            order.Distinct(StringComparer.Ordinal).Count() != order.Length)
        {
            throw new InvalidDataException(
                "The source-freeze rollback checkpoint contains an invalid restore order.");
        }

        var roles = checkpoint.SourceContainers
            .Select(container => container.Role)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (roles.Any(role => !order.Contains(role, StringComparer.Ordinal)))
        {
            throw new InvalidDataException(
                "The source-freeze rollback checkpoint does not order every frozen source role.");
        }

        return order;
    }

    private static void ValidateFrozenEvidence(
        IReadOnlyList<CutoverSourceContainer> expected,
        IReadOnlyList<CutoverFreezeContainerResult> frozen)
    {
        if (expected.Count == 0 || frozen.Count != expected.Count)
        {
            throw new InvalidDataException(
                "Source-freeze evidence does not contain the exact frozen container inventory.");
        }

        foreach (var container in expected)
        {
            var matches = frozen.Where(item =>
                    string.Equals(item.ContainerId, container.ContainerId, StringComparison.Ordinal))
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidDataException(
                    $"Source-freeze evidence does not contain exactly one result for '{container.ContainerName}'.");
            }

            var result = matches[0];
            if (!string.Equals(result.Role, container.Role, StringComparison.Ordinal) ||
                !string.Equals(result.ContainerName, container.ContainerName, StringComparison.Ordinal) ||
                !string.Equals(
                    NormalizeRestartPolicy(result.PreviousRestartPolicy),
                    NormalizeRestartPolicy(container.RestartPolicy),
                    StringComparison.Ordinal) ||
                result.WasRunning != container.WasRunning ||
                !result.Stopped ||
                !result.RestartPolicyDisabled)
            {
                throw new InvalidDataException(
                    $"Source-freeze result for '{container.ContainerName}' does not match its rollback checkpoint.");
            }
        }
    }

    private static void ValidateRetainedContainer(
        CutoverRetainedContainer expected,
        ContainerObservation current)
    {
        var expectedRunning = IsRunning(expected.State);
        var runningUnchanged = expectedRunning
            ? IsRunning(current.State)
            : !IsRunningOrRestarting(current.State);
        if (!string.Equals(current.ContainerId, expected.ContainerId, StringComparison.Ordinal) ||
            !string.Equals(current.ContainerName, expected.ContainerName, StringComparison.Ordinal) ||
            !string.Equals(current.ImageId, expected.ImageId, StringComparison.Ordinal) ||
            !RestartPoliciesEqual(current.RestartPolicy, expected.RestartPolicy) ||
            !runningUnchanged)
        {
            throw new InvalidOperationException(
                $"Retained source container '{expected.ContainerName}' changed after freeze.");
        }
    }

    private static void ValidateBeforeMutation(
        CutoverSourceContainer expected,
        ContainerObservation current)
    {
        if (!string.Equals(current.ContainerId, expected.ContainerId, StringComparison.Ordinal) ||
            !string.Equals(current.ContainerName, expected.ContainerName, StringComparison.Ordinal) ||
            !string.Equals(current.ImageId, expected.ImageId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Exact source container identity for '{expected.ContainerName}' changed after freeze.");
        }

        if (IsRunningOrRestarting(current.State) && !expected.WasRunning)
        {
            throw new InvalidOperationException(
                $"Source container '{expected.ContainerName}' was not running before freeze but is running now.");
        }

        var currentPolicy = NormalizeRestartPolicy(current.RestartPolicy);
        var expectedPolicy = NormalizeRestartPolicy(expected.RestartPolicy);
        if (!string.Equals(currentPolicy, "no", StringComparison.Ordinal) &&
            !string.Equals(currentPolicy, expectedPolicy, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Source container '{expected.ContainerName}' restart policy drifted after freeze.");
        }
    }

    private static string NormalizeRestartPolicy(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "no"
            : value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "no" or "always" or "unless-stopped" or "on-failure" => normalized,
            _ => throw new InvalidDataException(
                $"Unsupported captured Docker restart policy '{value}'.")
        };
    }

    private static bool RestartPoliciesEqual(string? left, string? right) =>
        string.Equals(
            NormalizeRestartPolicy(left),
            NormalizeRestartPolicy(right),
            StringComparison.Ordinal);

    private static bool IsRunning(string state) =>
        string.Equals(state, "running", StringComparison.OrdinalIgnoreCase);

    private static bool IsRunningOrRestarting(string state) =>
        IsRunning(state) ||
        string.Equals(state, "restarting", StringComparison.OrdinalIgnoreCase);

    private static bool IsServiceRole(string role) =>
        string.Equals(role, "matrix", StringComparison.Ordinal) ||
        string.Equals(role, "element", StringComparison.Ordinal);

    private static bool IsAcceptableHealth(string? health)
    {
        var normalized = NormalizeHealth(health);
        return normalized is "healthy" or "none";
    }

    private static string NormalizeHealth(string? health) =>
        string.IsNullOrWhiteSpace(health)
            ? "none"
            : health.Trim().ToLowerInvariant();

    private sealed record ContainerObservation(
        string ContainerId,
        string ContainerName,
        string ImageId,
        string State,
        string RestartPolicy,
        string Health);
}
