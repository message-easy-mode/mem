using System.Security.Claims;
using Modules.Auth.Identity;
using Modules.Operator.Diagnostics.Contracts;

namespace Modules.Operator.Diagnostics.Services;

public sealed class DiagnosticsCapabilityService
{
    public DiagnosticsCapabilities GetCapabilities(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var owner = principal.IsInRole(MemOperatorRoles.PlatformOwner);
        var operate = owner || principal.IsInRole(MemOperatorRoles.Operator);

        return new DiagnosticsCapabilities(
            CanReadTechnicalEvents: operate,
            CanGenerateSupportReport: operate,
            CanViewOwnerHealthFacts: owner,
            CanReadDockerEvidence: operate,
            CanVerifyPipeline: owner,
            CanOpenPortainer: owner,
            CanManageIncidentLifecycle: operate);
    }
}
