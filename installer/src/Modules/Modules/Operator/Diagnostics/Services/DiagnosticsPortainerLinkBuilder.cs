using System.Text.RegularExpressions;
using Modules.Integrations.Portainer.Services;

namespace Modules.Operator.Diagnostics.Services;

public sealed partial class DiagnosticsPortainerLinkBuilder(
    PortainerRuntimeOptions options)
{
    private const string SupportedRouteVersionPrefix = "2.39.";

    public DiagnosticsPortainerLinkSet Build(string? observedVersion = null)
    {
        if (!PortainerRuntimeOptionsValidator.TryNormalizeUiUrl(
                options.UiUrl,
                out var baseUri) ||
            baseUri is null)
        {
            return DiagnosticsPortainerLinkSet.Unavailable;
        }

        var environmentId = options.EnvironmentId is > 0
            ? options.EnvironmentId.Value
            : (int?)null;
        var version = string.IsNullOrWhiteSpace(observedVersion)
            ? null
            : observedVersion.Trim();
        var exactSupported = environmentId.HasValue &&
                             version is not null &&
                             version.StartsWith(
                                 SupportedRouteVersionPrefix,
                                 StringComparison.Ordinal);

        return new DiagnosticsPortainerLinkSet(
            Home: baseUri.ToString().TrimEnd('/'),
            Environment: environmentId.HasValue
                ? WithFragment(baseUri, $"!/endpoints/{environmentId.Value}/docker/dashboard")
                : null,
            Containers: environmentId.HasValue
                ? WithFragment(baseUri, $"!/endpoints/{environmentId.Value}/docker/containers")
                : null,
            ExactResourceLinksSupported: exactSupported,
            EnvironmentId: environmentId);
    }

    public string? BuildContainer(
        DiagnosticsPortainerLinkSet links,
        string? containerId)
    {
        if (!links.ExactResourceLinksSupported ||
            links.EnvironmentId is not > 0 ||
            string.IsNullOrWhiteSpace(containerId) ||
            !DockerContainerIdRegex().IsMatch(containerId.Trim()))
        {
            return null;
        }

        if (!PortainerRuntimeOptionsValidator.TryNormalizeUiUrl(
                options.UiUrl,
                out var baseUri) ||
            baseUri is null)
        {
            return null;
        }

        return WithFragment(
            baseUri,
            $"!/endpoints/{links.EnvironmentId.Value}/docker/containers/{containerId.Trim().ToLowerInvariant()}");
    }

    private static string WithFragment(Uri baseUri, string fragment)
    {
        var builder = new UriBuilder(baseUri)
        {
            Query = string.Empty,
            Fragment = fragment
        };
        return builder.Uri.ToString();
    }

    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DockerContainerIdRegex();
}

public sealed record DiagnosticsPortainerLinkSet(
    string? Home,
    string? Environment,
    string? Containers,
    bool ExactResourceLinksSupported,
    int? EnvironmentId)
{
    public static DiagnosticsPortainerLinkSet Unavailable { get; } = new(
        Home: null,
        Environment: null,
        Containers: null,
        ExactResourceLinksSupported: false,
        EnvironmentId: null);
}
