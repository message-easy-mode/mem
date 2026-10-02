using Modules.Integrations.Npm.Contracts;

namespace Modules.Setup.Secrets;

public sealed class ProtectedNpmApiCredentialProvider(
    NpmAdminCredentialService credentialService) : INpmApiCredentialProvider
{
    public async Task<NpmApiCredential?> ResolveCurrentAsync(
        CancellationToken cancellationToken)
    {
        var credential = await credentialService.ResolveCurrentAsync(
            cancellationToken);

        return credential is null
            ? null
            : new NpmApiCredential(
                InstallationId: credential.InstallationId,
                Identity: credential.Email,
                Secret: credential.Password,
                VerifiedAtUtc: credential.VerifiedAtUtc);
    }
}
