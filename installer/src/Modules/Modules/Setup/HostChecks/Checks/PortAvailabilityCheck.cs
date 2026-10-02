using Modules.Setup.HostChecks.Runtime;
using Shared.ControlPlane.Runtime;

namespace Modules.Setup.HostChecks.Checks;

public sealed class PortAvailabilityCheck(
    ISetupDockerRuntimeProbe dockerProbe,
    MemControlPlaneRuntimeContext runtimeContext) : IHostCheck
{
    private static readonly HashSet<(int Port, string Protocol)> WatchedBindings =
        BuildWatchedBindings();

    public string Key => "ports";
    public string GroupKey => "ports";
    public string GroupTitle => "Ports";
    public string GroupDescription => "Checks Docker-published ownership of the public and management ports used during platform setup.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SetupDockerContainer> containers;

        try
        {
            containers = await dockerProbe.ListContainersAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new HostCheckResultDto(
                Key,
                "Host port ownership",
                HostCheckStatus.Unavailable,
                Blocking: false,
                Summary: "Docker-published host port ownership could not be inspected.",
                Details: null,
                WhyItMatters: "NPM and the shared Coturn service require predictable ownership of their published platform ports.",
                RecommendedAction: "Resolve the Docker connectivity result above, then re-run server checks.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "Container inventory",
                        "Docker API request failed.")
                ]);
        }

        var watchedBindings = containers
            .SelectMany(container => (container.Ports ?? [])
                .Where(binding =>
                    binding.PublicPort > 0 &&
                    WatchedBindings.Contains((
                        (int)binding.PublicPort,
                        NormalizeProtocol(binding.Type))))
                .Select(binding => new PublishedPort(container, binding)))
            .Where(item => !IsExpectedControlPlaneBinding(item))
            .Where(item => !IsExpectedCoturnBinding(item))
            .OrderBy(item => item.Binding.PublicPort)
            .ThenBy(item => item.Container.Name, StringComparer.Ordinal)
            .ToArray();

        if (watchedBindings.Length > 0)
        {
            var ports = watchedBindings
                .Select(item => $"{item.Binding.PublicPort}/{NormalizeProtocol(item.Binding.Type)}")
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            return new HostCheckResultDto(
                Key,
                "Host port ownership",
                HostCheckStatus.Warning,
                Blocking: false,
                Summary: $"Docker reports existing published listeners on watched setup bindings: {string.Join(", ", ports)}.",
                Details: BuildDetails(watchedBindings),
                WhyItMatters: "NPM and the shared Coturn service require predictable ownership of their published platform ports.",
                RecommendedAction: "Confirm these Docker-published bindings are expected. Installation remains authoritative when NPM and Coturn attempt their actual host-port binds.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "Published ports",
                        BuildDetails(watchedBindings))
                ]);
        }

        return new HostCheckResultDto(
            Key,
            "Host port ownership",
            HostCheckStatus.Unavailable,
            Blocking: false,
            Summary: "No Docker-published conflicts were found on watched setup ports. Non-Docker host listeners are not inferred from inside the Control Plane runtime.",
            Details: null,
            WhyItMatters: "NPM and the shared Coturn service require predictable ownership of their published platform ports.",
            RecommendedAction: "No action is required now. Docker's actual host-port bind remains authoritative when NPM is created.",
            Evidence:
            [
                new DiagnosticEvidenceDto(
                    "Docker API",
                    "Published ports",
                    "No conflicting Docker-published watched ports were reported."),
                new DiagnosticEvidenceDto(
                    "Runtime",
                    "Authority",
                    $"RuntimeMode={runtimeContext.RuntimeMode}; non-Docker host listeners are intentionally not guessed from container-local tools.")
            ]);
    }

    private static HashSet<(int Port, string Protocol)> BuildWatchedBindings()
    {
        var watched = new HashSet<(int Port, string Protocol)>
        {
            (80, "tcp"),
            (81, "tcp"),
            (443, "tcp"),
            (8443, "tcp"),
            (Modules.Setup.Platform.Coturn.PlatformCoturnSetupDefaults.TurnPort, "tcp"),
            (Modules.Setup.Platform.Coturn.PlatformCoturnSetupDefaults.TurnPort, "udp")
        };

        for (var port = Modules.Setup.Platform.Coturn.PlatformCoturnSetupDefaults.RelayMinPort;
             port <= Modules.Setup.Platform.Coturn.PlatformCoturnSetupDefaults.RelayMaxPort;
             port++)
        {
            watched.Add((port, "udp"));
        }

        return watched;
    }

    private static string NormalizeProtocol(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "tcp"
            : value.Trim().ToLowerInvariant();

    private bool IsExpectedControlPlaneBinding(PublishedPort item)
    {
        if (item.Binding.PublicPort != 8443)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(runtimeContext.ConfiguredContainerName) &&
            string.Equals(
                item.Container.Name,
                runtimeContext.ConfiguredContainerName,
                StringComparison.Ordinal))
        {
            return true;
        }

        return item.Container.Labels.TryGetValue(
                   MemDockerOwnershipLabels.ControlPlaneInstanceKey,
                   out var instance) &&
               Guid.TryParse(instance, out var parsed) &&
               parsed == runtimeContext.ControlPlaneInstanceId;
    }

    private static bool IsExpectedCoturnBinding(PublishedPort item)
    {
        if (!string.Equals(
                item.Container.Name.Trim().TrimStart('/'),
                Modules.Setup.Platform.Coturn.PlatformCoturnSetupDefaults.ContainerName,
                StringComparison.Ordinal))
        {
            return false;
        }

        return item.Container.Labels.TryGetValue("mem.component", out var component) &&
               item.Container.Labels.TryGetValue("mem.service", out var service) &&
               string.Equals(
                   component,
                   Modules.Setup.Platform.Coturn.PlatformCoturnSetupDefaults.ComponentLabel,
                   StringComparison.Ordinal) &&
               string.Equals(
                   service,
                   Modules.Setup.Platform.Coturn.PlatformCoturnSetupDefaults.ServiceKey,
                   StringComparison.Ordinal);
    }

    private static string BuildDetails(IEnumerable<PublishedPort> bindings) =>
        string.Join(
            Environment.NewLine,
            bindings.Select(item =>
                $"Port {item.Binding.PublicPort}/{NormalizeProtocol(item.Binding.Type)} -> {item.Container.Name}:{item.Binding.PrivatePort}"));

    private sealed record PublishedPort(
        SetupDockerContainer Container,
        SetupDockerPortBinding Binding);
}
