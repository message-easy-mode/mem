using System.Security.Claims;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Modules.Auth;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Security;

public sealed class MemOperatorIdentityFoundationTests
{
    [Fact]
    public async Task SEC_AUTH_02A_seeds_only_the_three_named_operator_roles()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        var initializer = fixture.Provider
            .GetServices<IHostedService>()
            .OfType<MemOperatorRoleInitializer>()
            .Single();

        await initializer.StartAsync(CancellationToken.None);
        await initializer.StartAsync(CancellationToken.None);

        await using var scope = fixture.Provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var roles = await db.Roles
            .OrderBy(role => role.Name)
            .Select(role => role.Name)
            .ToArrayAsync();

        Assert.Equal(
            MemOperatorRoles.All.OrderBy(role => role),
            roles);
        Assert.Empty(await db.Users.ToArrayAsync());
    }

    [Fact]
    public async Task SEC_AUTH_02A_configures_a_strict_named_operator_cookie_and_identity_only_policies()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        var cookie = fixture.Provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal("mem_operator_auth", cookie.Cookie.Name);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.Equal(TimeSpan.FromMinutes(30), cookie.ExpireTimeSpan);
        Assert.True(cookie.SlidingExpiration);
        Assert.Equal(typeof(MemOperatorCookieEvents), cookie.EventsType);

        var securityStampOptions = fixture.Provider
            .GetRequiredService<IOptions<SecurityStampValidatorOptions>>()
            .Value;

        Assert.Equal(TimeSpan.Zero, securityStampOptions.ValidationInterval);

        var policyProvider = fixture.Provider
            .GetRequiredService<IAuthorizationPolicyProvider>();

        var managePlatform = await policyProvider.GetPolicyAsync(
            MemOperatorPolicies.ManagePlatform);

        Assert.NotNull(managePlatform);
        Assert.Contains(
            IdentityConstants.ApplicationScheme,
            managePlatform.AuthenticationSchemes);

        var roleRequirement = Assert.Single(
            managePlatform.Requirements.OfType<RolesAuthorizationRequirement>());

        Assert.Equal(
            [MemOperatorRoles.PlatformOwner],
            roleRequirement.AllowedRoles);
    }

    [Fact]
    public async Task OPERATOR_ACCOUNT_SECURITY_PASSWORD_CORR_01A_CORR_06_registers_the_identity_remember_me_companion_scheme_for_safe_stale_session_sign_out()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        var schemeProvider = fixture.Provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemeProvider.GetSchemeAsync(IdentityConstants.TwoFactorRememberMeScheme);

        Assert.NotNull(scheme);
        Assert.Equal(typeof(CookieAuthenticationHandler), scheme!.HandlerType);

        var cookie = fixture.Provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.TwoFactorRememberMeScheme);

        Assert.Equal("mem_operator_mfa_remember", cookie.Cookie.Name);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.Equal("/", cookie.Cookie.Path);
        Assert.Equal(TimeSpan.FromMinutes(5), cookie.ExpireTimeSpan);
        Assert.False(cookie.SlidingExpiration);
    }

    [Fact]
    public async Task OPERATOR_ACCOUNT_SECURITY_PASSWORD_CORR_01A_CORR_06_rejects_a_stale_application_principal_without_throwing_during_identity_sign_out()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var claimsFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<MemOperator>>();
        var cookieEvents = scope.ServiceProvider.GetRequiredService<MemOperatorCookieEvents>();
        var schemeProvider = scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
        var cookieOptions = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);
        var httpContextAccessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        var user = new MemOperator
        {
            Id = Guid.NewGuid(),
            UserName = "stale-session.operator",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var created = await userManager.CreateAsync(user, "Secure!Foundation123");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

        var stalePrincipal = await claimsFactory.CreateAsync(user);
        var rotated = await userManager.UpdateSecurityStampAsync(user);
        Assert.True(rotated.Succeeded, string.Join("; ", rotated.Errors.Select(error => error.Description)));

        var applicationScheme = await schemeProvider.GetSchemeAsync(IdentityConstants.ApplicationScheme);
        Assert.NotNull(applicationScheme);

        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        httpContext.Response.Body = new MemoryStream();
        httpContextAccessor.HttpContext = httpContext;

        var ticket = new AuthenticationTicket(
            stalePrincipal,
            new AuthenticationProperties(),
            IdentityConstants.ApplicationScheme);
        var validationContext = new CookieValidatePrincipalContext(
            httpContext,
            applicationScheme!,
            cookieOptions,
            ticket);

        try
        {
            await cookieEvents.ValidatePrincipal(validationContext);
        }
        finally
        {
            httpContextAccessor.HttpContext = null;
        }

        Assert.Null(validationContext.Principal);

        var setCookie = httpContext.Response.Headers.SetCookie.ToString();
        Assert.Contains("mem_operator_auth=;", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mem_operator_mfa_remember=;", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SEC_AUTH_04C_scopes_the_transient_enrollment_cookie_to_the_enrollment_api_only()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        var cookie = fixture.Provider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(MemOperatorEnrollmentAuthentication.Scheme);

        Assert.Equal("mem_operator_enrollment", cookie.Cookie.Name);
        Assert.True(cookie.Cookie.HttpOnly);
        Assert.Equal(SameSiteMode.Strict, cookie.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.Equal("/api/auth/enrollment", cookie.Cookie.Path);
        Assert.Equal(TimeSpan.FromMinutes(15), cookie.ExpireTimeSpan);
        Assert.False(cookie.SlidingExpiration);
        Assert.Equal(typeof(MemOperatorEnrollmentCookieEvents), cookie.EventsType);
    }

    [Fact]
    public async Task SEC_AUTH_02A_emits_seeded_operator_roles_into_a_future_identity_principal()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        var initializer = fixture.Provider
            .GetServices<IHostedService>()
            .OfType<MemOperatorRoleInitializer>()
            .Single();

        await initializer.StartAsync(CancellationToken.None);

        await using var scope = fixture.Provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var claimsFactory = scope.ServiceProvider.GetRequiredService<IUserClaimsPrincipalFactory<MemOperator>>();

        var user = new MemOperator
        {
            Id = Guid.NewGuid(),
            UserName = "role.operator",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var created = await userManager.CreateAsync(user, "Secure!Foundation123");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

        var roleAdded = await userManager.AddToRoleAsync(user, MemOperatorRoles.Operator);
        Assert.True(roleAdded.Succeeded, string.Join("; ", roleAdded.Errors.Select(error => error.Description)));

        var principal = await claimsFactory.CreateAsync(user);

        Assert.True(principal.IsInRole(MemOperatorRoles.Operator));
        Assert.False(principal.IsInRole(MemOperatorRoles.PlatformOwner));
    }

    [Fact]
    public async Task SEC_AUTH_02A_applies_the_locked_password_and_lockout_floor()
    {
        await using var fixture = await IdentityFixture.CreateAsync();

        var options = fixture.Provider
            .GetRequiredService<IOptions<IdentityOptions>>()
            .Value;

        Assert.Equal(14, options.Password.RequiredLength);
        Assert.Equal(4, options.Password.RequiredUniqueChars);
        Assert.True(options.Password.RequireDigit);
        Assert.True(options.Password.RequireLowercase);
        Assert.True(options.Password.RequireUppercase);
        Assert.True(options.Password.RequireNonAlphanumeric);
        Assert.True(options.Lockout.AllowedForNewUsers);
        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
    }

    [Fact]
    public async Task SEC_AUTH_02A_rejects_a_disabled_operator_during_session_revalidation()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var sessionValidator = scope.ServiceProvider.GetRequiredService<IMemOperatorSessionValidator>();

        var user = new MemOperator
        {
            Id = Guid.NewGuid(),
            UserName = "foundation.operator",
            IsEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var created = await userManager.CreateAsync(user, "Secure!Foundation123");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

        var principal = CreatePrincipal(user.Id);

        Assert.True(await sessionValidator.IsCurrentSessionAllowedAsync(principal));

        user.IsEnabled = false;
        var updated = await userManager.UpdateAsync(user);
        Assert.True(updated.Succeeded, string.Join("; ", updated.Errors.Select(error => error.Description)));

        Assert.False(await sessionValidator.IsCurrentSessionAllowedAsync(principal));
    }

    [Fact]
    public async Task SEC_AUTH_02A_persists_structured_audit_events_without_a_free_form_payload()
    {
        await using var fixture = await IdentityFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var audit = scope.ServiceProvider.GetRequiredService<IMemOperatorAuditService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var actorId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();

        await audit.WriteAsync(new MemOperatorAuditEventWrite(
            EventType: "identity.role.seeded",
            Outcome: "succeeded",
            ActorOperatorId: actorId,
            SubjectOperatorId: subjectId,
            CorrelationId: "sec-auth-02a-test",
            ReasonCode: "foundation-test"));

        var persisted = await db.MemOperatorAuditEvents.SingleAsync();

        Assert.Equal("identity.role.seeded", persisted.EventType);
        Assert.Equal("succeeded", persisted.Outcome);
        Assert.Equal(actorId, persisted.ActorOperatorId);
        Assert.Equal(subjectId, persisted.SubjectOperatorId);
        Assert.Equal("sec-auth-02a-test", persisted.CorrelationId);
        Assert.Equal("foundation-test", persisted.ReasonCode);
        Assert.Null(typeof(MemOperatorAuditEventEntity).GetProperty("DetailsJson"));
        Assert.Null(typeof(MemOperatorAuditEventEntity).GetProperty("RequestBody"));
    }

    private static ClaimsPrincipal CreatePrincipal(Guid operatorId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, operatorId.ToString("D"))],
            IdentityConstants.ApplicationScheme);

        return new ClaimsPrincipal(identity);
    }

    private sealed class IdentityFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private IdentityFixture(string databasePath, ServiceProvider provider)
        {
            _databasePath = databasePath;
            Provider = provider;
        }

        public ServiceProvider Provider { get; }

        public static async Task<IdentityFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-sec-auth-02a-{Guid.NewGuid():N}.db");

            var settings = new Dictionary<string, string?>
            {
                ["MemOperatorIdentity:CookieName"] = "mem_operator_auth",
                ["MemOperatorIdentity:CookieIdleMinutes"] = "30",
                ["MemOperatorIdentity:PasswordMinimumLength"] = "14",
                ["MemOperatorIdentity:PasswordRequiredUniqueChars"] = "4",
                ["MemOperatorIdentity:LockoutMinutes"] = "15",
                ["MemOperatorIdentity:LockoutMaxFailedAccessAttempts"] = "5",
                ["MemOperatorIdentity:SecurityStampValidationSeconds"] = "0"
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddHttpContextAccessor();
            services.AddDbContext<MemDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));
            services.AddMemOperatorIdentity(
                configuration,
                new TestHostEnvironment(Environments.Production));
            services.AddInstallerAuth(configuration);

            var provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            return new IdentityFixture(databasePath, provider);
        }

        public ValueTask DisposeAsync()
        {
            Provider.Dispose();

            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
