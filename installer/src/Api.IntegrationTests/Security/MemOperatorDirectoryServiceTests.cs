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
using Modules.Auth.Contracts;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Security;

public sealed class MemOperatorDirectoryServiceTests
{
    [Fact]
    public async Task SEC_AUTH_04B_lists_a_safe_platform_owner_directory_with_roles_and_lifecycle_state()
    {
        await using var fixture = await DirectoryFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var directory = scope.ServiceProvider.GetRequiredService<IMemOperatorDirectoryService>();

        var owner = await CreateOperatorAsync(
            userManager,
            username: "owner.alpha",
            email: "owner.alpha@example.test",
            enabled: true,
            twoFactorEnabled: true,
            roles: [MemOperatorRoles.PlatformOwner]);

        var auditor = await CreateOperatorAsync(
            userManager,
            username: "audit.reader",
            email: null,
            enabled: false,
            twoFactorEnabled: false,
            roles: [MemOperatorRoles.Auditor]);

        var entries = await directory.ListAsync(owner.Id);

        Assert.Equal(["audit.reader", "owner.alpha"], entries.Select(entry => entry.Username));

        var ownerEntry = Assert.Single(entries, entry => entry.OperatorId == owner.Id);
        Assert.True(ownerEntry.IsCurrentOperator);
        Assert.True(ownerEntry.IsEnabled);
        Assert.True(ownerEntry.HasPassword);
        Assert.True(ownerEntry.HasTotp);
        Assert.Equal([MemOperatorRoles.PlatformOwner], ownerEntry.Roles);
        Assert.Equal("owner.alpha@example.test", ownerEntry.Email);

        var auditorEntry = Assert.Single(entries, entry => entry.OperatorId == auditor.Id);
        Assert.False(auditorEntry.IsCurrentOperator);
        Assert.False(auditorEntry.IsEnabled);
        Assert.True(auditorEntry.HasPassword);
        Assert.False(auditorEntry.HasTotp);
        Assert.Equal([MemOperatorRoles.Auditor], auditorEntry.Roles);

        Assert.DoesNotContain(
            typeof(MemOperatorDirectoryEntry).GetProperties(),
            property => property.Name.Contains("Hash", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Secret", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Recovery", StringComparison.OrdinalIgnoreCase) ||
                        property.Name.Contains("Stamp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SEC_AUTH_04B_directory_registration_uses_the_existing_platform_owner_policy_boundary()
    {
        await using var fixture = await DirectoryFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var policyProvider = scope.ServiceProvider
            .GetRequiredService<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider>();

        var policy = await policyProvider.GetPolicyAsync(MemOperatorPolicies.ManagePlatform);

        Assert.NotNull(policy);
        Assert.Contains(
            policy.Requirements,
            requirement => requirement is Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement roles &&
                roles.AllowedRoles.SequenceEqual([MemOperatorRoles.PlatformOwner]));
    }

    private static async Task<MemOperator> CreateOperatorAsync(
        UserManager<MemOperator> userManager,
        string username,
        string? email,
        bool enabled,
        bool twoFactorEnabled,
        IReadOnlyList<string> roles)
    {
        var user = new MemOperator
        {
            Id = Guid.NewGuid(),
            UserName = username,
            Email = email,
            IsEnabled = enabled,
            TwoFactorEnabled = twoFactorEnabled,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var created = await userManager.CreateAsync(user, "Secure!Foundation123");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

        var rolesAdded = await userManager.AddToRolesAsync(user, roles);
        Assert.True(rolesAdded.Succeeded, string.Join("; ", rolesAdded.Errors.Select(error => error.Description)));

        return user;
    }

    private sealed class DirectoryFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private DirectoryFixture(string databasePath, ServiceProvider provider)
        {
            _databasePath = databasePath;
            Provider = provider;
        }

        public ServiceProvider Provider { get; }

        public static async Task<DirectoryFixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"mem-sec-auth-04b-{Guid.NewGuid():N}.db");

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

            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection();
            services.AddHttpContextAccessor();
            services.AddDbContext<MemDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            services.AddMemOperatorIdentity(configuration, new TestHostEnvironment(Environments.Production));
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

            return new DirectoryFixture(databasePath, provider);
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
