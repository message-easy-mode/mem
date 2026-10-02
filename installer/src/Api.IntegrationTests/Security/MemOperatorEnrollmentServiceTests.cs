using System.Security.Cryptography;
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

public sealed class MemOperatorEnrollmentServiceTests
{
    [Fact]
    public async Task SEC_AUTH_04C_issues_a_one_time_hashed_enrollment_code_and_reissues_cleanly()
    {
        await using var fixture = await EnrollmentFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var enrollment = scope.ServiceProvider.GetRequiredService<IMemOperatorEnrollmentService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var owner = await CreateReadyOperatorAsync(
            userManager,
            "owner.alpha",
            [MemOperatorRoles.PlatformOwner]);

        var pending = await CreatePendingOperatorAsync(
            userManager,
            "operator.pending",
            [MemOperatorRoles.Operator]);

        var first = await enrollment.IssueAsync(owner.Id, pending.Id, "issue-first");

        Assert.StartsWith("mem_enrol_", first.EnrollmentCode);
        Assert.Equal(pending.Id, first.OperatorId);
        Assert.Equal("operator.pending", first.Username);

        var firstPersisted = await db.MemOperatorEnrollmentGrants
            .SingleAsync(item => item.Id != Guid.Empty);

        Assert.Equal(pending.Id, firstPersisted.OperatorId);
        Assert.Equal(MemOperatorEnrollmentGrantStatuses.Active, firstPersisted.Status);
        Assert.Equal(64, firstPersisted.CodeHash.Length);
        Assert.NotEqual(first.EnrollmentCode, firstPersisted.CodeHash);
        Assert.DoesNotContain(
            await db.MemOperatorAuditEvents.ToArrayAsync(),
            entry => string.Equals(entry.ReasonCode, first.EnrollmentCode, StringComparison.Ordinal) ||
                     string.Equals(entry.CorrelationId, first.EnrollmentCode, StringComparison.Ordinal));

        var second = await enrollment.IssueAsync(owner.Id, pending.Id, "issue-second");

        Assert.NotEqual(first.EnrollmentCode, second.EnrollmentCode);

        var grants = (await db.MemOperatorEnrollmentGrants
            .ToArrayAsync())
            .OrderBy(item => item.CreatedAtUtc)
            .ToArray();

        Assert.Equal(2, grants.Length);
        Assert.Equal(MemOperatorEnrollmentGrantStatuses.Cancelled, grants[0].Status);
        Assert.Equal(MemOperatorEnrollmentGrantStatuses.Active, grants[1].Status);

        var events = await db.MemOperatorAuditEvents
            .Where(item => item.SubjectOperatorId == pending.Id)
            .ToArrayAsync();

        Assert.Contains(events, item =>
            item.EventType == "identity.operator.enrollment_grant_issued" &&
            item.ActorOperatorId == owner.Id);
        Assert.Contains(events, item =>
            item.EventType == "identity.operator.enrollment_grant_cancelled" &&
            item.ActorOperatorId == owner.Id &&
            item.ReasonCode == "reissued");
    }

    [Fact]
    public async Task SEC_AUTH_04C_reissue_cancels_a_claimed_grant_and_resets_partial_credentials()
    {
        await using var fixture = await EnrollmentFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var enrollment = scope.ServiceProvider.GetRequiredService<IMemOperatorEnrollmentService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var owner = await CreateReadyOperatorAsync(
            userManager,
            "owner.alpha",
            [MemOperatorRoles.PlatformOwner]);

        var pending = await CreatePendingOperatorAsync(
            userManager,
            "operator.pending",
            [MemOperatorRoles.Operator]);

        var first = await enrollment.IssueAsync(owner.Id, pending.Id);
        var begun = await enrollment.BeginAsync(first.EnrollmentCode);

        var prepared = await enrollment.PrepareAsync(
            begun.GrantId,
            new PrepareMemOperatorEnrollmentRequest("Secure!Enrollment123"));

        Assert.False(string.IsNullOrWhiteSpace(prepared.ManualEntryKey));

        var replacement = await enrollment.IssueAsync(owner.Id, pending.Id);

        Assert.NotEqual(first.EnrollmentCode, replacement.EnrollmentCode);
        Assert.False(await enrollment.IsGrantCurrentAsync(begun.GrantId));

        var resetSubject = await db.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == pending.Id);

        Assert.False(resetSubject.IsEnabled);
        Assert.False(resetSubject.TwoFactorEnabled);
        Assert.True(string.IsNullOrWhiteSpace(resetSubject.PasswordHash));

        var firstGrant = await db.MemOperatorEnrollmentGrants
            .SingleAsync(grant => grant.Id == begun.GrantId);

        Assert.Equal(MemOperatorEnrollmentGrantStatuses.Cancelled, firstGrant.Status);

        var replacementBegin = await enrollment.BeginAsync(replacement.EnrollmentCode);

        Assert.Equal("password", replacementBegin.State.Stage);
    }

    [Fact]
    public async Task SEC_AUTH_04C_completes_pending_operator_password_totp_recovery_and_normal_access()
    {
        await using var fixture = await EnrollmentFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var enrollment = scope.ServiceProvider.GetRequiredService<IMemOperatorEnrollmentService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var owner = await CreateReadyOperatorAsync(
            userManager,
            "owner.alpha",
            [MemOperatorRoles.PlatformOwner]);

        var pending = await CreatePendingOperatorAsync(
            userManager,
            "operator.pending",
            [MemOperatorRoles.Operator]);

        var issued = await enrollment.IssueAsync(owner.Id, pending.Id);
        var begun = await enrollment.BeginAsync(issued.EnrollmentCode);

        Assert.True(begun.State.Active);
        Assert.Equal("password", begun.State.Stage);
        Assert.Equal("operator.pending", begun.State.Username);

        var prepared = await enrollment.PrepareAsync(
            begun.GrantId,
            new PrepareMemOperatorEnrollmentRequest("Secure!Enrollment123"));

        Assert.Equal("operator.pending", prepared.Username);
        Assert.StartsWith("otpauth://totp/", prepared.AuthenticatorUri);
        Assert.False(string.IsNullOrWhiteSpace(prepared.ManualEntryKey));

        await enrollment.VerifyTotpAsync(
            begun.GrantId,
            CreateCurrentTotp(prepared.ManualEntryKey));

        var completed = await enrollment.CompleteAsync(begun.GrantId);

        Assert.Equal("operator.pending", completed.Username);
        Assert.Equal(10, completed.RecoveryCodes.Count);
        Assert.All(completed.RecoveryCodes, code => Assert.False(string.IsNullOrWhiteSpace(code)));

        var activated = await userManager.FindByIdAsync(pending.Id.ToString("D"));

        Assert.NotNull(activated);
        Assert.True(activated!.IsEnabled);
        Assert.True(activated.TwoFactorEnabled);
        Assert.False(string.IsNullOrWhiteSpace(activated.PasswordHash));
        Assert.True(await userManager.IsInRoleAsync(activated, MemOperatorRoles.Operator));

        Assert.False(await enrollment.IsGrantCurrentAsync(begun.GrantId));

        var persistedGrant = await db.MemOperatorEnrollmentGrants
            .SingleAsync(item => item.Id == begun.GrantId);

        Assert.Equal(MemOperatorEnrollmentGrantStatuses.Completed, persistedGrant.Status);
        Assert.NotNull(persistedGrant.ConsumedAtUtc);

        var events = await db.MemOperatorAuditEvents
            .Where(item => item.SubjectOperatorId == pending.Id)
            .ToArrayAsync();

        Assert.Contains(events, item =>
            item.EventType == "identity.operator.enrollment_completed" &&
            item.ActorOperatorId == pending.Id &&
            item.ReasonCode == "password_totp_recovery_codes");
    }

    [Fact]
    public async Task SEC_AUTH_04C_rejects_self_issue_and_invalid_or_replayed_codes()
    {
        await using var fixture = await EnrollmentFixture.CreateAsync();
        await using var scope = fixture.Provider.CreateAsyncScope();

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var enrollment = scope.ServiceProvider.GetRequiredService<IMemOperatorEnrollmentService>();

        var owner = await CreateReadyOperatorAsync(
            userManager,
            "owner.alpha",
            [MemOperatorRoles.PlatformOwner]);

        var self = await Assert.ThrowsAsync<OperatorEnrollmentException>(() =>
            enrollment.IssueAsync(owner.Id, owner.Id));

        Assert.Equal("operator_self_management_not_allowed", self.Code);

        var invalid = await Assert.ThrowsAsync<OperatorEnrollmentException>(() =>
            enrollment.BeginAsync("mem_enrol_0123456789ABCDEF0123456789ABCDEF"));

        Assert.Equal("enrollment_code_invalid", invalid.Code);

        var pending = await CreatePendingOperatorAsync(
            userManager,
            "operator.pending",
            [MemOperatorRoles.Auditor]);

        var issued = await enrollment.IssueAsync(owner.Id, pending.Id);
        await enrollment.BeginAsync(issued.EnrollmentCode);

        var replayed = await Assert.ThrowsAsync<OperatorEnrollmentException>(() =>
            enrollment.BeginAsync(issued.EnrollmentCode));

        Assert.Equal("enrollment_code_invalid", replayed.Code);
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

    private static async Task<MemOperator> CreatePendingOperatorAsync(
        UserManager<MemOperator> userManager,
        string username,
        IReadOnlyList<string> roles)
    {
        var user = new MemOperator
        {
            Id = Guid.NewGuid(),
            UserName = username,
            IsEnabled = false,
            TwoFactorEnabled = false,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        var created = await userManager.CreateAsync(user);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

        var rolesAdded = await userManager.AddToRolesAsync(user, roles);
        Assert.True(rolesAdded.Succeeded, string.Join("; ", rolesAdded.Errors.Select(error => error.Description)));

        return user;
    }

    private static string CreateCurrentTotp(string base32Secret)
    {
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        Span<byte> counterBytes = stackalloc byte[8];

        for (var index = counterBytes.Length - 1; index >= 0; index -= 1)
        {
            counterBytes[index] = (byte)(counter & 0xff);
            counter >>= 8;
        }

        using var hmac = new HMACSHA1(DecodeBase32(base32Secret));
        var digest = hmac.ComputeHash(counterBytes.ToArray());
        var offset = digest[^1] & 0x0f;
        var binary =
            ((digest[offset] & 0x7f) << 24) |
            (digest[offset + 1] << 16) |
            (digest[offset + 2] << 8) |
            digest[offset + 3];

        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var normalized = value
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .TrimEnd('=')
            .ToUpperInvariant();

        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;

        foreach (var character in normalized)
        {
            var digit = alphabet.IndexOf(character);

            if (digit < 0)
            {
                throw new InvalidOperationException("Invalid Base32 test secret.");
            }

            buffer = (buffer << 5) | digit;
            bits += 5;

            while (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)((buffer >> bits) & 0xff));
            }
        }

        return bytes.ToArray();
    }

    private sealed class EnrollmentFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private EnrollmentFixture(string databasePath, ServiceProvider provider)
        {
            _databasePath = databasePath;
            Provider = provider;
        }

        public ServiceProvider Provider { get; }

        public static async Task<EnrollmentFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-sec-auth-04c-{Guid.NewGuid():N}.db");

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

            return new EnrollmentFixture(databasePath, provider);
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
