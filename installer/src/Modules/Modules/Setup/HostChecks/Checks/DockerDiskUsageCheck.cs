using Modules.Setup.HostChecks.Runtime;

namespace Modules.Setup.HostChecks.Checks;

public sealed class DockerDiskUsageCheck(ISetupDockerRuntimeProbe dockerProbe) : IHostCheck
{
    public string Key => "docker-disk-usage";
    public string GroupKey => "docker";
    public string GroupTitle => "Docker";
    public string GroupDescription => "Checks Docker daemon resource inventory without requiring a Docker CLI inside the Control Plane.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await dockerProbe.GetSystemInfoAsync(cancellationToken);

            return new HostCheckResultDto(
                Key,
                "Docker disk usage",
                HostCheckStatus.Skipped,
                Blocking: false,
                Summary: $"Byte-level Docker disk usage is not collected by this bounded Control Plane check. The daemon currently reports {info.ImageCount:N0} images and {info.ContainerCount:N0} containers.",
                Details: null,
                WhyItMatters: "Docker images, containers, volumes, and build cache can consume significant disk space, but container-local filesystem tools must not be presented as host Docker storage evidence.",
                RecommendedAction: "Use a host-authoritative Docker storage view such as Portainer or host-side Docker tooling when byte-level Docker disk usage is required.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "Resource inventory",
                        $"Images={info.ImageCount}; Containers={info.ContainerCount}")
                ]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new HostCheckResultDto(
                Key,
                "Docker disk usage",
                HostCheckStatus.Skipped,
                Blocking: false,
                Summary: "Byte-level Docker disk usage was not collected.",
                Details: null,
                WhyItMatters: "Docker storage evidence must describe the host daemon rather than the Control Plane container filesystem.",
                RecommendedAction: "Use host-authoritative Docker storage tooling if disk usage needs to be inspected.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "Resource inventory",
                        "Docker API inventory unavailable.")
                ]);
        }
    }
}
