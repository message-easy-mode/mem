using Modules.Setup.HostChecks.Runtime;

namespace Modules.Setup.HostChecks.Checks;

public sealed class DockerDataRootCheck(ISetupDockerRuntimeProbe dockerProbe) : IHostCheck
{
    public string Key => "docker-data-root";
    public string GroupKey => "docker";
    public string GroupTitle => "Docker";
    public string GroupDescription => "Checks Docker daemon identity and storage-root information through the Docker API.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await dockerProbe.GetSystemInfoAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(info.DockerRootDir))
            {
                return new HostCheckResultDto(
                    Key,
                    "Docker data root",
                    HostCheckStatus.Unknown,
                    Blocking: false,
                    Summary: "Docker is reachable, but the daemon did not report a Docker data-root path.",
                    Details: null,
                    WhyItMatters: "Docker data root identifies where the daemon stores images, writable layers, and local volume state.",
                    RecommendedAction: "Inspect the Docker daemon configuration if storage placement needs to be verified.",
                    Evidence:
                    [
                        new DiagnosticEvidenceDto(
                            "Docker API",
                            "System info",
                            "DockerRootDir was not reported.")
                    ]);
            }

            return new HostCheckResultDto(
                Key,
                "Docker data root",
                HostCheckStatus.Pass,
                Blocking: false,
                Summary: $"Docker data root reported by the host daemon: {info.DockerRootDir}.",
                Details: null,
                WhyItMatters: "Docker data root identifies where the daemon stores images, writable layers, and local volume state.",
                RecommendedAction: null,
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "DockerRootDir",
                        info.DockerRootDir)
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
                "Docker data root",
                HostCheckStatus.Warning,
                Blocking: false,
                Summary: "Unable to inspect Docker data-root information through the Docker API.",
                Details: null,
                WhyItMatters: "Docker data root identifies where the daemon stores images, writable layers, and local volume state.",
                RecommendedAction: "Confirm the Docker daemon is reachable from the MEM Control Plane.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "System info",
                        "Docker API request failed.")
                ]);
        }
    }
}
