using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Configuration;
using Modules.Auth.Identity;
using Modules.Auth.Services;

namespace Modules.Auth;

public static class InstallerAuthServiceCollectionExtensions
{
    public static IServiceCollection AddInstallerAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<InstallerAuthOptions>(
            configuration.GetSection(InstallerAuthOptions.SectionName));

        services.AddScoped<IInstallerSetupTokenStore, InstallerSetupTokenStore>();
        services.AddScoped<IInstallerTokenValidator, InstallerTokenValidator>();
        services.AddScoped<InstallerAuthCookieEvents>();

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                var authOptions = new InstallerAuthOptions();
                configuration
                    .GetSection(InstallerAuthOptions.SectionName)
                    .Bind(authOptions);

                options.Cookie.Name = authOptions.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;

                // Local dev may run over HTTP.
                // Bootstrap/container mode runs over HTTPS.
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                options.ExpireTimeSpan = TimeSpan.FromHours(authOptions.SessionHours);
                options.SlidingExpiration = true;

                options.LoginPath = "/unlock";
                options.AccessDeniedPath = "/unlock";
                options.EventsType = typeof(InstallerAuthCookieEvents);
            });

        services.AddAuthorization(options =>
        {
            // Normal browser cookies and the opaque CLI device scheme are
            // authenticated independently. A request with a device credential
            // can reach only the existing fallback-gated control-plane surface;
            // browser-specific routes retain their explicit cookie policies.
            options.FallbackPolicy = new AuthorizationPolicyBuilder(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    IdentityConstants.ApplicationScheme,
                    MemCliDeviceAuthentication.Scheme)
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}