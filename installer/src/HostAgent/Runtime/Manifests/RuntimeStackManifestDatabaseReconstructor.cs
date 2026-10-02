using System.Globalization;
using System.Text.Json;
using HostAgent.Runtime.ServiceRuntime;
using HostAgent.Runtime.Stacks.Identity;
using Infrastructure.Data.Entities;

namespace HostAgent.Runtime.Manifests;

/// <summary>
/// Rebuilds the browser/operator runtime-stack manifest projection from the
/// durable RuntimeStack, service-instance, and route records. This is a
/// recovery boundary only; it does not inspect or mutate live Docker/NPM state.
/// </summary>
public static class RuntimeStackManifestDatabaseReconstructor
{
    public static RuntimeStackManifest Build(RuntimeStackEntity stack)
    {
        ArgumentNullException.ThrowIfNull(stack);

        if (IsDestroyedRuntimeStack(stack))
        {
            throw new InvalidOperationException(
                $"Runtime stack '{stack.Slug}' is already recorded as destroyed.");
        }

        var matrix = BuildServiceSnapshot(stack, ServiceKeys.Matrix, required: true)!;
        var element = BuildServiceSnapshot(stack, ServiceKeys.ElementWeb, required: false);
        var lastVerifiedAt = stack.LastVerifiedAtUtc ?? stack.UpdatedAtUtc;

        var durableMetadata = ParseMetadata(stack.MetadataJson);
        var metadata = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ownershipSource"] = "database-reconstruction",
            ["manifestPath"] = stack.ManifestPath,
            ["dataRoot"] = stack.DataRoot,
            ["runtimeNetworkName"] = stack.RuntimeNetworkName,
            [RuntimeStackIdentity.DisplayNameMetadataKey] =
                RuntimeStackIdentity.NormalizeDisplayName(stack.DisplayName, stack.Slug)
        };

        if (durableMetadata.TryGetValue(RuntimeStackIdentity.CategoryMetadataKey, out var category) &&
            RuntimeStackIdentity.NormalizeCategory(category) is { } normalizedCategory)
        {
            metadata[RuntimeStackIdentity.CategoryMetadataKey] = normalizedCategory;
        }

        RuntimeStackIdentity.CopyLogoMetadata(durableMetadata, metadata);

        return new RuntimeStackManifest(
            Source: "control-plane-database-reconstruction",
            StackId: stack.Id,
            Slug: stack.Slug,
            LastVerifiedStatus: stack.LastVerifiedStatus ?? stack.Status,
            LastVerifiedAtUtc: new DateTimeOffset(
                DateTime.SpecifyKind(lastVerifiedAt, DateTimeKind.Utc)),
            Matrix: matrix,
            Element: element,
            Warnings:
            [
                "The active runtime-stack manifest was missing. MEM reconstructed the operator manifest projection from durable database records. Runtime Reconciliation should be used to verify live Docker and NPM state."
            ],
            Metadata: metadata);
    }

    private static RuntimeStackServiceManifest? BuildServiceSnapshot(
        RuntimeStackEntity stack,
        string serviceKey,
        bool required)
    {
        var services = stack.ServiceInstances
            .Where(x => string.Equals(
                x.ServiceKey,
                serviceKey,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (services.Length == 0)
        {
            if (!required)
            {
                return null;
            }

            throw new InvalidOperationException(
                $"Runtime stack '{stack.Slug}' cannot be reconstructed because its '{serviceKey}' service record is missing.");
        }

        if (services.Length != 1)
        {
            throw new InvalidOperationException(
                $"Runtime stack '{stack.Slug}' cannot be reconstructed because it has {services.Length} '{serviceKey}' service records.");
        }

        var service = services[0];
        var routes = stack.Routes
            .Where(x =>
                x.RuntimeServiceInstanceId == service.Id ||
                string.Equals(
                    x.ServiceKey,
                    serviceKey,
                    StringComparison.OrdinalIgnoreCase))
            .DistinctBy(x => x.Id)
            .ToArray();

        if (routes.Length > 1)
        {
            throw new InvalidOperationException(
                $"Runtime stack '{stack.Slug}' cannot be reconstructed because '{serviceKey}' has {routes.Length} route records.");
        }

        var route = routes.SingleOrDefault();
        if (route is not null &&
            !string.Equals(route.Provider, "npm", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Runtime stack '{stack.Slug}' cannot be reconstructed because '{serviceKey}' uses unsupported route provider '{route.Provider}'.");
        }

        var metadata = ParseMetadata(service.RuntimeMetadataJson);
        metadata["runtimeNetworkName"] = service.NetworkName ?? stack.RuntimeNetworkName;
        metadata["publicForwardHost"] = route?.ForwardHost;
        metadata["publicForwardPort"] = route?.ForwardPort.ToString(CultureInfo.InvariantCulture);
        metadata["activeNpmCertificateId"] = (route?.NpmCertificateId ?? stack.ActiveNpmCertificateId)
            ?.ToString(CultureInfo.InvariantCulture);

        return new RuntimeStackServiceManifest(
            InstanceId: service.InstanceId,
            ServiceKey: service.ServiceKey,
            ContainerId: service.ContainerId,
            ContainerName: service.ContainerName,
            HostPort: service.HostPort ?? 0,
            DataPath: service.DataPath,
            ServerName: service.ServerName,
            PublicHost: route?.PublicHost ?? service.PublicHost,
            PublicBaseUrl: route?.PublicBaseUrl ?? service.PublicBaseUrl,
            InternalHost: service.InternalHost,
            InternalBaseUrl: service.InternalBaseUrl,
            PublicRouteId: route?.ProviderRouteId,
            InternalRouteId: null,
            NpmCertificateId: route?.NpmCertificateId ?? stack.ActiveNpmCertificateId,
            RuntimeMetadata: metadata);
    }

    private static Dictionary<string, string?> ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) is { } parsed
                ? new Dictionary<string, string?>(parsed, StringComparer.Ordinal)
                : new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }

    private static bool IsDestroyedRuntimeStack(RuntimeStackEntity stack) =>
        string.Equals(stack.Status, "destroyed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(stack.LastVerifiedStatus, "destroyed", StringComparison.OrdinalIgnoreCase) ||
        stack.Slug.Contains("--destroyed-", StringComparison.OrdinalIgnoreCase);
}
