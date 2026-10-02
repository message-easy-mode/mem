using HostAgent.Runtime.Manifests;

namespace HostAgent.Matrix.Federation;

public interface IRuntimeStackFederationManifestResolver
{
    Task<RuntimeStackManifest?> FindAsync(
        string slugOrId,
        CancellationToken ct);
}

public sealed class RuntimeStackFederationManifestResolver
    : IRuntimeStackFederationManifestResolver
{
    private readonly RuntimeStackManifestStore _store;

    public RuntimeStackFederationManifestResolver(RuntimeStackManifestStore store)
    {
        _store = store;
    }

    public Task<RuntimeStackManifest?> FindAsync(
        string slugOrId,
        CancellationToken ct) =>
        _store.FindAsync(slugOrId, ct);
}
