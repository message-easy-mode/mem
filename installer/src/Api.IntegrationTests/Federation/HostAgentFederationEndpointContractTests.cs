using System.Security.Claims;
using System.Text.Json;
using Carter;
using HostAgent.Matrix.Federation;
using HostAgent.Matrix.Federation.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Federation;

public sealed class HostAgentFederationEndpointContractTests
{
    [Fact]
    public async Task Federation_state_is_a_read_only_carter_get_route()
    {
        var builder = WebApplication.CreateBuilder();
        RegisterFederationServices(builder, new FakeStateService(State()));
        await using var application = builder.Build();

        var module = new HostAgentFederationEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);

        var endpoint = FindEndpoint(application);
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.NotNull(methods);
        Assert.Equal(HttpMethods.Get, Assert.Single(methods!.HttpMethods));
    }

    [Fact]
    public async Task Federation_state_requires_a_current_control_plane_session()
    {
        var builder = WebApplication.CreateBuilder();
        RegisterFederationServices(builder, new FakeStateService(State()));
        await using var application = builder.Build();
        new HostAgentFederationEndpoints().AddRoutes(application);

        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity())
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/internal/host-agent/runtime-stacks/demo-stack/federation";
        context.Request.RouteValues["slugOrId"] = "demo-stack";
        context.Response.Body = new MemoryStream();

        await InvokeAsync(application, context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Federation_state_returns_safe_no_store_json_without_yaml_content()
    {
        var builder = WebApplication.CreateBuilder();
        RegisterFederationServices(builder, new FakeStateService(State()));
        await using var application = builder.Build();
        new HostAgentFederationEndpoints().AddRoutes(application);

        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = PlatformOwner()
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/internal/host-agent/runtime-stacks/demo-stack/federation";
        context.Request.RouteValues["slugOrId"] = "demo-stack";
        context.Response.Body = new MemoryStream();

        await InvokeAsync(application, context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var state = JsonSerializer.Deserialize<RuntimeStackFederationStateResponse>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(state);
        Assert.Equal(FederationModes.Public, state!.Mode);
        Assert.DoesNotContain("homeserver.yaml", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("registration_shared_secret", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Federation_state_returns_a_stable_not_found_problem()
    {
        var builder = WebApplication.CreateBuilder();
        RegisterFederationServices(builder, new FakeStateService(null));
        await using var application = builder.Build();
        new HostAgentFederationEndpoints().AddRoutes(application);

        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = PlatformOwner()
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/internal/host-agent/runtime-stacks/missing/federation";
        context.Request.RouteValues["slugOrId"] = "missing";
        context.Response.Body = new MemoryStream();

        await InvokeAsync(application, context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("federation_stack_not_found", body, StringComparison.Ordinal);
    }

    private static void RegisterFederationServices(
        WebApplicationBuilder builder,
        IRuntimeStackFederationStateService stateService)
    {
        builder.Services.AddSingleton(stateService);
        builder.Services.AddSingleton<IRuntimeStackFederationReviewService>(new FakeReviewService());
    }

    private static RuntimeStackFederationStateResponse State() =>
        new(
            Source: "control-plane",
            Status: "ok",
            RuntimeStackId: Guid.Parse("7085b97d-3d30-434e-976a-62df0178be16"),
            Slug: "demo-stack",
            Mode: FederationModes.Public,
            ConfigurationState: FederationConfigurationStates.Healthy,
            Allowlist: [],
            EnforcementKind: "synapse_unrestricted",
            MatrixContainerRunning: true,
            MatrixDirectHostPortExposed: false,
            IngressMode: FederationIngressModes.Normal,
            ServerWellKnownPublished: true,
            FederationPathsPubliclyForwarded: true,
            SigningKeyPathsPubliclyForwarded: true,
            CanonicalRouteEnabled: true,
            CanonicalRouteTargetsMatrix: true,
            CanonicalCertificatePresent: true,
            AlternateMatrixRouteDetected: false,
            StateFingerprint: "sha256:safe",
            LatestOperation: null,
            Checks: [],
            Warnings: [],
            Problems: []);

    private static ClaimsPrincipal PlatformOwner() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
        ],
        authenticationType: IdentityConstants.ApplicationScheme));

    private static RouteEndpoint FindEndpoint(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => string.Equals(
                candidate.RoutePattern.RawText,
                "/internal/host-agent/runtime-stacks/{slugOrId}/federation",
                StringComparison.Ordinal));

    private static async Task InvokeAsync(WebApplication application, HttpContext context)
    {
        var requestDelegate = FindEndpoint(application).RequestDelegate
            ?? throw new InvalidOperationException("Federation endpoint has no request delegate.");
        await requestDelegate(context);
    }

    private sealed class FakeStateService(RuntimeStackFederationStateResponse? state)
        : IRuntimeStackFederationStateService
    {
        public Task<RuntimeStackFederationStateResponse?> GetAsync(
            string slugOrId,
            CancellationToken ct) => Task.FromResult(state);
    }

    private sealed class FakeReviewService : IRuntimeStackFederationReviewService
    {
        public Task<RuntimeStackFederationReviewResponse?> ReviewAsync(
            string slugOrId,
            RuntimeStackFederationPolicyRequest request,
            CancellationToken ct) => Task.FromResult<RuntimeStackFederationReviewResponse?>(null);
    }
}
