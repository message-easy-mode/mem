using Modules.Setup.HostChecks.Runtime;

namespace Modules.Setup.HostChecks.Checks;

public sealed class MemoryCheck(ISetupDockerRuntimeProbe dockerProbe) : IHostCheck
{
    public string Key => "memory";
    public string GroupKey => "host";
    public string GroupTitle => "Host";
    public string GroupDescription => "Checks operating-system and host-capacity facts reported by the Docker daemon.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await dockerProbe.GetSystemInfoAsync(cancellationToken);

            if (info.MemoryBytes <= 0)
            {
                return Unavailable("Docker is reachable, but the host daemon did not report total memory.");
            }

            var totalMb = info.MemoryBytes / (1024d * 1024d);
            var status = totalMb < 2048
                ? HostCheckStatus.Warning
                : HostCheckStatus.Pass;

            return new HostCheckResultDto(
                Key,
                "Memory",
                status,
                Blocking: false,
                Summary: $"Docker reports approximately {totalMb:N0} MB host RAM.",
                Details: null,
                WhyItMatters: "Low memory can cause Postgres, NPM, the private Control Plane, and Matrix workloads to become unstable.",
                RecommendedAction: status == HostCheckStatus.Pass
                    ? null
                    : "Use at least 2 GB RAM for basic platform work. More is recommended for Matrix workloads.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "MemTotal",
                        info.MemoryBytes.ToString())
                ]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable("Unable to read host memory information from the Docker daemon.");
        }
    }

    private HostCheckResultDto Unavailable(string summary) =>
        new(
            Key,
            "Memory",
            HostCheckStatus.Unavailable,
            Blocking: false,
            Summary: summary,
            Details: null,
            WhyItMatters: "Low memory can cause Postgres, NPM, the private Control Plane, and Matrix workloads to become unstable.",
            RecommendedAction: "Resolve the Docker connectivity result above, then re-run server checks.",
            Evidence:
            [
                new DiagnosticEvidenceDto(
                    "Docker API",
                    "System info",
                    "Memory evidence unavailable.")
            ]);
}
