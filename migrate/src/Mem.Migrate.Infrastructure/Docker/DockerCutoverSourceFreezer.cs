using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Processes;

namespace Mem.Migrate.Infrastructure.Docker;

public sealed class DockerCutoverSourceFreezer(IProcessRunner processRunner)
    : ICutoverSourceFreezer
{
    private static readonly string[] RoleOrder =
    [
        "legacy-web",
        "legacy-api",
        "element",
        "matrix"
    ];

    public async Task<CutoverFreezeContainerResult[]> FreezeAsync(
        CutoverPlanDocument plan,
        string dockerCommand,
        int commandTimeoutSeconds,
        int stopTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var timeout = TimeSpan.FromSeconds(commandTimeoutSeconds);
        var ordered = plan.SourceContainersToFreeze
            .OrderBy(container => Array.IndexOf(RoleOrder, container.Role))
            .ThenBy(container => container.StackId)
            .ThenBy(container => container.ContainerName, StringComparer.Ordinal)
            .ToArray();

        var changedPolicies = new List<CutoverSourceContainer>();
        try
        {
            foreach (var container in ordered)
            {
                await RunRequiredAsync(
                    dockerCommand,
                    ["update", "--restart=no", container.ContainerId],
                    timeout,
                    "disable restart policy",
                    container.ContainerName,
                    cancellationToken);
                changedPolicies.Add(container);
            }

            foreach (var container in ordered.Where(item => item.WasRunning))
            {
                await RunRequiredAsync(
                    dockerCommand,
                    ["stop", "--time", stopTimeoutSeconds.ToString(), container.ContainerId],
                    timeout + TimeSpan.FromSeconds(stopTimeoutSeconds),
                    "stop",
                    container.ContainerName,
                    cancellationToken);
            }

            var results = new List<CutoverFreezeContainerResult>();
            foreach (var container in ordered)
            {
                var state = await InspectFieldAsync(
                    dockerCommand,
                    container.ContainerId,
                    "{{.State.Status}}",
                    timeout,
                    cancellationToken);
                var restart = await InspectFieldAsync(
                    dockerCommand,
                    container.ContainerId,
                    "{{.HostConfig.RestartPolicy.Name}}",
                    timeout,
                    cancellationToken);
                var stopped = !string.Equals(
                    state,
                    "running",
                    StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(
                        state,
                        "restarting",
                        StringComparison.OrdinalIgnoreCase);
                var disabled = string.Equals(
                    restart,
                    "no",
                    StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrEmpty(restart);
                if (!stopped || !disabled)
                {
                    throw new InvalidOperationException(
                        $"Container '{container.ContainerName}' did not reach the required frozen state.");
                }

                results.Add(new CutoverFreezeContainerResult(
                    container.Role,
                    container.ContainerId,
                    container.ContainerName,
                    container.RestartPolicy,
                    container.WasRunning,
                    state,
                    restart,
                    disabled,
                    stopped));
            }

            return results.ToArray();
        }
        catch
        {
            await RestoreAfterFailureAsync(
                changedPolicies,
                dockerCommand,
                timeout,
                CancellationToken.None);
            throw;
        }
    }

    private async Task RestoreAfterFailureAsync(
        IEnumerable<CutoverSourceContainer> containers,
        string dockerCommand,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        foreach (var container in containers.Reverse())
        {
            if (container.WasRunning)
            {
                await TryRunAsync(
                    dockerCommand,
                    ["start", container.ContainerId],
                    timeout,
                    cancellationToken);
            }

            var policy = string.IsNullOrWhiteSpace(container.RestartPolicy)
                ? "no"
                : container.RestartPolicy;
            await TryRunAsync(
                dockerCommand,
                ["update", $"--restart={policy}", container.ContainerId],
                timeout,
                cancellationToken);
        }
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
                $"Docker could not verify container '{containerId}'.");
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

    private async Task TryRunAsync(
        string dockerCommand,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await processRunner.RunAsync(
                new ProcessRequest(dockerCommand, arguments, timeout),
                cancellationToken);
        }
        catch
        {
            // Best-effort restoration after a failed freeze. The original failure remains authoritative.
        }
    }
}
