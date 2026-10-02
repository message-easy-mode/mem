using Shared.ControlPlane.Runtime;

namespace Modules.Integrations.Seq.Services;

/// <summary>
/// Identifies the currently running MEM API process for delivery activation
/// proof. The value is the canonical runtime-context ApiProcessInstanceId,
/// so Seq evidence and the rest of MEM refer to the same process identity.
/// </summary>
public sealed record SeqDeliveryProcessIdentity(Guid Value)
{
    public static SeqDeliveryProcessIdentity FromRuntimeContext(
        MemControlPlaneRuntimeContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        return new SeqDeliveryProcessIdentity(runtimeContext.ApiProcessInstanceId);
    }
}
