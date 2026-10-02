using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Auth;
using Modules.Auth.Contracts;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Security;

public sealed class MemBootstrapGrantServiceTests
{
    [Fact]
    public async Task SEC_AUTH_03A_issues_an_opaque_short_lived_grant_without_storing_a_raw_setup_code()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var bootstrap = scope.ServiceProvider.GetRequiredService<IMemBootstrapGrantService>();
        var state = await bootstrap.GetStateAsync();

        Assert.True(state.RequiresFirstOwnerBootstrap);
        Assert.False(state.HasCompletedPlatformOwner);

        var grant = await bootstrap.IssueGrantAsync();

        Assert.True(grant.ExpiresAtUtc > grant.CreatedAtUtc);
        Assert.Equal(MemBootstrapGrantStatuses.Active, grant.Status);
        Assert.Null(grant.PendingOperatorId);
        Assert.DoesNotContain(
            typeof(MemBootstrapGrantEntity).GetProperties(),
            property => property.Name.Contains("Token", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SEC_AUTH_03A_prepares_a_disabled_roleless_owner_until_mfa_completion()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var bootstrap = scope.ServiceProvider.GetRequiredService<IMemBootstrapGrantService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var grant = await bootstrap.IssueGrantAsync();

        var enrollment = await bootstrap.PrepareFirstOwnerAsync(
            grant.Id,
            new CreateFirstMemOperatorRequest(
                Username: "first.owner",
                Email: "owner@example.test",
                Password: "Secure!Foundation123"));

        var user = await userManager.FindByNameAsync("first.owner");

        Assert.NotNull(user);
        Assert.False(user.IsEnabled);
        Assert.True(user.IsBootstrapProvisioning);
        Assert.False(user.TwoFactorEnabled);
        Assert.Empty(await userManager.GetRolesAsync(user));
        Assert.Equal("first.owner", enrollment.Username);
        Assert.NotEmpty(enrollment.ManualEntryKey);
        Assert.Contains("otpauth://totp/", enrollment.AuthenticatorUri, StringComparison.Ordinal);

        var persistedGrant = await db.MemBootstrapGrants.SingleAsync();
        Assert.Equal(user.Id, persistedGrant.PendingOperatorId);
        Assert.Equal(MemBootstrapGrantStatuses.Active, persistedGrant.Status);
    }

    [Fact]
    public async Task SEC_AUTH_03A_completes_only_after_totp_verification_and_generates_one_time_recovery_codes()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var bootstrap = scope.ServiceProvider.GetRequiredService<IMemBootstrapGrantService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var grant = await bootstrap.IssueGrantAsync();

        await bootstrap.PrepareFirstOwnerAsync(
            grant.Id,
            new CreateFirstMemOperatorRequest(
                Username: "first.owner",
                Email: null,
                Password: "Secure!Foundation123"));

        await Assert.ThrowsAsync<BootstrapFlowException>(() =>
            bootstrap.CompleteAsync(grant.Id));

        var persistedGrant = await db.MemBootstrapGrants.SingleAsync();
        persistedGrant.Status = MemBootstrapGrantStatuses.TotpVerified;
        persistedGrant.TotpVerifiedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        var completed = await bootstrap.CompleteAsync(grant.Id);
        var user = await userManager.FindByNameAsync("first.owner");

        Assert.NotNull(user);
        Assert.True(user.IsEnabled);
        Assert.False(user.IsBootstrapProvisioning);
        Assert.True(user.TwoFactorEnabled);
        Assert.Contains(
            MemOperatorRoles.PlatformOwner,
            await userManager.GetRolesAsync(user));
        Assert.Equal(10, completed.RecoveryCodes.Count);
        Assert.All(completed.RecoveryCodes, Assert.NotEmpty);

        var completedGrant = await db.MemBootstrapGrants.SingleAsync();
        Assert.Equal(MemBootstrapGrantStatuses.Completed, completedGrant.Status);
        Assert.NotNull(completedGrant.ConsumedAtUtc);
    }

    [Fact]
    public async Task SEC_AUTH_03A_cancellation_removes_the_disabled_provisioning_account()
    {
        await using var fixture = await BootstrapFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var bootstrap = scope.ServiceProvider.GetRequiredService<IMemBootstrapGrantService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var grant = await bootstrap.IssueGrantAsync();

        await bootstrap.PrepareFirstOwnerAsync(
            grant.Id,
            new CreateFirstMemOperatorRequest(
                Username: "cancelled.owner",
                Email: null,
                Password: "Secure!Foundation123"));

        await bootstrap.CancelAsync(grant.Id);

        Assert.Null(await userManager.FindByNameAsync("cancelled.owner"));

        var cancelledGrant = await db.MemBootstrapGrants.SingleAsync();
        Assert.Equal(MemBootstrapGrantStatuses.Cancelled, cancelledGrant.Status);
        Assert.NotNull(cancelledGrant.CancelledAtUtc);
        Assert.False(await bootstrap.IsGrantCurrentAsync(grant.Id));
    }

    private sealed class BootstrapFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private BootstrapFixture(string databasePath, ServiceProvider provider)
        {
            _databasePath = databasePath;
            Provider = provider;
        }

        public ServiceProvider Provider { get; }

        public static async Task<BootstrapFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-sec-auth-03a-{Guid.NewGuid():N}.db");

            var settings = new Dictionary<string, string?>
            {
                ["MemOperatorIdentity:CookieName"] = "mem_operator_auth",
                ["MemOperatorIdentity:CookieIdleMinutes"] = "30",
                ["MemOperatorIdentity:BootstrapCookieName"] = "mem_bootstrap_auth",
                ["MemOperatorIdentity:BootstrapGrantMinutes"] = "15",
                ["MemOperatorIdentity:BootstrapRecoveryCodeCount"] = "10",
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

                var initializer = provider
                    .GetServices<IHostedService>()
                    .OfType<MemOperatorRoleInitializer>()
                    .Single();

                await initializer.StartAsync(CancellationToken.None);
            }

            return new BootstrapFixture(databasePath, provider);
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
