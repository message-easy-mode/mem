using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HostAgent.Commands;
using HostAgent.Matrix.Federation.PrivateNetwork;
using HostAgent.Matrix.Federation.PrivateNetwork.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Federation;

public sealed class HostAgentPrivateNetworkFederationSettingsEndpointContractTests
{
    [Fact]
    public async Task Private_network_settings_are_platform_owner_routes_under_Settings()
    {
        var service = new FakeService();
        await using var application = BuildApplication(
            AuthorizationResult.Failed(),
            service,
            new ServiceResolutionProbe());

        var routes = GetRoutes(application);

        Assert.Contains(routes, endpoint =>
            endpoint.RoutePattern.RawText == "/internal/host-agent/security/private-network-federation" &&
            endpoint.Metadata.OfType<HttpMethodMetadata>()
                .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Get)));
        Assert.Contains(routes, endpoint =>
            endpoint.RoutePattern.RawText == "/internal/host-agent/security/private-network-federation/{slugOrId}/review" &&
            endpoint.Metadata.OfType<HttpMethodMetadata>()
                .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post)));
        Assert.Contains(routes, endpoint =>
            endpoint.RoutePattern.RawText == "/internal/host-agent/security/private-network-federation/{slugOrId}/apply" &&
            endpoint.Metadata.OfType<HttpMethodMetadata>()
                .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post)));
    }

    [Fact]
    public async Task Private_network_inventory_requires_a_named_Platform_Owner()
    {
        var service = new FakeService();
        await using var application = BuildApplication(
            AuthorizationResult.Failed(),
            service,
            new ServiceResolutionProbe());
        var context = new DefaultHttpContext
        {
            RequestServices = application.Services,
            User = CreateOperatorPrincipal()
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/internal/host-agent/security/private-network-federation";
        context.Response.Body = new MemoryStream();

        await FindEndpoint(application, HttpMethods.Get, context.Request.Path.ToString()).RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<HostAgentErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("platform_owner_required", response!.Error);
        Assert.Equal(0, service.InventoryCallCount);
    }

    [Fact]
    public async Task Private_network_apply_requires_unconditional_recent_step_up_before_resolving_mutation()
    {
        var service = new FakeService();
        var resolution = new ServiceResolutionProbe();
        await using var application = BuildApplication(
            AuthorizationResult.Failed(),
            service,
            resolution);
        var context = CreateApplyContext(application.Services);

        await FindEndpoint(
            application,
            HttpMethods.Post,
            "/internal/host-agent/security/private-network-federation/{slugOrId}/apply")
            .RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(0, resolution.Count);
        Assert.Equal(0, service.ApplyCallCount);
        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<HostAgentErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("step_up_required", response!.Error);
    }

    [Fact]
    public async Task Private_network_apply_returns_the_server_owned_operation_outcome_after_step_up()
    {
        var service = new FakeService();
        var resolution = new ServiceResolutionProbe();
        await using var application = BuildApplication(
            AuthorizationResult.Success(),
            service,
            resolution);
        var context = CreateApplyContext(application.Services);

        await FindEndpoint(
            application,
            HttpMethods.Post,
            "/internal/host-agent/security/private-network-federation/{slugOrId}/apply")
            .RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(1, resolution.Count);
        Assert.Equal(1, service.ApplyCallCount);
        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<PrivateNetworkFederationApplyResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(response);
        Assert.Equal("succeeded", response!.Status);
        Assert.Equal("10.0.0.238/32", response.CanonicalCidr);
    }

    private static WebApplication BuildApplication(
        AuthorizationResult authorizationResult,
        FakeService service,
        ServiceResolutionProbe resolution)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.Replace(ServiceDescriptor.Singleton<IAuthorizationService>(
            new FixedAuthorizationService(authorizationResult)));
        builder.Services.AddSingleton<IRuntimeStackPrivateNetworkFederationService>(_ =>
        {
            resolution.Count++;
            return service;
        });

        var application = builder.Build();
        new HostAgentPrivateNetworkFederationSettingsEndpoints().AddRoutes(application);
        return application;
    }

    private static IReadOnlyList<RouteEndpoint> GetRoutes(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

    private static RouteEndpoint FindEndpoint(
        WebApplication application,
        string method,
        string route) =>
        GetRoutes(application).Single(endpoint =>
            endpoint.RoutePattern.RawText == route &&
            endpoint.Metadata.OfType<HttpMethodMetadata>()
                .Any(metadata => metadata.HttpMethods.Contains(method)));

    private static DefaultHttpContext CreateApplyContext(IServiceProvider services)
    {
        var body = Encoding.UTF8.GetBytes(
            """
            {
              "address": "10.0.0.238",
              "action": "add",
              "reviewHash": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "idempotencyKey": "private-network-endpoint-test"
            }
            """);
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = CreatePlatformOwnerPrincipal()
        };
        context.Features.Set<IHttpRequestBodyDetectionFeature>(
            new RequestBodyDetectionFeature());
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/internal/host-agent/security/private-network-federation/demo-stack/apply";
        context.Request.RouteValues["slugOrId"] = "demo-stack";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static ClaimsPrincipal CreatePlatformOwnerPrincipal() =>
        Principal(MemOperatorRoles.PlatformOwner);

    private static ClaimsPrincipal CreateOperatorPrincipal() =>
        Principal(MemOperatorRoles.Operator);

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Name, "operator"),
            new Claim(ClaimTypes.Role, role)
        ],
        IdentityConstants.ApplicationScheme));

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class FixedAuthorizationService(AuthorizationResult result)
        : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(result);

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(result);
    }

    private sealed class ServiceResolutionProbe
    {
        public int Count { get; set; }
    }

    private sealed class FakeService : IRuntimeStackPrivateNetworkFederationService
    {
        public int InventoryCallCount { get; private set; }
        public int ApplyCallCount { get; private set; }

        public Task<PrivateNetworkFederationInventoryResponse> GetInventoryAsync(CancellationToken ct)
        {
            InventoryCallCount++;
            return Task.FromResult(new PrivateNetworkFederationInventoryResponse(
                "control-plane",
                "ok",
                []));
        }

        public Task<PrivateNetworkFederationReviewResponse?> ReviewAsync(
            string slugOrId,
            PrivateNetworkFederationReviewRequest request,
            CancellationToken ct) =>
            Task.FromResult<PrivateNetworkFederationReviewResponse?>(null);

        public Task<PrivateNetworkFederationApplyResponse?> ApplyAsync(
            string slugOrId,
            PrivateNetworkFederationApplyRequest request,
            string requestedBy,
            Guid? actorOperatorId,
            CancellationToken requestCancellation)
        {
            ApplyCallCount++;
            return Task.FromResult<PrivateNetworkFederationApplyResponse?>(new(
                "control-plane",
                "succeeded",
                Guid.NewGuid(),
                Guid.NewGuid(),
                slugOrId,
                PrivateNetworkExceptionActions.Add,
                "10.0.0.238/32",
                ["10.0.0.238/32"],
                false,
                null,
                null,
                "The exact private network exception is active."));
        }
    }
}
