using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.ServiceRuntime;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Stacks.Destroy;

internal sealed record RuntimeStackDestroyContainerObservation(
    string Id,
    string Name,
    bool Running,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<string> MountSources);

internal static class RuntimeStackDestroyContainerOwnershipValidator
{
    public static void ValidateDatabaseReconstruction(
        Guid runtimeStackId,
        RuntimeStackServiceManifest service,
        RuntimeStackDestroyContainerObservation observed,
        MemControlPlaneRuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(runtimeContext);

        var expectedName = NormalizeName(service.ContainerName);
        var actualName = NormalizeName(observed.Name);

        if (expectedName.Length == 0)
        {
            throw Refused(
                runtimeStackId,
                service.ServiceKey,
                "the durable service record does not contain a container name");
        }

        if (!string.Equals(expectedName, actualName, StringComparison.OrdinalIgnoreCase))
        {
            throw Refused(
                runtimeStackId,
                service.ServiceKey,
                $"the observed container name '{actualName}' does not match the recorded name '{expectedName}'");
        }

        if (!string.IsNullOrWhiteSpace(service.ContainerId) &&
            !string.Equals(
                service.ContainerId.Trim(),
                observed.Id,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Refused(
                runtimeStackId,
                service.ServiceKey,
                "the observed container id does not match the durable service record");
        }

        RequireLabel(
            observed.Labels,
            MemDockerOwnershipLabels.ManagedKey,
            "true",
            runtimeStackId,
            service.ServiceKey);
        RequireLabel(
            observed.Labels,
            MemDockerOwnershipLabels.ResourceKey,
            "matrix-stack-service",
            runtimeStackId,
            service.ServiceKey);
        RequireLabel(
            observed.Labels,
            MemDockerOwnershipLabels.ControlPlaneInstanceKey,
            runtimeContext.ControlPlaneInstanceId.ToString("D"),
            runtimeStackId,
            service.ServiceKey);
        RequireLabel(
            observed.Labels,
            MemDockerOwnershipLabels.RuntimeModeKey,
            runtimeContext.RuntimeMode,
            runtimeStackId,
            service.ServiceKey);
        RequireLabel(
            observed.Labels,
            MemDockerOwnershipLabels.ServiceKey,
            ExpectedOwnershipService(service.ServiceKey),
            runtimeStackId,
            service.ServiceKey);
        RequireLabel(
            observed.Labels,
            "mem.managed-by",
            "host-agent",
            runtimeStackId,
            service.ServiceKey);
        RequireLabel(
            observed.Labels,
            "mem.component",
            ExpectedComponent(service.ServiceKey),
            runtimeStackId,
            service.ServiceKey);

        if (string.IsNullOrWhiteSpace(service.DataPath))
        {
            throw Refused(
                runtimeStackId,
                service.ServiceKey,
                "the durable service record does not contain a data path for mount verification");
        }

        var expectedDataPath = Path.GetFullPath(service.DataPath);
        if (!ContainsRuntimeStackPath(expectedDataPath, runtimeStackId))
        {
            throw Refused(
                runtimeStackId,
                service.ServiceKey,
                "the recorded data path is not inside the expected runtime-stack instance directory");
        }

        var hasExpectedMount = observed.MountSources
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Select(Path.GetFullPath)
            .Any(source => IsSameOrChild(source, expectedDataPath));

        if (!hasExpectedMount)
        {
            throw Refused(
                runtimeStackId,
                service.ServiceKey,
                "the observed container does not mount the recorded stack data path");
        }
    }

    internal static bool ContainsRuntimeStackPath(
        string path,
        Guid runtimeStackId)
    {
        var fullPath = Path.GetFullPath(path);
        var expectedSegment =
            $"{Path.DirectorySeparatorChar}instances{Path.DirectorySeparatorChar}{runtimeStackId:N}{Path.DirectorySeparatorChar}";
        var normalized = fullPath.EndsWith(Path.DirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;

        return normalized.Contains(
            expectedSegment,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrChild(
        string candidate,
        string parent)
    {
        var normalizedCandidate = Path.GetFullPath(candidate)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedParent = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(
                   normalizedCandidate,
                   normalizedParent,
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedCandidate.StartsWith(
                   normalizedParent + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireLabel(
        IReadOnlyDictionary<string, string> labels,
        string key,
        string expectedValue,
        Guid runtimeStackId,
        string serviceKey)
    {
        if (!labels.TryGetValue(key, out var actualValue) ||
            !string.Equals(
                actualValue?.Trim(),
                expectedValue,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Refused(
                runtimeStackId,
                serviceKey,
                $"required Docker ownership label '{key}' did not match '{expectedValue}'");
        }
    }

    private static string ExpectedOwnershipService(string serviceKey) =>
        serviceKey switch
        {
            ServiceKeys.Matrix => "synapse",
            ServiceKeys.ElementWeb => "element",
            _ => throw new RuntimeStackDestroyOwnershipRefusedException(
                $"Unsupported runtime-stack service key for destroy ownership validation: {serviceKey}")
        };

    private static string ExpectedComponent(string serviceKey) =>
        serviceKey switch
        {
            ServiceKeys.Matrix => "matrix",
            ServiceKeys.ElementWeb => "element-web",
            _ => throw new RuntimeStackDestroyOwnershipRefusedException(
                $"Unsupported runtime-stack service key for destroy ownership validation: {serviceKey}")
        };

    private static string NormalizeName(string? name) =>
        (name ?? string.Empty).Trim().TrimStart('/');

    private static RuntimeStackDestroyOwnershipRefusedException Refused(
        Guid runtimeStackId,
        string serviceKey,
        string reason) =>
        new(
            $"Refusing database-reconstructed destroy for runtime stack '{runtimeStackId:D}' service '{serviceKey}' because {reason}.");
}
