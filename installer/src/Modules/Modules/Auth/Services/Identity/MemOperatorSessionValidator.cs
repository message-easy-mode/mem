using System.Security.Claims;
using Infrastructure.Data.Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace Modules.Auth.Services.Identity;

public interface IMemOperatorSessionValidator
{
    Task<bool> IsCurrentSessionAllowedAsync(
        ClaimsPrincipal principal,
        CancellationToken ct = default);
}

/// <summary>
/// The application-cookie validation hook. ASP.NET Core Identity's security
/// stamp validator verifies credential/session invalidation; this additional
/// check makes account disablement fail closed as soon as the identity cookie
/// is revalidated.
/// </summary>
public sealed class MemOperatorSessionValidator(
    UserManager<MemOperator> userManager) : IMemOperatorSessionValidator
{
    public async Task<bool> IsCurrentSessionAllowedAsync(
        ClaimsPrincipal principal,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ct.ThrowIfCancellationRequested();

        var userId = userManager.GetUserId(principal);

        if (!Guid.TryParse(userId, out _))
        {
            return false;
        }

        var user = await userManager.FindByIdAsync(userId);

        return user is { IsEnabled: true };
    }
}
