using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Shared.ControlPlane.Runtime;

namespace Modules.Shared.Docker;

public interface IControlPlaneExposureInspector
{
    Task<MemControlPlaneExposureProjection> InspectAsync(
        CancellationToken cancellationToken);
}

public interface IControlPlaneContainerInspector
{
    Task<DockerContainerInspection?> InspectAsync(
        string containerName,
        CancellationToken cancellationToken);
}

public sealed class DockerControlPlaneContainerInspector(
    DockerHost dockerHost) : IControlPlaneContainerInspector
{
    public Task<DockerContainerInspection?> InspectAsync(
        string containerName,
        CancellationToken cancellationToken) =>
        dockerHost.InspectByNameAsync(containerName, cancellationToken);
}

/// <summary>
/// Inspects the actual Docker host publication of the running MEM Control Plane.
/// Bootstrap intent and labels are deliberately not treated as authority here:
/// the Docker binding currently exposing container port 8443 is what determines
/// whether the administration plane is private.
/// </summary>
public sealed class ControlPlaneExposureService(
    IControlPlaneContainerInspector inspector,
    MemControlPlaneRuntimeContext runtimeContext)
    : IControlPlaneExposureInspector
{
    private const uint ControlPlaneHttpsPort = 8443;

    public async Task<MemControlPlaneExposureProjection> InspectAsync(
        CancellationToken cancellationToken)
    {
        if (!MemRuntimeModes.IsContainerized(runtimeContext.RuntimeMode))
        {
            return MemControlPlaneExposureProjection.NotApplicable;
        }

        var containerName = runtimeContext.ConfiguredContainerName?.Trim();
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return Unavailable("control_plane_exposure_container_identity_missing");
        }

        DockerContainerInspection? container;
        try
        {
            container = await inspector.InspectAsync(
                containerName,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Unavailable("control_plane_exposure_inspection_failed");
        }

        if (container is null)
        {
            return Unavailable("control_plane_exposure_container_not_found");
        }

        var bindings = container.Ports
            .Where(port =>
                port.PrivatePort == ControlPlaneHttpsPort &&
                string.Equals(port.Type, "tcp", StringComparison.OrdinalIgnoreCase) &&
                port.PublicPort > 0)
            .ToArray();

        if (bindings.Length == 0)
        {
            return Unavailable("control_plane_exposure_https_binding_missing");
        }

        if (bindings.Length != 1)
        {
            return new MemControlPlaneExposureProjection(
                MemControlPlaneExposureStates.NeedsAttention,
                MemControlPlaneAccessModes.Unsupported,
                HostAddress: null,
                HostPort: null,
                BindingCount: bindings.Length,
                WarningCode: "control_plane_exposure_multiple_bindings");
        }

        var binding = bindings[0];
        var hostAddress = NormalizeHostAddress(binding.Ip);
        var hostPort = checked((int)binding.PublicPort);

        if (string.Equals(hostAddress, "127.0.0.1", StringComparison.Ordinal))
        {
            return new MemControlPlaneExposureProjection(
                MemControlPlaneExposureStates.Private,
                MemControlPlaneAccessModes.SshTunnel,
                hostAddress,
                hostPort,
                BindingCount: 1,
                WarningCode: null);
        }

        if (IsRfc1918Ipv4(hostAddress))
        {
            return new MemControlPlaneExposureProjection(
                MemControlPlaneExposureStates.Private,
                MemControlPlaneAccessModes.TrustedLan,
                hostAddress,
                hostPort,
                BindingCount: 1,
                WarningCode: null);
        }

        var warningCode = hostAddress switch
        {
            "0.0.0.0" or "::" or "" => "control_plane_exposure_wildcard_binding",
            "::1" => "control_plane_exposure_noncanonical_loopback",
            _ when IsIpv4(hostAddress) => "control_plane_exposure_public_or_nonprivate_binding",
            _ => "control_plane_exposure_unsupported_binding"
        };

        return new MemControlPlaneExposureProjection(
            MemControlPlaneExposureStates.NeedsAttention,
            MemControlPlaneAccessModes.Unsupported,
            hostAddress,
            hostPort,
            BindingCount: 1,
            WarningCode: warningCode);
    }

    private static MemControlPlaneExposureProjection Unavailable(string warningCode) =>
        new(
            MemControlPlaneExposureStates.Unavailable,
            MemControlPlaneAccessModes.Unknown,
            HostAddress: null,
            HostPort: null,
            BindingCount: 0,
            WarningCode: warningCode);

    private static string NormalizeHostAddress(string? address) =>
        string.IsNullOrWhiteSpace(address) ? "0.0.0.0" : address.Trim();

    private static bool IsRfc1918Ipv4(string address)
    {
        if (!TryParseIpv4(address, out var octets))
        {
            return false;
        }

        return octets[0] == 10 ||
               (octets[0] == 172 && octets[1] is >= 16 and <= 31) ||
               (octets[0] == 192 && octets[1] == 168);
    }

    private static bool IsIpv4(string address) => TryParseIpv4(address, out _);

    private static bool TryParseIpv4(string address, out int[] octets)
    {
        octets = [];
        var parts = address.Split('.', StringSplitOptions.None);
        if (parts.Length != 4)
        {
            return false;
        }

        var values = new int[4];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out var value) || value is < 0 or > 255)
            {
                return false;
            }

            values[index] = value;
        }

        octets = values;
        return true;
    }
}
