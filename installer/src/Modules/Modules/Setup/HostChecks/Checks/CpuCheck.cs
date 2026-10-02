using Modules.Setup.HostChecks.Runtime;

namespace Modules.Setup.HostChecks.Checks;

public sealed class CpuCheck(ISetupDockerRuntimeProbe dockerProbe) : IHostCheck
{
    public string Key => "cpu";
    public string GroupKey => "host";
    public string GroupTitle => "Host";
    public string GroupDescription => "Checks operating-system and host-capacity facts reported by the Docker daemon.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await dockerProbe.GetSystemInfoAsync(cancellationToken);
            var cpuCount = info.CpuCount;

            if (cpuCount <= 0)
            {
                return Unavailable("Docker is reachable, but the host daemon did not report a CPU count.");
            }

            var status = cpuCount < 2
                ? HostCheckStatus.Warning
                : HostCheckStatus.Pass;

            return new HostCheckResultDto(
                Key,
                "CPU",
                status,
                Blocking: false,
                Summary: $"Docker reports {cpuCount} host CPU core{(cpuCount == 1 ? "" : "s")}.",
                Details: null,
                WhyItMatters: "MEM, Postgres, Synapse, Element, and supporting services need enough CPU capacity to stay responsive.",
                RecommendedAction: status == HostCheckStatus.Pass
                    ? null
                    : "Use at least 2 CPU cores for a realistic MEM/Matrix host. More is recommended for production or federated homeservers.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "NCPU",
                        cpuCount.ToString())
                ]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable("Unable to read host CPU information from the Docker daemon.");
        }
    }

    private HostCheckResultDto Unavailable(string summary) =>
        new(
            Key,
            "CPU",
            HostCheckStatus.Unavailable,
            Blocking: false,
            Summary: summary,
            Details: null,
            WhyItMatters: "MEM, Postgres, Synapse, Element, and supporting services need enough CPU capacity to stay responsive.",
            RecommendedAction: "Resolve the Docker connectivity result above, then re-run server checks.",
            Evidence:
            [
                new DiagnosticEvidenceDto(
                    "Docker API",
                    "System info",
                    "CPU evidence unavailable.")
            ]);
}
