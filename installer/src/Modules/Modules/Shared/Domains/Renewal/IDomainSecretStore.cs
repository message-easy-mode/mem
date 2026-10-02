namespace Modules.Shared.Domains.Renewal;

public interface IDomainSecretStore
{
    Task SetProtectedAsync(
        Guid domainId,
        string category,
        string key,
        string secret,
        string? description,
        CancellationToken cancellationToken);

    Task<string?> ResolveProtectedAsync(
        Guid domainId,
        string category,
        string key,
        CancellationToken cancellationToken);

    Task<bool> ExistsAsync(
        Guid domainId,
        string category,
        string key,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid domainId,
        string category,
        string key,
        CancellationToken cancellationToken);
}
