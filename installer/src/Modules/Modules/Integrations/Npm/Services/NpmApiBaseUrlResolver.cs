using Core.RuntimeDefinition;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modules.Integrations.Npm.Contracts;
using Shared.ControlPlane.Runtime;

namespace Modules.Integrations.Npm.Services;

public sealed class NpmApiBaseUrlResolver(
    MemDbContext db,
    IOptions<NpmApiOptions> options,
    ILogger<NpmApiBaseUrlResolver> logger,
    IMemManagedServiceAuthorityResolver authorityResolver)
{
    public async Task<NpmApiBaseUrlResolution> ResolveAsync(
        CancellationToken cancellationToken)
    {
        var configured = NormalizeBaseUrl(options.Value.BaseUrl);

        var runtime = await db.RuntimeServices
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ServiceName == ManagedServiceNames.Npm, cancellationToken);

        var managed = TryResolveManagedAuthority(
            runtime?.SelectedHostPort is > 0 ? runtime.SelectedHostPort : null,
            configured,
            runtime is not null);

        if (managed is not null)
        {
            return managed;
        }

        if (!string.IsNullOrWhiteSpace(configured))
        {
            var warning = runtime is null
                ? "NPM runtime database registration is missing, so MEM is using the explicitly configured NPM API URL."
                : "The NPM runtime database record does not expose a usable managed-service authority, so MEM is using the explicitly configured NPM API URL.";

            logger.LogDebug("{Warning} BaseUrl={BaseUrl}", warning, configured);

            return new NpmApiBaseUrlResolution(
                BaseUrl: configured,
                Source: MemManagedServiceRouteKinds.ConfiguredAuthority,
                RuntimeRecordFound: runtime is not null,
                Warning: warning);
        }

        throw new InvalidOperationException(
            "NPM API base URL could not be resolved from the active runtime. Ensure the NPM container is reachable through the MEM managed-service authority.");
    }

    public string ResolveConfigured()
    {
        var configured = NormalizeBaseUrl(options.Value.BaseUrl);

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("NPM API base URL is not configured.");
        }

        return configured;
    }

    private NpmApiBaseUrlResolution? TryResolveManagedAuthority(
        int? publishedHostPort,
        string configured,
        bool runtimeRecordFound)
    {
        try
        {
            var authority = authorityResolver.Resolve(
                MemManagedServicePurposes.Administration,
                new MemManagedServiceAuthoritySource(
                    ServiceName: ManagedServiceNames.Npm,
                    PublishedHostPort: publishedHostPort,
                    DockerNetworkAlias: ManagedNetworkAliases.Npm,
                    HealthPort: 81,
                    AdministrationPort: 81,
                    IngestionPort: null,
                    ConfiguredAdministrationAuthority: ToAuthority(configured)));

            string? warning = null;
            if (!runtimeRecordFound)
            {
                warning = authority.RouteKind == MemManagedServiceRouteKinds.DockerNetwork
                    ? "NPM runtime database registration is missing; MEM resolved the server-owned Docker-network NPM administration authority directly."
                    : null;
            }
            else if (publishedHostPort is null &&
                     authority.RouteKind == MemManagedServiceRouteKinds.DockerNetwork)
            {
                warning = "The NPM runtime database record does not expose a selected host admin port; MEM is using the server-owned Docker-network administration authority.";
            }

            if (!string.IsNullOrWhiteSpace(warning))
            {
                logger.LogDebug(
                    "{Warning} RouteKind={RouteKind} Authority={Authority}",
                    warning,
                    authority.RouteKind,
                    authority.Authority);
            }

            return new NpmApiBaseUrlResolution(
                BaseUrl: new Uri(authority.Authority, "/api").ToString().TrimEnd('/'),
                Source: authority.RouteKind,
                RuntimeRecordFound: runtimeRecordFound,
                Warning: warning);
        }
        catch (MemManagedServiceAuthorityException)
        {
            return null;
        }
    }

    private static string NormalizeBaseUrl(string? baseUrl) =>
        (baseUrl ?? "").Trim().TrimEnd('/');

    private static Uri? ToAuthority(string configuredApiUrl)
    {
        if (!Uri.TryCreate(configuredApiUrl, UriKind.Absolute, out var uri) ||
            (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var builder = new UriBuilder(uri)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri;
    }
}

public sealed record NpmApiBaseUrlResolution(
    string BaseUrl,
    string Source,
    bool RuntimeRecordFound,
    string? Warning);
