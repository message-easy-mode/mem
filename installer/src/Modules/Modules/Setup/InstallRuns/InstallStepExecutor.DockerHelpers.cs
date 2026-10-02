namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private async Task<InstallStepResult> CheckRequiredPortsAvailableAsync(
        IReadOnlyCollection<int> ports,
        CancellationToken cancellationToken,
        string? allowedContainerName = null)
    {
        var distinctPorts = ports
            .Distinct()
            .OrderBy(port => port)
            .ToArray();

        foreach (var port in distinctPorts)
        {
            if (port is < 1 or > 65535)
            {
                return Failed(
                    "NPM / ingress port validation failed.",
                    $"Port '{port}' is not valid.");
            }
        }

        IReadOnlyList<SetupDockerContainer> containers;
        try
        {
            containers = await _dockerRuntimeProbe.ListContainersAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(
                "NPM / ingress port validation failed.",
                $"Docker-published host ports could not be inspected: {ex.Message}");
        }

        var conflicts = containers
            .Where(container =>
                string.IsNullOrWhiteSpace(allowedContainerName) ||
                !string.Equals(container.Name, allowedContainerName, StringComparison.Ordinal))
            .SelectMany(container => (container.Ports ?? [])
                .Where(binding =>
                    binding.PublicPort > 0 &&
                    string.Equals(binding.Type, "tcp", StringComparison.OrdinalIgnoreCase) &&
                    distinctPorts.Contains((int)binding.PublicPort))
                .Select(binding => new
                {
                    Container = container.Name,
                    HostPort = binding.PublicPort,
                    ContainerPort = binding.PrivatePort
                }))
            .OrderBy(item => item.HostPort)
            .ThenBy(item => item.Container, StringComparer.Ordinal)
            .ToArray();

        if (conflicts.Length > 0)
        {
            var detail = string.Join(
                Environment.NewLine,
                conflicts.Select(item =>
                    $"Port {item.HostPort}/tcp is published by '{item.Container}' -> {item.ContainerPort}/tcp."));

            return Failed(
                "NPM / ingress requires a host port already published by another Docker container.",
                detail);
        }

        return Succeeded(
            "No Docker-published conflicts were found for the planned NPM / ingress ports. " +
            "Docker container creation remains authoritative for non-Docker host listener conflicts.");
    }

    private async Task EnsureContainerNetworkAsync(
        string containerName,
        CancellationToken cancellationToken)
    {
        await _dockerHost.ConnectContainerToNetworkAsync(
            containerName,
            NetworkName,
            cancellationToken);
    }

    private async Task<InstallStepResult?> EnsureControlPlaneManagedServiceNetworkAsync(
        CancellationToken cancellationToken)
    {
        if (_runtimeContext is null || !_runtimeContext.RunningInContainer)
        {
            return null;
        }

        var controlPlaneContainerName = _runtimeContext.ConfiguredContainerName?.Trim();
        if (string.IsNullOrWhiteSpace(controlPlaneContainerName))
        {
            return Failed(
                "Managed-service Docker network preparation failed.",
                "The active containerized Control Plane does not expose its server-owned container identity.");
        }

        try
        {
            await _dockerHost.ConnectContainerToNetworkAsync(
                controlPlaneContainerName,
                NetworkName,
                cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Failed(
                "Managed-service Docker network preparation failed.",
                $"The active Control Plane container '{controlPlaneContainerName}' could not be connected to Docker network '{NetworkName}'. {ex.Message}");
        }
    }

}
