using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Modules.Auth.Identity;

namespace Modules.Auth.Services.Identity;

/// <summary>
/// Seeds only the stable role names. It deliberately never seeds an account,
/// credential, MFA factor, recovery code, or generic administrator session.
/// </summary>
public sealed class MemOperatorRoleInitializer(
    IServiceScopeFactory scopeFactory,
    ILogger<MemOperatorRoleInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();

        var roleManager = scope.ServiceProvider
            .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var roleName in MemOperatorRoles.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName)
            {
                Id = Guid.NewGuid()
            });

            if (!result.Succeeded)
            {
                var errors = string.Join(
                    "; ",
                    result.Errors.Select(error => $"{error.Code}: {error.Description}"));

                throw new InvalidOperationException(
                    $"Failed to seed MEM operator role '{roleName}'. {errors}");
            }

            logger.LogInformation("Seeded MEM operator role {RoleName}.", roleName);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
