using System.Net;
using System.Security.Claims;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modules.Auth;
using Modules.Auth.Contracts;
using Modules.Auth.Endpoints;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Security;

public sealed class MemSecuritySettingsServiceTests
{
    [Fact]
    public async Task SEC_SETTINGS_01A_defaults_to_required_with_recommended_reuse_window_when_no_row_exists()
    {
        await using var fixture = await SettingsFixture.CreateAsync();

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IMemSecuritySettingsService>();

        var snapshot = await settings.GetEffectiveAsync();

        Assert.True(snapshot.RequireHighRiskStepUp);
        Assert.Equal(15, snapshot.HighRiskStepUpGrantMinutes);
        Assert.True(snapshot.IsDefaulted);
        Assert.Null(snapshot.UpdatedAtUtc);
        Assert.Null(snapshot.UpdatedByOperatorId);
    }

    [Fact]
    public async Task SEC_SETTINGS_01A_persists_policy_window_and_secret_free_audit_event()
    {
        await using var fixture = await SettingsFixture.CreateAsync();
        var actorId = Guid.NewGuid();

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<IMemSecuritySettingsService>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();

        var updated = await settings.UpdateHighRiskStepUpAsync(
            actorId,
            new UpdateMemSecuritySettingsCommand(
                RequireHighRiskStepUp: false,
                HighRiskStepUpGrantMinutes: 60),
            correlationId: "settings-test-correlation");

        Assert.False(updated.RequireHighRiskStepUp);
        Assert.Equal(60, updated.HighRiskStepUpGrantMinutes);
        Assert.False(updated.IsDefaulted);
        Assert.Equal(actorId, updated.UpdatedByOperatorId);

        var row = await db.MemSecuritySettings.SingleAsync();
        Assert.False(row.RequireHighRiskStepUp);
        Assert.Equal(60, row.HighRiskStepUpGrantMinutes);
        Assert.False(string.IsNullOrWhiteSpace(row.ConcurrencyStamp));

        var audit = await db.MemOperatorAuditEvents.SingleAsync(entry =>
            entry.EventType == "security.high_risk_step_up_policy.changed");

        Assert.Equal("succeeded", audit.Outcome);
        Assert.Equal(actorId, audit.ActorOperatorId);
        Assert.Equal("settings-test-correlation", audit.CorrelationId);
        Assert.Equal("required:required->not_required;reuse:15->60", audit.ReasonCode);

        var auditJson = JsonSerializer.Serialize(audit);
        Assert.DoesNotContain("password", auditJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("totp", auditJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recovery", auditJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SEC_SETTINGS_01A_patch_requires_recent_step_up_even_when_policy_is_not_required()
    {
        var authorization = new RecordingAuthorizationService(AuthorizationResult.Failed());
        await using var fixture = await SettingsFixture.CreateAsync(authorization);

        var actorId = Guid.NewGuid();
        await fixture.SeedSettingsAsync(
            requireHighRiskStepUp: false,
            reuseMinutes: 60,
            actorId);

        var response = await fixture.PatchSettingsAsOwnerAsync(
            actorId,
            new UpdateMemHighRiskStepUpSettingsRequest(
                Required: true,
                ReuseVerificationMinutes: 15));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("no-store", response.Headers["Cache-Control"], StringComparison.Ordinal);
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);

        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("step_up_required", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SEC_SETTINGS_01A_step_up_grant_uses_persisted_reuse_window()
    {
        await using var fixture = await SettingsFixture.CreateAsync();
        var subject = await fixture.CreateReadyOperatorAsync("owner.settings-window", "Secure!Foundation123");
        var sessionId = Guid.NewGuid();

        await fixture.SeedSettingsAsync(
            requireHighRiskStepUp: true,
            reuseMinutes: 30,
            subject.Id);

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var stepUp = scope.ServiceProvider.GetRequiredService<IMemOperatorStepUpService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
        var persisted = await userManager.FindByIdAsync(subject.Id.ToString("D"));

        Assert.NotNull(persisted);

        var before = DateTimeOffset.UtcNow;
        var grant = await stepUp.IssueForSessionAsync(persisted!, sessionId);
        var after = DateTimeOffset.UtcNow;

        Assert.True(grant.ExpiresAtUtc >= before.AddMinutes(30).AddSeconds(-2));
        Assert.True(grant.ExpiresAtUtc <= after.AddMinutes(30).AddSeconds(2));

        var row = await db.MemOperatorStepUpGrants.SingleAsync(entry =>
            entry.OperatorId == subject.Id && entry.SessionId == sessionId);

        Assert.Equal(grant.ExpiresAtUtc, row.ExpiresAtUtc);
    }


    [Fact]
    public async Task SEC_SETTINGS_01A_active_step_up_grant_is_invalidated_when_policy_window_changes()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero));
        await using var fixture = await SettingsFixture.CreateAsync(timeProvider: clock);
        var subject = await fixture.CreateReadyOperatorAsync("owner.settings-window-current", "Secure!Foundation123");
        var sessionId = Guid.NewGuid();

        await fixture.SeedSettingsAsync(
            requireHighRiskStepUp: true,
            reuseMinutes: 60,
            subject.Id);

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var stepUp = scope.ServiceProvider.GetRequiredService<IMemOperatorStepUpService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
        var persisted = await userManager.FindByIdAsync(subject.Id.ToString("D"));

        Assert.NotNull(persisted);

        await stepUp.IssueForSessionAsync(persisted!, sessionId);
        var principal = fixture.CreateOperatorPrincipal(subject, sessionId);

        Assert.True(await stepUp.HasActiveGrantAsync(principal));

        await fixture.SeedSettingsAsync(
            requireHighRiskStepUp: true,
            reuseMinutes: 5,
            subject.Id);

        Assert.False(await stepUp.HasActiveGrantAsync(principal));

        var row = await db.MemOperatorStepUpGrants.SingleAsync(entry =>
            entry.OperatorId == subject.Id && entry.SessionId == sessionId);

        Assert.NotNull(row.RevokedAtUtc);
        Assert.Equal("security_settings_changed", row.RevokedReasonCode);
    }

    [Fact]
    public async Task SEC_SETTINGS_01A_reenabling_required_policy_invalidates_grants_issued_before_change()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 8, 10, 0, 0, TimeSpan.Zero));
        await using var fixture = await SettingsFixture.CreateAsync(timeProvider: clock);
        var subject = await fixture.CreateReadyOperatorAsync("owner.settings-reenabled", "Secure!Foundation123");
        var sessionId = Guid.NewGuid();

        await fixture.SeedSettingsAsync(
            requireHighRiskStepUp: false,
            reuseMinutes: 60,
            subject.Id);

        await using var scope = fixture.Application.Services.CreateAsyncScope();
        var stepUp = scope.ServiceProvider.GetRequiredService<IMemOperatorStepUpService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MemOperator>>();
        var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
        var persisted = await userManager.FindByIdAsync(subject.Id.ToString("D"));

        Assert.NotNull(persisted);

        await stepUp.IssueForSessionAsync(persisted!, sessionId);
        var principal = fixture.CreateOperatorPrincipal(subject, sessionId);

        Assert.True(await stepUp.HasActiveGrantAsync(principal));

        clock.Advance(TimeSpan.FromMinutes(1));

        await fixture.SeedSettingsAsync(
            requireHighRiskStepUp: true,
            reuseMinutes: 5,
            subject.Id);

        Assert.False(await stepUp.HasActiveGrantAsync(principal));

        var row = await db.MemOperatorStepUpGrants.SingleAsync(entry =>
            entry.OperatorId == subject.Id && entry.SessionId == sessionId);

        Assert.NotNull(row.RevokedAtUtc);
        Assert.Equal("security_settings_changed", row.RevokedReasonCode);
    }

    private sealed class SettingsFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly IReadOnlyDictionary<string, RouteEndpoint> _routes;

        private SettingsFixture(
            string databasePath,
            WebApplication application,
            IReadOnlyDictionary<string, RouteEndpoint> routes)
        {
            _databasePath = databasePath;
            Application = application;
            _routes = routes;
        }

        public WebApplication Application { get; }

        public static async Task<SettingsFixture> CreateAsync(
            IAuthorizationService? authorizationService = null,
            TimeProvider? timeProvider = null)
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-sec-settings-01a-{Guid.NewGuid():N}.db");

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
                    ["MemOperatorIdentity:StepUpGrantMinutes"] = "15",
                    ["MemOperatorIdentity:SecurityStampValidationSeconds"] = "0"
                });

            builder.Services.AddDataProtection();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddDbContext<MemDbContext>(options =>
                options.UseSqlite($"Data Source={databasePath}"));
            builder.Services.AddMemOperatorIdentity(
                builder.Configuration,
                builder.Environment);
            if (timeProvider is not null)
            {
                builder.Services.Replace(ServiceDescriptor.Singleton(timeProvider));
            }

            builder.Services.AddInstallerAuth(builder.Configuration);

            if (authorizationService is not null)
            {
                builder.Services.Replace(
                    ServiceDescriptor.Singleton<IAuthorizationService>(authorizationService));
            }

            var application = builder.Build();
            new MemSecuritySettingsEndpoints().AddRoutes(application);

            await using (var scope = application.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MemDbContext>();
                await db.Database.EnsureCreatedAsync();
            }

            var endpoints = ((IEndpointRouteBuilder)application).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .ToArray();

            var settingsEndpoint = FindRoute(
                endpoints,
                "/api/security/settings",
                HttpMethods.Get);
            var patchEndpoint = FindRoute(
                endpoints,
                "/api/security/settings/high-risk-step-up",
                HttpMethods.Patch);

            Assert.Contains(
                settingsEndpoint.Metadata.OfType<IAuthorizeData>(),
                entry => entry.Policy == MemOperatorPolicies.ManagePlatform);
            Assert.Contains(
                patchEndpoint.Metadata.OfType<IAuthorizeData>(),
                entry => entry.Policy == MemOperatorPolicies.ManagePlatform);

            var routes = new Dictionary<string, RouteEndpoint>(StringComparer.Ordinal)
            {
                ["/api/security/settings"] = settingsEndpoint,
                ["/api/security/settings/high-risk-step-up"] = patchEndpoint
            };

            return new SettingsFixture(databasePath, application, routes);
        }


        private static RouteEndpoint FindRoute(
            IEnumerable<RouteEndpoint> endpoints,
            string path,
            string method)
        {
            return Assert.Single(endpoints.Where(endpoint =>
                NormalizeRoutePattern(endpoint.RoutePattern.RawText) == path
                && endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods
                    .Contains(method, StringComparer.OrdinalIgnoreCase) == true));
        }

        private static string NormalizeRoutePattern(string? rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
            {
                return string.Empty;
            }

            var normalized = rawText.TrimEnd('/');
            return normalized.Length == 0 ? "/" : normalized;
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

        public async Task SeedSettingsAsync(
            bool requireHighRiskStepUp,
            int reuseMinutes,
            Guid actorId)
        {
            await using var scope = Application.Services.CreateAsyncScope();
            var settings = scope.ServiceProvider.GetRequiredService<IMemSecuritySettingsService>();

            await settings.UpdateHighRiskStepUpAsync(
                actorId,
                new UpdateMemSecuritySettingsCommand(requireHighRiskStepUp, reuseMinutes));
        }

        public async Task<ResponseSnapshot> PatchSettingsAsOwnerAsync(
            Guid operatorId,
            UpdateMemHighRiskStepUpSettingsRequest request)
        {
            var endpoint = _routes["/api/security/settings/high-risk-step-up"];

            await using var scope = Application.Services.CreateAsyncScope();
            var context = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                User = CreatePlatformOwnerPrincipal(operatorId)
            };

            context.TraceIdentifier = $"sec-settings-01a-{Guid.NewGuid():N}";
            context.Request.Method = HttpMethods.Patch;
            context.Request.Path = "/api/security/settings/high-risk-step-up";
            context.Request.ContentType = "application/json";

            var requestBody = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                request,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

            context.Request.ContentLength = requestBody.Length;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(
                new CanHaveBodyRequestFeature());
            context.Request.Body = new MemoryStream(requestBody);
            context.Response.Body = new MemoryStream();

            await endpoint.RequestDelegate(context);

            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
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

        public ClaimsPrincipal CreateOperatorPrincipal(
            MemOperator user,
            Guid sessionId)
        {
            return new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
                        new Claim(ClaimTypes.Name, user.UserName ?? string.Empty),
                        new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner),
                        new Claim(MemOperatorSessionClaims.SessionIdClaimType, sessionId.ToString("D"))
                    ],
                    IdentityConstants.ApplicationScheme));
        }

        private static ClaimsPrincipal CreatePlatformOwnerPrincipal(Guid operatorId)
        {
            return new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, operatorId.ToString("D")),
                        new Claim(ClaimTypes.Name, "owner.settings"),
                        new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
                    ],
                    IdentityConstants.ApplicationScheme));
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

    private sealed class ManualTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value)
        {
            _utcNow = _utcNow.Add(value);
        }
    }

    private sealed class RecordingAuthorizationService(
        AuthorizationResult result) : IAuthorizationService
    {
        public string? LastPolicyName { get; private set; }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements)
        {
            return Task.FromResult(result);
        }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
        {
            LastPolicyName = policyName;
            return Task.FromResult(result);
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
}
