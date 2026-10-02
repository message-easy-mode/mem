using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modules.Auth.Contracts;
using Modules.Auth.Endpoints;
using Modules.Auth.Identity;
using Modules.Auth.Services.Identity;

namespace Api.IntegrationTests.Security;

/// <summary>
/// Direct contract coverage for the narrow CLI device-authorization routes.
/// These tests intentionally omit device services on rejection paths: same-
/// origin review must fail before service resolution, and approval must fail
/// on a missing RecentStepUp grant before Identity/device services resolve.
/// </summary>
public sealed class MemCliDeviceAuthorizationEndpointContractTests
{
    [Fact]
    public async Task CLI_AUTH_02C_registers_only_narrow_anonymous_mechanics_and_named_browser_review_and_decision_routes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();

        await using var application = builder.Build();
        new MemCliDeviceAuthorizationEndpoints().AddRoutes(application);

        var routes = GetDeviceAuthorizationRoutes(application);

        Assert.Equal(5, routes.Length);

        var start = FindRoute(
            routes,
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations");
        var poll = FindRoute(
            routes,
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations/{authorizationId:guid}/poll");
        var review = FindRoute(
            routes,
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations/review");
        var approve = FindRoute(
            routes,
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations/approve");
        var deny = FindRoute(
            routes,
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations/deny");

        Assert.Contains(start.Metadata.GetOrderedMetadata<IAllowAnonymous>(), _ => true);
        Assert.Contains(poll.Metadata.GetOrderedMetadata<IAllowAnonymous>(), _ => true);

        Assert.Contains(
            review.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            policy => string.Equals(
                policy.Policy,
                MemOperatorPolicies.ReadSafeStatus,
                StringComparison.Ordinal));
        Assert.Contains(
            approve.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            policy => string.Equals(
                policy.Policy,
                MemOperatorPolicies.ReadSafeStatus,
                StringComparison.Ordinal));
        Assert.Contains(
            deny.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            policy => string.Equals(
                policy.Policy,
                MemOperatorPolicies.ReadSafeStatus,
                StringComparison.Ordinal));

        Assert.DoesNotContain(review.Metadata.GetOrderedMetadata<IAllowAnonymous>(), _ => true);
        Assert.DoesNotContain(approve.Metadata.GetOrderedMetadata<IAllowAnonymous>(), _ => true);
        Assert.DoesNotContain(deny.Metadata.GetOrderedMetadata<IAllowAnonymous>(), _ => true);
    }

    [Fact]
    public async Task CLI_AUTH_02B_start_derives_installation_binding_server_side_and_ignores_client_supplied_unknown_fields()
    {
        var serverInstallationId = Guid.NewGuid();
        var clientSelectedInstallationId = Guid.NewGuid();
        var binding = new RecordingInstallationBindingService(serverInstallationId);
        var deviceAuthorizations = new RecordingDeviceAuthorizationService();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IMemCliDeviceAuthorizationRateLimiter>(
            new FixedDeviceAuthorizationRateLimiter());
        builder.Services.AddSingleton<IMemCliDeviceInstallationBindingService>(binding);
        builder.Services.AddSingleton<IMemCliDeviceAuthorizationService>(deviceAuthorizations);

        await using var application = builder.Build();
        new MemCliDeviceAuthorizationEndpoints().AddRoutes(application);

        var endpoint = FindRoute(
            GetDeviceAuthorizationRoutes(application),
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations");

        var context = CreateJsonContext(
            application,
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations",
            $$"""
            {
              "verifierChallenge": "5iRYj3iA4p4o_n2lVxI8M9D_-S1oQe5GJ3LGFA6ddfQ",
              "deviceLabel": "SSH host shell",
              "installationId": "{{clientSelectedInstallationId:D}}"
            }
            """);

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
        Assert.Equal(serverInstallationId, deviceAuthorizations.StartInstallationId);
        Assert.NotEqual(clientSelectedInstallationId, deviceAuthorizations.StartInstallationId);
        Assert.Equal("SSH host shell", deviceAuthorizations.StartDeviceLabel);
        Assert.Equal(1, binding.CallCount);

        context.Response.Body.Position = 0;

        var response = await JsonSerializer.DeserializeAsync<StartMemCliDeviceAuthorizationResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal("authorization_started", response!.Status);
        Assert.Equal(deviceAuthorizations.Started.AuthorizationId, response.AuthorizationId);
        Assert.Equal(deviceAuthorizations.Started.UserCode, response.UserCode);
        Assert.Equal(deviceAuthorizations.Started.ExpiresAtUtc, response.ExpiresAtUtc);
        Assert.Null(response.BrowserApprovalUrl);
        Assert.DoesNotContain(clientSelectedInstallationId.ToString("D"), await ReadResponseAsync(context), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CLI_AUTH_03B_02A_start_returns_only_the_server_configured_browser_approval_url()
    {
        var serverInstallationId = Guid.NewGuid();
        var binding = new RecordingInstallationBindingService(serverInstallationId);
        var deviceAuthorizations = new RecordingDeviceAuthorizationService();

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>(
                "App:PublicBaseUrl",
                "https://mem.example.internal")
        ]);
        builder.Services.AddSingleton<IMemCliDeviceAuthorizationRateLimiter>(
            new FixedDeviceAuthorizationRateLimiter());
        builder.Services.AddSingleton<IMemCliDeviceInstallationBindingService>(binding);
        builder.Services.AddSingleton<IMemCliDeviceAuthorizationService>(deviceAuthorizations);

        await using var application = builder.Build();
        new MemCliDeviceAuthorizationEndpoints().AddRoutes(application);

        var endpoint = FindRoute(
            GetDeviceAuthorizationRoutes(application),
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations");

        var context = CreateJsonContext(
            application,
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations",
            """
            {
              "verifierChallenge": "5iRYj3iA4p4o_n2lVxI8M9D_-S1oQe5GJ3LGFA6ddfQ",
              "deviceLabel": "SSH host shell",
              "browserApprovalUrl": "https://attacker.example/cli/authorize"
            }
            """);

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);

        context.Response.Body.Position = 0;

        var response = await JsonSerializer.DeserializeAsync<StartMemCliDeviceAuthorizationResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal(
            "https://mem.example.internal/cli/authorize",
            response!.BrowserApprovalUrl);
        Assert.DoesNotContain(
            "attacker.example",
            await ReadResponseAsync(context),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CLI_AUTH_02B_rate_limits_anonymous_start_before_installation_or_device_services_resolve()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IMemCliDeviceAuthorizationRateLimiter>(
            new FixedDeviceAuthorizationRateLimiter(
                startLease: new MemCliDeviceAuthorizationRateLimitLease(
                    false,
                    TimeSpan.FromSeconds(17))));

        await using var application = builder.Build();
        new MemCliDeviceAuthorizationEndpoints().AddRoutes(application);

        var endpoint = FindRoute(
            GetDeviceAuthorizationRoutes(application),
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations");

        var context = CreateJsonContext(
            application,
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations",
            "{\"verifierChallenge\":\"5iRYj3iA4p4o_n2lVxI8M9D_-S1oQe5GJ3LGFA6ddfQ\"}");

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("17", context.Response.Headers["Retry-After"].ToString());
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());

        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.Equal("rate_limited", response.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CLI_AUTH_02C_refuses_browser_review_without_the_same_origin_header_before_device_services_resolve()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();

        await using var application = builder.Build();
        new MemCliDeviceAuthorizationEndpoints().AddRoutes(application);

        var endpoint = FindRoute(
            GetDeviceAuthorizationRoutes(application),
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations/review");

        var context = CreateJsonContext(
            application,
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations/review",
            "{\"userCode\":\"ABCD-EFGH\"}");
        context.User = CreatePlatformOwnerPrincipal();

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());

        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.Equal("forbidden", response.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("ABCD-EFGH", response.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CLI_AUTH_02C_returns_only_safe_pending_review_details()
    {
        var deviceAuthorizations = new RecordingDeviceAuthorizationService();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IMemCliDeviceAuthorizationService>(deviceAuthorizations);

        await using var application = builder.Build();
        new MemCliDeviceAuthorizationEndpoints().AddRoutes(application);

        var endpoint = FindRoute(
            GetDeviceAuthorizationRoutes(application),
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations/review");

        var context = CreateJsonContext(
            application,
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations/review",
            "{\"userCode\":\"ABCD-EFGH\"}");
        context.User = CreatePlatformOwnerPrincipal();
        context.Request.Headers["X-MEM-Operator-Request"] = "1";

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("ABCD-EFGH", deviceAuthorizations.ReviewUserCode);

        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.Equal("authorization_pending", response.RootElement.GetProperty("status").GetString());
        Assert.Equal("SSH host shell", response.RootElement.GetProperty("deviceLabel").GetString());
        Assert.True(response.RootElement.TryGetProperty("expiresAtUtc", out _));
        Assert.Equal(3, response.RootElement.EnumerateObject().Count());
        Assert.DoesNotContain("deviceCredential", response.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("verifier", response.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CLI_AUTH_02B_refuses_browser_approval_without_recent_step_up_before_device_authorization_services_resolve()
    {
        var authorization = new RecordingAuthorizationService(
            AuthorizationResult.Failed());

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.Replace(
            ServiceDescriptor.Singleton<IAuthorizationService>(authorization));

        await using var application = builder.Build();
        new MemCliDeviceAuthorizationEndpoints().AddRoutes(application);

        var endpoint = FindRoute(
            GetDeviceAuthorizationRoutes(application),
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations/approve");

        var context = CreateJsonContext(
            application,
            HttpMethods.Post,
            "/api/auth/cli-device/authorizations/approve",
            "{\"userCode\":\"ABCD-EFGH\"}");
        context.User = CreatePlatformOwnerPrincipal();
        context.Request.Headers["X-MEM-Operator-Request"] = "1";

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
        Assert.Equal(MemOperatorPolicies.RecentStepUp, authorization.LastPolicyName);

        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.Equal("step_up_required", response.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("ABCD-EFGH", response.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CLI_AUTH_03B_03B_registers_session_revocation_as_device_scheme_only()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();

        await using var application = builder.Build();
        new MemCliDeviceAuthorizationEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                string.Equals(
                    candidate.RoutePattern.RawText,
                    "/api/auth/cli-device/session",
                    StringComparison.Ordinal) &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Delete)));

        Assert.Contains(
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            policy => string.Equals(
                policy.Policy,
                MemOperatorPolicies.CliDeviceSession,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task CLI_AUTH_03B_03B_revokes_only_the_current_device_session_claim_and_returns_safe_status()
    {
        var sessionId = Guid.NewGuid();
        var revokedAtUtc = DateTimeOffset.UtcNow;
        var deviceAuthorizations = new RecordingDeviceAuthorizationService
        {
            Revocation = new MemCliDeviceSessionRevocationResult(
                "revoked",
                revokedAtUtc)
        };

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IMemCliDeviceAuthorizationService>(deviceAuthorizations);

        await using var application = builder.Build();
        new MemCliDeviceAuthorizationEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                string.Equals(
                    candidate.RoutePattern.RawText,
                    "/api/auth/cli-device/session",
                    StringComparison.Ordinal) &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Delete)));

        var context = new DefaultHttpContext
        {
            RequestServices = application.Services
        };
        context.Request.Method = HttpMethods.Delete;
        context.Request.Path = "/api/auth/cli-device/session";
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(
                    MemCliDeviceAuthentication.SessionIdClaimType,
                    sessionId.ToString("D"))
            ],
            MemCliDeviceAuthentication.Scheme));

        await endpoint.RequestDelegate(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(sessionId, deviceAuthorizations.RevokedSessionId);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());

        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.Equal("revoked", response.RootElement.GetProperty("status").GetString());
        Assert.True(response.RootElement.TryGetProperty("revokedAtUtc", out _));
        Assert.DoesNotContain("deviceCredential", response.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(sessionId.ToString("D"), response.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    private static RouteEndpoint[] GetDeviceAuthorizationRoutes(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(
                "/api/auth/cli-device/authorizations",
                StringComparison.Ordinal) == true)
            .ToArray();

    private static RouteEndpoint FindRoute(
        IEnumerable<RouteEndpoint> routes,
        string method,
        string template) =>
        routes.Single(candidate =>
            string.Equals(
                candidate.RoutePattern.RawText,
                template,
                StringComparison.Ordinal) &&
            candidate.Metadata.OfType<HttpMethodMetadata>()
                .Any(metadata => metadata.HttpMethods.Contains(method)));

    private static DefaultHttpContext CreateJsonContext(
        WebApplication application,
        string method,
        string path,
        string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var context = new DefaultHttpContext
        {
            RequestServices = application.Services
        };

        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Features.Set<IHttpRequestBodyDetectionFeature>(
            new CanHaveBodyRequestFeature());
        context.Response.Body = new MemoryStream();

        return context;
    }

    private static async Task<string> ReadResponseAsync(DefaultHttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    private static ClaimsPrincipal CreatePlatformOwnerPrincipal() =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                new Claim(ClaimTypes.Name, "owner.cli-device-endpoint-test"),
                new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
            ],
            IdentityConstants.ApplicationScheme));

    private sealed class RecordingInstallationBindingService(Guid installationId)
        : IMemCliDeviceInstallationBindingService
    {
        public int CallCount { get; private set; }

        public Task<Guid?> GetCurrentInstallationIdAsync(CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult<Guid?>(installationId);
        }
    }

    private sealed class FixedDeviceAuthorizationRateLimiter(
        MemCliDeviceAuthorizationRateLimitLease? startLease = null,
        MemCliDeviceAuthorizationRateLimitLease? pollLease = null)
        : IMemCliDeviceAuthorizationRateLimiter
    {
        public MemCliDeviceAuthorizationRateLimitLease TryAcquireStart() =>
            startLease ?? new MemCliDeviceAuthorizationRateLimitLease(true, null);

        public MemCliDeviceAuthorizationRateLimitLease TryAcquirePoll() =>
            pollLease ?? new MemCliDeviceAuthorizationRateLimitLease(true, null);
    }

    private sealed class RecordingDeviceAuthorizationService : IMemCliDeviceAuthorizationService
    {
        public MemCliDeviceAuthorizationStarted Started { get; } = new(
            Guid.NewGuid(),
            "ABCD-EFGH",
            DateTimeOffset.UtcNow.AddMinutes(10));

        public Guid? StartInstallationId { get; private set; }

        public string? StartVerifierChallenge { get; private set; }

        public string? StartDeviceLabel { get; private set; }

        public string? ReviewUserCode { get; private set; }

        public MemCliDeviceAuthorizationReview Review { get; } = new(
            "authorization_pending",
            "SSH host shell",
            DateTimeOffset.UtcNow.AddMinutes(10));

        public Guid? RevokedSessionId { get; private set; }

        public MemCliDeviceSessionRevocationResult Revocation { get; init; } =
            new("revoked", DateTimeOffset.UtcNow);

        public Task<MemCliDeviceAuthorizationStarted> StartAsync(
            Guid installationId,
            string verifierChallenge,
            string? deviceLabel,
            string? correlationId = null,
            CancellationToken ct = default)
        {
            StartInstallationId = installationId;
            StartVerifierChallenge = verifierChallenge;
            StartDeviceLabel = deviceLabel;
            return Task.FromResult(Started);
        }

        public Task<MemCliDeviceAuthorizationReview> ReviewAsync(
            string userCode,
            string? correlationId = null,
            CancellationToken ct = default)
        {
            ReviewUserCode = userCode;
            return Task.FromResult(Review);
        }

        public Task<MemCliDeviceAuthorizationDecision> ApproveAsync(
            string userCode,
            Guid approvingOperatorId,
            string? correlationId = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MemCliDeviceAuthorizationDecision> DenyAsync(
            string userCode,
            Guid denyingOperatorId,
            string? correlationId = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MemCliDeviceAuthorizationPollResult> PollAsync(
            Guid authorizationId,
            string verifier,
            string? correlationId = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MemCliDeviceSessionValidationResult> ValidateAsync(
            Guid installationId,
            string deviceCredential,
            string? correlationId = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<MemCliDeviceSessionRevocationResult> RevokeSessionAsync(
            Guid sessionId,
            string? correlationId = null,
            CancellationToken ct = default)
        {
            RevokedSessionId = sessionId;
            return Task.FromResult(Revocation);
        }
    }

    private sealed class RecordingAuthorizationService(
        AuthorizationResult result) : IAuthorizationService
    {
        public string? LastPolicyName { get; private set; }

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(result);

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName)
        {
            LastPolicyName = policyName;
            return Task.FromResult(result);
        }
    }

    private sealed class CanHaveBodyRequestFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
