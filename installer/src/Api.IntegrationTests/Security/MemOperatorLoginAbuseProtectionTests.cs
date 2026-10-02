using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using System.Text;
using System.Text.Json;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Auth;
using Modules.Auth.Contracts;
using Modules.Auth.Endpoints;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Security;

public sealed class MemOperatorLoginAbuseProtectionTests
{
    [Fact]
    public async Task SEC_AUTH_04D_returns_the_same_generic_failure_for_unknown_and_wrong_password_then_throttles()
    {
        await using var fixture = await LoginFixture.CreateAsync(permitLimit: 5);

        await fixture.CreateReadyOperatorAsync(
            username: "owner.alpha",
            password: "Secure!Foundation123",
            requireTotp: false);

        var unknown = await fixture.PostJsonAsync(
            "/api/auth/login",
            new MemOperatorLoginRequest("unknown.operator", "wrong-password"));

        var wrongPassword = await fixture.PostJsonAsync(
            "/api/auth/login",
            new MemOperatorLoginRequest("owner.alpha", "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(unknown.Body, wrongPassword.Body);

        for (var attempt = 0; attempt < 3; attempt += 1)
        {
            var additionalUnknown = await fixture.PostJsonAsync(
                "/api/auth/login",
                new MemOperatorLoginRequest($"unknown.operator.{attempt}", "wrong-password"));

            Assert.Equal(HttpStatusCode.Unauthorized, additionalUnknown.StatusCode);
        }

        var throttled = await fixture.PostJsonAsync(
            "/api/auth/login",
            new MemOperatorLoginRequest("owner.alpha", "wrong-password"));

        Assert.Equal(HttpStatusCode.TooManyRequests, throttled.StatusCode);
        AssertSensitiveNoStore(throttled.Headers);
        Assert.True(throttled.Headers.ContainsKey("Retry-After"));

        var payload = JsonSerializer.Deserialize<MemOperatorLoginResponse>(
            throttled.Body,
            JsonDefaults);

        Assert.NotNull(payload);
        Assert.Equal("rate_limited", payload!.Status);
        Assert.Null(payload.Session);
    }

    [Fact]
    public async Task SEC_AUTH_04D_locks_a_real_operator_after_password_failures_and_records_the_transition()
    {
        await using var fixture = await LoginFixture.CreateAsync(permitLimit: 50);

        var user = await fixture.CreateReadyOperatorAsync(
            username: "owner.password",
            password: "Secure!Foundation123",
            requireTotp: false);

        for (var attempt = 0; attempt < 5; attempt += 1)
        {
            var response = await fixture.PostJsonAsync(
                "/api/auth/login",
                new MemOperatorLoginRequest("owner.password", "wrong-password"));

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));

        Assert.NotNull(persisted);
        Assert.True(await userManager.IsLockedOutAsync(persisted!));

        var events = await db.MemOperatorAuditEvents
            .Where(entry => entry.SubjectOperatorId == user.Id)
            .ToArrayAsync();

        Assert.Contains(events, entry =>
            entry.EventType == "identity.login.failed" &&
            entry.Outcome == "locked_out" &&
            entry.ReasonCode == "lockout");

        Assert.Contains(events, entry =>
            entry.EventType == "identity.lockout.triggered" &&
            entry.Outcome == "triggered" &&
            entry.ReasonCode == "credential_failure_threshold");
    }

    [Fact]
    public async Task SEC_AUTH_04D_locks_a_real_operator_after_totp_failures_and_records_the_transition()
    {
        await using var fixture = await LoginFixture.CreateAsync(permitLimit: 50);

        var user = await fixture.CreateReadyOperatorAsync(
            username: "owner.totp",
            password: "Secure!Foundation123",
            requireTotp: true);

        var passwordStage = await fixture.PostJsonAsync(
            "/api/auth/login",
            new MemOperatorLoginRequest("owner.totp", "Secure!Foundation123"));

        Assert.Equal(HttpStatusCode.OK, passwordStage.StatusCode);

        var passwordPayload = JsonSerializer.Deserialize<MemOperatorLoginResponse>(
            passwordStage.Body,
            JsonDefaults);

        Assert.NotNull(passwordPayload);
        Assert.Equal("mfa_required", passwordPayload!.Status);

        var pendingCookie = ReadCookie(
            passwordStage.Headers,
            "mem_operator_mfa_pending");

        for (var attempt = 0; attempt < 5; attempt += 1)
        {
            var response = await fixture.PostJsonAsync(
                "/api/auth/login/totp",
                new VerifyMemOperatorTotpRequest("0000000000"),
                pendingCookie);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));

        Assert.NotNull(persisted);
        Assert.True(await userManager.IsLockedOutAsync(persisted!));

        var events = await db.MemOperatorAuditEvents
            .Where(entry => entry.SubjectOperatorId == user.Id)
            .ToArrayAsync();

        Assert.Contains(events, entry =>
            entry.EventType == "identity.login.failed" &&
            entry.Outcome == "locked_out" &&
            entry.ReasonCode == "lockout");

        Assert.Contains(events, entry =>
            entry.EventType == "identity.lockout.triggered" &&
            entry.Outcome == "triggered" &&
            entry.ReasonCode == "credential_failure_threshold");
    }

    [Fact]
    public async Task SEC_AUTH_05A_completes_only_the_current_mfa_challenge_with_a_recovery_code_then_rejects_replay_and_audits_safely()
    {
        await using var fixture = await LoginFixture.CreateAsync(permitLimit: 50);

        var user = await fixture.CreateReadyOperatorAsync(
            username: "owner.recovery",
            password: "Secure!Foundation123",
            requireTotp: true);
        var recoveryCodes = await fixture.GenerateRecoveryCodesAsync(user);
        var recoveryCode = recoveryCodes.First();

        var passwordStage = await fixture.PostJsonAsync(
            "/api/auth/login",
            new MemOperatorLoginRequest("owner.recovery", "Secure!Foundation123"));

        Assert.Equal(HttpStatusCode.OK, passwordStage.StatusCode);
        AssertSensitiveNoStore(passwordStage.Headers);
        Assert.Equal("no-cache", passwordStage.Headers["Pragma"]);

        var pendingCookie = ReadCookie(
            passwordStage.Headers,
            "mem_operator_mfa_pending");

        var completed = await fixture.PostJsonAsync(
            "/api/auth/login/recovery-code",
            new VerifyMemOperatorRecoveryCodeRequest(recoveryCode),
            pendingCookie);

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        AssertSensitiveNoStore(completed.Headers);
        Assert.Equal("no-cache", completed.Headers["Pragma"]);
        Assert.False(completed.Body.Contains(recoveryCode, StringComparison.Ordinal));
        Assert.False(
            completed.Headers
                .GetValueOrDefault("Set-Cookie", string.Empty)
                .Contains(recoveryCode, StringComparison.Ordinal));

        var completedPayload = JsonSerializer.Deserialize<MemOperatorLoginResponse>(
            completed.Body,
            JsonDefaults);

        Assert.NotNull(completedPayload);
        Assert.Equal("authenticated", completedPayload!.Status);
        Assert.NotNull(completedPayload.Session);
        Assert.Equal("owner.recovery", completedPayload.Session!.DisplayName);

        var replayPasswordStage = await fixture.PostJsonAsync(
            "/api/auth/login",
            new MemOperatorLoginRequest("owner.recovery", "Secure!Foundation123"));
        var replayPendingCookie = ReadCookie(
            replayPasswordStage.Headers,
            "mem_operator_mfa_pending");

        var replay = await fixture.PostJsonAsync(
            "/api/auth/login/recovery-code",
            new VerifyMemOperatorRecoveryCodeRequest(recoveryCode),
            replayPendingCookie);

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        AssertSensitiveNoStore(replay.Headers);
        Assert.Equal("no-cache", replay.Headers["Pragma"]);
        Assert.False(replay.Body.Contains(recoveryCode, StringComparison.Ordinal));

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var events = await db.MemOperatorAuditEvents
            .Where(entry => entry.SubjectOperatorId == user.Id)
            .ToArrayAsync();

        Assert.Contains(events, entry =>
            entry.EventType == "identity.mfa.recovery-code.used" &&
            entry.Outcome == "succeeded" &&
            entry.ReasonCode == "login_mfa_completion");
        Assert.Contains(events, entry =>
            entry.EventType == "identity.login.succeeded" &&
            entry.Outcome == "succeeded" &&
            entry.ReasonCode == "password_and_recovery_code");
        Assert.Contains(events, entry =>
            entry.EventType == "identity.login.failed" &&
            entry.Outcome == "failed" &&
            entry.ReasonCode == "invalid_recovery_code");
    }

    [Fact]
    public async Task SEC_AUTH_05A_does_not_allow_a_recovery_code_to_bypass_an_existing_account_lockout()
    {
        await using var fixture = await LoginFixture.CreateAsync(permitLimit: 50);

        var user = await fixture.CreateReadyOperatorAsync(
            username: "owner.locked-recovery",
            password: "Secure!Foundation123",
            requireTotp: true);
        var recoveryCode = (await fixture.GenerateRecoveryCodesAsync(user)).First();

        var passwordStage = await fixture.PostJsonAsync(
            "/api/auth/login",
            new MemOperatorLoginRequest("owner.locked-recovery", "Secure!Foundation123"));
        var pendingCookie = ReadCookie(
            passwordStage.Headers,
            "mem_operator_mfa_pending");

        await fixture.LockOperatorAsync(user);

        var lockedAttempt = await fixture.PostJsonAsync(
            "/api/auth/login/recovery-code",
            new VerifyMemOperatorRecoveryCodeRequest(recoveryCode),
            pendingCookie);

        Assert.Equal(HttpStatusCode.Unauthorized, lockedAttempt.StatusCode);
        AssertSensitiveNoStore(lockedAttempt.Headers);
        Assert.Equal("no-cache", lockedAttempt.Headers["Pragma"]);
        Assert.False(lockedAttempt.Body.Contains(recoveryCode, StringComparison.Ordinal));

        await using (var scope = fixture.Application.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
            var events = await db.MemOperatorAuditEvents
                .Where(entry => entry.SubjectOperatorId == user.Id)
                .ToArrayAsync();

            Assert.Contains(events, entry =>
                entry.EventType == "identity.login.failed" &&
                entry.Outcome == "locked_out" &&
                entry.ReasonCode == "account_locked");
        }

        await fixture.UnlockOperatorAsync(user);

        var retryPasswordStage = await fixture.PostJsonAsync(
            "/api/auth/login",
            new MemOperatorLoginRequest("owner.locked-recovery", "Secure!Foundation123"));
        var retryPendingCookie = ReadCookie(
            retryPasswordStage.Headers,
            "mem_operator_mfa_pending");

        var recovered = await fixture.PostJsonAsync(
            "/api/auth/login/recovery-code",
            new VerifyMemOperatorRecoveryCodeRequest(recoveryCode),
            retryPendingCookie);

        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Fact]
    public async Task SEC_AUTH_05A_rejects_a_recovery_code_without_the_server_managed_mfa_pending_cookie()
    {
        await using var fixture = await LoginFixture.CreateAsync(permitLimit: 50);

        var user = await fixture.CreateReadyOperatorAsync(
            username: "owner.no-pending",
            password: "Secure!Foundation123",
            requireTotp: true);
        var recoveryCode = (await fixture.GenerateRecoveryCodesAsync(user)).First();

        var response = await fixture.PostJsonAsync(
            "/api/auth/login/recovery-code",
            new VerifyMemOperatorRecoveryCodeRequest(recoveryCode));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertSensitiveNoStore(response.Headers);
        Assert.Equal("no-cache", response.Headers["Pragma"]);
        Assert.False(response.Body.Contains(recoveryCode, StringComparison.Ordinal));
    }


    [Fact]
    public async Task SEC_AUTH_06A_password_totp_login_issues_recent_step_up_for_the_new_browser_session()
    {
        await using var fixture = await LoginFixture.CreateAsync(permitLimit: 50);

        var user = await fixture.CreateReadyOperatorAsync(
            username: "owner.login-step-up",
            password: "Secure!Foundation123",
            requireTotp: true);

        var passwordStage = await fixture.PostJsonAsync(
            "/api/auth/login",
            new MemOperatorLoginRequest("owner.login-step-up", "Secure!Foundation123"));

        Assert.Equal(HttpStatusCode.OK, passwordStage.StatusCode);

        var pendingCookie = ReadCookie(
            passwordStage.Headers,
            "mem_operator_mfa_pending");
        var currentTotp = await fixture.GetCurrentTotpAsync(user);

        var completed = await fixture.PostJsonAsync(
            "/api/auth/login/totp",
            new VerifyMemOperatorTotpRequest(currentTotp),
            pendingCookie);

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        AssertSensitiveNoStore(completed.Headers);
        Assert.False(completed.Body.Contains(currentTotp, StringComparison.Ordinal));

        var operatorCookie = ReadCookie(
            completed.Headers,
            "mem_operator_auth");
        var principal = await fixture.AuthenticateOperatorCookieAsync(operatorCookie);

        Assert.True(await fixture.AuthorizeRecentStepUpAsync(principal));

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
        var grant = await db.MemOperatorStepUpGrants.SingleAsync(entry =>
            entry.OperatorId == user.Id);
        var events = await db.MemOperatorAuditEvents
            .Where(entry => entry.SubjectOperatorId == user.Id)
            .ToArrayAsync();

        Assert.NotEqual(Guid.Empty, grant.SessionId);
        Assert.Null(grant.RevokedAtUtc);
        Assert.True(grant.ExpiresAtUtc > DateTimeOffset.UtcNow);
        Assert.Contains(events, entry =>
            entry.EventType == "identity.step-up.succeeded" &&
            entry.Outcome == "succeeded" &&
            entry.ReasonCode == "password_and_totp");
    }

    [Fact]
    public async Task SEC_AUTH_06A_recovery_code_login_does_not_issue_recent_step_up()
    {
        await using var fixture = await LoginFixture.CreateAsync(permitLimit: 50);

        var user = await fixture.CreateReadyOperatorAsync(
            username: "owner.recovery-no-step-up",
            password: "Secure!Foundation123",
            requireTotp: true);
        var recoveryCode = (await fixture.GenerateRecoveryCodesAsync(user)).First();

        var passwordStage = await fixture.PostJsonAsync(
            "/api/auth/login",
            new MemOperatorLoginRequest("owner.recovery-no-step-up", "Secure!Foundation123"));

        Assert.Equal(HttpStatusCode.OK, passwordStage.StatusCode);

        var pendingCookie = ReadCookie(
            passwordStage.Headers,
            "mem_operator_mfa_pending");

        var completed = await fixture.PostJsonAsync(
            "/api/auth/login/recovery-code",
            new VerifyMemOperatorRecoveryCodeRequest(recoveryCode),
            pendingCookie);

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        AssertSensitiveNoStore(completed.Headers);
        Assert.False(completed.Body.Contains(recoveryCode, StringComparison.Ordinal));

        var operatorCookie = ReadCookie(
            completed.Headers,
            "mem_operator_auth");
        var principal = await fixture.AuthenticateOperatorCookieAsync(operatorCookie);

        Assert.False(await fixture.AuthorizeRecentStepUpAsync(principal));

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        Assert.False(await db.MemOperatorStepUpGrants.AnyAsync(entry =>
            entry.OperatorId == user.Id));
    }

    private static void AssertSensitiveNoStore(
        IReadOnlyDictionary<string, string> headers)
    {
        Assert.True(headers.TryGetValue("Cache-Control", out var cacheControl));
        Assert.Contains("no-store", cacheControl, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadCookie(
        IReadOnlyDictionary<string, string> headers,
        string cookieName)
    {
        Assert.True(headers.TryGetValue("Set-Cookie", out var setCookie));

        var cookie = setCookie
            .Split(',', StringSplitOptions.TrimEntries)
            .FirstOrDefault(value => value.StartsWith(
                $"{cookieName}=",
                StringComparison.Ordinal));

        Assert.False(string.IsNullOrWhiteSpace(cookie));

        return cookie!.Split(';', 2)[0];
    }

    private static readonly JsonSerializerOptions JsonDefaults =
        new(JsonSerializerDefaults.Web);

    private sealed class LoginFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly IReadOnlyDictionary<string, RouteEndpoint> _routes;

        private LoginFixture(
            string databasePath,
            WebApplication application,
            IReadOnlyDictionary<string, RouteEndpoint> routes)
        {
            _databasePath = databasePath;
            Application = application;
            _routes = routes;
        }

        public WebApplication Application { get; }

        public static async Task<LoginFixture> CreateAsync(int permitLimit)
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-sec-auth-04d-{Guid.NewGuid():N}.db");

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
                    ["MemOperatorIdentity:LoginRateLimitPermitLimit"] = permitLimit.ToString(),
                    ["MemOperatorIdentity:LoginRateLimitWindowSeconds"] = "60",
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
                    "/api/auth/login" or
                    "/api/auth/login/totp" or
                    "/api/auth/login/recovery-code")
                .ToDictionary(
                    endpoint => endpoint.RoutePattern.RawText!,
                    StringComparer.Ordinal);

            Assert.Equal(3, routes.Count);

            return new LoginFixture(databasePath, application, routes);
        }

        public async Task<MemOperator> CreateReadyOperatorAsync(
            string username,
            string password,
            bool requireTotp)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();

            var user = new MemOperator
            {
                Id = Guid.NewGuid(),
                UserName = username,
                IsEnabled = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            var created = await userManager.CreateAsync(user, password);
            Assert.True(
                created.Succeeded,
                string.Join("; ", created.Errors.Select(error => error.Description)));

            if (requireTotp)
            {
                var resetKey = await userManager.ResetAuthenticatorKeyAsync(user);
                Assert.True(
                    resetKey.Succeeded,
                    string.Join("; ", resetKey.Errors.Select(error => error.Description)));

                var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);
                Assert.True(
                    enabled.Succeeded,
                    string.Join("; ", enabled.Errors.Select(error => error.Description)));
            }

            return user;
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

        public async Task<ClaimsPrincipal> AuthenticateOperatorCookieAsync(string cookie)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider
            };

            context.Request.Headers.Cookie = cookie;

            var result = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);

            Assert.True(result.Succeeded);
            Assert.NotNull(result.Principal);

            return result.Principal!;
        }

        public async Task<bool> AuthorizeRecentStepUpAsync(ClaimsPrincipal principal)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();

            var result = await authorization.AuthorizeAsync(
                principal,
                resource: null,
                policyName: MemOperatorPolicies.RecentStepUp);

            return result.Succeeded;
        }

        public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(
            MemOperator user)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));

            Assert.NotNull(persisted);

            var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                persisted!,
                10);

            Assert.NotNull(recoveryCodes);
            Assert.NotEmpty(recoveryCodes!);

            return recoveryCodes!.ToArray();
        }

        public async Task LockOperatorAsync(MemOperator user)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));

            Assert.NotNull(persisted);

            var locked = await userManager.SetLockoutEndDateAsync(
                persisted!,
                DateTimeOffset.UtcNow.AddMinutes(5));

            Assert.True(
                locked.Succeeded,
                string.Join("; ", locked.Errors.Select(error => error.Description)));
        }

        public async Task UnlockOperatorAsync(MemOperator user)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));

            Assert.NotNull(persisted);

            var unlocked = await userManager.SetLockoutEndDateAsync(persisted!, null);

            Assert.True(
                unlocked.Succeeded,
                string.Join("; ", unlocked.Errors.Select(error => error.Description)));
        }

        public async Task<ResponseSnapshot> PostJsonAsync(
            string route,
            object payload,
            string? cookie = null)
        {
            Assert.True(_routes.TryGetValue(route, out var endpoint));
            Assert.NotNull(endpoint);

            await using var scope = Application.Services.CreateAsyncScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider
            };

            context.TraceIdentifier = $"sec-auth-04d-{Guid.NewGuid():N}";
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = route;
            context.Request.ContentType = "application/json";

            var requestBody = Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(payload, JsonDefaults));

            // Generated minimal-API body binding uses the request body's
            // CanHaveBody contract. DefaultHttpContext treats an unspecified
            // content length as bodyless, so declare the in-memory JSON length
            // before invoking the endpoint delegate directly.
            context.Request.ContentLength = requestBody.Length;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(
                new CanHaveBodyRequestFeature());
            context.Request.Body = new MemoryStream(requestBody);
            context.Response.Body = new MemoryStream();

            if (!string.IsNullOrWhiteSpace(cookie))
            {
                context.Request.Headers["Cookie"] = cookie;
            }

            var accessor = scope.ServiceProvider
                .GetRequiredService<IHttpContextAccessor>();
            accessor.HttpContext = context;

            try
            {
                await endpoint.RequestDelegate(context);
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

    private sealed class CanHaveBodyRequestFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed record ResponseSnapshot(
        HttpStatusCode StatusCode,
        string Body,
        IReadOnlyDictionary<string, string> Headers);
}
