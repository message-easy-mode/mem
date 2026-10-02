using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Security;

public static class AuthorizationExtensions
{
    public static IServiceCollection AddAppAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var staffRoles = configuration.GetSection("Authorization:StaffRoles")
            .Get<string[]>() ?? Array.Empty<string>();

        var adminRoles = configuration.GetSection("Authorization:AdminRoles")
            .Get<string[]>() ?? Array.Empty<string>();

        // ✅ NEW: customer roles (b2c_customer by default)
        var customerRoles = configuration.GetSection("Authorization:CustomerRoles")
            .Get<string[]>() ?? new[] { "b2c_customer" };

        services.AddAuthorization(options =>
        {
            // ✅ Default: everything requires auth unless AllowAnonymous
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();

            options.AddPolicy("SystemAdminOnly", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx => HasRealmRole(ctx.User, "SystemAdmin"));
            });

            options.AddPolicy("Admin", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    HasRealmRole(ctx.User, "SystemAdmin") ||
                    HasAnyClientRole(ctx.User, "deltacore", adminRoles));
            });

            options.AddPolicy("Staff", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    HasRealmRole(ctx.User, "SystemAdmin") ||
                    HasAnyClientRole(ctx.User, "deltacore", staffRoles));
            });

            // ✅ RENAMED: Shopper -> Customer
            options.AddPolicy("Customer", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    HasRealmRole(ctx.User, "SystemAdmin") || // optional override
                    HasAnyClientRole(ctx.User, "deltacore", customerRoles));
            });

            options.AddPolicy("B2BAdmin", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(ctx =>
                    HasRealmRole(ctx.User, "SystemAdmin") ||
                    HasClientRole(ctx.User, "deltacore", "b2b_admin"));
            });
        });

        return services;
    }

    // ----------------- helpers -----------------

    private static bool HasRealmRole(ClaimsPrincipal user, string role)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;

        var claim = user.FindFirst("realm_access")?.Value;
        if (string.IsNullOrWhiteSpace(claim)) return false;

        try
        {
            using var doc = JsonDocument.Parse(claim);
            if (!doc.RootElement.TryGetProperty("roles", out var rolesEl) ||
                rolesEl.ValueKind != JsonValueKind.Array)
                return false;

            return rolesEl.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString())
                .Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool HasClientRole(ClaimsPrincipal user, string clientId, string role)
    {
        if (user?.Identity?.IsAuthenticated != true) return false;

        var claim = user.FindFirst("resource_access")?.Value;
        if (string.IsNullOrWhiteSpace(claim)) return false;

        try
        {
            using var doc = JsonDocument.Parse(claim);

            if (!doc.RootElement.TryGetProperty(clientId, out var clientEl) ||
                clientEl.ValueKind != JsonValueKind.Object)
                return false;

            if (!clientEl.TryGetProperty("roles", out var rolesEl) ||
                rolesEl.ValueKind != JsonValueKind.Array)
                return false;

            return rolesEl.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString())
                .Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static bool HasAnyClientRole(ClaimsPrincipal user, string clientId, IEnumerable<string> roles) =>
        roles.Any(r => HasClientRole(user, clientId, r));
}
