using System.Security.Claims;
using System.Text;
using System.Text.Json;
using HostAgent.Commands;
using HostAgent.Matrix.Federation;
using HostAgent.Matrix.Federation.Endpoints;
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

public sealed class HostAgentFederationApplyEndpointContractTests
{
    [Fact]
    public async Task Federation_apply_is_a_step_up_protected_Carter_post_route()
    {
        await using var application = BuildApplication(
            AuthorizationResult.Failed(),
            new FakeApplyService(),
            new ApplyServiceResolutionProbe());

        var endpoint = FindEndpoint(application);

        Assert.Contains(
            endpoint.Metadata.OfType<HttpMethodMetadata>().Single().HttpMethods,
            method => string.Equals(method, HttpMethods.Post, StringComparison.Ordinal));
        Assert.Equal(
            "ApplyHostAgentRuntimeStackFederation",
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName);
    }

    [Fact]
    public async Task Federation_apply_requires_recent_step_up_before_resolving_mutation()
    {
        var apply = new FakeApplyService();
        var resolution = new ApplyServiceResolutionProbe();
        await using var application = BuildApplication(
            AuthorizationResult.Failed(),
            apply,
            resolution);
        var context = CreateContext(application.Services);

        await FindEndpoint(application).RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(0, resolution.Count);
        Assert.Equal(0, apply.CallCount);
        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<HostAgentErrorResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("step_up_required", response!.Error);
    }

    [Fact]
    public async Task Federation_apply_returns_the_final_server_owned_outcome_after_step_up()
    {
        var apply = new FakeApplyService();
        var resolution = new ApplyServiceResolutionProbe();
        await using var application = BuildApplication(
            AuthorizationResult.Success(),
            apply,
            resolution);
        var context = CreateContext(application.Services);

        await FindEndpoint(application).RequestDelegate!(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal(1, resolution.Count);
        Assert.Equal(1, apply.CallCount);
        context.Response.Body.Position = 0;
        var response = await JsonSerializer.DeserializeAsync<RuntimeStackFederationApplyResponse>(
            context.Response.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(response);
        Assert.Equal("succeeded", response!.Status);
        Assert.Equal(FederationModes.Restricted, response.ObservedMode);
    }

    private static WebApplication BuildApplication(
        AuthorizationResult authorizationResult,
        IRuntimeStackFederationApplyService applyService,
        ApplyServiceResolutionProbe resolution)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.Replace(ServiceDescriptor.Singleton<IAuthorizationService>(
            new FixedAuthorizationService(authorizationResult)));
        builder.Services.AddSingleton<IRuntimeStackFederationStateService>(
            new FakeStateService());
        builder.Services.AddSingleton<IRuntimeStackFederationReviewService>(
            new FakeReviewService());
        builder.Services.AddSingleton<IRuntimeStackFederationApplyService>(_ =>
        {
            resolution.Count++;
            return applyService;
        });

        var application = builder.Build();
        new HostAgentFederationEndpoints().AddRoutes(application);
        return application;
    }

    private static RouteEndpoint FindEndpoint(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                candidate.RoutePattern.RawText ==
                    "/internal/host-agent/runtime-stacks/{slugOrId}/federation/apply" &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Post)));

    private static DefaultHttpContext CreateContext(IServiceProvider services)
    {
        var body = Encoding.UTF8.GetBytes(
            """
            {
              "mode": "restricted",
              "allowlist": ["partner.example"],
              "reviewHash": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "idempotencyKey": "federation-endpoint-test"
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
        context.Request.Path = "/internal/host-agent/runtime-stacks/test-stack/federation/apply";
        context.Request.RouteValues["slugOrId"] = "test-stack";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = body.Length;
        context.Request.Body = new MemoryStream(body);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static ClaimsPrincipal CreatePlatformOwnerPrincipal()
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
                new Claim(ClaimTypes.Name, "owner"),
                new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
            },
            IdentityConstants.ApplicationScheme);
        return new ClaimsPrincipal(identity);
    }

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class FixedAuthorizationService : IAuthorizationService
    {
        private readonly AuthorizationResult _result;
        public FixedAuthorizationService(AuthorizationResult result) => _result = result;

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(_result);

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user,
            object? resource,
            string policyName) =>
            Task.FromResult(_result);
    }

    private sealed class FakeStateService : IRuntimeStackFederationStateService
    {
        public Task<RuntimeStackFederationStateResponse?> GetAsync(
            string slugOrId,
            CancellationToken ct) =>
            Task.FromResult<RuntimeStackFederationStateResponse?>(null);
    }

    private sealed class FakeReviewService : IRuntimeStackFederationReviewService
    {
        public Task<RuntimeStackFederationReviewResponse?> ReviewAsync(
            string slugOrId,
            RuntimeStackFederationPolicyRequest request,
            CancellationToken ct) =>
            Task.FromResult<RuntimeStackFederationReviewResponse?>(null);
    }

    private sealed class ApplyServiceResolutionProbe
    {
        public int Count { get; set; }
    }

    private sealed class FakeApplyService : IRuntimeStackFederationApplyService
    {
        public int CallCount { get; private set; }

        public Task<RuntimeStackFederationApplyResponse?> ApplyAsync(
            string slugOrId,
            RuntimeStackFederationApplyRequest request,
            string requestedBy,
            Guid? actorOperatorId,
            CancellationToken requestCancellation)
        {
            CallCount++;
            return Task.FromResult<RuntimeStackFederationApplyResponse?>(new(
                Source: "control-plane",
                Status: "succeeded",
                OperationId: Guid.NewGuid(),
                PreviousMode: FederationModes.Public,
                RequestedMode: FederationModes.Restricted,
                ObservedMode: FederationModes.Restricted,
                RollbackAttempted: false,
                RollbackSucceeded: null,
                Checks: [],
                ErrorCode: null,
                Detail: "Restricted federation is active."));
        }
    }
}
