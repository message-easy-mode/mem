using System.Security.Claims;
using HostAgent.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Modules.Auth;
using Modules.Auth.Identity;

namespace HostAgent.Tests.Security;

public sealed class HostAgentEndpointOperatorGuardContractTests
{
    [Fact]
    public void SEC_AUTH_03A_accepts_a_named_platform_owner_session()
    {
        var httpContext = CreatePlatformOwnerContext();

        var result = HostAgentEndpointOperatorGuard
            .ValidateCurrentControlPlaneSession(httpContext);

        Assert.Null(result);
    }

    [Fact]
    public void SEC_AUTH_03A_rejects_a_named_operator_without_platform_owner_role()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, MemOperatorRoles.Operator)
        };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                IdentityConstants.ApplicationScheme))
        };

        var result = HostAgentEndpointOperatorGuard
            .ValidateCurrentControlPlaneSession(httpContext);

        Assert.NotNull(result);
    }

    [Fact]
    public void SEC_AUTH_01A_keeps_the_existing_installer_transition_session_until_bootstrap_completes()
    {
        var claims = new[]
        {
            new Claim(
                InstallerAuthClaims.TransitionalInstallerUnlocked,
                InstallerAuthClaims.True)
        };

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "installer"))
        };

        var result = HostAgentEndpointOperatorGuard
            .ValidateCurrentControlPlaneSession(httpContext);

        Assert.Null(result);
    }

    [Fact]
    public void SEC_AUTH_03A_rejects_a_legacy_header_without_a_server_session()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-MEM-Agent-Secret"] = "must-not-authorize";

        var result = HostAgentEndpointOperatorGuard
            .ValidateCurrentControlPlaneSession(httpContext);

        Assert.NotNull(result);
    }

    private static DefaultHttpContext CreatePlatformOwnerContext()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
        };

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                claims,
                IdentityConstants.ApplicationScheme))
        };
    }
}
