using Modules.Setup.HostChecks.Runtime;
using Shared.ControlPlane;

namespace Modules.Setup.HostChecks.Checks;

public sealed class ExistingMemContainersCheck(ISetupDockerRuntimeProbe dockerProbe) : IHostCheck
{
    public string Key => "existing-docker-containers";
    public string GroupKey => "existing-docker-resources";
    public string GroupTitle => "Existing Docker resources";
    public string GroupDescription => "Checks for existing Control Plane, platform, stack, development, legacy, and support-tool Docker resources on this host.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SetupDockerContainer> observed;
        try
        {
            observed = await dockerProbe.ListContainersAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new HostCheckResultDto(
                Key,
                "Existing MEM-related containers",
                HostCheckStatus.Warning,
                Blocking: false,
                Summary: "Unable to inspect existing Docker containers through the Docker API.",
                Details: null,
                WhyItMatters: "Existing containers may indicate a Control Plane runtime, active Matrix stacks, previous platform services, development compose resources, or support tools.",
                RecommendedAction: "Confirm Docker is reachable and inspect the host's Docker containers.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "Container inventory",
                        "Docker API request failed.")
                ]);
        }

        var containers = observed
            .Select(container => new ClassifiedContainer(
                container.Name,
                container.Image,
                container.Status,
                ClassifyContainer(container)))
            .Where(container => container.Kind != DetectedDockerResourceKind.Ignored)
            .ToArray();

        if (containers.Length == 0)
        {
            return new HostCheckResultDto(
                Key,
                "Existing MEM-related containers",
                HostCheckStatus.Pass,
                Blocking: false,
                Summary: "No MEM-related Docker containers were detected.",
                Details: null,
                WhyItMatters: "A clean container state reduces the chance of conflicting services, ports, networks, or stale runtime state.",
                RecommendedAction: null,
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "Container inventory",
                        "No MEM-related containers detected.")
                ]);
        }

        var summary = BuildSummary(containers);
        var details = BuildDetails(containers);

        var hasRiskyResources = containers.Any(container =>
            container.Kind is DetectedDockerResourceKind.PlatformContainer
                or DetectedDockerResourceKind.LegacyApplication
                or DetectedDockerResourceKind.MatrixStackRuntime
                or DetectedDockerResourceKind.DevelopmentCompose
                or DetectedDockerResourceKind.LegacyCompose
                or DetectedDockerResourceKind.UnknownMemRelated);

        return new HostCheckResultDto(
            Key,
            "Existing MEM-related containers",
            hasRiskyResources ? HostCheckStatus.Warning : HostCheckStatus.Pass,
            Blocking: false,
            Summary: summary,
            Details: details,
            WhyItMatters: "Existing Docker containers may be expected on a developer/operator machine, but they can also indicate active workloads, previous installs, or resources that a fresh install should not overwrite blindly.",
            RecommendedAction: hasRiskyResources
                ? "Review the detected containers before continuing. If this is a target install host, confirm whether these resources should remain, be imported, or be removed."
                : "No action required unless these support tools are unexpected.",
            Evidence:
            [
                new DiagnosticEvidenceDto(
                    "Docker API",
                    "Container inventory",
                    BuildCompactEvidence(containers))
            ]);
    }

    private static DetectedDockerResourceKind ClassifyContainer(SetupDockerContainer container)
    {
        var name = container.Name;
        var composeProject = GetLabel(container.Labels, "com.docker.compose.project");

        if (string.Equals(
                composeProject,
                "mem-dev",
                StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.DevelopmentCompose;
        }

        if (string.Equals(
                composeProject,
                "mem",
                StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.LegacyCompose;
        }

        if (MemControlPlaneIdentity.RecognizedContainerNames.Contains(
                name,
                StringComparer.OrdinalIgnoreCase) ||
            string.Equals(
                name,
                MemControlPlaneIdentity.DevelopmentContainerName,
                StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(
                MemControlPlaneIdentity.DevelopmentContainerName + "-",
                StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.ControlPlaneRuntime;
        }

        if (name.StartsWith("mem-matrix-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("mem-element-web-", StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.MatrixStackRuntime;
        }

        if (name is "mem-api" or "mem-web")
        {
            return DetectedDockerResourceKind.LegacyApplication;
        }

        if (name is "mem-postgres" or "mem-npm")
        {
            return DetectedDockerResourceKind.PlatformContainer;
        }

        if (name is "mem-seq"
            or "seq"
            or "mem-pgadmin"
            or "pgadmin"
            or "portainer"
            or "npm"
            || name.Contains("nginx-proxy-manager", StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.SupportTool;
        }

        if (name.Contains("mem", StringComparison.OrdinalIgnoreCase) ||
            LabelsContainMem(container.Labels))
        {
            return DetectedDockerResourceKind.UnknownMemRelated;
        }

        return DetectedDockerResourceKind.Ignored;
    }

    private static string? GetLabel(
        IReadOnlyDictionary<string, string> labels,
        string key) =>
        labels.TryGetValue(key, out var value) ? value : null;

    private static bool LabelsContainMem(IReadOnlyDictionary<string, string> labels) =>
        labels.Any(label =>
            label.Key.Contains("mem", StringComparison.OrdinalIgnoreCase) ||
            label.Value.Contains("mem", StringComparison.OrdinalIgnoreCase));

    private static string BuildSummary(IReadOnlyCollection<ClassifiedContainer> containers)
    {
        var parts = containers
            .GroupBy(container => container.Kind)
            .OrderBy(group => group.Key.ToString())
            .Select(group => $"{group.Count()} {GetKindLabel(group.Key).ToLowerInvariant()}");

        return $"Detected {string.Join(", ", parts)}.";
    }

    private static string BuildDetails(IReadOnlyCollection<ClassifiedContainer> containers)
    {
        return string.Join(
            Environment.NewLine,
            containers
                .OrderBy(container => container.Kind.ToString())
                .ThenBy(container => container.Name)
                .Select(container =>
                    $"- {container.Name} | {container.Image} | {container.Status} | {GetKindLabel(container.Kind)}"));
    }

    private static string BuildCompactEvidence(IReadOnlyCollection<ClassifiedContainer> containers) =>
        BuildDetails(containers);

    private static string GetKindLabel(DetectedDockerResourceKind kind)
    {
        return kind switch
        {
            DetectedDockerResourceKind.ControlPlaneRuntime => "Control Plane runtime",
            DetectedDockerResourceKind.PlatformContainer => "Platform container",
            DetectedDockerResourceKind.LegacyApplication => "Legacy MEM API/Web application",
            DetectedDockerResourceKind.MatrixStackRuntime => "Matrix stack runtime",
            DetectedDockerResourceKind.DevelopmentCompose => "Development compose",
            DetectedDockerResourceKind.LegacyCompose => "Legacy compose",
            DetectedDockerResourceKind.SupportTool => "Support tool",
            DetectedDockerResourceKind.UnknownMemRelated => "Unknown MEM-related",
            _ => "Ignored"
        };
    }

    private enum DetectedDockerResourceKind
    {
        Ignored,
        ControlPlaneRuntime,
        PlatformContainer,
        LegacyApplication,
        MatrixStackRuntime,
        DevelopmentCompose,
        LegacyCompose,
        SupportTool,
        UnknownMemRelated
    }

    private sealed record ClassifiedContainer(
        string Name,
        string Image,
        string Status,
        DetectedDockerResourceKind Kind);
}
