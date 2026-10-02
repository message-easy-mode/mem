using System.Globalization;
using HostAgent.Runtime.Manifests;
using Modules.Integrations.Npm.Contracts;

namespace HostAgent.Runtime.Stacks.Destroy;

internal static class RuntimeStackDestroyRouteOwnershipValidator
{
    public static void ValidateDatabaseReconstruction(
        Guid runtimeStackId,
        RuntimeStackServiceManifest service,
        NpmProxyHost observed)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(observed);

        var expectedDomain = NormalizeHost(service.PublicHost);
        if (expectedDomain.Length == 0)
        {
            throw Refused(
                runtimeStackId,
                service.ServiceKey,
                "the durable service record does not contain a public route hostname");
        }

        var matchingDomains = (observed.domain_names ?? [])
            .Select(NormalizeHost)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!matchingDomains.Contains(expectedDomain))
        {
            throw Refused(
                runtimeStackId,
                service.ServiceKey,
                $"the observed NPM proxy host does not contain the recorded domain '{expectedDomain}'");
        }

        var expectedForwardHost = ReadRequiredMetadata(
            runtimeStackId,
            service,
            "publicForwardHost");
        var expectedForwardPort = ReadRequiredPortMetadata(
            runtimeStackId,
            service,
            "publicForwardPort");

        if (!string.Equals(
                expectedForwardHost,
                observed.forward_host?.Trim(),
                StringComparison.OrdinalIgnoreCase) ||
            expectedForwardPort != observed.forward_port)
        {
            throw Refused(
                runtimeStackId,
                service.ServiceKey,
                $"the observed NPM target '{observed.forward_host}:{observed.forward_port}' does not match the recorded target '{expectedForwardHost}:{expectedForwardPort}'");
        }

        if (service.NpmCertificateId is > 0 &&
            observed.certificate_id != service.NpmCertificateId)
        {
            throw Refused(
                runtimeStackId,
                service.ServiceKey,
                $"the observed NPM certificate id '{observed.certificate_id}' does not match the recorded id '{service.NpmCertificateId}'");
        }
    }

    private static string ReadRequiredMetadata(
        Guid runtimeStackId,
        RuntimeStackServiceManifest service,
        string key)
    {
        if (service.RuntimeMetadata.TryGetValue(key, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        throw Refused(
            runtimeStackId,
            service.ServiceKey,
            $"the durable route metadata does not contain '{key}'");
    }

    private static int ReadRequiredPortMetadata(
        Guid runtimeStackId,
        RuntimeStackServiceManifest service,
        string key)
    {
        var value = ReadRequiredMetadata(runtimeStackId, service, key);
        if (int.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var port) &&
            port is > 0 and <= 65535)
        {
            return port;
        }

        throw Refused(
            runtimeStackId,
            service.ServiceKey,
            $"the durable route metadata '{key}' is invalid");
    }

    private static string NormalizeHost(string? value) =>
        (value ?? string.Empty)
            .Trim()
            .TrimEnd('.')
            .ToLowerInvariant();

    private static RuntimeStackDestroyOwnershipRefusedException Refused(
        Guid runtimeStackId,
        string serviceKey,
        string reason) =>
        new(
            $"Refusing database-reconstructed destroy for runtime stack '{runtimeStackId:D}' service '{serviceKey}' because {reason}.");
}
