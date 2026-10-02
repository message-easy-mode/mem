namespace HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

/// <summary>
/// Narrow execution boundary for an isolated, catalog-bound private-staging
/// run. The Restore Workspace depends on this contract so it can be tested
/// without a Docker host while the production adapter uses
/// <see cref="PrivateStagingService"/>.
/// </summary>
public interface IPrivateStagingRunner
{
    Task<PrivateStagingRunResult> CreatePrivateSynapseFromCatalogAsync(
        string catalogEntryId,
        PrivateStagingRunRequest request,
        CancellationToken ct);
}

/// <summary>
/// Production adapter for <see cref="IPrivateStagingRunner"/>.
/// </summary>
public sealed class PrivateStagingRunner : IPrivateStagingRunner
{
    private readonly PrivateStagingService _service;

    public PrivateStagingRunner(PrivateStagingService service)
    {
        _service = service;
    }

    public Task<PrivateStagingRunResult> CreatePrivateSynapseFromCatalogAsync(
        string catalogEntryId,
        PrivateStagingRunRequest request,
        CancellationToken ct) =>
        _service.CreatePrivateSynapseFromCatalogAsync(catalogEntryId, request, ct);
}
