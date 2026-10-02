using System.Security.Cryptography;

namespace Modules.Setup.Secrets;

public sealed class InstallationCredentialService
{
    private readonly IInstallationSecretStore _secretStore;

    public InstallationCredentialService(IInstallationSecretStore secretStore)
    {
        _secretStore = secretStore;
    }

    public async Task EnsurePostgresPasswordAsync(
        Guid installationId,
        CancellationToken cancellationToken)
    {
        if (await _secretStore.ExistsAsync(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.PostgresPassword,
                cancellationToken))
        {
            return;
        }

        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        await _secretStore.SetProtectedAsync(
            installationId,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.PostgresPassword,
            password,
            "Generated Postgres bootstrap credential for this managed installation.",
            cancellationToken);
    }
}
