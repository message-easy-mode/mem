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

public sealed class MemOperatorLifecycleServiceTests
{
    [Fact]
    public async Task SEC_AUTH_04B_02_creates_a_disabled_pending_operator_with_safe_audit_events()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IMemOperatorLifecycleService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var owner = await CreateReadyOperatorAsync(
            userManager,
            "owner.alpha",
            roles: [MemOperatorRoles.PlatformOwner]);

        var created = await lifecycle.CreatePendingAsync(
            owner.Id,
            new CreatePendingMemOperatorRequest(
                Username: "audit.reader",
                Email: "audit.reader@example.test",
                Roles: [MemOperatorRoles.Auditor]),
            correlationId: "create-pending");

        Assert.Equal("audit.reader", created.Username);
        Assert.Equal("audit.reader@example.test", created.Email);
        Assert.False(created.IsEnabled);
        Assert.False(created.IsBootstrapProvisioning);
        Assert.False(created.HasPassword);
        Assert.False(created.HasTotp);
        Assert.Equal([MemOperatorRoles.Auditor], created.Roles);

        var persisted = await userManager.FindByIdAsync(created.OperatorId.ToString("D"));
        Assert.NotNull(persisted);
        Assert.Null(persisted!.PasswordHash);
        Assert.False(persisted.IsEnabled);

        var events = await db.MemOperatorAuditEvents
            .Where(entry => entry.SubjectOperatorId == created.OperatorId)
            .OrderBy(entry => entry.EventType)
            .ToArrayAsync();

        Assert.Collection(
            events,
            entry =>
            {
                Assert.Equal("identity.operator.created", entry.EventType);
                Assert.Equal("enrolment_required", entry.ReasonCode);
                Assert.Equal(owner.Id, entry.ActorOperatorId);
            },
            entry =>
            {
                Assert.Equal("identity.operator.role_granted", entry.EventType);
                Assert.Equal(MemOperatorRoles.Auditor, entry.ReasonCode);
                Assert.Equal(owner.Id, entry.ActorOperatorId);
            });
    }

    [Fact]
    public async Task SEC_AUTH_04B_02_does_not_enable_an_operator_until_password_totp_and_role_enrolment_are_complete()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IMemOperatorLifecycleService>();

        var owner = await CreateReadyOperatorAsync(
            userManager,
            "owner.alpha",
            roles: [MemOperatorRoles.PlatformOwner]);

        var pending = await lifecycle.CreatePendingAsync(
            owner.Id,
            new CreatePendingMemOperatorRequest(
                Username: "pending.operator",
                Email: null,
                Roles: [MemOperatorRoles.Operator]));

        var exception = await Assert.ThrowsAsync<OperatorAdministrationException>(() =>
            lifecycle.SetEnabledAsync(
                owner.Id,
                pending.OperatorId,
                new SetMemOperatorEnabledRequest(true)));

        Assert.Equal("operator_enrolment_required", exception.Code);
        Assert.Equal(OperatorAdministrationFailureKind.Conflict, exception.Kind);

        var persisted = await userManager.FindByIdAsync(pending.OperatorId.ToString("D"));
        Assert.NotNull(persisted);
        Assert.False(persisted!.IsEnabled);
    }

    [Fact]
    public async Task SEC_AUTH_04B_02_protects_the_final_active_platform_owner_from_disable_or_role_removal()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IMemOperatorLifecycleService>();

        var finalOwner = await CreateReadyOperatorAsync(
            userManager,
            "owner.final",
            roles: [MemOperatorRoles.PlatformOwner]);

        var disable = await Assert.ThrowsAsync<OperatorAdministrationException>(() =>
            lifecycle.SetEnabledAsync(
                Guid.NewGuid(),
                finalOwner.Id,
                new SetMemOperatorEnabledRequest(false)));

        Assert.Equal("last_active_platform_owner", disable.Code);

        var roles = await Assert.ThrowsAsync<OperatorAdministrationException>(() =>
            lifecycle.SetRolesAsync(
                Guid.NewGuid(),
                finalOwner.Id,
                new SetMemOperatorRolesRequest([MemOperatorRoles.Operator])));

        Assert.Equal("last_active_platform_owner", roles.Code);

        var persisted = await userManager.FindByIdAsync(finalOwner.Id.ToString("D"));
        Assert.NotNull(persisted);
        Assert.True(persisted!.IsEnabled);
        Assert.True(await userManager.IsInRoleAsync(persisted, MemOperatorRoles.PlatformOwner));
    }

    [Fact]
    public async Task SEC_AUTH_04B_02_changes_roles_and_revokes_existing_sessions_with_structured_audit_events()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IMemOperatorLifecycleService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var owner = await CreateReadyOperatorAsync(
            userManager,
            "owner.alpha",
            roles: [MemOperatorRoles.PlatformOwner]);

        var subject = await CreateReadyOperatorAsync(
            userManager,
            "operator.beta",
            roles: [MemOperatorRoles.Operator]);

        var beforeRoleChangeStamp = subject.SecurityStamp;

        var changed = await lifecycle.SetRolesAsync(
            owner.Id,
            subject.Id,
            new SetMemOperatorRolesRequest([MemOperatorRoles.Auditor]),
            correlationId: "roles-change");

        Assert.Equal([MemOperatorRoles.Auditor], changed.Roles);

        var afterRoleChange = await userManager.FindByIdAsync(subject.Id.ToString("D"));
        Assert.NotNull(afterRoleChange);
        Assert.NotEqual(beforeRoleChangeStamp, afterRoleChange!.SecurityStamp);
        Assert.True(await userManager.IsInRoleAsync(afterRoleChange, MemOperatorRoles.Auditor));
        Assert.False(await userManager.IsInRoleAsync(afterRoleChange, MemOperatorRoles.Operator));

        var beforeExplicitRevocationStamp = afterRoleChange.SecurityStamp;

        await lifecycle.RevokeSessionsAsync(
            owner.Id,
            subject.Id,
            correlationId: "explicit-revoke");

        var afterExplicitRevocation = await userManager.FindByIdAsync(subject.Id.ToString("D"));
        Assert.NotNull(afterExplicitRevocation);
        Assert.NotEqual(beforeExplicitRevocationStamp, afterExplicitRevocation!.SecurityStamp);

        var events = await db.MemOperatorAuditEvents
            .Where(entry => entry.SubjectOperatorId == subject.Id)
            .ToArrayAsync();

        Assert.Contains(events, entry =>
            entry.EventType == "identity.operator.role_granted" &&
            entry.ReasonCode == MemOperatorRoles.Auditor &&
            entry.ActorOperatorId == owner.Id);

        Assert.Contains(events, entry =>
            entry.EventType == "identity.operator.role_revoked" &&
            entry.ReasonCode == MemOperatorRoles.Operator &&
            entry.ActorOperatorId == owner.Id);

        Assert.Equal(
            2,
            events.Count(entry => entry.EventType == "identity.operator.sessions_revoked"));
    }

    [Fact]
    public async Task SEC_AUTH_06B_rejects_a_role_change_that_would_leave_an_operator_without_any_roles()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IMemOperatorLifecycleService>();

        var owner = await CreateReadyOperatorAsync(
            userManager,
            "owner.alpha",
            roles: [MemOperatorRoles.PlatformOwner]);

        var subject = await CreateReadyOperatorAsync(
            userManager,
            "operator.beta",
            roles: [MemOperatorRoles.Operator]);

        var rejected = await Assert.ThrowsAsync<OperatorAdministrationException>(() =>
            lifecycle.SetRolesAsync(
                owner.Id,
                subject.Id,
                new SetMemOperatorRolesRequest([]),
                correlationId: "empty-role-membership"));

        Assert.Equal("operator_role_required", rejected.Code);
        Assert.Equal(OperatorAdministrationFailureKind.Validation, rejected.Kind);

        var persisted = await userManager.FindByIdAsync(subject.Id.ToString("D"));
        Assert.NotNull(persisted);
        Assert.True(persisted!.IsEnabled);
        Assert.True(await userManager.IsInRoleAsync(persisted, MemOperatorRoles.Operator));
    }

    [Fact]
    public async Task SEC_AUTH_04B_02_disables_a_ready_operator_and_rejects_self_management()
    {
        await using var fixture = await LifecycleFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<IMemOperatorLifecycleService>();

        var owner = await CreateReadyOperatorAsync(
            userManager,
            "owner.alpha",
            roles: [MemOperatorRoles.PlatformOwner]);

        var subject = await CreateReadyOperatorAsync(
            userManager,
            "operator.beta",
            roles: [MemOperatorRoles.Operator]);

        var beforeDisableStamp = subject.SecurityStamp;

        var disabled = await lifecycle.SetEnabledAsync(
            owner.Id,
            subject.Id,
            new SetMemOperatorEnabledRequest(false));

        Assert.False(disabled.IsEnabled);

        var persisted = await userManager.FindByIdAsync(subject.Id.ToString("D"));
        Assert.NotNull(persisted);
        Assert.False(persisted!.IsEnabled);
        Assert.NotEqual(beforeDisableStamp, persisted.SecurityStamp);

        var self = await Assert.ThrowsAsync<OperatorAdministrationException>(() =>
            lifecycle.RevokeSessionsAsync(
                owner.Id,
                owner.Id));

        Assert.Equal("operator_self_management_not_allowed", self.Code);
    }

    private static async Task<MemOperator> CreateReadyOperatorAsync(
        UserManager<MemOperator> userManager,
        string username,
        IReadOnlyList<string> roles)
    {
        var user = new MemOperator
        {
            Id = Guid.NewGuid(),
            UserName = username,
            IsEnabled = true,
            TwoFactorEnabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var created = await userManager.CreateAsync(user, "Secure!Foundation123");
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

        var rolesAdded = await userManager.AddToRolesAsync(user, roles);
        Assert.True(rolesAdded.Succeeded, string.Join("; ", rolesAdded.Errors.Select(error => error.Description)));

        return user;
    }

    private sealed class LifecycleFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private LifecycleFixture(string databasePath, ServiceProvider provider)
        {
            _databasePath = databasePath;
            Provider = provider;
        }

        public ServiceProvider Provider { get; }

        public static async Task<LifecycleFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-sec-auth-04b-02-{Guid.NewGuid():N}.db");

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

            return new LifecycleFixture(databasePath, provider);
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
