namespace Modules.Setup.InstallRuns;

public interface IInstallRunCoordinator
{
    InstallRunQueueResult Queue(Guid installationId);
}

public sealed record InstallRunQueueResult(
    Guid InstallationId,
    bool Queued,
    bool AlreadyOwned);
