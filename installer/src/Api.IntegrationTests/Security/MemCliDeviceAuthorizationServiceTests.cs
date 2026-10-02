using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Auth;
using Modules.Auth.Contracts;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Security;

/// <summary>
/// Direct service coverage for the persisted CLI device-authorization
/// lifecycle. Endpoint and browser tests separately prove the narrow HTTP and
/// React contracts; these tests keep the secret-handling and safe-review
/// behaviour grounded in the server-side state machine.
/// </summary>
public sealed class MemCliDeviceAuthorizationServiceTests
{
    private static readonly JsonSerializerOptions JsonDefaults = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CLI_AUTH_02A_requires_the_matching_high_entropy_verifier_before_issuing_a_device_credential()
    {
        await using var fixture = await DeviceSessionFixture.CreateAsync();
        var operatorAccount = await fixture.CreateReadyOperatorAsync("owner.device-flow");
        var verifier = CreateOpaqueValue();
        var challenge = CreateChallenge(verifier);

        await using var scope = fixture.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMemCliDeviceAuthorizationService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var started = await service.StartAsync(
            fixture.InstallationId,
            challenge,
            "Nigel workstation",
            correlationId: "cli-auth-02a-start");

        Assert.Equal(9, started.UserCode.Length);
        Assert.Equal('-', started.UserCode[4]);
        Assert.True(started.ExpiresAtUtc > DateTimeOffset.UtcNow);

        // A displayed short user code is deliberately not the polling proof.
        // It cannot be exchanged for an opaque device credential by itself.
        var codeOnlyPoll = await service.PollAsync(
            started.AuthorizationId,
            started.UserCode,
            correlationId: "cli-auth-02a-code-only");

        Assert.Equal("authorization_invalid", codeOnlyPoll.Status);
        Assert.Null(codeOnlyPoll.DeviceCredential);

        var pending = await service.PollAsync(
            started.AuthorizationId,
            verifier,
            correlationId: "cli-auth-02a-pending");

        Assert.Equal("authorization_pending", pending.Status);
        Assert.Null(pending.DeviceCredential);

        var approved = await service.ApproveAsync(
            started.UserCode,
            operatorAccount.Id,
            correlationId: "cli-auth-02a-approve");

        Assert.Equal("authorization_approved", approved.Status);

        var completed = await service.PollAsync(
            started.AuthorizationId,
            verifier,
            correlationId: "cli-auth-02a-complete");

        Assert.Equal("authorized", completed.Status);
        Assert.False(string.IsNullOrWhiteSpace(completed.DeviceCredential));
        Assert.NotNull(completed.IdleExpiresAtUtc);
        Assert.NotNull(completed.AbsoluteExpiresAtUtc);
        Assert.True(
            completed.IdleExpiresAtUtc!.Value <
            completed.AbsoluteExpiresAtUtc!.Value);

        var session = await db.MemCliDeviceSessions.SingleAsync();
        var attempt = await db.MemCliDeviceAuthorizationAttempts.SingleAsync();

        Assert.Equal(MemCliDeviceAuthorizationAttemptStatuses.Consumed, attempt.Status);
        Assert.Equal(operatorAccount.Id, session.OperatorId);
        Assert.Equal(fixture.InstallationId, session.InstallationId);
        Assert.Equal(Sha256Hex(completed.DeviceCredential!), session.CredentialHash);
        Assert.NotEqual(completed.DeviceCredential, session.CredentialHash);
        Assert.NotEqual(verifier, attempt.VerifierHash);
        Assert.NotEqual(started.UserCode, attempt.UserCodeHash);

        var replayed = await service.PollAsync(
            started.AuthorizationId,
            verifier,
            correlationId: "cli-auth-02a-replay");

        Assert.Equal("authorization_consumed", replayed.Status);
        Assert.Null(replayed.DeviceCredential);

        var auditJson = JsonSerializer.Serialize(
            await db.MemOperatorAuditEvents.ToArrayAsync(),
            JsonDefaults);

        Assert.Contains("identity.cli-device.authorization.started", auditJson, StringComparison.Ordinal);
        Assert.Contains("identity.cli-device.authorization.approved", auditJson, StringComparison.Ordinal);
        Assert.Contains("identity.cli-device.session.created", auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(started.UserCode, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(verifier, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(completed.DeviceCredential!, auditJson, StringComparison.Ordinal);
        Assert.Null(typeof(MemCliDeviceAuthorizationAttemptEntity).GetProperty("UserCode"));
        Assert.Null(typeof(MemCliDeviceAuthorizationAttemptEntity).GetProperty("Verifier"));
        Assert.Null(typeof(MemCliDeviceSessionEntity).GetProperty("DeviceCredential"));
    }

    [Fact]
    public async Task CLI_AUTH_02C_reviews_only_safe_pending_device_details()
    {
        await using var fixture = await DeviceSessionFixture.CreateAsync();
        var operatorAccount = await fixture.CreateReadyOperatorAsync("owner.device-review");
        var verifier = CreateOpaqueValue();

        await using var scope = fixture.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMemCliDeviceAuthorizationService>();

        var started = await service.StartAsync(
            fixture.InstallationId,
            CreateChallenge(verifier),
            "SSH host shell");

        var pending = await service.ReviewAsync(started.UserCode);

        Assert.Equal("authorization_pending", pending.Status);
        Assert.Equal("SSH host shell", pending.DeviceLabel);
        Assert.NotNull(pending.ExpiresAtUtc);
        Assert.DoesNotContain(started.UserCode, JsonSerializer.Serialize(pending, JsonDefaults), StringComparison.Ordinal);
        Assert.DoesNotContain(verifier, JsonSerializer.Serialize(pending, JsonDefaults), StringComparison.Ordinal);

        var denied = await service.DenyAsync(started.UserCode, operatorAccount.Id);
        Assert.Equal("authorization_denied", denied.Status);

        var terminal = await service.ReviewAsync(started.UserCode);

        Assert.Equal("authorization_denied", terminal.Status);
        Assert.Null(terminal.DeviceLabel);
        Assert.NotNull(terminal.ExpiresAtUtc);
    }

    [Fact]
    public async Task CLI_AUTH_02A_expires_and_single_uses_authorization_attempts_without_issuing_credentials()
    {
        await using var fixture = await DeviceSessionFixture.CreateAsync();
        var operatorAccount = await fixture.CreateReadyOperatorAsync("owner.device-expiry");
        var verifier = CreateOpaqueValue();

        await using var scope = fixture.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMemCliDeviceAuthorizationService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var started = await service.StartAsync(
            fixture.InstallationId,
            CreateChallenge(verifier),
            "Expiry test");

        var attempt = await db.MemCliDeviceAuthorizationAttempts.SingleAsync();
        attempt.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();

        var expired = await service.PollAsync(started.AuthorizationId, verifier);

        Assert.Equal("authorization_expired", expired.Status);
        Assert.Null(expired.DeviceCredential);
        Assert.Equal(MemCliDeviceAuthorizationAttemptStatuses.Expired, attempt.Status);
        Assert.Empty(await db.MemCliDeviceSessions.ToArrayAsync());

        var stillExpired = await service.ApproveAsync(started.UserCode, operatorAccount.Id);
        Assert.Equal("authorization_expired", stillExpired.Status);

        Assert.Contains(
            await db.MemOperatorAuditEvents.ToArrayAsync(),
            entry => entry.EventType == "identity.cli-device.authorization.expired" &&
                entry.Outcome == "expired");
    }

    [Fact]
    public async Task CLI_AUTH_02A_rejects_cross_installation_and_security_stamp_changed_device_sessions()
    {
        await using var fixture = await DeviceSessionFixture.CreateAsync();
        var operatorAccount = await fixture.CreateReadyOperatorAsync("owner.device-session");
        var verifier = CreateOpaqueValue();

        await using var scope = fixture.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMemCliDeviceAuthorizationService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var started = await service.StartAsync(
            fixture.InstallationId,
            CreateChallenge(verifier),
            "Session validation test");

        Assert.Equal(
            "authorization_approved",
            (await service.ApproveAsync(started.UserCode, operatorAccount.Id)).Status);

        var completed = await service.PollAsync(started.AuthorizationId, verifier);
        Assert.Equal("authorized", completed.Status);
        Assert.NotNull(completed.DeviceCredential);

        var wrongInstallation = await service.ValidateAsync(
            Guid.NewGuid(),
            completed.DeviceCredential!);

        Assert.Equal("invalid", wrongInstallation.Status);

        var valid = await service.ValidateAsync(
            fixture.InstallationId,
            completed.DeviceCredential!);

        Assert.Equal("authenticated", valid.Status);
        Assert.Equal(operatorAccount.Id, valid.OperatorId);
        Assert.NotNull(valid.SessionId);

        var trackedOperator = await userManager.FindByIdAsync(operatorAccount.Id.ToString("D"));
        Assert.NotNull(trackedOperator);

        var stampUpdated = await userManager.UpdateSecurityStampAsync(trackedOperator!);
        Assert.True(stampUpdated.Succeeded);

        var invalidated = await service.ValidateAsync(
            fixture.InstallationId,
            completed.DeviceCredential!);

        Assert.Equal("revoked", invalidated.Status);

        var persistedSession = await db.MemCliDeviceSessions.SingleAsync();
        Assert.NotNull(persistedSession.RevokedAtUtc);
        Assert.Equal("security_stamp_changed", persistedSession.RevokedReasonCode);

        Assert.Contains(
            await db.MemOperatorAuditEvents.ToArrayAsync(),
            entry => entry.EventType == "identity.cli-device.session.revoked" &&
                entry.ReasonCode == "security_stamp_changed");
    }

    [Fact]
    public async Task CLI_AUTH_03B_03B_revokes_current_device_sessions_and_blocks_later_validation()
    {
        await using var fixture = await DeviceSessionFixture.CreateAsync();
        var operatorAccount = await fixture.CreateReadyOperatorAsync("owner.device-logout");
        var verifier = CreateOpaqueValue();

        await using var scope = fixture.Provider.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IMemCliDeviceAuthorizationService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var started = await service.StartAsync(
            fixture.InstallationId,
            CreateChallenge(verifier),
            "Logout test");

        Assert.Equal(
            "authorization_approved",
            (await service.ApproveAsync(started.UserCode, operatorAccount.Id)).Status);

        var completed = await service.PollAsync(started.AuthorizationId, verifier);
        Assert.Equal("authorized", completed.Status);
        Assert.NotNull(completed.DeviceCredential);

        var valid = await service.ValidateAsync(
            fixture.InstallationId,
            completed.DeviceCredential!);

        Assert.Equal("authenticated", valid.Status);
        Assert.NotNull(valid.SessionId);

        var revoked = await service.RevokeSessionAsync(
            valid.SessionId!.Value,
            correlationId: "cli-auth-03b-03b-logout");

        Assert.Equal("revoked", revoked.Status);
        Assert.NotNull(revoked.RevokedAtUtc);

        var rejectedAfterLogout = await service.ValidateAsync(
            fixture.InstallationId,
            completed.DeviceCredential!);

        Assert.Equal("revoked", rejectedAfterLogout.Status);

        var persistedSession = await db.MemCliDeviceSessions.SingleAsync();
        Assert.NotNull(persistedSession.RevokedAtUtc);
        Assert.Equal("device_logout", persistedSession.RevokedReasonCode);

        Assert.Contains(
            await db.MemOperatorAuditEvents.ToArrayAsync(),
            entry => entry.EventType == "identity.cli-device.session.revoked" &&
                entry.ReasonCode == "device_logout");
    }

    private static string CreateOpaqueValue() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateChallenge(string verifier) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));

    private static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class DeviceSessionFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private DeviceSessionFixture(string databasePath, ServiceProvider provider)
        {
            _databasePath = databasePath;
            Provider = provider;
        }

        public ServiceProvider Provider { get; }

        public Guid InstallationId { get; } = Guid.NewGuid();

        public static async Task<DeviceSessionFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-cli-auth-02a-{Guid.NewGuid():N}.db");

            var settings = new Dictionary<string, string?>
            {
                ["MemOperatorIdentity:CookieName"] = "mem_operator_auth",
                ["MemOperatorIdentity:CookieIdleMinutes"] = "30",
                ["MemOperatorIdentity:PasswordMinimumLength"] = "14",
                ["MemOperatorIdentity:PasswordRequiredUniqueChars"] = "4",
                ["MemOperatorIdentity:LockoutMinutes"] = "15",
                ["MemOperatorIdentity:LockoutMaxFailedAccessAttempts"] = "5",
                ["MemOperatorIdentity:SecurityStampValidationSeconds"] = "0",
                ["MemCliDeviceSessions:AuthorizationAttemptMinutes"] = "10",
                ["MemCliDeviceSessions:SessionIdleMinutes"] = "480",
                ["MemCliDeviceSessions:SessionAbsoluteHours"] = "168"
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

            var provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            return new DeviceSessionFixture(databasePath, provider);
        }

        public async Task<MemOperator> CreateReadyOperatorAsync(string username)
        {
            await using var scope = Provider.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();

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

            return user;
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
