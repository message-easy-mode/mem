using Modules.Setup.HostChecks.Runtime;
using Shared.ControlPlane;

namespace Modules.Setup.HostChecks.Checks;

public sealed class ExistingDockerNetworksCheck(ISetupDockerRuntimeProbe dockerProbe) : IHostCheck
{
    public string Key => "existing-docker-networks";
    public string GroupKey => "existing-docker-resources";
    public string GroupTitle => "Existing Docker resources";
    public string GroupDescription => "Checks for existing Control Plane, platform, stack, development, legacy, and support-tool Docker resources on this host.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SetupDockerNetwork> observed;
        try
        {
            observed = await dockerProbe.ListNetworksAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new HostCheckResultDto(
                Key,
                "Existing MEM-related networks",
                HostCheckStatus.Warning,
                Blocking: false,
                Summary: "Unable to inspect existing Docker networks through the Docker API.",
                Details: null,
                WhyItMatters: "Existing Docker networks may indicate current MEM runtime state, development compose state, or legacy deployment state.",
                RecommendedAction: "Confirm Docker is reachable and inspect the host's Docker networks.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "Network inventory",
                        "Docker API request failed.")
                ]);
        }

        var networks = observed
            .Select(network => new ClassifiedNetwork(
                network.Name,
                network.Driver,
                ClassifyNetwork(network.Name)))
            .Where(network => network.Kind != DetectedDockerResourceKind.Ignored)
            .ToArray();

        if (networks.Length == 0)
        {
            return new HostCheckResultDto(
                Key,
                "Existing MEM-related networks",
                HostCheckStatus.Pass,
                Blocking: false,
                Summary: "No MEM-related Docker networks were detected.",
                Details: null,
                WhyItMatters: "A clean network state reduces the chance of unexpected service discovery or container routing conflicts.",
                RecommendedAction: null,
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "Network inventory",
                        "No MEM-related networks detected.")
                ]);
        }

        return new HostCheckResultDto(
            Key,
            "Existing MEM-related networks",
            HostCheckStatus.Pass,
            Blocking: false,
            Summary: BuildSummary(networks),
            Details: BuildDetails(networks),
            WhyItMatters: "MEM commonly uses Docker bridge networks for Control Plane, platform, and stack connectivity.",
            RecommendedAction: "Confirm detected MEM networks are expected for this host.",
            Evidence:
            [
                new DiagnosticEvidenceDto(
                    "Docker API",
                    "Network inventory",
                    BuildDetails(networks))
            ]);
    }

    private static DetectedDockerResourceKind ClassifyNetwork(string name)
    {
        if (name.Equals("mem-gateway", StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.MemGateway;
        }

        if (name.Equals(
                MemControlPlaneIdentity.DevelopmentContainerName,
                StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(
                MemControlPlaneIdentity.DevelopmentContainerName + "_",
                StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("mem-dev", StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.DevelopmentCompose;
        }

        if (name.StartsWith("mem", StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.UnknownMemRelated;
        }

        return DetectedDockerResourceKind.Ignored;
    }

    private static string BuildSummary(IReadOnlyCollection<ClassifiedNetwork> networks)
    {
        var parts = networks
            .GroupBy(network => network.Kind)
            .OrderBy(group => group.Key.ToString())
            .Select(group => $"{group.Count()} {GetKindLabel(group.Key).ToLowerInvariant()}");

        return $"Detected {string.Join(", ", parts)}.";
    }

    private static string BuildDetails(IReadOnlyCollection<ClassifiedNetwork> networks)
    {
        return string.Join(
            Environment.NewLine,
            networks
                .OrderBy(network => network.Kind.ToString())
                .ThenBy(network => network.Name)
                .Select(network =>
                    $"- {network.Name} | {network.Driver} | {GetKindLabel(network.Kind)}"));
    }

    private static string GetKindLabel(DetectedDockerResourceKind kind)
    {
        return kind switch
        {
            DetectedDockerResourceKind.MemGateway => "MEM gateway network",
            DetectedDockerResourceKind.DevelopmentCompose => "Development compose",
            DetectedDockerResourceKind.UnknownMemRelated => "Unknown MEM-related",
            _ => "Ignored"
        };
    }

    private enum DetectedDockerResourceKind
    {
        Ignored,
        MemGateway,
        DevelopmentCompose,
        UnknownMemRelated
    }

    private sealed record ClassifiedNetwork(
        string Name,
        string Driver,
        DetectedDockerResourceKind Kind);
}
