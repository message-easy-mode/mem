using Modules.Setup.HostChecks.Runtime;
using Shared.ControlPlane;

namespace Modules.Setup.HostChecks.Checks;

public sealed class ExistingDockerVolumesCheck(ISetupDockerRuntimeProbe dockerProbe) : IHostCheck
{
    public string Key => "existing-docker-volumes";
    public string GroupKey => "existing-docker-resources";
    public string GroupTitle => "Existing Docker resources";
    public string GroupDescription => "Checks for existing Control Plane, platform, stack, development, legacy, and support-tool Docker resources on this host.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<SetupDockerVolume> observed;
        try
        {
            observed = await dockerProbe.ListVolumesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new HostCheckResultDto(
                Key,
                "Existing MEM-related volumes",
                HostCheckStatus.Warning,
                Blocking: false,
                Summary: "Unable to inspect existing Docker volumes through the Docker API.",
                Details: null,
                WhyItMatters: "Existing volumes may contain persistent platform, development, legacy, Control Plane, or support-tool state.",
                RecommendedAction: "Confirm Docker is reachable and inspect the host's Docker volumes.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "Volume inventory",
                        "Docker API request failed.")
                ]);
        }

        var volumes = observed
            .Select(volume => new ClassifiedVolume(
                volume.Name,
                volume.Driver,
                ClassifyVolume(volume.Name)))
            .Where(volume => volume.Kind != DetectedDockerResourceKind.Ignored)
            .ToArray();

        if (volumes.Length == 0)
        {
            return new HostCheckResultDto(
                Key,
                "Existing MEM-related volumes",
                HostCheckStatus.Pass,
                Blocking: false,
                Summary: "No MEM-related Docker volumes were detected.",
                Details: null,
                WhyItMatters: "A clean volume state reduces the chance of accidentally reusing stale platform data.",
                RecommendedAction: null,
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "Volume inventory",
                        "No MEM-related volumes detected.")
                ]);
        }

        var hasRiskyVolumes = volumes.Any(volume =>
            volume.Kind is DetectedDockerResourceKind.DevelopmentCompose
                or DetectedDockerResourceKind.LegacyCompose
                or DetectedDockerResourceKind.PlatformVolume
                or DetectedDockerResourceKind.UnknownMemRelated);

        return new HostCheckResultDto(
            Key,
            "Existing MEM-related volumes",
            hasRiskyVolumes ? HostCheckStatus.Warning : HostCheckStatus.Pass,
            Blocking: false,
            Summary: BuildSummary(volumes),
            Details: BuildDetails(volumes),
            WhyItMatters: "Docker volumes can contain persistent database, ingress, log, Control Plane, or application state. Reusing or deleting them without understanding ownership can cause data loss or confusing installs.",
            RecommendedAction: hasRiskyVolumes
                ? "Review the detected volumes before continuing. Do not remove volumes unless you are certain they are stale or intentionally disposable."
                : "No action required unless these support-tool or Control Plane volumes are unexpected.",
            Evidence:
            [
                new DiagnosticEvidenceDto(
                    "Docker API",
                    "Volume inventory",
                    BuildDetails(volumes))
            ]);
    }

    private static DetectedDockerResourceKind ClassifyVolume(string name)
    {
        if (string.Equals(
                name,
                MemControlPlaneIdentity.DevelopmentVolumeName,
                StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.ControlPlaneRuntime;
        }

        if (name.StartsWith("mem-dev_", StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.DevelopmentCompose;
        }

        if (name.StartsWith("mem_mem_", StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.LegacyCompose;
        }

        if (MemControlPlaneIdentity.RecognizedPersistentVolumeNames.Any(
                recognized => name.StartsWith(
                    recognized,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return DetectedDockerResourceKind.ControlPlaneRuntime;
        }

        if (name.StartsWith("portainer", StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.SupportTool;
        }

        if (name is "mem_postgres_data"
            or "mem_logs"
            or "mem_npm_data"
            or "mem_npm_letsencrypt"
            or "mem_pgadmin_data"
            or "mem_seq_data")
        {
            return DetectedDockerResourceKind.PlatformVolume;
        }

        if (name.Contains("mem", StringComparison.OrdinalIgnoreCase))
        {
            return DetectedDockerResourceKind.UnknownMemRelated;
        }

        return DetectedDockerResourceKind.Ignored;
    }

    private static string BuildSummary(IReadOnlyCollection<ClassifiedVolume> volumes)
    {
        var parts = volumes
            .GroupBy(volume => volume.Kind)
            .OrderBy(group => group.Key.ToString())
            .Select(group => $"{group.Count()} {GetKindLabel(group.Key).ToLowerInvariant()}");

        return $"Detected {string.Join(", ", parts)}.";
    }

    private static string BuildDetails(IReadOnlyCollection<ClassifiedVolume> volumes)
    {
        return string.Join(
            Environment.NewLine,
            volumes
                .OrderBy(volume => volume.Kind.ToString())
                .ThenBy(volume => volume.Name)
                .Select(volume =>
                    $"- {volume.Name} | {volume.Driver} | {GetKindLabel(volume.Kind)}"));
    }

    private static string GetKindLabel(DetectedDockerResourceKind kind)
    {
        return kind switch
        {
            DetectedDockerResourceKind.ControlPlaneRuntime => "Control Plane runtime",
            DetectedDockerResourceKind.PlatformVolume => "Platform volume",
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
        PlatformVolume,
        DevelopmentCompose,
        LegacyCompose,
        SupportTool,
        UnknownMemRelated
    }

    private sealed record ClassifiedVolume(
        string Name,
        string Driver,
        DetectedDockerResourceKind Kind);
}
