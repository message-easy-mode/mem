namespace Core.Runtime;

public sealed class RuntimePortPlanner(
    PortCheckService portCheckService)
{
    public RuntimePortPlan BuildPortPlan(
        int containerPort,
        int preferredHostPort,
        List<string> warnings,
        HashSet<int> reservedPorts)
    {
        if (preferredHostPort < 1 || preferredHostPort > 65535)
            throw new ArgumentOutOfRangeException(nameof(preferredHostPort));

        var isPreferredAvailable =
            !reservedPorts.Contains(preferredHostPort) &&
            portCheckService.IsPortAvailable(preferredHostPort);

        int selectedHostPort;
        var portWarnings = new List<string>();

        if (isPreferredAvailable)
        {
            selectedHostPort = preferredHostPort;
        }
        else
        {
            selectedHostPort = FindAvailablePortExcluding(
                Math.Max(preferredHostPort + 100, 1024),
                reservedPorts);

            string warning;

            if (preferredHostPort < 1024)
            {
                warning =
                    $"Preferred host port {preferredHostPort} for container port {containerPort} could not be safely probed by the current process because ports below 1024 may require elevated privileges. Selected fallback port {selectedHostPort}.";
            }
            else
            {
                warning =
                    $"Preferred host port {preferredHostPort} for container port {containerPort} is unavailable. Selected fallback port {selectedHostPort}.";
            }

            warnings.Add(warning);
            portWarnings.Add(warning);
        }

        reservedPorts.Add(selectedHostPort);

        return new RuntimePortPlan(
            ContainerPort: containerPort,
            PreferredHostPort: preferredHostPort,
            SelectedHostPort: selectedHostPort,
            IsPreferredPortAvailable: isPreferredAvailable,
            Protocol: "tcp",
            Warnings: portWarnings
        );
    }

    public int FindAvailablePortExcluding(
        int startingPort,
        HashSet<int> reservedPorts,
        int range = 1000)
    {
        var upper = Math.Min(startingPort + range, 65535);

        for (var port = startingPort; port <= upper; port++)
        {
            if (reservedPorts.Contains(port))
                continue;

            if (portCheckService.IsPortAvailable(port))
                return port;
        }

        throw new InvalidOperationException(
            $"No available port found in range {startingPort}-{upper}.");
    }
}