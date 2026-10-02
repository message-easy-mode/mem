namespace Shared.ControlPlane.Runtime;

public static class MemDockerOwnershipLabels
{
    public const string ManagedKey = "io.message-easy-mode.managed";
    public const string ServiceKey = "io.message-easy-mode.service";
    public const string ControlPlaneInstanceKey =
        "io.message-easy-mode.control-plane-instance";
    public const string RuntimeModeKey = "io.message-easy-mode.runtime-mode";
    public const string ResourceKey = "io.message-easy-mode.resource";

    public static IDictionary<string, string> AddTo(
        IDictionary<string, string> labels,
        MemControlPlaneRuntimeContext? runtimeContext,
        string resource,
        string service)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(service);

        labels[ManagedKey] = "true";
        labels[ServiceKey] = service.Trim();
        labels[ResourceKey] = resource.Trim();

        if (runtimeContext is not null)
        {
            labels[ControlPlaneInstanceKey] =
                runtimeContext.ControlPlaneInstanceId.ToString("D");
            labels[RuntimeModeKey] = runtimeContext.RuntimeMode;
        }

        return labels;
    }
}
