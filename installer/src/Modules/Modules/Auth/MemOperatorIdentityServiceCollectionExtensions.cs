using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modules.Auth.Configuration;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Modules.Auth;

/// <summary>
/// Registers ASP.NET Core Identity for future named control-plane operators.
/// The current installer-unlock cookie remains the default authentication
/// scheme only during the SEC-AUTH-01A to SEC-AUTH-03 transition.
/// </summary>
public static class MemOperatorIdentityServiceCollectionExtensions
{
    public static IServiceCollection AddMemOperatorIdentity(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var identityOptions = new MemOperatorIdentityOptions();
        configuration
            .GetSection(MemOperatorIdentityOptions.SectionName)
            .Bind(identityOptions);

        Validate(identityOptions);

        services.Configure<MemOperatorIdentityOptions>(
            configuration.GetSection(MemOperatorIdentityOptions.SectionName));

        var cliDeviceSessionOptions = new MemCliDeviceSessionOptions();
        configuration
            .GetSection(MemCliDeviceSessionOptions.SectionName)
            .Bind(cliDeviceSessionOptions);

        ValidateCliDeviceSessionOptions(cliDeviceSessionOptions);

        services.Configure<MemCliDeviceSessionOptions>(
            configuration.GetSection(MemCliDeviceSessionOptions.SectionName));

        services
            .AddIdentityCore<MemOperator>(options =>
            {
                options.Password.RequiredLength = identityOptions.PasswordMinimumLength;
                options.Password.RequiredUniqueChars = identityOptions.PasswordRequiredUniqueChars;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = identityOptions.LockoutMaxFailedAccessAttempts;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(identityOptions.LockoutMinutes);

                // MEM does not have email delivery or public self-registration in v0.1.1.
                options.SignIn.RequireConfirmedAccount = false;
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedPhoneNumber = false;
                options.User.RequireUniqueEmail = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddClaimsPrincipalFactory<UserClaimsPrincipalFactory<MemOperator, IdentityRole<Guid>>>()
            .AddEntityFrameworkStores<MemDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Do not set a default scheme here. InstallerAuth deliberately remains
        // the default only until SEC-AUTH-03 replaces the generic unlock session.
        services
            .AddAuthentication()
            .AddCookie(IdentityConstants.ApplicationScheme, options =>
            {
                options.Cookie.Name = identityOptions.CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.Cookie.Path = "/";

                options.ExpireTimeSpan = TimeSpan.FromMinutes(identityOptions.CookieIdleMinutes);
                options.SlidingExpiration = true;

                options.LoginPath = "/login";
                options.AccessDeniedPath = "/access-denied";
                options.EventsType = typeof(MemOperatorCookieEvents);
            })
            .AddCookie(MemBootstrapAuthentication.Scheme, options =>
            {
                options.Cookie.Name = identityOptions.BootstrapCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(identityOptions.BootstrapGrantMinutes);
                options.SlidingExpiration = false;
                options.EventsType = typeof(MemBootstrapCookieEvents);
            })
            .AddCookie(MemOperatorEnrollmentAuthentication.Scheme, options =>
            {
                options.Cookie.Name = identityOptions.EnrollmentCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                // The temporary enrolment cookie is not a normal control-plane
                // session. Send it only to the route tree that validates its
                // opaque grant id, reducing needless exposure to other API and
                // SPA requests.
                options.Cookie.Path = "/api/auth/enrollment";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(identityOptions.EnrollmentGrantMinutes);
                options.SlidingExpiration = false;
                options.EventsType = typeof(MemOperatorEnrollmentCookieEvents);
            })
            .AddCookie(IdentityConstants.TwoFactorUserIdScheme, options =>
            {
                options.Cookie.Name = "mem_operator_mfa_pending";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                options.SlidingExpiration = false;
            })
            .AddCookie(IdentityConstants.TwoFactorRememberMeScheme, options =>
            {
                // MEM does not expose a remember-this-browser MFA feature.
                // ASP.NET Identity still signs this companion scheme out when
                // security-stamp validation rejects a stale application cookie,
                // so the handler must exist even though MEM never issues it.
                options.Cookie.Name = "mem_operator_mfa_remember";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                options.SlidingExpiration = false;
            })
            .AddScheme<AuthenticationSchemeOptions, MemCliDeviceAuthenticationHandler>(
                MemCliDeviceAuthentication.Scheme,
                _ => { });

        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            options.ValidationInterval = TimeSpan.FromSeconds(
                identityOptions.SecurityStampValidationSeconds);
            options.OnRefreshingPrincipal = context =>
            {
                MemOperatorSessionClaims.PreserveOnSecurityStampRefresh(context);
                return Task.CompletedTask;
            };
        });

        services.AddScoped<MemOperatorCookieEvents>();
        services.AddScoped<MemBootstrapCookieEvents>();
        services.AddScoped<MemOperatorEnrollmentCookieEvents>();
        services.AddScoped<IMemOperatorSessionValidator, MemOperatorSessionValidator>();
        services.AddSingleton<IMemOperatorLoginRateLimiter, MemOperatorLoginRateLimiter>();
        services.AddSingleton<IMemCliDeviceAuthorizationRateLimiter, MemCliDeviceAuthorizationRateLimiter>();
        services.AddScoped<IMemOperatorAuditService, MemOperatorAuditService>();
        services.AddScoped<IMemSecuritySettingsService, MemSecuritySettingsService>();
        services.AddScoped<IMemOperatorStepUpService, MemOperatorStepUpService>();
        services.AddScoped<IMemCliDeviceInstallationBindingService, MemCliDeviceInstallationBindingService>();
        services.AddScoped<IMemCliDeviceAuthorizationService, MemCliDeviceAuthorizationService>();
        services.AddScoped<IAuthorizationHandler, MemRecentStepUpAuthorizationHandler>();
        services.AddScoped<IMemOperatorDirectoryService, MemOperatorDirectoryService>();
        services.AddScoped<IMemOperatorLifecycleService, MemOperatorLifecycleService>();
        services.AddScoped<IMemOperatorHostRecoveryService, MemOperatorHostRecoveryService>();
        services.AddScoped<IMemOperatorEnrollmentService, MemOperatorEnrollmentService>();
        services.AddScoped<IMemBootstrapGrantService, MemBootstrapGrantService>();
        services.AddSingleton(TimeProvider.System);
        services.AddHostedService<MemOperatorRoleInitializer>();

        services.AddAuthorization(options =>
        {
            options.AddPolicy(MemOperatorPolicies.ReadSafeStatus, policy =>
            {
                policy.AuthenticationSchemes.Add(IdentityConstants.ApplicationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireRole(MemOperatorRoles.PlatformOwner, MemOperatorRoles.Operator, MemOperatorRoles.Auditor);
            });

            options.AddPolicy(MemOperatorPolicies.Operate, policy =>
            {
                policy.AuthenticationSchemes.Add(IdentityConstants.ApplicationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireRole(MemOperatorRoles.PlatformOwner, MemOperatorRoles.Operator);
            });

            options.AddPolicy(MemOperatorPolicies.ManagePlatform, policy =>
            {
                policy.AuthenticationSchemes.Add(IdentityConstants.ApplicationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireRole(MemOperatorRoles.PlatformOwner);
            });

            // Device credentials are accepted only by explicit CLI-facing
            // endpoints and the existing fallback-gated control-plane routes.
            // Browser approval, password/TOTP step-up, bootstrap, and recovery
            // remain bound to the protected application cookie schemes.
            options.AddPolicy(MemOperatorPolicies.CliDeviceSession, policy =>
            {
                policy.AuthenticationSchemes.Add(MemCliDeviceAuthentication.Scheme);
                policy.RequireAuthenticatedUser();
                policy.RequireRole(
                    MemOperatorRoles.PlatformOwner,
                    MemOperatorRoles.Operator,
                    MemOperatorRoles.Auditor);
            });

            options.AddPolicy(MemOperatorPolicies.MigrationIntakeOperate, policy =>
            {
                policy.AuthenticationSchemes.Add(IdentityConstants.ApplicationScheme);
                policy.AuthenticationSchemes.Add(MemCliDeviceAuthentication.Scheme);
                policy.RequireAuthenticatedUser();
                policy.RequireAssertion(context =>
                {
                    var browserOperator = context.User.Identities.Any(identity =>
                            identity.IsAuthenticated &&
                            string.Equals(
                                identity.AuthenticationType,
                                IdentityConstants.ApplicationScheme,
                                StringComparison.Ordinal)) &&
                        (context.User.IsInRole(MemOperatorRoles.PlatformOwner) ||
                         context.User.IsInRole(MemOperatorRoles.Operator));

                    if (browserOperator)
                    {
                        return true;
                    }

                    return context.User.Identities.Any(identity =>
                            identity.IsAuthenticated &&
                            string.Equals(
                                identity.AuthenticationType,
                                MemCliDeviceAuthentication.Scheme,
                                StringComparison.Ordinal)) &&
                        context.User.IsInRole(MemOperatorRoles.PlatformOwner);
                });
            });

            options.AddPolicy(MemOperatorPolicies.RecentStepUp, policy =>
            {
                policy.AuthenticationSchemes.Add(IdentityConstants.ApplicationScheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new MemRecentStepUpRequirement());
            });
        });

        return services;
    }

    private static void ValidateCliDeviceSessionOptions(MemCliDeviceSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.AuthorizationAttemptMinutes is < 5 or > 15)
        {
            throw new InvalidOperationException(
                "MemCliDeviceSessions:AuthorizationAttemptMinutes must be between 5 and 15.");
        }

        if (options.SessionIdleMinutes is < 30 or > 720)
        {
            throw new InvalidOperationException(
                "MemCliDeviceSessions:SessionIdleMinutes must be between 30 and 720.");
        }

        if (options.SessionAbsoluteHours is < 24 or > 30 * 24)
        {
            throw new InvalidOperationException(
                "MemCliDeviceSessions:SessionAbsoluteHours must be between 24 and 720.");
        }

        if (options.SessionIdleMinutes >= options.SessionAbsoluteHours * 60)
        {
            throw new InvalidOperationException(
                "MemCliDeviceSessions:SessionIdleMinutes must be shorter than SessionAbsoluteHours.");
        }

        if (options.AuthorizationStartRateLimitPermitLimit is < 3 or > 60)
        {
            throw new InvalidOperationException(
                "MemCliDeviceSessions:AuthorizationStartRateLimitPermitLimit must be between 3 and 60.");
        }

        if (options.AuthorizationStartRateLimitWindowSeconds is < 10 or > 300)
        {
            throw new InvalidOperationException(
                "MemCliDeviceSessions:AuthorizationStartRateLimitWindowSeconds must be between 10 and 300.");
        }

        if (options.AuthorizationPollRateLimitPermitLimit is < 20 or > 300)
        {
            throw new InvalidOperationException(
                "MemCliDeviceSessions:AuthorizationPollRateLimitPermitLimit must be between 20 and 300.");
        }

        if (options.AuthorizationPollRateLimitWindowSeconds is < 10 or > 300)
        {
            throw new InvalidOperationException(
                "MemCliDeviceSessions:AuthorizationPollRateLimitWindowSeconds must be between 10 and 300.");
        }
    }

    private static void Validate(MemOperatorIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.CookieName))
        {
            throw new InvalidOperationException("MemOperatorIdentity:CookieName is required.");
        }

        if (options.CookieIdleMinutes <= 0)
        {
            throw new InvalidOperationException("MemOperatorIdentity:CookieIdleMinutes must be positive.");
        }

        if (string.IsNullOrWhiteSpace(options.BootstrapCookieName))
        {
            throw new InvalidOperationException("MemOperatorIdentity:BootstrapCookieName is required.");
        }

        if (options.BootstrapGrantMinutes is < 5 or > 30)
        {
            throw new InvalidOperationException("MemOperatorIdentity:BootstrapGrantMinutes must be between 5 and 30.");
        }

        if (options.BootstrapRecoveryCodeCount is < 8 or > 20)
        {
            throw new InvalidOperationException("MemOperatorIdentity:BootstrapRecoveryCodeCount must be between 8 and 20.");
        }

        if (string.IsNullOrWhiteSpace(options.EnrollmentCookieName))
        {
            throw new InvalidOperationException("MemOperatorIdentity:EnrollmentCookieName is required.");
        }

        if (options.EnrollmentGrantMinutes is < 5 or > 60)
        {
            throw new InvalidOperationException("MemOperatorIdentity:EnrollmentGrantMinutes must be between 5 and 60.");
        }

        if (options.EnrollmentRecoveryCodeCount is < 8 or > 20)
        {
            throw new InvalidOperationException("MemOperatorIdentity:EnrollmentRecoveryCodeCount must be between 8 and 20.");
        }

        if (options.OperatorRecoveryCodeCount is < 8 or > 20)
        {
            throw new InvalidOperationException("MemOperatorIdentity:OperatorRecoveryCodeCount must be between 8 and 20.");
        }

        if (options.PasswordMinimumLength < 14)
        {
            throw new InvalidOperationException("MemOperatorIdentity:PasswordMinimumLength cannot be lower than 14.");
        }

        if (options.PasswordRequiredUniqueChars < 1)
        {
            throw new InvalidOperationException("MemOperatorIdentity:PasswordRequiredUniqueChars must be positive.");
        }

        if (options.LockoutMinutes <= 0 || options.LockoutMaxFailedAccessAttempts <= 0)
        {
            throw new InvalidOperationException("MemOperatorIdentity lockout values must be positive.");
        }

        if (options.LoginRateLimitPermitLimit is < 5 or > 120)
        {
            throw new InvalidOperationException(
                "MemOperatorIdentity:LoginRateLimitPermitLimit must be between 5 and 120.");
        }

        if (options.LoginRateLimitWindowSeconds is < 10 or > 300)
        {
            throw new InvalidOperationException(
                "MemOperatorIdentity:LoginRateLimitWindowSeconds must be between 10 and 300.");
        }

        if (options.StepUpGrantMinutes is < 5 or > 60)
        {
            throw new InvalidOperationException(
                "MemOperatorIdentity:StepUpGrantMinutes must be between 5 and 60.");
        }

        if (options.SecurityStampValidationSeconds < 0)
        {
            throw new InvalidOperationException("MemOperatorIdentity:SecurityStampValidationSeconds cannot be negative.");
        }
    }
}
