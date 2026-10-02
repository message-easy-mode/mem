using Modules.Setup.HostChecks.Runtime;
using Shared.ControlPlane.Runtime;

namespace Modules.Setup.HostChecks.Checks;

public sealed class DiskSpaceCheck(
    ISetupDockerRuntimeProbe dockerProbe,
    MemControlPlaneRuntimeContext runtimeContext) : IHostCheck
{
    public string Key => "disk-space";
    public string GroupKey => "storage";
    public string GroupTitle => "Storage";
    public string GroupDescription => "Checks host storage only when the active runtime has an authoritative host filesystem view.";

    public async Task<HostCheckResultDto> RunAsync(CancellationToken cancellationToken)
    {
        if (MemRuntimeModes.IsContainerized(runtimeContext.RuntimeMode))
        {
            return new HostCheckResultDto(
                Key,
                "Host filesystem free space",
                HostCheckStatus.Unavailable,
                Blocking: false,
                Summary: "Host filesystem capacity is unavailable from the containerized Control Plane without pretending the container filesystem is the host.",
                Details: null,
                WhyItMatters: "Matrix media, Postgres, Docker images, logs, and backups can consume substantial host storage.",
                RecommendedAction: "Use host-side storage tooling when exact byte-level host filesystem capacity is required.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Runtime",
                        "Authority",
                        $"RuntimeMode={runtimeContext.RuntimeMode}; container-local df output is intentionally not used as host evidence.")
                ]);
        }

        if (!string.Equals(
                runtimeContext.RuntimeMode,
                MemRuntimeModes.LocalDevelopment,
                StringComparison.Ordinal))
        {
            return new HostCheckResultDto(
                Key,
                "Host filesystem free space",
                HostCheckStatus.Unavailable,
                Blocking: false,
                Summary: "Host filesystem capacity is not available in this runtime context.",
                Details: null,
                WhyItMatters: "Matrix media, Postgres, Docker images, logs, and backups can consume substantial host storage.",
                RecommendedAction: "Inspect host storage with host-side tooling before a production installation.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "Runtime",
                        "Authority",
                        $"RuntimeMode={runtimeContext.RuntimeMode}")
                ]);
        }

        try
        {
            var info = await dockerProbe.GetSystemInfoAsync(cancellationToken);
            var dockerRoot = string.IsNullOrWhiteSpace(info.DockerRootDir)
                ? "/"
                : info.DockerRootDir;
            var root = Path.GetPathRoot(dockerRoot);

            if (string.IsNullOrWhiteSpace(root))
            {
                return Unavailable("Docker reported a data-root path that could not be mapped to a local filesystem root.");
            }

            var drive = new DriveInfo(root);
            var availableMb = drive.AvailableFreeSpace / (1024d * 1024d);
            var status = availableMb < 10_240
                ? HostCheckStatus.Warning
                : HostCheckStatus.Pass;

            return new HostCheckResultDto(
                Key,
                "Host filesystem free space",
                status,
                Blocking: false,
                Summary: $"Local-development host view reports approximately {availableMb:N0} MB free on the filesystem backing Docker data.",
                Details: null,
                WhyItMatters: "Matrix media, Postgres, Docker images, logs, and backups can consume substantial host storage.",
                RecommendedAction: status == HostCheckStatus.Pass
                    ? null
                    : "Free more host disk space before installing. At least 10 GB free is a practical minimum for early testing.",
                Evidence:
                [
                    new DiagnosticEvidenceDto(
                        "System",
                        "Host filesystem",
                        $"DockerRootDir={dockerRoot}; AvailableFreeBytes={drive.AvailableFreeSpace}")
                ]);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Unavailable("Unable to inspect host filesystem capacity from local-development.");
        }
    }

    private HostCheckResultDto Unavailable(string summary) =>
        new(
            Key,
            "Host filesystem free space",
            HostCheckStatus.Unavailable,
            Blocking: false,
            Summary: summary,
            Details: null,
            WhyItMatters: "Matrix media, Postgres, Docker images, logs, and backups can consume substantial host storage.",
            RecommendedAction: "Inspect host storage with host-side tooling before a production installation.",
            Evidence: []);
}
