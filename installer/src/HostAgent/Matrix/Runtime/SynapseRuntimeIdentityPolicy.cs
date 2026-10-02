using System.Runtime.InteropServices;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Matrix.Runtime;

/// <summary>
/// Resolves the optional Docker <c>User</c> override for Synapse containers.
///
/// Host-process development may deliberately run Synapse as the effective
/// developer uid/gid so bind-mounted files remain writable to that developer.
/// A containerized Control Plane must not mistake its own process uid/gid for
/// an Ubuntu host-login identity. In containerized modes MEM therefore leaves
/// Docker's User override unset and lets the approved Synapse image apply its
/// normal runtime identity/bootstrap contract.
/// </summary>
internal static class SynapseRuntimeIdentityPolicy
{
    internal const uint DefaultSynapseUid = 991;
    internal const uint DefaultSynapseGid = 991;

    public static SynapseRuntimeIdentityDecision Resolve(
        MemControlPlaneRuntimeContext? runtimeContext)
    {
        if (!OperatingSystem.IsLinux())
        {
            return ResolveForRuntime(
                runtimeContext?.RuntimeMode,
                runtimeContext?.RunningInContainer ?? false,
                isLinux: false,
                effectiveUid: 0,
                effectiveGid: 0);
        }

        return ResolveForRuntime(
            runtimeContext?.RuntimeMode,
            runtimeContext?.RunningInContainer ?? false,
            isLinux: true,
            NativeMethods.GetEffectiveUserId(),
            NativeMethods.GetEffectiveGroupId());
    }

    internal static SynapseRuntimeIdentityDecision ResolveForRuntime(
        string? runtimeMode,
        bool runningInContainer,
        bool isLinux,
        uint effectiveUid,
        uint effectiveGid)
    {
        var normalizedRuntimeMode = string.IsNullOrWhiteSpace(runtimeMode)
            ? "unspecified"
            : runtimeMode.Trim();

        if (!isLinux)
        {
            return new SynapseRuntimeIdentityDecision(
                ContainerUser: null,
                RuntimeMode: normalizedRuntimeMode,
                IdentitySource: "synapse-image-default",
                Detail:
                    "No Linux uid/gid override is applicable on this platform; the approved Synapse image runtime identity is used.");
        }

        if (runningInContainer ||
            MemRuntimeModes.IsContainerized(normalizedRuntimeMode))
        {
            return new SynapseRuntimeIdentityDecision(
                ContainerUser: null,
                RuntimeMode: normalizedRuntimeMode,
                IdentitySource: "synapse-image-default",
                Detail:
                    "Containerized MEM leaves the Synapse container user unset so the approved image owns its runtime identity and normal /data ownership bootstrap.");
        }

        if (effectiveUid == 0)
        {
            return new SynapseRuntimeIdentityDecision(
                ContainerUser: null,
                RuntimeMode: normalizedRuntimeMode,
                IdentitySource: "synapse-image-default",
                Detail:
                    "Host-process MEM is running as root; MEM does not propagate root as an explicit Synapse container user and relies on the approved image runtime identity.");
        }

        return new SynapseRuntimeIdentityDecision(
            ContainerUser: $"{effectiveUid}:{effectiveGid}",
            RuntimeMode: normalizedRuntimeMode,
            IdentitySource: "control-plane-process",
            Detail:
                $"Host-process MEM uses its effective process identity {effectiveUid}:{effectiveGid} for Synapse bind-mounted development data.");
    }

    private static class NativeMethods
    {
        [DllImport("libc", EntryPoint = "geteuid")]
        internal static extern uint GetEffectiveUserId();

        [DllImport("libc", EntryPoint = "getegid")]
        internal static extern uint GetEffectiveGroupId();
    }
}

internal sealed record SynapseRuntimeIdentityDecision(
    string? ContainerUser,
    string RuntimeMode,
    string IdentitySource,
    string Detail);
