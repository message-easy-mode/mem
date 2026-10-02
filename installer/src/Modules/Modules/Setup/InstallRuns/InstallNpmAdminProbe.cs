using Shared.ControlPlane.Runtime;

namespace Modules.Setup.InstallRuns;

public sealed record InstallNpmAdminProbeResult(
    bool Reachable,
    string RouteKind,
    string? Error);

/// <summary>
/// Probes NPM using the server-owned managed-service authority for the active
/// Control Plane runtime. Local development uses the published host port;
/// containerized runtimes use Docker-network authority on NPM container port 81.
/// </summary>
public sealed class InstallNpmAdminProbe(
    HttpClient httpClient,
    IMemManagedServiceAuthorityResolver authorityResolver)
{
    public async Task<InstallNpmAdminProbeResult> ProbeAsync(
        string containerName,
        int publishedAdminPort,
        CancellationToken cancellationToken)
    {
        try
        {
            var authority = authorityResolver.Resolve(
                MemManagedServicePurposes.Administration,
                new MemManagedServiceAuthoritySource(
                    ServiceName: "npm",
                    PublishedHostPort: publishedAdminPort,
                    DockerNetworkAlias: containerName,
                    HealthPort: 81,
                    AdministrationPort: 81,
                    IngestionPort: null));

            using var request = new HttpRequestMessage(HttpMethod.Get, authority.Authority);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            return new InstallNpmAdminProbeResult(
                Reachable: response.IsSuccessStatusCode,
                RouteKind: authority.RouteKind,
                Error: response.IsSuccessStatusCode
                    ? null
                    : $"NPM admin probe returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new InstallNpmAdminProbeResult(
                Reachable: false,
                RouteKind: "timeout",
                Error: "NPM administration probe timed out.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new InstallNpmAdminProbeResult(
                Reachable: false,
                RouteKind: "unavailable",
                Error: ex.Message);
        }
    }
}
