using Modules.Setup.HostChecks.Runtime;

namespace Modules.Setup.HostChecks.Checks;

public sealed class DockerReachableCheck(ISetupDockerRuntimeProbe dockerProbe) : IHostCheck
{
    public string Key => "docker-reachable";
    public string GroupKey => "docker";
    public string GroupTitle => "Docker";
    public string GroupDescription => "Checks whether the MEM Control Plane can reach the host Docker daemon through its configured Docker API endpoint.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await dockerProbe.GetSystemInfoAsync(cancellationToken);
            var version = string.IsNullOrWhiteSpace(info.ServerVersion)
                ? "unknown"
                : info.ServerVersion;

            return new HostCheckResultDto(
                Key,
                "Docker reachable",
                HostCheckStatus.Pass,
                Blocking: true,
                Summary: $"Docker is reachable through the Control Plane Docker API client. Server version: {version}.",
                Details: null,
                WhyItMatters: "The MEM Control Plane requires Docker API access to inspect and manage networks, volumes, and containers.",
                RecommendedAction: null,
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "System info",
                        $"ServerVersion={version}")
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
                "Docker reachable",
                HostCheckStatus.Fail,
                Blocking: true,
                Summary: "Docker is not reachable through the Control Plane Docker API client.",
                Details: null,
                WhyItMatters: "The MEM Control Plane requires Docker API access to inspect and manage networks, volumes, and containers.",
                RecommendedAction: "Confirm the Docker daemon is running and that the configured Docker endpoint is available to the MEM Control Plane.",
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
