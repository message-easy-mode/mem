namespace Modules.Setup.InstallRuns;

public interface IInstallRunner
{
    Task RunAsync(Guid installationId, CancellationToken cancellationToken);
}
