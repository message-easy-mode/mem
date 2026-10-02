using System.Net;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Runtime.Manifests;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;

namespace HostAgent.Matrix.Federation;

public sealed record FederationContainerObservation(
    bool Exists,
    bool Running,
    bool IdentityMatches,
    bool? ExpectedNetworkAttached,
    bool DirectHostPortExposed,
    string? ContainerId,
    string? ContainerName,
    string? Image,
    string? ProblemCode,
    string? Detail);

public interface IRuntimeStackFederationContainerInspector
{
    Task<FederationContainerObservation> InspectAsync(
        RuntimeStackServiceManifest matrix,
        CancellationToken ct);
}

public sealed class DockerRuntimeStackFederationContainerInspector
    : IRuntimeStackFederationContainerInspector
{
    private readonly DockerClient _docker;

    public DockerRuntimeStackFederationContainerInspector(DockerClient docker)
    {
        _docker = docker;
    }

    public async Task<FederationContainerObservation> InspectAsync(
        RuntimeStackServiceManifest matrix,
        CancellationToken ct)
    {
        var identity = !string.IsNullOrWhiteSpace(matrix.ContainerId)
            ? matrix.ContainerId
            : matrix.ContainerName;

        if (string.IsNullOrWhiteSpace(identity))
        {
            return Missing(
                "federation_manifest_incomplete",
                "The Runtime Stack manifest does not contain a Matrix container identity.");
        }

        ContainerInspectResponse inspect;
        try
        {
            inspect = await _docker.Containers.InspectContainerAsync(identity, ct);
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Missing(
                "federation_matrix_container_missing",
                "The Matrix container recorded by the Runtime Stack manifest was not found.");
        }

        var inspectedName = inspect.Name?.TrimStart('/');
        var idMatches = string.IsNullOrWhiteSpace(matrix.ContainerId) ||
            IdsMatch(inspect.ID, matrix.ContainerId);
        var nameMatches = string.IsNullOrWhiteSpace(matrix.ContainerName) ||
            string.Equals(inspectedName, matrix.ContainerName, StringComparison.OrdinalIgnoreCase);

        var expectedNetwork = matrix.RuntimeMetadata.TryGetValue("runtimeNetworkName", out var network)
            ? network?.Trim()
            : null;
        bool? networkAttached = string.IsNullOrWhiteSpace(expectedNetwork)
            ? null
            : inspect.NetworkSettings?.Networks?.ContainsKey(expectedNetwork) == true;

        var directPortExposed = inspect.HostConfig?.PortBindings?.Any(binding =>
            binding.Value is not null && binding.Value.Count > 0) == true;

        return new FederationContainerObservation(
            Exists: true,
            Running: inspect.State?.Running == true,
            IdentityMatches: idMatches && nameMatches,
            ExpectedNetworkAttached: networkAttached,
            DirectHostPortExposed: directPortExposed,
            ContainerId: inspect.ID,
            ContainerName: inspectedName,
            Image: inspect.Image ?? inspect.Config?.Image,
            ProblemCode: !idMatches || !nameMatches
                ? "federation_matrix_container_identity_mismatch"
                : string.IsNullOrWhiteSpace(expectedNetwork)
                    ? "federation_manifest_incomplete"
                    : null,
            Detail: !idMatches || !nameMatches
                ? "The inspected Matrix container does not match the Runtime Stack manifest identity."
                : string.IsNullOrWhiteSpace(expectedNetwork)
                    ? "The Runtime Stack manifest does not contain the expected Matrix runtime network."
                    : null);
    }

    private static bool IdsMatch(string? actual, string expected)
    {
        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        return actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase) ||
            expected.StartsWith(actual, StringComparison.OrdinalIgnoreCase);
    }

    private static FederationContainerObservation Missing(
        string code,
        string detail) =>
        new(
            Exists: false,
            Running: false,
            IdentityMatches: false,
            ExpectedNetworkAttached: null,
            DirectHostPortExposed: false,
            ContainerId: null,
            ContainerName: null,
            Image: null,
            ProblemCode: code,
            Detail: detail);
}

public sealed record FederationIngressObservation(
    bool Available,
    bool CanonicalRouteFound,
    string IngressMode,
    bool RouteEnabled,
    bool RouteTargetsExpectedMatrix,
    bool CertificatePresent,
    bool ServerWellKnownPublished,
    bool FederationPathsPubliclyForwarded,
    bool SigningKeyPathsPubliclyForwarded,
    bool AlternateMatrixRouteDetected,
    string? RouteSnapshotSha256,
    string? ProblemCode,
    string? Detail);

public interface IRuntimeStackFederationIngressInspector
{
    Task<FederationIngressObservation> InspectAsync(
        RuntimeStackServiceManifest matrix,
        CancellationToken ct);
}

public sealed class NpmRuntimeStackFederationIngressInspector
    : IRuntimeStackFederationIngressInspector
{
    private readonly NpmProxyHostService _npm;

    public NpmRuntimeStackFederationIngressInspector(NpmProxyHostService npm)
    {
        _npm = npm;
    }

    public async Task<FederationIngressObservation> InspectAsync(
        RuntimeStackServiceManifest matrix,
        CancellationToken ct)
    {
        var publicHost = matrix.PublicHost?.Trim();
        var internalHost = matrix.InternalHost?.Trim();
        if (string.IsNullOrWhiteSpace(publicHost) || string.IsNullOrWhiteSpace(internalHost))
        {
            return Unavailable(
                "federation_manifest_incomplete",
                "The Runtime Stack manifest does not contain the Matrix public and internal hosts required for NPM inspection.");
        }

        var expectedPort = ResolveInternalPort(matrix.InternalBaseUrl);
        var canonical = await _npm.GetByDomainAsync(publicHost, ct);
        var routes = await _npm.ListAsync(ct);

        if (canonical is null)
        {
            return new FederationIngressObservation(
                Available: true,
                CanonicalRouteFound: false,
                IngressMode: FederationIngressModes.Missing,
                RouteEnabled: false,
                RouteTargetsExpectedMatrix: false,
                CertificatePresent: false,
                ServerWellKnownPublished: false,
                FederationPathsPubliclyForwarded: false,
                SigningKeyPathsPubliclyForwarded: false,
                AlternateMatrixRouteDetected: HasAlternateRoute(routes, null, internalHost, expectedPort),
                RouteSnapshotSha256: null,
                ProblemCode: "federation_ingress_missing",
                Detail: "The canonical Matrix NPM proxy host could not be found.");
        }

        var ingressMode = FederationIngressPolicy.Classify(canonical.advanced_config);
        var enabled = canonical.enabled ?? true;
        var targetMatches = string.Equals(
                canonical.forward_host?.Trim(),
                internalHost,
                StringComparison.OrdinalIgnoreCase) &&
            (canonical.forward_port ?? 0) == expectedPort &&
            string.Equals(canonical.forward_scheme?.Trim() ?? "http", "http", StringComparison.OrdinalIgnoreCase);
        var certificatePresent = (canonical.certificate_id ?? 0) > 0 && (canonical.ssl_forced ?? false);
        var routeHash = FederationIngressPolicy.ComputeSnapshotHash(
            canonical.id.ToString(),
            string.Join(",", (canonical.domain_names ?? []).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)),
            canonical.forward_host,
            canonical.forward_port?.ToString(),
            canonical.forward_scheme,
            canonical.certificate_id?.ToString(),
            canonical.ssl_forced?.ToString(),
            canonical.enabled?.ToString(),
            FederationIngressPolicy.Normalize(canonical.advanced_config));

        var normal = string.Equals(ingressMode, FederationIngressModes.Normal, StringComparison.Ordinal);
        var localOnly = string.Equals(ingressMode, FederationIngressModes.LocalOnly, StringComparison.Ordinal);

        return new FederationIngressObservation(
            Available: true,
            CanonicalRouteFound: true,
            IngressMode: ingressMode,
            RouteEnabled: enabled && canonical.meta?.nginx_online != false,
            RouteTargetsExpectedMatrix: targetMatches,
            CertificatePresent: certificatePresent,
            ServerWellKnownPublished: normal,
            FederationPathsPubliclyForwarded: normal,
            SigningKeyPathsPubliclyForwarded: normal,
            AlternateMatrixRouteDetected: HasAlternateRoute(routes, canonical.id, internalHost, expectedPort),
            RouteSnapshotSha256: routeHash,
            ProblemCode: localOnly || normal ? null : "federation_ingress_custom_unsupported",
            Detail: localOnly || normal
                ? null
                : "The canonical Matrix NPM proxy host uses advanced configuration that MEM does not recognise as a supported federation ingress mode.");
    }

    private static int ResolveInternalPort(string? internalBaseUrl)
    {
        if (Uri.TryCreate(internalBaseUrl, UriKind.Absolute, out var uri))
        {
            return uri.Port;
        }

        return 8008;
    }

    private static bool HasAlternateRoute(
        IReadOnlyList<NpmProxyHost> routes,
        int? canonicalId,
        string expectedHost,
        int expectedPort) =>
        routes.Any(route =>
            route.id != canonicalId &&
            (route.enabled ?? true) &&
            route.meta?.nginx_online != false &&
            string.Equals(route.forward_host?.Trim(), expectedHost, StringComparison.OrdinalIgnoreCase) &&
            (route.forward_port ?? 0) == expectedPort);

    private static FederationIngressObservation Unavailable(
        string code,
        string detail) =>
        new(
            Available: false,
            CanonicalRouteFound: false,
            IngressMode: FederationIngressModes.Unavailable,
            RouteEnabled: false,
            RouteTargetsExpectedMatrix: false,
            CertificatePresent: false,
            ServerWellKnownPublished: false,
            FederationPathsPubliclyForwarded: false,
            SigningKeyPathsPubliclyForwarded: false,
            AlternateMatrixRouteDetected: false,
            RouteSnapshotSha256: null,
            ProblemCode: code,
            Detail: detail);
}
