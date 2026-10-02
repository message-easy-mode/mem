namespace Modules.Setup.Secrets;

public interface IInstallationSecretStore
{
    Task SetProtectedAsync(
        Guid installationId,
        string category,
        string key,
        string secret,
        string? description,
        CancellationToken cancellationToken);

    Task<string?> ResolveProtectedAsync(
        Guid installationId,
        string category,
        string key,
        CancellationToken cancellationToken);

    Task<bool> ExistsAsync(
        Guid installationId,
        string category,
        string key,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid installationId,
        string category,
        string key,
        CancellationToken cancellationToken);
}
