using System.Security.Claims;
using Modules.Auth.Identity;
using Modules.Operator.Diagnostics.Services;

namespace Api.IntegrationTests.Diagnostics;

public sealed class DiagnosticsCapabilityServiceTests
{
    [Theory]
    [InlineData(MemOperatorRoles.Auditor, false, false, false, false, false, false, false)]
    [InlineData(MemOperatorRoles.Operator, true, true, false, true, false, false, true)]
    [InlineData(MemOperatorRoles.PlatformOwner, true, true, true, true, true, true, true)]
    public void Capability_projection_matches_the_diagnostics_authority_matrix(
        string role,
        bool technicalEvents,
        bool supportReports,
        bool ownerHealthFacts,
        bool dockerEvidence,
        bool verifyPipeline,
        bool openPortainer,
        bool manageIncidentLifecycle)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, role)
        ],
        "test"));

        var capabilities = new DiagnosticsCapabilityService()
            .GetCapabilities(principal);

        Assert.Equal(technicalEvents, capabilities.CanReadTechnicalEvents);
        Assert.Equal(supportReports, capabilities.CanGenerateSupportReport);
        Assert.Equal(ownerHealthFacts, capabilities.CanViewOwnerHealthFacts);
        Assert.Equal(dockerEvidence, capabilities.CanReadDockerEvidence);
        Assert.Equal(verifyPipeline, capabilities.CanVerifyPipeline);
        Assert.Equal(openPortainer, capabilities.CanOpenPortainer);
        Assert.Equal(manageIncidentLifecycle, capabilities.CanManageIncidentLifecycle);
    }
}
