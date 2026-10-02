namespace Shared.ControlPlane.Runtime;

public static class MemManagedServicePurposes
{
    public const string Health = "health";
    public const string Administration = "administration";
    public const string Ingestion = "ingestion";
    public const string Browser = "browser";
}

public static class MemManagedServiceRouteKinds
{
    public const string HostPublishedLoopback = "host-published-loopback";
    public const string HostPublishedPrivate = "host-published-private";
    public const string DockerNetwork = "docker-network";
    public const string BrowserAuthority = "browser-authority";
    public const string ConfiguredAuthority = "configured-authority";
}

public sealed record MemManagedServiceAuthoritySource(
    string ServiceName,
    int? PublishedHostPort,
    string? DockerNetworkAlias,
    int? HealthPort,
    int? AdministrationPort,
    int? IngestionPort,
    Uri? BrowserAuthority = null,
    Uri? ConfiguredHealthAuthority = null,
    Uri? ConfiguredAdministrationAuthority = null,
    Uri? ConfiguredIngestionAuthority = null);

public sealed record MemManagedServiceAuthority(
    string ServiceName,
    string Purpose,
    Uri Authority,
    string RouteKind);

public interface IMemManagedServiceAuthorityResolver
{
    MemManagedServiceAuthority Resolve(
        string purpose,
        MemManagedServiceAuthoritySource source);
}

public sealed class MemManagedServiceAuthorityResolver(
    MemControlPlaneRuntimeContext runtimeContext) : IMemManagedServiceAuthorityResolver
{
    public MemManagedServiceAuthority Resolve(
        string purpose,
        MemManagedServiceAuthoritySource source) =>
        Resolve(runtimeContext, purpose, source);

    public static MemManagedServiceAuthority Resolve(
        MemControlPlaneRuntimeContext runtimeContext,
        string purpose,
        MemManagedServiceAuthoritySource source)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.ServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var normalizedPurpose = purpose.Trim().ToLowerInvariant();
        if (normalizedPurpose == MemManagedServicePurposes.Browser)
        {
            if (source.BrowserAuthority is not null)
            {
                return new MemManagedServiceAuthority(
                    source.ServiceName,
                    normalizedPurpose,
                    source.BrowserAuthority,
                    MemManagedServiceRouteKinds.BrowserAuthority);
            }

            if (source.PublishedHostPort is not (> 0 and <= 65535))
            {
                throw Unavailable(source.ServiceName, normalizedPurpose);
            }

            if (string.Equals(
                    runtimeContext.RuntimeMode,
                    MemRuntimeModes.LocalDevelopment,
                    StringComparison.Ordinal) ||
                string.Equals(
                    runtimeContext.RuntimeMode,
                    MemRuntimeModes.ContainerizedDevelopment,
                    StringComparison.Ordinal))
            {
                return new MemManagedServiceAuthority(
                    source.ServiceName,
                    normalizedPurpose,
                    new UriBuilder(
                        Uri.UriSchemeHttp,
                        "127.0.0.1",
                        source.PublishedHostPort.Value).Uri,
                    MemManagedServiceRouteKinds.HostPublishedLoopback);
            }

            if (string.Equals(
                    runtimeContext.RuntimeMode,
                    MemRuntimeModes.ContainerizedProduction,
                    StringComparison.Ordinal) &&
                MemHostAccessIpv4.IsPrivateBrowserCandidate(
                    runtimeContext.HostAccessIpv4))
            {
                return new MemManagedServiceAuthority(
                    source.ServiceName,
                    normalizedPurpose,
                    new UriBuilder(
                        Uri.UriSchemeHttp,
                        runtimeContext.HostAccessIpv4!,
                        source.PublishedHostPort.Value).Uri,
                    MemManagedServiceRouteKinds.HostPublishedPrivate);
            }

            throw Unavailable(source.ServiceName, normalizedPurpose);
        }

        if (string.Equals(
                runtimeContext.RuntimeMode,
                MemRuntimeModes.LocalDevelopment,
                StringComparison.Ordinal))
        {
            if (source.PublishedHostPort is > 0 and <= 65535)
            {
                return new MemManagedServiceAuthority(
                    source.ServiceName,
                    normalizedPurpose,
                    new UriBuilder(
                        Uri.UriSchemeHttp,
                        "127.0.0.1",
                        source.PublishedHostPort.Value).Uri,
                    MemManagedServiceRouteKinds.HostPublishedLoopback);
            }

            throw Unavailable(source.ServiceName, normalizedPurpose);
        }

        if (MemRuntimeModes.IsContainerized(runtimeContext.RuntimeMode))
        {
            var port = normalizedPurpose switch
            {
                MemManagedServicePurposes.Health => source.HealthPort,
                MemManagedServicePurposes.Administration => source.AdministrationPort,
                MemManagedServicePurposes.Ingestion => source.IngestionPort,
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(source.DockerNetworkAlias) &&
                port is > 0 and <= 65535)
            {
                return new MemManagedServiceAuthority(
                    source.ServiceName,
                    normalizedPurpose,
                    new UriBuilder(
                        Uri.UriSchemeHttp,
                        source.DockerNetworkAlias.Trim(),
                        port.Value).Uri,
                    MemManagedServiceRouteKinds.DockerNetwork);
            }

            throw Unavailable(source.ServiceName, normalizedPurpose);
        }

        if (string.Equals(
                runtimeContext.RuntimeMode,
                MemRuntimeModes.AutomatedTest,
                StringComparison.Ordinal))
        {
            return ResolveConfigured(source, normalizedPurpose);
        }

        throw Unavailable(source.ServiceName, normalizedPurpose);
    }

    private static MemManagedServiceAuthority ResolveConfigured(
        MemManagedServiceAuthoritySource source,
        string purpose)
    {
        var authority = purpose switch
        {
            MemManagedServicePurposes.Health => source.ConfiguredHealthAuthority,
            MemManagedServicePurposes.Administration => source.ConfiguredAdministrationAuthority,
            MemManagedServicePurposes.Ingestion => source.ConfiguredIngestionAuthority,
            _ => null
        };

        return authority is not null
            ? new MemManagedServiceAuthority(
                source.ServiceName,
                purpose,
                authority,
                MemManagedServiceRouteKinds.ConfiguredAuthority)
            : throw Unavailable(source.ServiceName, purpose);
    }

    private static MemManagedServiceAuthorityException Unavailable(
        string serviceName,
        string purpose) => new(
        "managed_service_authority_unavailable",
        $"No server-owned {purpose} authority is available for managed service '{serviceName}'.");
}

public sealed class MemManagedServiceAuthorityException(
    string code,
    string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}
