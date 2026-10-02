using Modules.Setup.HostChecks.Runtime;

namespace Modules.Setup.HostChecks.Checks;

public sealed class UbuntuVersionCheck(ISetupDockerRuntimeProbe dockerProbe) : IHostCheck
{
    public string Key => "ubuntu-version";
    public string GroupKey => "host";
    public string GroupTitle => "Host";
    public string GroupDescription => "Checks operating-system and host-capacity facts reported by the Docker daemon.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var info = await dockerProbe.GetSystemInfoAsync(cancellationToken);
            var operatingSystem = info.OperatingSystem.Trim();

            if (string.IsNullOrWhiteSpace(operatingSystem))
            {
                return new HostCheckResultDto(
                    Key,
                    "Ubuntu version",
                    HostCheckStatus.Unavailable,
                    Blocking: false,
                    Summary: "Docker is reachable, but the host daemon did not report an operating-system description.",
                    Details: null,
                    WhyItMatters: "MEM v0.2.0 is released for supported Ubuntu hosts.",
                    RecommendedAction: "Confirm the host operating system before relying on the installation.",
                    Evidence:
                    [
                        new DiagnosticEvidenceDto(
                            "Docker API",
                            "OperatingSystem",
                            "Docker daemon did not report OperatingSystem.")
                    ]);
            }

            var supported = operatingSystem.Contains(
                "Ubuntu",
                StringComparison.OrdinalIgnoreCase);

            return new HostCheckResultDto(
                Key,
                "Ubuntu version",
                supported ? HostCheckStatus.Pass : HostCheckStatus.Warning,
                Blocking: false,
                Summary: supported
                    ? $"Docker reports host operating system: {operatingSystem}."
                    : $"Docker reports host operating system: {operatingSystem}.",
                Details: null,
                WhyItMatters: "MEM v0.2.0 is released for supported Ubuntu hosts.",
                RecommendedAction: supported
                    ? null
                    : "Proceed only if this host operating system is intentionally supported for the current MEM release.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Docker API",
                        "OperatingSystem",
                        operatingSystem)
                ]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return DockerEvidenceUnavailable(
                "Ubuntu version",
                "Unable to read host operating-system information from the Docker daemon.");
        }
    }

    private HostCheckResultDto DockerEvidenceUnavailable(
        string title,
        string summary) =>
        new(
            Key,
            title,
            HostCheckStatus.Unavailable,
            Blocking: false,
            Summary: summary,
            Details: null,
            WhyItMatters: "MEM v0.2.0 is released for supported Ubuntu hosts.",
            RecommendedAction: "Resolve the Docker connectivity result above, then re-run server checks.",
            Evidence:
            [
                new DiagnosticEvidenceDto(
                    "Docker API",
                    "System info",
                    "Docker API request failed.")
            ]);
}
