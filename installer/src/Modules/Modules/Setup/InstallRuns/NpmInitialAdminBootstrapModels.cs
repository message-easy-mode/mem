namespace Modules.Setup.InstallRuns;

public sealed record NpmInitialAdminBootstrapTarget(
    string ContainerName,
    string Image,
    int HttpPort,
    int HttpsPort,
    int AdminPort,
    string DataVolumeName,
    string LetsEncryptVolumeName,
    string NetworkName,
    string NetworkAlias = "npm");

public sealed record NpmInitialAdminBootstrapResult(
    bool Succeeded,
    string Status,
    string Message,
    string? ErrorCode,
    bool InitialLoginVerified,
    bool Recreated,
    bool FinalLoginVerified,
    bool BootstrapEnvironmentRemoved);


public interface INpmInitialAdminBootstrapService
{
    Task<NpmInitialAdminBootstrapResult> BootstrapAsync(
        Guid installationId,
        NpmInitialAdminBootstrapTarget target,
        CancellationToken cancellationToken);
}
