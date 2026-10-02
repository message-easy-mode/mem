namespace Modules.Setup.HostChecks.Checks;

public sealed class DockerComposePluginCheck : IHostCheck
{
    public string Key => "docker-compose-plugin";
    public string GroupKey => "docker";
    public string GroupTitle => "Docker";
    public string GroupDescription => "Separates host bootstrap/developer tooling from Control Plane runtime requirements.";

    public Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new HostCheckResultDto(
            Key,
            "Docker Compose plugin",
            HostCheckStatus.Skipped,
            Blocking: false,
            Summary: "Docker Compose is not required inside the running MEM Control Plane.",
            Details: null,
            WhyItMatters: "The Control Plane manages Docker through the Docker Engine API. Compose is a host bootstrap/developer-harness concern, not an in-container runtime dependency.",
            RecommendedAction: null,
            Evidence:
            [
                new DiagnosticEvidenceDto(
                    "Runtime contract",
                    "Docker access",
                    "Docker Engine API via Docker.DotNet")
            ]));
    }
}
