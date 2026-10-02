using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth;
using Modules.Auth.Contracts;
using Modules.Auth.Endpoints;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Security;

public sealed class MemOperatorStepUpFoundationTests
{
    [Fact]
    public async Task SEC_AUTH_06A_requires_password_and_current_totp_then_binds_the_grant_to_one_session_and_security_stamp()
    {
        await using var fixture = await StepUpFixture.CreateAsync();

        var subject = await fixture.CreateReadyOperatorAsync(
            username: "owner.stepup",
            password: "Secure!Foundation123");
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var principalA = fixture.CreateOperatorPrincipal(subject, sessionA);
        var principalB = fixture.CreateOperatorPrincipal(subject, sessionB);
        var currentTotp = await fixture.GetCurrentTotpAsync(subject);

        Assert.False(await fixture.AuthorizeRecentStepUpAsync(principalA));

        var rejected = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/step-up",
            new VerifyMemOperatorStepUpRequest("wrong-password", currentTotp),
            principalA);

        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        AssertSensitiveNoStore(rejected.Headers);
        Assert.DoesNotContain("wrong-password", rejected.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(currentTotp, rejected.Body, StringComparison.Ordinal);

        var rejectedPayload = JsonSerializer.Deserialize<MemOperatorStepUpResponse>(
            rejected.Body,
            JsonDefaults);

        Assert.NotNull(rejectedPayload);
        Assert.Equal("step_up_failed", rejectedPayload!.Status);
        Assert.Null(rejectedPayload.ExpiresAtUtc);

        var completed = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/step-up",
            new VerifyMemOperatorStepUpRequest("Secure!Foundation123", currentTotp),
            principalA);

        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        AssertSensitiveNoStore(completed.Headers);
        Assert.DoesNotContain("Secure!Foundation123", completed.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(currentTotp, completed.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionA.ToString("D"), completed.Body, StringComparison.Ordinal);

        var completedPayload = JsonSerializer.Deserialize<MemOperatorStepUpResponse>(
            completed.Body,
            JsonDefaults);

        Assert.NotNull(completedPayload);
        Assert.Equal("step_up_authenticated", completedPayload!.Status);
        Assert.NotNull(completedPayload.ExpiresAtUtc);
        Assert.True(completedPayload.ExpiresAtUtc > DateTimeOffset.UtcNow);

        Assert.True(await fixture.AuthorizeRecentStepUpAsync(principalA));
        Assert.False(await fixture.AuthorizeRecentStepUpAsync(principalB));

        await fixture.UpdateSecurityStampAsync(subject);

        // The same browser session no longer qualifies after a security-stamp
        // invalidation. The server-side grant is revoked when it is checked.
        Assert.False(await fixture.AuthorizeRecentStepUpAsync(principalA));

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var persisted = await userManager.FindByIdAsync(subject.Id.ToString("D"));
        var grant = await db.MemOperatorStepUpGrants.SingleAsync(entry =>
            entry.OperatorId == subject.Id && entry.SessionId == sessionA);
        var events = await db.MemOperatorAuditEvents
            .Where(entry => entry.SubjectOperatorId == subject.Id)
            .ToArrayAsync();

        Assert.NotNull(persisted);
        Assert.NotNull(persisted!.LastStepUpAtUtc);
        Assert.NotNull(grant.RevokedAtUtc);
        Assert.Equal("security_stamp_changed", grant.RevokedReasonCode);
        Assert.Contains(events, entry =>
            entry.EventType == "identity.step-up.failed" &&
            entry.Outcome == "failed" &&
            entry.ReasonCode == "invalid_credentials");
        Assert.Contains(events, entry =>
            entry.EventType == "identity.step-up.succeeded" &&
            entry.Outcome == "succeeded" &&
            entry.ReasonCode == "password_and_totp");
        Assert.Contains(events, entry =>
            entry.EventType == "identity.step-up.revoked" &&
            entry.Outcome == "revoked" &&
            entry.ReasonCode == "security_stamp_changed");

        var auditJson = JsonSerializer.Serialize(events, JsonDefaults);
        Assert.DoesNotContain("Secure!Foundation123", auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(currentTotp, auditJson, StringComparison.Ordinal);
        Assert.DoesNotContain(sessionA.ToString("D"), auditJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SEC_AUTH_05A_regenerates_recovery_codes_only_after_recent_step_up_and_audits_without_secret_material()
    {
        await using var fixture = await StepUpFixture.CreateAsync();

        var subject = await fixture.CreateReadyOperatorAsync(
            username: "owner.recovery-regeneration",
            password: "Secure!Foundation123");
        var sessionId = Guid.NewGuid();
        var principal = fixture.CreateOperatorPrincipal(subject, sessionId);
        var priorRecoveryCode = await fixture.GenerateRecoveryCodeAsync(subject);

        var denied = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/recovery-codes/regenerate",
            new { },
            principal);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        AssertSensitiveNoStore(denied.Headers);

        using (var deniedDocument = JsonDocument.Parse(denied.Body))
        {
            Assert.Equal(
                "step_up_required",
                deniedDocument.RootElement.GetProperty("status").GetString());
        }

        var currentTotp = await fixture.GetCurrentTotpAsync(subject);
        var stepUp = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/step-up",
            new VerifyMemOperatorStepUpRequest("Secure!Foundation123", currentTotp),
            principal);

        Assert.Equal(HttpStatusCode.OK, stepUp.StatusCode);

        var regenerated = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/recovery-codes/regenerate",
            new { },
            principal);

        Assert.Equal(HttpStatusCode.OK, regenerated.StatusCode);
        AssertSensitiveNoStore(regenerated.Headers);
        Assert.DoesNotContain(priorRecoveryCode, regenerated.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Secure!Foundation123",
            regenerated.Body,
            StringComparison.Ordinal);

        var response = JsonSerializer.Deserialize<MemOperatorRecoveryCodesResponse>(
            regenerated.Body,
            JsonDefaults);

        Assert.NotNull(response);
        Assert.Equal("recovery_codes_regenerated", response!.Status);
        Assert.Equal(10, response.RecoveryCodes.Count);
        Assert.All(response.RecoveryCodes, code => Assert.False(string.IsNullOrWhiteSpace(code)));
        Assert.DoesNotContain(priorRecoveryCode, response.RecoveryCodes, StringComparer.Ordinal);

        // Regeneration invalidates every prior unused recovery code immediately.
        Assert.False(await fixture.RedeemRecoveryCodeAsync(subject, priorRecoveryCode));
        Assert.True(await fixture.RedeemRecoveryCodeAsync(subject, response.RecoveryCodes[0]));

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
        var events = await db.MemOperatorAuditEvents
            .Where(entry => entry.SubjectOperatorId == subject.Id)
            .ToArrayAsync();

        Assert.Contains(events, entry =>
            entry.EventType == "identity.mfa.recovery-codes.regenerated" &&
            entry.Outcome == "succeeded" &&
            entry.ReasonCode == "self_service_step_up");

        var auditJson = JsonSerializer.Serialize(events, JsonDefaults);
        Assert.DoesNotContain(priorRecoveryCode, auditJson, StringComparison.Ordinal);
        Assert.All(
            response.RecoveryCodes,
            code => Assert.DoesNotContain(code, auditJson, StringComparison.Ordinal));
    }

    [Fact]
    public async Task SEC_AUTH_06A_does_not_accept_or_consume_a_recovery_code_as_step_up_authentication()
    {
        await using var fixture = await StepUpFixture.CreateAsync();

        var subject = await fixture.CreateReadyOperatorAsync(
            username: "owner.recovery-not-stepup",
            password: "Secure!Foundation123");
        var sessionId = Guid.NewGuid();
        var principal = fixture.CreateOperatorPrincipal(subject, sessionId);
        var recoveryCode = await fixture.GenerateRecoveryCodeAsync(subject);

        var rejected = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/step-up",
            new VerifyMemOperatorStepUpRequest("Secure!Foundation123", recoveryCode),
            principal);

        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        AssertSensitiveNoStore(rejected.Headers);

        var rejectedPayload = JsonSerializer.Deserialize<MemOperatorStepUpResponse>(
            rejected.Body,
            JsonDefaults);

        Assert.NotNull(rejectedPayload);
        Assert.Equal("step_up_failed", rejectedPayload!.Status);

        // A recovery code that step-up refused must remain untouched. Only the
        // existing ordinary MFA-completion route is allowed to redeem it.
        Assert.True(await fixture.RedeemRecoveryCodeAsync(subject, recoveryCode));
    }

    [Fact]
    public async Task SEC_AUTH_06A_revokes_the_server_grant_when_a_platform_owner_changes_the_operator_role()
    {
        await using var fixture = await StepUpFixture.CreateAsync();

        await fixture.EnsureRolesAsync(
            MemOperatorRoles.PlatformOwner,
            MemOperatorRoles.Operator,
            MemOperatorRoles.Auditor);

        var owner = await fixture.CreateReadyOperatorAsync(
            username: "owner.role-change",
            password: "Secure!Foundation123");
        await fixture.AssignRolesAsync(owner, MemOperatorRoles.PlatformOwner);

        var subject = await fixture.CreateReadyOperatorAsync(
            username: "operator.role-change",
            password: "Secure!Foundation123");
        await fixture.AssignRolesAsync(subject, MemOperatorRoles.Operator);

        var sessionId = Guid.NewGuid();
        var principal = fixture.CreateOperatorPrincipal(subject, sessionId);
        var code = await fixture.GetCurrentTotpAsync(subject);

        var stepUp = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/step-up",
            new VerifyMemOperatorStepUpRequest("Secure!Foundation123", code),
            principal);

        Assert.Equal(HttpStatusCode.OK, stepUp.StatusCode);
        Assert.True(await fixture.AuthorizeRecentStepUpAsync(principal));

        await using (var scope = fixture.Application.Services.CreateAsyncScope())
        {
            var lifecycle = scope.ServiceProvider.GetRequiredService<IMemOperatorLifecycleService>();

            await lifecycle.SetRolesAsync(
                owner.Id,
                subject.Id,
                new SetMemOperatorRolesRequest([MemOperatorRoles.Auditor]),
                correlationId: "step-up-role-change");
        }

        Assert.False(await fixture.AuthorizeRecentStepUpAsync(principal));

        await using var verifyScope = fixture.Application.Services.CreateAsyncScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<MemDbContext>();
        var grant = await db.MemOperatorStepUpGrants.SingleAsync(entry =>
            entry.OperatorId == subject.Id && entry.SessionId == sessionId);

        Assert.NotNull(grant.RevokedAtUtc);
        Assert.Equal("role_membership_changed", grant.RevokedReasonCode);
    }

    [Fact]
    public async Task SEC_AUTH_06A_expires_and_logout_revokes_the_current_session_grant()
    {
        await using var fixture = await StepUpFixture.CreateAsync();

        var subject = await fixture.CreateReadyOperatorAsync(
            username: "owner.expiry",
            password: "Secure!Foundation123");
        var sessionId = Guid.NewGuid();
        var principal = fixture.CreateOperatorPrincipal(subject, sessionId);

        var firstCode = await fixture.GetCurrentTotpAsync(subject);
        var firstStepUp = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/step-up",
            new VerifyMemOperatorStepUpRequest("Secure!Foundation123", firstCode),
            principal);

        Assert.Equal(HttpStatusCode.OK, firstStepUp.StatusCode);
        Assert.True(await fixture.AuthorizeRecentStepUpAsync(principal));

        await fixture.ExpireGrantAsync(subject.Id, sessionId);

        Assert.False(await fixture.AuthorizeRecentStepUpAsync(principal));

        // Re-authentication creates a fresh current grant after an expired
        // grant. The same protected browser session is then explicitly
        // invalidated when it signs out.
        var nextCode = await fixture.GetCurrentTotpAsync(subject);
        var secondStepUp = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/step-up",
            new VerifyMemOperatorStepUpRequest("Secure!Foundation123", nextCode),
            principal);

        Assert.Equal(HttpStatusCode.OK, secondStepUp.StatusCode);
        Assert.True(await fixture.AuthorizeRecentStepUpAsync(principal));

        var logout = await fixture.PostJsonAsOperatorAsync(
            "/api/auth/logout",
            new { },
            principal);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        AssertSensitiveNoStore(logout.Headers);
        Assert.False(await fixture.AuthorizeRecentStepUpAsync(principal));

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
        var grant = await db.MemOperatorStepUpGrants.SingleAsync(entry =>
            entry.OperatorId == subject.Id && entry.SessionId == sessionId);
        var events = await db.MemOperatorAuditEvents
            .Where(entry => entry.SubjectOperatorId == subject.Id)
            .ToArrayAsync();

        Assert.NotNull(grant.RevokedAtUtc);
        Assert.Equal("logout", grant.RevokedReasonCode);
        Assert.Contains(events, entry =>
            entry.EventType == "identity.step-up.expired" &&
            entry.Outcome == "expired" &&
            entry.ReasonCode == "expired");
        Assert.Contains(events, entry =>
            entry.EventType == "identity.step-up.revoked" &&
            entry.Outcome == "revoked" &&
            entry.ReasonCode == "logout");
    }

    [Fact]
    public void SEC_AUTH_06A_protects_a_non_secret_session_identifier_inside_the_identity_ticket()
    {
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))],
                IdentityConstants.ApplicationScheme));
        var properties = new AuthenticationProperties();

        var sessionId = MemOperatorSessionClaims.EnsureSessionId(principal, properties);

        Assert.True(MemOperatorSessionClaims.TryGetSessionId(principal, out var persistedSessionId));
        Assert.Equal(sessionId, persistedSessionId);
        Assert.Equal(sessionId.ToString("D"), properties.Items["mem.operator.session_id"]);
        Assert.Single(principal.FindAll(MemOperatorSessionClaims.SessionIdClaimType));
    }

    private static void AssertSensitiveNoStore(
        IReadOnlyDictionary<string, string> headers)
    {
        Assert.True(headers.TryGetValue("Cache-Control", out var cacheControl));
        Assert.Contains("no-store", cacheControl, StringComparison.OrdinalIgnoreCase);
        Assert.True(headers.TryGetValue("Pragma", out var pragma));
        Assert.Contains("no-cache", pragma, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions JsonDefaults =
        new(JsonSerializerDefaults.Web);

    private sealed class StepUpFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly IReadOnlyDictionary<string, RouteEndpoint> _routes;

        private StepUpFixture(
            string databasePath,
            WebApplication application,
            IReadOnlyDictionary<string, RouteEndpoint> routes)
        {
            _databasePath = databasePath;
            Application = application;
            _routes = routes;
        }

        public WebApplication Application { get; }

        public static async Task<StepUpFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-sec-auth-06a-{Guid.NewGuid():N}.db");

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
                    "/api/auth/logout" or
                    "/api/auth/recovery-codes/regenerate")
                .ToDictionary(
                    endpoint => endpoint.RoutePattern.RawText!,
                    StringComparer.Ordinal);

            Assert.Equal(3, routes.Count);

            var stepUpEndpoint = routes["/api/auth/step-up"];
            Assert.Contains(
                stepUpEndpoint.Metadata.OfType<IAuthorizeData>(),
                entry => entry.Policy == MemOperatorPolicies.ReadSafeStatus);

            var recoveryCodeEndpoint = routes["/api/auth/recovery-codes/regenerate"];
            Assert.Contains(
                recoveryCodeEndpoint.Metadata.OfType<IAuthorizeData>(),
                entry => entry.Policy == MemOperatorPolicies.ReadSafeStatus);

            return new StepUpFixture(databasePath, application, routes);
        }

        public async Task<MemOperator> CreateReadyOperatorAsync(
            string username,
            string password)
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

            var resetKey = await userManager.ResetAuthenticatorKeyAsync(user);
            Assert.True(
                resetKey.Succeeded,
                string.Join("; ", resetKey.Errors.Select(error => error.Description)));

            var enabled = await userManager.SetTwoFactorEnabledAsync(user, true);
            Assert.True(
                enabled.Succeeded,
                string.Join("; ", enabled.Errors.Select(error => error.Description)));

            return user;
        }

        public ClaimsPrincipal CreateOperatorPrincipal(
            MemOperator user,
            Guid sessionId)
        {
            return new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
                        new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
                        new Claim(MemOperatorSessionClaims.SessionIdClaimType, sessionId.ToString("D"))
                    ],
                    IdentityConstants.ApplicationScheme));
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

        public async Task<string> GenerateRecoveryCodeAsync(MemOperator user)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));

            Assert.NotNull(persisted);

            var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                persisted!,
                number: 1);

            return Assert.Single(codes);
        }

        public async Task<bool> RedeemRecoveryCodeAsync(MemOperator user, string recoveryCode)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));

            Assert.NotNull(persisted);

            var result = await userManager.RedeemTwoFactorRecoveryCodeAsync(
                persisted!,
                recoveryCode);

            return result.Succeeded;
        }

        public async Task EnsureRolesAsync(params string[] roles)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            foreach (var role in roles)
            {
                if (await roleManager.RoleExistsAsync(role))
                {
                    continue;
                }

                var created = await roleManager.CreateAsync(
                    new IdentityRole<Guid>(role)
                    {
                        Id = Guid.NewGuid()
                    });

                Assert.True(
                    created.Succeeded,
                    string.Join("; ", created.Errors.Select(error => error.Description)));
            }
        }

        public async Task AssignRolesAsync(MemOperator user, params string[] roles)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));

            Assert.NotNull(persisted);

            var added = await userManager.AddToRolesAsync(persisted!, roles);

            Assert.True(
                added.Succeeded,
                string.Join("; ", added.Errors.Select(error => error.Description)));
        }

        public async Task UpdateSecurityStampAsync(MemOperator user)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
            var persisted = await userManager.FindByIdAsync(user.Id.ToString("D"));

            Assert.NotNull(persisted);

            var updated = await userManager.UpdateSecurityStampAsync(persisted!);
            Assert.True(
                updated.Succeeded,
                string.Join("; ", updated.Errors.Select(error => error.Description)));
        }

        public async Task ExpireGrantAsync(Guid operatorId, Guid sessionId)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
            var grant = await db.MemOperatorStepUpGrants.SingleAsync(entry =>
                entry.OperatorId == operatorId && entry.SessionId == sessionId);

            grant.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
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

            context.TraceIdentifier = $"sec-auth-06a-{Guid.NewGuid():N}";
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
