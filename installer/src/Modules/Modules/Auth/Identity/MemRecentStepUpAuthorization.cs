using Microsoft.AspNetCore.Authorization;
using Modules.Auth.Services.Identity;

namespace Modules.Auth.Identity;

/// <summary>
/// Requires an active, server-side grant produced by a fresh password plus
/// current-TOTP confirmation in this exact protected browser session.
/// </summary>
public sealed class MemRecentStepUpRequirement : IAuthorizationRequirement
{
}

public sealed class MemRecentStepUpAuthorizationHandler(
    IMemOperatorStepUpService stepUp) : AuthorizationHandler<MemRecentStepUpRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MemRecentStepUpRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (await stepUp.HasActiveGrantAsync(context.User))
        {
            context.Succeed(requirement);
        }
    }
}
