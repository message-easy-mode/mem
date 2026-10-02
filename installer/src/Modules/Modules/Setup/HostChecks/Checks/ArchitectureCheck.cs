using Modules.Setup.HostChecks.Runtime;

namespace Modules.Setup.HostChecks.Checks;

public sealed class ArchitectureCheck(ISetupDockerRuntimeProbe dockerProbe) : IHostCheck
{
    public string Key => "architecture";
    public string GroupKey => "host";
    public string GroupTitle => "Host";
    public string GroupDescription => "Checks operating-system and host-capacity facts reported by the Docker daemon.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await dockerProbe.GetSystemInfoAsync(cancellationToken);
            var architecture = info.Architecture.Trim();

            if (string.IsNullOrWhiteSpace(architecture))
            {
                return Unavailable("Docker is reachable, but the host daemon did not report an architecture.");
            }

            var normalized = architecture.ToLowerInvariant();
            var supported = normalized is "x86_64" or "amd64";

            return new HostCheckResultDto(
                Key,
                "Architecture",
                supported ? HostCheckStatus.Pass : HostCheckStatus.Warning,
                Blocking: false,
                Summary: $"Docker reports host architecture: {architecture}.",
                Details: null,
                WhyItMatters: "The current MEM installer image and release tooling target supported Linux architectures.",
                RecommendedAction: supported
                    ? null
                    : "Confirm this architecture is supported by the MEM release and every selected runtime image.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "Architecture",
                        architecture)
                ]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable("Unable to read host architecture information from the Docker daemon.");
        }
    }

    private HostCheckResultDto Unavailable(string summary) =>
        new(
            Key,
            "Architecture",
            HostCheckStatus.Unavailable,
            Blocking: false,
            Summary: summary,
            Details: null,
            WhyItMatters: "The current MEM installer image and release tooling target supported Linux architectures.",
            RecommendedAction: "Resolve the Docker connectivity result above, then re-run server checks.",
            Evidence:
            [
                new DiagnosticEvidenceDto(
                    "Docker API",
                    "System info",
                    "Architecture evidence unavailable.")
            ]);
}
