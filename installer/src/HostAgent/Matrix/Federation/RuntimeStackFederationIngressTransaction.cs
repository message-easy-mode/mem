using HostAgent.Runtime.Ingress;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;

namespace HostAgent.Matrix.Federation;

public sealed class RuntimeStackFederationIngressStateChangedException : InvalidOperationException
{
    public RuntimeStackFederationIngressStateChangedException(string detail)
        : base(detail)
    {
    }
}

public interface IRuntimeStackFederationIngressTransaction
{
    Task ApplyModeAsync(
        RuntimeStackFederationSnapshot snapshot,
        string mode,
        CancellationToken ct);

    Task RestoreBeforeAsync(
        RuntimeStackFederationSnapshot snapshot,
        CancellationToken ct);
}

public sealed class RuntimeStackFederationIngressTransaction
    : IRuntimeStackFederationIngressTransaction
{
    private readonly NpmProxyHostService _npm;

    public RuntimeStackFederationIngressTransaction(NpmProxyHostService npm)
    {
        _npm = npm;
    }

    public async Task ApplyModeAsync(
        RuntimeStackFederationSnapshot snapshot,
        string mode,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var current = await _npm.GetByDomainAsync(snapshot.NpmLookupDomain, ct);
        if (!NpmProxyHostService.SnapshotMatches(
                snapshot.NpmRouteSnapshot,
                current,
                requireSameId: true))
        {
            throw new RuntimeStackFederationIngressStateChangedException(
                "The canonical NPM route changed after federation review and snapshot capture.");
        }

        var desired = snapshot.NpmRouteSnapshot with
        {
            AdvancedConfig = TemplateFor(mode)
        };

        var restored = await _npm.RestoreSnapshotAsync(
            desired,
            snapshot.NpmLookupDomain,
            ct);
        if (!NpmProxyHostService.SnapshotMatches(
                desired,
                restored,
                requireSameId: true))
        {
            throw new InvalidOperationException(
                "NPM did not retain the requested canonical federation ingress configuration.");
        }

        var readBack = await _npm.GetByDomainAsync(snapshot.NpmLookupDomain, ct);
        if (!NpmProxyHostService.SnapshotMatches(
                desired,
                readBack,
                requireSameId: true))
        {
            throw new InvalidOperationException(
                "The canonical NPM federation ingress configuration did not match after read-back.");
        }
    }

    public async Task RestoreBeforeAsync(
        RuntimeStackFederationSnapshot snapshot,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        await _npm.RestoreSnapshotAsync(
            snapshot.NpmRouteSnapshot,
            snapshot.NpmLookupDomain,
            ct);

        var readBack = await _npm.GetByDomainAsync(snapshot.NpmLookupDomain, ct);
        if (!NpmProxyHostService.SnapshotMatches(
                snapshot.NpmRouteSnapshot,
                readBack,
                requireSameId: false))
        {
            throw new InvalidOperationException(
                "MEM could not restore and read back the exact previous NPM route state.");
        }
    }

    private static string TemplateFor(string mode) => mode switch
    {
        FederationModes.LocalOnly => IngressAdvancedConfigTemplates.MatrixLocalOnly(),
        FederationModes.Public or FederationModes.Restricted => IngressAdvancedConfigTemplates.MatrixWellKnown(),
        _ => throw new ArgumentOutOfRangeException(
            nameof(mode),
            mode,
            "Only Public, Restricted, and Local-only federation ingress modes are supported.")
    };
}
