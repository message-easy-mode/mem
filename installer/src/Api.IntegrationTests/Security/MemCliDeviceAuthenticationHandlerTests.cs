using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Auth;
using Modules.Auth.Identity;
using Modules.Auth.Endpoints;
using Modules.Auth.Services.Identity;
using HostAgent.Security;

namespace Api.IntegrationTests.Security;

/// <summary>
/// Direct authentication-pipeline coverage for browser-approved opaque MEM CLI
/// device credentials. The persisted service tests cover credential hashing and
/// expiry; these tests prove the credential maps to a named role-bearing
/// principal without becoming a browser cookie or a step-up substitute.
/// </summary>
public sealed class MemCliDeviceAuthenticationHandlerTests
{
    [Fact]
    public async Task CLI_AUTH_03A_authenticates_a_current_installation_device_credential_without_exposing_it_as_a_claim()
    {
        await using var fixture = await DeviceAuthenticationFixture.CreateAsync();
        var owner = await fixture.CreateReadyPlatformOwnerAsync("owner.cli-auth-handler");
        var credential = await fixture.CreateDeviceCredentialAsync(
            owner.Id,
            fixture.InstallationId);

        await using var scope = fixture.Provider.CreateAsyncScope();

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        context.Request.Headers.Authorization = $"Bearer {credential}";

        var authentication = scope.ServiceProvider
            .GetRequiredService<IAuthenticationService>();

        var result = await authentication.AuthenticateAsync(
            context,
            MemCliDeviceAuthentication.Scheme);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Principal);

        var principal = result.Principal!;
        Assert.Equal(
            MemCliDeviceAuthentication.Scheme,
            principal.Identity?.AuthenticationType);
        Assert.Equal(owner.Id.ToString("D"), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.True(principal.IsInRole(MemOperatorRoles.PlatformOwner));
        Assert.True(HostAgentEndpointOperatorGuard.HasCurrentControlPlaneSession(principal));
        Assert.NotNull(principal.FindFirst(MemCliDeviceAuthentication.SessionIdClaimType));
        Assert.NotNull(principal.FindFirst(MemCliDeviceAuthentication.IdleExpiresAtUtcClaimType));
        Assert.NotNull(principal.FindFirst(MemCliDeviceAuthentication.AbsoluteExpiresAtUtcClaimType));
        Assert.DoesNotContain(
            credential,
            principal.Claims.Select(claim => claim.Value),
            StringComparer.Ordinal);
    }

    [Fact]
    public async Task CLI_AUTH_03A_rejects_a_valid_device_credential_when_the_server_current_installation_changes()
    {
        await using var fixture = await DeviceAuthenticationFixture.CreateAsync();
        var owner = await fixture.CreateReadyPlatformOwnerAsync("owner.cli-installation-change");
        var credential = await fixture.CreateDeviceCredentialAsync(
            owner.Id,
            fixture.InstallationId);

        await fixture.AddCompletedInstallationAsync(Guid.NewGuid(), DateTime.UtcNow.AddMinutes(1));

        await using var scope = fixture.Provider.CreateAsyncScope();

        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider
        };
        context.Request.Headers.Authorization = $"Bearer {credential}";

        var authentication = scope.ServiceProvider
            .GetRequiredService<IAuthenticationService>();

        var result = await authentication.AuthenticateAsync(
            context,
            MemCliDeviceAuthentication.Scheme);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failure);
    }

    [Fact]
    public async Task CLI_AUTH_03A_allows_device_credentials_only_for_explicit_cli_session_policy_and_keeps_recent_step_up_cookie_bound()
    {
        await using var fixture = await DeviceAuthenticationFixture.CreateAsync();

        var policies = fixture.Provider
            .GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await policies.GetFallbackPolicyAsync();
        var cliSession = await policies.GetPolicyAsync(
            MemOperatorPolicies.CliDeviceSession);
        var recentStepUp = await policies.GetPolicyAsync(
            MemOperatorPolicies.RecentStepUp);
        var migrationIntake = await policies.GetPolicyAsync(
            MemOperatorPolicies.MigrationIntakeOperate);

        Assert.NotNull(fallback);
        Assert.Contains(
            MemCliDeviceAuthentication.Scheme,
            fallback!.AuthenticationSchemes);

        Assert.NotNull(cliSession);
        Assert.Equal(
            [MemCliDeviceAuthentication.Scheme],
            cliSession!.AuthenticationSchemes);

        Assert.NotNull(recentStepUp);
        Assert.Contains(
            IdentityConstants.ApplicationScheme,
            recentStepUp!.AuthenticationSchemes);
        Assert.DoesNotContain(
            MemCliDeviceAuthentication.Scheme,
            recentStepUp.AuthenticationSchemes);

        Assert.NotNull(migrationIntake);
        Assert.Contains(
            IdentityConstants.ApplicationScheme,
            migrationIntake!.AuthenticationSchemes);
        Assert.Contains(
            MemCliDeviceAuthentication.Scheme,
            migrationIntake.AuthenticationSchemes);

        var deviceOwner = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                    new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
                ],
                MemCliDeviceAuthentication.Scheme));

        var deviceOperator = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                    new Claim(ClaimTypes.Role, MemOperatorRoles.Operator)
                ],
                MemCliDeviceAuthentication.Scheme));

        Assert.True(HostAgentEndpointOperatorGuard.HasCurrentControlPlaneSession(deviceOwner));
        Assert.False(HostAgentEndpointOperatorGuard.HasCurrentControlPlaneSession(deviceOperator));


        var authorization = fixture.Provider
            .GetRequiredService<IAuthorizationService>();

        Assert.True((await authorization.AuthorizeAsync(
            deviceOwner,
            resource: null,
            policyName: MemOperatorPolicies.MigrationIntakeOperate)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            deviceOperator,
            resource: null,
            policyName: MemOperatorPolicies.MigrationIntakeOperate)).Succeeded);
    }

    [Fact]
    public async Task CLI_AUTH_03A_registers_the_safe_session_projection_as_device_scheme_only()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();

        await using var application = builder.Build();
        new MemCliDeviceAuthorizationEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                string.Equals(
                    candidate.RoutePattern.RawText,
                    "/api/auth/cli-device/session",
                    StringComparison.Ordinal) &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Get)));

        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            policy => string.Equals(
                policy.Policy,
                MemOperatorPolicies.CliDeviceSession,
                StringComparison.Ordinal));
    }

    private static string CreateOpaqueValue() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateChallenge(string verifier) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));

    private sealed class DeviceAuthenticationFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private DeviceAuthenticationFixture(string databasePath, ServiceProvider provider)
        {
            _databasePath = databasePath;
            Provider = provider;
        }

        public ServiceProvider Provider { get; }

        public Guid InstallationId { get; } = Guid.NewGuid();

        public static async Task<DeviceAuthenticationFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-cli-auth-03a-{Guid.NewGuid():N}.db");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MemCliDeviceSessions:AuthorizationAttemptMinutes"] = "10",
                    ["MemCliDeviceSessions:SessionIdleMinutes"] = "480",
                    ["MemCliDeviceSessions:SessionAbsoluteHours"] = "168"
                })
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

            var fixture = new DeviceAuthenticationFixture(databasePath, provider);
            await fixture.InitializeAsync();

            return fixture;
        }

        public async Task<MemOperator> CreateReadyPlatformOwnerAsync(string username)
        {
            await using var scope = Provider.CreateAsyncScope();

            var roleManager = scope.ServiceProvider
                .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            if (!await roleManager.RoleExistsAsync(MemOperatorRoles.PlatformOwner))
            {
                var roleCreated = await roleManager.CreateAsync(
                    new IdentityRole<Guid>(MemOperatorRoles.PlatformOwner));

                Assert.True(
                    roleCreated.Succeeded,
                    string.Join("; ", roleCreated.Errors.Select(error => error.Description)));
            }

            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<MemOperator>>();

            var user = new MemOperator
            {
                Id = Guid.NewGuid(),
                UserName = username,
                IsEnabled = true,
                IsBootstrapProvisioning = false,
                TwoFactorEnabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            var created = await userManager.CreateAsync(user, "Secure!Foundation123");
            Assert.True(
                created.Succeeded,
                string.Join("; ", created.Errors.Select(error => error.Description)));

            var roleAdded = await userManager.AddToRoleAsync(
                user,
                MemOperatorRoles.PlatformOwner);
            Assert.True(
                roleAdded.Succeeded,
                string.Join("; ", roleAdded.Errors.Select(error => error.Description)));

            return user;
        }

        public async Task<string> CreateDeviceCredentialAsync(
            Guid operatorId,
            Guid installationId)
        {
            var verifier = CreateOpaqueValue();

            await using var scope = Provider.CreateAsyncScope();

            var authorizations = scope.ServiceProvider
                .GetRequiredService<IMemCliDeviceAuthorizationService>();

            var started = await authorizations.StartAsync(
                installationId,
                CreateChallenge(verifier),
                "CLI-AUTH-03A test device");

            Assert.Equal(
                "authorization_approved",
                (await authorizations.ApproveAsync(
                    started.UserCode,
                    operatorId)).Status);

            var completed = await authorizations.PollAsync(
                started.AuthorizationId,
                verifier);

            Assert.Equal("authorized", completed.Status);
            Assert.False(string.IsNullOrWhiteSpace(completed.DeviceCredential));

            return completed.DeviceCredential!;
        }

        public async Task AddCompletedInstallationAsync(
            Guid installationId,
            DateTime completedAtUtc)
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

            db.Installations.Add(new InstallationEntity
            {
                Id = installationId,
                Status = "Succeeded",
                CreatedAtUtc = completedAtUtc,
                UpdatedAtUtc = completedAtUtc,
                CompletedAtUtc = completedAtUtc
            });

            await db.SaveChangesAsync();
        }

        private async Task InitializeAsync()
        {
            await using var scope = Provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

            await db.Database.EnsureCreatedAsync();

            await AddCompletedInstallationAsync(
                InstallationId,
                DateTime.UtcNow);
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
