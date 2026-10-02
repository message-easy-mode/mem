using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Modules.Auth.Services;

public interface IInstallerTokenValidator
{
    Task<bool> IsValidAsync(string? suppliedToken, CancellationToken ct = default);
}

public sealed class InstallerTokenValidator(
    IInstallerSetupTokenStore tokenStore,
    ILogger<InstallerTokenValidator> logger)
    : IInstallerTokenValidator
{
    public async Task<bool> IsValidAsync(string? suppliedToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(suppliedToken))
        {
            return false;
        }

        var expectedToken = await tokenStore.GetSetupTokenAsync(ct);

        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            logger.LogError("Installer setup token is not configured.");
            return false;
        }

        return SecureTokenEquals(suppliedToken.Trim(), expectedToken);
    }

    private static bool SecureTokenEquals(string suppliedToken, string expectedToken)
    {
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken));
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedToken));

        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }
}