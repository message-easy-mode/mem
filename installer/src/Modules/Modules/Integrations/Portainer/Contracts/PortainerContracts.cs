namespace Modules.Integrations.Portainer.Contracts;

public interface IPortainerRuntimeStatusReader
{
    Task<PortainerRuntimeStatus> GetStatusAsync(
        CancellationToken cancellationToken);
}

public sealed record PortainerInstallRequest(
    int? PreferredUiHostPort = null,
    bool ForcePreferredPort = false,
    bool UseExistingIfDetected = true);

public sealed record PortainerRuntimeStatus(
    string ServiceName,
    string ContainerName,
    string ApprovedVersion,
    string ApprovedImageReference,
    bool Exists,
    bool Running,
    bool Ready,
    bool Managed,
    string OwnershipState,
    bool UsesApprovedRuntime,
    bool UpgradeAvailable,
    string? ObservedImageReference,
    string? ObservedVersion,
    int? UiHostPort,
    bool DataRetained,
    bool PublishesPublicIngress,
    IReadOnlyList<string> Warnings);

public sealed record PortainerInstallResult(
    string Status,
    string Message,
    bool PulledImage,
    bool CreatedContainer,
    bool ReusedExisting,
    PortainerRuntimeStatus Runtime);
