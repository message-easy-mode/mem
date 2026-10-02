using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth;
using Modules.Auth.Contracts;
using Modules.Auth.Endpoints;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Security;

public sealed class MemOperatorPasswordChangeTests
{
    private const string OldPassword = "Secure!Foundation123";
    private const string NewPassword = "Different!Foundation456";

    [Fact]
    public async Task OPERATOR_ACCOUNT_SECURITY_PASSWORD_CORR_01A_rotates_only_the_current_operator_password_and_invalidates_existing_authority()
    {
        await using var fixture = await PasswordFixture.CreateAsync();

        var subject = await fixture.CreateReadyOperatorAsync(
            "owner.password-change",
            OldPassword,
            MemOperatorRoles.PlatformOwner);
        var recoveryCodes = await fixture.GenerateRecoveryCodesAsync(subject, 3);
        var sessionId = Guid.NewGuid();
        var principal = fixture.CreateOperatorPrincipal(subject, sessionId, MemOperatorRoles.PlatformOwner);
        var before = await fixture.GetAccountSnapshotAsync(subject);
        var currentTotp = await fixture.GetCurrentTotpAsync(subject);
        var cliSession = await fixture.CreateCliDeviceSessionAsync(subject);

        var stepUp = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/step-up",
            new VerifyMemOperatorStepUpRequest(OldPassword, currentTotp),
            principal);

        Assert.Equal(HttpStatusCode.OK, stepUp.StatusCode);

        var changed = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/password",
            new ChangeMemOperatorPasswordRequest(NewPassword, NewPassword),
            principal);

        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        AssertSensitiveNoStore(changed.Headers);
        AssertBrowserAuthenticationCookiesExpired(changed.Headers);
        Assert.DoesNotContain(OldPassword, changed.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, changed.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(currentTotp, changed.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(cliSession.RawCredential, changed.Body, StringComparison.Ordinal);

        var response = JsonSerializer.Deserialize<MemOperatorPasswordChangeResponse>(
            changed.Body,
            JsonDefaults);

        Assert.NotNull(response);
        Assert.Equal("password_changed", response!.Status);

        var after = await fixture.GetAccountSnapshotAsync(subject);

        Assert.False(await fixture.CheckPasswordAsync(subject, OldPassword));
        Assert.True(await fixture.CheckPasswordAsync(subject, NewPassword));
        Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);
        Assert.Equal(before.AuthenticatorKey, after.AuthenticatorKey);
        Assert.Equal(before.RecoveryCodeCount, after.RecoveryCodeCount);
        Assert.Equal(before.Roles.ToArray(), after.Roles.ToArray());
        Assert.Equal(before.Username, after.Username);
        Assert.Equal(before.Email, after.Email);
        Assert.True(after.TwoFactorEnabled);
        foreach (var recoveryCode in recoveryCodes)
        {
            Assert.True(await fixture.IsRecoveryCodeStillUsableAsync(subject, recoveryCode));
        }

        var cliValidation = await fixture.ValidateCliDeviceSessionAsync(
            cliSession.InstallationId,
            cliSession.RawCredential);

        Assert.Equal("revoked", cliValidation.Status);

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
        var grant = await db.MemOperatorStepUpGrants.SingleAsync(entry =>
            entry.OperatorId == subject.Id && entry.SessionId == sessionId);
        var persistedCliSession = await db.MemCliDeviceSessions.SingleAsync(entry =>
            entry.Id == cliSession.SessionId);
        var events = (await db.MemOperatorAuditEvents
                .Where(entry => entry.SubjectOperatorId == subject.Id)
                .ToArrayAsync())
            .OrderBy(entry => entry.OccurredAtUtc)
            .ToArray();

        Assert.NotNull(grant.RevokedAtUtc);
        Assert.Equal("password_changed", grant.RevokedReasonCode);
        Assert.NotNull(persistedCliSession.RevokedAtUtc);
        Assert.Equal("security_stamp_changed", persistedCliSession.RevokedReasonCode);
        Assert.Contains(events, entry =>
            entry.EventType == "identity.password.changed" &&
            entry.Outcome == "succeeded" &&
            entry.ActorOperatorId == subject.Id &&
            entry.SubjectOperatorId == subject.Id &&
            entry.ReasonCode == "self_service_password_change");

        var auditJson = JsonSerializer.Serialize(events, JsonDefaults);
        Assert.DoesNotContain(OldPassword, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(NewPassword, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(currentTotp, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(cliSession.RawCredential, auditJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OPERATOR_ACCOUNT_SECURITY_PASSWORD_CORR_01A_requires_fresh_step_up_and_expired_step_up_leaves_the_old_password_valid()
    {
        await using var fixture = await PasswordFixture.CreateAsync();

        var subject = await fixture.CreateReadyOperatorAsync(
            "operator.password-step-up",
            OldPassword,
            MemOperatorRoles.Operator);
        var sessionId = Guid.NewGuid();
        var principal = fixture.CreateOperatorPrincipal(subject, sessionId, MemOperatorRoles.Operator);
        var initialStamp = (await fixture.GetAccountSnapshotAsync(subject)).SecurityStamp;

        var withoutStepUp = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/password",
            new ChangeMemOperatorPasswordRequest(NewPassword, NewPassword),
            principal);

        Assert.Equal(HttpStatusCode.Forbidden, withoutStepUp.StatusCode);
        Assert.Equal("step_up_required", ReadStatus(withoutStepUp.Body));
        Assert.True(await fixture.CheckPasswordAsync(subject, OldPassword));
        Assert.False(await fixture.CheckPasswordAsync(subject, NewPassword));
        Assert.Equal(initialStamp, (await fixture.GetAccountSnapshotAsync(subject)).SecurityStamp);

        await fixture.IssueStepUpAsync(subject, sessionId);
        await fixture.ExpireStepUpAsync(subject.Id, sessionId);

        var expiredStepUp = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/password",
            new ChangeMemOperatorPasswordRequest(NewPassword, NewPassword),
            principal);

        Assert.Equal(HttpStatusCode.Forbidden, expiredStepUp.StatusCode);
        Assert.Equal("step_up_required", ReadStatus(expiredStepUp.Body));
        Assert.True(await fixture.CheckPasswordAsync(subject, OldPassword));
        Assert.False(await fixture.CheckPasswordAsync(subject, NewPassword));
        Assert.Equal(initialStamp, (await fixture.GetAccountSnapshotAsync(subject)).SecurityStamp);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task OPERATOR_ACCOUNT_SECURITY_PASSWORD_CORR_01A_disabled_or_incomplete_operator_cannot_rotate_password(
        bool isEnabled,
        bool isBootstrapProvisioning)
    {
        await using var fixture = await PasswordFixture.CreateAsync();

        var subject = await fixture.CreateReadyOperatorAsync(
            isEnabled ? "operator.password-incomplete" : "operator.password-disabled",
            OldPassword,
            MemOperatorRoles.Operator);
        var sessionId = Guid.NewGuid();
        var principal = fixture.CreateOperatorPrincipal(subject, sessionId, MemOperatorRoles.Operator);

        await fixture.IssueStepUpAsync(subject, sessionId);
        await fixture.SetAccountAvailabilityAsync(subject, isEnabled, isBootstrapProvisioning);

        var changed = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/password",
            new ChangeMemOperatorPasswordRequest(NewPassword, NewPassword),
            principal);

        Assert.Equal(HttpStatusCode.Forbidden, changed.StatusCode);
        Assert.Equal("step_up_required", ReadStatus(changed.Body));
        Assert.True(await fixture.CheckPasswordAsync(subject, OldPassword));
        Assert.False(await fixture.CheckPasswordAsync(subject, NewPassword));
    }

    [Fact]
    public async Task OPERATOR_ACCOUNT_SECURITY_PASSWORD_CORR_01A_rejects_mismatch_weak_and_reused_passwords_without_consuming_a_valid_retry()
    {
        await using var fixture = await PasswordFixture.CreateAsync();

        var subject = await fixture.CreateReadyOperatorAsync(
            "auditor.password-validation",
            OldPassword,
            MemOperatorRoles.Auditor);
        var sessionId = Guid.NewGuid();
        var principal = fixture.CreateOperatorPrincipal(subject, sessionId, MemOperatorRoles.Auditor);
        var initialStamp = (await fixture.GetAccountSnapshotAsync(subject)).SecurityStamp;

        await fixture.IssueStepUpAsync(subject, sessionId);

        var mismatch = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/password",
            new ChangeMemOperatorPasswordRequest(NewPassword, "Different!Foundation789"),
            principal);

        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Equal("password_confirmation_mismatch", ReadStatus(mismatch.Body));
        AssertBrowserAuthenticationCookiesNotExpired(mismatch.Headers);
        Assert.True(await fixture.CheckPasswordAsync(subject, OldPassword));
        Assert.Equal(initialStamp, (await fixture.GetAccountSnapshotAsync(subject)).SecurityStamp);

        var weak = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/password",
            new ChangeMemOperatorPasswordRequest("weak", "weak"),
            principal);

        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        Assert.Equal("password_not_accepted", ReadStatus(weak.Body));
        AssertBrowserAuthenticationCookiesNotExpired(weak.Headers);
        Assert.True(await fixture.CheckPasswordAsync(subject, OldPassword));
        Assert.Equal(initialStamp, (await fixture.GetAccountSnapshotAsync(subject)).SecurityStamp);

        var reused = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/password",
            new ChangeMemOperatorPasswordRequest(OldPassword, OldPassword),
            principal);

        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        Assert.Equal("new_password_must_differ", ReadStatus(reused.Body));
        AssertBrowserAuthenticationCookiesNotExpired(reused.Headers);
        Assert.True(await fixture.CheckPasswordAsync(subject, OldPassword));
        Assert.Equal(initialStamp, (await fixture.GetAccountSnapshotAsync(subject)).SecurityStamp);

        var retry = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/password",
            new ChangeMemOperatorPasswordRequest(NewPassword, NewPassword),
            principal);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.False(await fixture.CheckPasswordAsync(subject, OldPassword));
        Assert.True(await fixture.CheckPasswordAsync(subject, NewPassword));
    }

    private static string? ReadStatus(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("status").GetString();
    }

    private static void AssertBrowserAuthenticationCookiesExpired(
        IReadOnlyDictionary<string, string> headers)
    {
        Assert.True(headers.TryGetValue("Set-Cookie", out var setCookie));
        Assert.Contains("mem_operator_auth=;", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mem_operator_mfa_pending=;", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mem_operator_mfa_remember=;", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertBrowserAuthenticationCookiesNotExpired(
        IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("Set-Cookie", out var setCookie))
        {
            return;
        }

        Assert.DoesNotContain("mem_operator_auth=;", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mem_operator_mfa_pending=;", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mem_operator_mfa_remember=;", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSensitiveNoStore(IReadOnlyDictionary<string, string> headers)
    {
        Assert.True(headers.TryGetValue("Cache-Control", out var cacheControl));
        Assert.Contains("no-store", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.True(headers.TryGetValue("Pragma", out var pragma));
        Assert.Contains("no-cache", pragma, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions JsonDefaults =
        new(JsonSerializerDefaults.Web);

    private sealed class PasswordFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly IReadOnlyDictionary<string, RouteEndpoint> _routes;

        private PasswordFixture(
            string databasePath,
            WebApplication application,
            IReadOnlyDictionary<string, RouteEndpoint> routes)
        {
            _databasePath = databasePath;
            Application = application;
            _routes = routes;
        }

        public WebApplication Application { get; }

        public static async Task<PasswordFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-password-change-{Guid.NewGuid():N}.db");

            var builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
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
                    ["MemOperatorIdentity:LoginRateLimitPermitLimit"] = "50",
                    ["MemOperatorIdentity:LoginRateLimitWindowSeconds"] = "60",
                    ["MemOperatorIdentity:StepUpGrantMinutes"] = "10",
                    ["MemOperatorIdentity:SecurityStampValidationSeconds"] = "0"
                });

            builder.Services.AddDataProtection();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddDbContext<MemDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));
            builder.Services.AddMemOperatorIdentity(
                builder.Configuration,
                builder.Environment);
            builder.Services.AddInstallerAuth(builder.Configuration);

            var application = builder.Build();
            new MemOperatorAuthEndpoints().AddRoutes(application);

            await using (var scope = application.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            var routes = ((IEndpointRouteBuilder)application).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Where(endpoint => endpoint.RoutePattern.RawText is
                    "/api/auth/step-up" or
                    "/api/auth/password")
                .ToDictionary(
                    endpoint => endpoint.RoutePattern.RawText!,
                    StringComparer.Ordinal);

            Assert.Equal(2, routes.Count);

            var passwordEndpoint = routes["/api/auth/password"];
            Assert.Contains(
                passwordEndpoint.Metadata.OfType<IAuthorizeData>(),
                entry => entry.Policy == MemOperatorPolicies.ReadSafeStatus);

            return new PasswordFixture(databasePath, application, routes);
        }

        public async Task<MemOperator> CreateReadyOperatorAsync(
            string username,
            string password,
            string role)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            if (!await roleManager.RoleExistsAsync(role))
            {
                var roleCreated = await roleManager.CreateAsync(
                    new IdentityRole<Guid>(role) { Id = Guid.NewGuid() });
                Assert.True(roleCreated.Succeeded);
            }

            var user = new MemOperator
            {
                Id = Guid.NewGuid(),
                UserName = username,
                Email = $"{username}@example.invalid",
                IsEnabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            var created = await userManager.CreateAsync(user, password);
            Assert.True(
                created.Succeeded,
                string.Join("; ", created.Errors.Select(error => error.Description)));

            var resetKey = await userManager.ResetAuthenticatorKeyAsync(user);
            Assert.True(resetKey.Succeeded);

            var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);
            Assert.True(enabled.Succeeded);

            var roleAdded = await userManager.AddToRoleAsync(user, role);
            Assert.True(roleAdded.Succeeded);

            return user;
        }

        public ClaimsPrincipal CreateOperatorPrincipal(
            MemOperator user,
            Guid sessionId,
            string role)
        {
            return new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
                        new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
                        new Claim(ClaimTypes.Role, role),
                        new Claim(MemOperatorSessionClaims.SessionIdClaimType, sessionId.ToString("D"))
                    ],
                    IdentityConstants.ApplicationScheme));
        }

        public async Task IssueStepUpAsync(MemOperator user, Guid sessionId)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var stepUp = scope.ServiceProvider.GetRequiredService<IMemOperatorStepUpService>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(persisted);
            await stepUp.IssueForSessionAsync(persisted!, sessionId, "password-change-test");
        }

        public async Task SetAccountAvailabilityAsync(
            MemOperator user,
            bool isEnabled,
            bool isBootstrapProvisioning)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(persisted);

            persisted!.IsEnabled = isEnabled;
            persisted.IsBootstrapProvisioning = isBootstrapProvisioning;
            var updated = await userManager.UpdateAsync(persisted);
            Assert.True(
                updated.Succeeded,
                string.Join("; ", updated.Errors.Select(error => error.Description)));
        }

        public async Task ExpireStepUpAsync(Guid operatorId, Guid sessionId)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
            var grant = await db.MemOperatorStepUpGrants.SingleAsync(entry =>
                entry.OperatorId == operatorId && entry.SessionId == sessionId);
            grant.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        public async Task<string> GetCurrentTotpAsync(MemOperator user)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(persisted);
            var key = await userManager.GetAuthenticatorKeyAsync(persisted!);
            Assert.False(string.IsNullOrWhiteSpace(key));
            return CreateTotp(key!, DateTimeOffset.UtcNow);
        }

        public async Task<string[]> GenerateRecoveryCodesAsync(MemOperator user, int count)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(persisted);
            var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(persisted!, count);
            return codes.ToArray();
        }

        public async Task<bool> IsRecoveryCodeStillUsableAsync(MemOperator user, string recoveryCode)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(persisted);

            // Redeem proves the existing code survived password rotation. The
            // test owns this disposable account, so consuming the code here is
            // safe and avoids ever exposing its persisted representation.
            var redeemed = await userManager.RedeemTwoFactorRecoveryCodeAsync(persisted!, recoveryCode);
            return redeemed.Succeeded;
        }

        public async Task<bool> CheckPasswordAsync(MemOperator user, string password)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(persisted);
            return await userManager.CheckPasswordAsync(persisted!, password);
        }

        public async Task<AccountSnapshot> GetAccountSnapshotAsync(MemOperator user)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(persisted);

            return new AccountSnapshot(
                persisted!.SecurityStamp ?? string.Empty,
                await userManager.GetAuthenticatorKeyAsync(persisted),
                await userManager.CountRecoveryCodesAsync(persisted),
                (await userManager.GetRolesAsync(persisted)).OrderBy(role => role, StringComparer.Ordinal).ToArray(),
                persisted.UserName,
                persisted.Email,
                persisted.TwoFactorEnabled);
        }

        public async Task<CliSessionFixture> CreateCliDeviceSessionAsync(MemOperator user)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(persisted);
            Assert.False(string.IsNullOrWhiteSpace(persisted!.SecurityStamp));

            var rawCredential = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var installationId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            await db.MemCliDeviceSessions.AddAsync(new MemCliDeviceSessionEntity
            {
                Id = sessionId,
                AuthorizationAttemptId = Guid.NewGuid(),
                InstallationId = installationId,
                OperatorId = persisted.Id,
                CredentialHash = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(rawCredential))),
                SecurityStamp = persisted.SecurityStamp!,
                DeviceLabel = "Password rotation test CLI",
                CreatedAtUtc = now,
                LastSeenAtUtc = now,
                IdleExpiresAtUtc = now.AddHours(1),
                AbsoluteExpiresAtUtc = now.AddDays(1)
            });
            await db.SaveChangesAsync();

            return new CliSessionFixture(sessionId, installationId, rawCredential);
        }

        public async Task<MemCliDeviceSessionValidationResult> ValidateCliDeviceSessionAsync(
            Guid installationId,
            string rawCredential)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IMemCliDeviceAuthorizationService>();
            return await service.ValidateAsync(
                installationId,
                rawCredential,
                "password-change-cli-validation");
        }

        public async Task<ResponseSnapshot> PostJsonAsOperatorAsync(
            string route,
            object payload,
            ClaimsPrincipal principal)
        {
            Assert.True(_routes.TryGetValue(route, out var endpoint));
            Assert.NotNull(endpoint);

            await using var scope = Application.Services.CreateAsyncScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = principal
            };

            context.TraceIdentifier = $"password-change-{Guid.NewGuid():N}";
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = route;
            context.Request.ContentType = "application/json";

            var requestBody = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(payload, JsonDefaults));

            context.Request.ContentLength = requestBody.Length;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(
                new CanHaveBodyRequestFeature());
            context.Request.Body = new MemoryStream(requestBody);
            context.Response.Body = new MemoryStream();

            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = context;

            try
            {
                await endpoint!.RequestDelegate(context);
            }
            finally
            {
                accessor.HttpContext = null;
            }

            context.Response.Body.Position = 0;
            using var reader = new StreamReader(
                context.Response.Body,
                Encoding.UTF8,
                leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            var headers = context.Response.Headers.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);

            return new ResponseSnapshot(
                (HttpStatusCode)context.Response.StatusCode,
                body,
                headers);
        }

        public async ValueTask DisposeAsync()
        {
            await Application.DisposeAsync();

            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
    }

    private sealed record AccountSnapshot(
        string SecurityStamp,
        string? AuthenticatorKey,
        int RecoveryCodeCount,
        IReadOnlyList<string> Roles,
        string? Username,
        string? Email,
        bool TwoFactorEnabled);

    private sealed record CliSessionFixture(
        Guid SessionId,
        Guid InstallationId,
        string RawCredential);

    private sealed record ResponseSnapshot(
        HttpStatusCode StatusCode,
        string Body,
        IReadOnlyDictionary<string, string> Headers);

    private sealed class CanHaveBodyRequestFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private static string CreateTotp(string base32Key, DateTimeOffset now)
    {
        var key = DecodeBase32(base32Key);
        var counter = now.ToUnixTimeSeconds() / 30;
        Span<byte> counterBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes.ToArray());
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var normalized = value.Trim().TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>();
        var buffer = 0;
        var bits = 0;

        foreach (var character in normalized)
        {
            var index = alphabet.IndexOf(character, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException("The test authenticator key was not valid Base32.");
            }

            buffer = (buffer << 5) | index;
            bits += 5;

            while (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return output.ToArray();
    }
}
