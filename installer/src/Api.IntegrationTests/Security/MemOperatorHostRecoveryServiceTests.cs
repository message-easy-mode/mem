using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Auth;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Security;

public sealed class MemOperatorHostRecoveryServiceTests
{
    [Fact]
    public async Task Host_recovery_resets_only_password_and_session_state_for_a_platform_owner()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var recovery = scope.ServiceProvider.GetRequiredService<IMemOperatorHostRecoveryService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var owner = await CreateOperatorAsync(
            userManager,
            "owner.recovery",
            MemOperatorRoles.PlatformOwner);

        var userStore = scope.ServiceProvider.GetRequiredService<IUserStore<MemOperator>>();
        var authenticatorStore = Assert.IsAssignableFrom<IUserAuthenticatorKeyStore<MemOperator>>(userStore);
        var recoveryCodeStore = Assert.IsAssignableFrom<IUserTwoFactorRecoveryCodeStore<MemOperator>>(userStore);

        var authenticatorKey = "JBSWY3DPEHPK3PXP";
        await authenticatorStore.SetAuthenticatorKeyAsync(
            owner,
            authenticatorKey,
            CancellationToken.None);
        owner.TwoFactorEnabled = true;

        var recoveryCodes = Enumerable.Range(1, 6)
            .Select(index => $"RECOVERY-{index:00}-CODE")
            .ToArray();
        await recoveryCodeStore.ReplaceCodesAsync(
            owner,
            recoveryCodes,
            CancellationToken.None);

        Assert.True((await userManager.UpdateAsync(owner)).Succeeded);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await userManager.AccessFailedAsync(owner);
        }

        var before = await userManager.FindByIdAsync(owner.Id.ToString("D"));
        Assert.NotNull(before);
        var securityStampBefore = before!.SecurityStamp;
        var rolesBefore = await userManager.GetRolesAsync(before);
        var recoveryCountBefore = await userManager.CountRecoveryCodesAsync(before);

        var result = await recovery.ResetPlatformOwnerPasswordAsync(
            "owner.recovery",
            "Different!SecurePassword456",
            correlationId: "host-recovery-test");

        var after = await userManager.FindByIdAsync(owner.Id.ToString("D"));
        Assert.NotNull(after);

        Assert.True(await userManager.CheckPasswordAsync(
            after!,
            "Different!SecurePassword456"));
        Assert.False(await userManager.CheckPasswordAsync(
            after,
            "Secure!Foundation123"));

        Assert.NotEqual(securityStampBefore, after.SecurityStamp);
        Assert.Equal(0, await userManager.GetAccessFailedCountAsync(after));
        Assert.False(await userManager.IsLockedOutAsync(after));

        Assert.True(after.TwoFactorEnabled);
        Assert.Equal(
            authenticatorKey,
            await userManager.GetAuthenticatorKeyAsync(after));
        Assert.Equal(
            recoveryCountBefore,
            await userManager.CountRecoveryCodesAsync(after));
        Assert.Equal(
            rolesBefore.OrderBy(role => role),
            (await userManager.GetRolesAsync(after)).OrderBy(role => role));

        Assert.Equal(after.Id, result.OperatorId);
        Assert.Equal("owner.recovery", result.Username);
        Assert.True(result.TotpPreserved);
        Assert.Equal(recoveryCountBefore, result.RecoveryCodeCount);
        Assert.Contains(MemOperatorRoles.PlatformOwner, result.Roles);

        var audit = await db.MemOperatorAuditEvents
            .SingleAsync(entry =>
                entry.EventType ==
                    "identity.platform_owner.host_password_recovered" &&
                entry.SubjectOperatorId == after.Id);

        Assert.Null(audit.ActorOperatorId);
        Assert.Equal("succeeded", audit.Outcome);
        Assert.Equal(
            "host_authoritative_password_reset",
            audit.ReasonCode);
        Assert.Equal("host-recovery-test", audit.CorrelationId);
    }

    [Fact]
    public async Task Host_recovery_rejects_non_owner_disabled_and_incomplete_accounts()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var recovery = scope.ServiceProvider.GetRequiredService<IMemOperatorHostRecoveryService>();

        await CreateOperatorAsync(
            userManager,
            "ordinary.operator",
            MemOperatorRoles.Operator);

        var disabled = await CreateOperatorAsync(
            userManager,
            "disabled.owner",
            MemOperatorRoles.PlatformOwner);
        disabled.IsEnabled = false;
        Assert.True((await userManager.UpdateAsync(disabled)).Succeeded);

        var incomplete = await CreateOperatorAsync(
            userManager,
            "incomplete.owner",
            MemOperatorRoles.PlatformOwner);
        incomplete.IsBootstrapProvisioning = true;
        Assert.True((await userManager.UpdateAsync(incomplete)).Succeeded);

        var nonOwner = await Assert.ThrowsAsync<MemOperatorHostRecoveryException>(() =>
            recovery.ResetPlatformOwnerPasswordAsync(
                "ordinary.operator",
                "Different!SecurePassword456"));
        Assert.Equal("platform_owner_role_required", nonOwner.Code);

        var disabledResult = await Assert.ThrowsAsync<MemOperatorHostRecoveryException>(() =>
            recovery.ResetPlatformOwnerPasswordAsync(
                "disabled.owner",
                "Different!SecurePassword456"));
        Assert.Equal("platform_owner_disabled", disabledResult.Code);

        var incompleteResult = await Assert.ThrowsAsync<MemOperatorHostRecoveryException>(() =>
            recovery.ResetPlatformOwnerPasswordAsync(
                "incomplete.owner",
                "Different!SecurePassword456"));
        Assert.Equal(
            "platform_owner_enrollment_incomplete",
            incompleteResult.Code);
    }

    [Fact]
    public async Task Host_recovery_enforces_the_normal_password_policy_and_leaves_old_password_valid_on_rejection()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var recovery = scope.ServiceProvider.GetRequiredService<IMemOperatorHostRecoveryService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var owner = await CreateOperatorAsync(
            userManager,
            "owner.policy",
            MemOperatorRoles.PlatformOwner);
        var stampBefore = owner.SecurityStamp;

        var rejected = await Assert.ThrowsAsync<MemOperatorHostRecoveryException>(() =>
            recovery.ResetPlatformOwnerPasswordAsync(
                "owner.policy",
                "weak"));

        Assert.Equal("password_policy_rejected", rejected.Code);
        Assert.NotEmpty(rejected.IdentityErrors);

        var after = await userManager.FindByIdAsync(owner.Id.ToString("D"));
        Assert.NotNull(after);
        Assert.True(await userManager.CheckPasswordAsync(
            after!,
            "Secure!Foundation123"));
        Assert.Equal(stampBefore, after.SecurityStamp);
        Assert.False(await db.MemOperatorAuditEvents.AnyAsync());
    }

    private static async Task<MemOperator> CreateOperatorAsync(
        UserManager<MemOperator> userManager,
        string username,
        string role)
    {
        var user = new MemOperator
        {
            Id = Guid.NewGuid(),
            UserName = username,
            IsEnabled = true,
            IsBootstrapProvisioning = false,
            TwoFactorEnabled = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var created = await userManager.CreateAsync(
            user,
            "Secure!Foundation123");
        Assert.True(
            created.Succeeded,
            string.Join(
                "; ",
                created.Errors.Select(error => error.Description)));

        var roleAdded = await userManager.AddToRoleAsync(user, role);
        Assert.True(
            roleAdded.Succeeded,
            string.Join(
                "; ",
                roleAdded.Errors.Select(error => error.Description)));

        return user;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private Fixture(
            string databasePath,
            ServiceProvider provider)
        {
            _databasePath = databasePath;
            Provider = provider;
        }

        public ServiceProvider Provider { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-owner-host-recovery-{Guid.NewGuid():N}.db");

            var settings = new Dictionary<string, string?>
            {
                ["MemOperatorIdentity:CookieName"] = "mem_operator_auth",
                ["MemOperatorIdentity:CookieIdleMinutes"] = "30",
                ["MemOperatorIdentity:BootstrapCookieName"] = "mem_bootstrap_auth",
                ["MemOperatorIdentity:BootstrapGrantMinutes"] = "15",
                ["MemOperatorIdentity:BootstrapRecoveryCodeCount"] = "10",
                ["MemOperatorIdentity:EnrollmentCookieName"] = "mem_operator_enrollment",
                ["MemOperatorIdentity:EnrollmentGrantMinutes"] = "15",
                ["MemOperatorIdentity:EnrollmentRecoveryCodeCount"] = "10",
                ["MemOperatorIdentity:OperatorRecoveryCodeCount"] = "10",
                ["MemOperatorIdentity:PasswordMinimumLength"] = "14",
                ["MemOperatorIdentity:PasswordRequiredUniqueChars"] = "4",
                ["MemOperatorIdentity:LockoutMinutes"] = "15",
                ["MemOperatorIdentity:LockoutMaxFailedAccessAttempts"] = "5",
                ["MemOperatorIdentity:LoginRateLimitPermitLimit"] = "20",
                ["MemOperatorIdentity:LoginRateLimitWindowSeconds"] = "60",
                ["MemOperatorIdentity:StepUpGrantMinutes"] = "15",
                ["MemOperatorIdentity:SecurityStampValidationSeconds"] = "0",
                ["MemCliDeviceSessions:AuthorizationAttemptMinutes"] = "10",
                ["MemCliDeviceSessions:SessionIdleMinutes"] = "480",
                ["MemCliDeviceSessions:SessionAbsoluteHours"] = "168",
                ["MemCliDeviceSessions:AuthorizationStartRateLimitPermitLimit"] = "10",
                ["MemCliDeviceSessions:AuthorizationStartRateLimitWindowSeconds"] = "60",
                ["MemCliDeviceSessions:AuthorizationPollRateLimitPermitLimit"] = "60",
                ["MemCliDeviceSessions:AuthorizationPollRateLimitWindowSeconds"] = "60"
            };

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddDbContext<MemDbContext>(
                options => options.UseSqlite($"Data Source={databasePath}"));
            services.AddMemOperatorIdentity(
                configuration,
                new TestHostEnvironment(Environments.Production));

            var provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                await db.Database.EnsureCreatedAsync();

                var initializer = provider
                    .GetServices<IHostedService>()
                    .OfType<MemOperatorRoleInitializer>()
                    .Single();
                await initializer.StartAsync(CancellationToken.None);
            }

            return new Fixture(databasePath, provider);
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

    private sealed class TestHostEnvironment(
        string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Api.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
