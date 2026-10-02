using System.Security.Claims;
using System.Text.Json;
using Carter;
using HostAgent.Matrix.Federation;
using HostAgent.Matrix.Federation.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Federation;

public sealed class HostAgentFederationReviewEndpointContractTests
{
    private const string Route = "/internal/host-agent/runtime-stacks/{slugOrId}/federation/review";

    [Fact]
    public async Task Federation_review_is_a_stateless_carter_post_route()
    {
        var builder = WebApplication.CreateBuilder();
        RegisterFederationServices(builder);
        await using var application = builder.Build();

        var module = new HostAgentFederationEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);

        var endpoint = FindEndpoint(application);
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.NotNull(methods);
        Assert.Equal(HttpMethods.Post, Assert.Single(methods!.HttpMethods));
    }

    [Fact]
    public async Task Federation_review_requires_a_current_control_plane_session()
    {
        var builder = WebApplication.CreateBuilder();
        RegisterFederationServices(builder);
        await using var application = builder.Build();
        new HostAgentFederationEndpoints().AddRoutes(application);

        await using var scope = application.Services.CreateAsyncScope();
        var context = Context(scope.ServiceProvider, new ClaimsPrincipal(new ClaimsIdentity()));

        await InvokeAsync(application, context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Federation_review_returns_no_store_canonical_json_without_persisting_a_draft()
    {
        var builder = WebApplication.CreateBuilder();
        RegisterFederationServices(builder);
        await using var application = builder.Build();
        new HostAgentFederationEndpoints().AddRoutes(application);

        await using var scope = application.Services.CreateAsyncScope();
        var context = Context(scope.ServiceProvider, PlatformOwner());

        await InvokeAsync(application, context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var review = JsonSerializer.Deserialize<RuntimeStackFederationReviewResponse>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(review);
        Assert.Equal(FederationModes.Restricted, review!.ProposedMode);
        Assert.Equal(["partner.example"], review.CanonicalAllowlist);
        Assert.StartsWith("sha256:", review.ReviewHash, StringComparison.Ordinal);
        Assert.DoesNotContain("homeserver.yaml", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("registration_shared_secret", body, StringComparison.OrdinalIgnoreCase);
    }


    private static void RegisterFederationServices(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IRuntimeStackFederationStateService>(
            new FakeStateService());
        builder.Services.AddSingleton<IRuntimeStackFederationReviewService>(
            new FakeReviewService(Review()));
    }

    private static DefaultHttpContext Context(
        IServiceProvider services,
        ClaimsPrincipal user)
    {
        var request = new RuntimeStackFederationPolicyRequest(
            FederationModes.Restricted,
            ["Partner.Example."]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            request,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = user
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/internal/host-agent/runtime-stacks/demo-stack/federation/review";
        context.Request.RouteValues["slugOrId"] = "demo-stack";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bytes.Length;
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new CanHaveBodyRequestFeature());
        context.Request.Body = new MemoryStream(bytes);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static RuntimeStackFederationReviewResponse Review() =>
        new(
            Source: "control-plane",
            Status: "ready",
            RuntimeStackId: Guid.Parse("7085b97d-3d30-434e-976a-62df0178be16"),
            Slug: "demo-stack",
            CurrentMode: FederationModes.Public,
            ProposedMode: FederationModes.Restricted,
            CurrentAllowlist: [],
            CanonicalAllowlist: ["partner.example"],
            AddedDomains: ["partner.example"],
            RemovedDomains: [],
            RestartRequired: true,
            IngressChangeRequired: false,
            NoChange: false,
            Warnings:
            [
                new FederationReviewWarningResponse(
                    "federation_existing_rooms_may_be_affected",
                    "Existing federated rooms may be affected.")
            ],
            ConfirmationText: "Apply Restricted federation and restart Matrix.",
            ReviewHash: "sha256:review");

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
                Route,
                StringComparison.Ordinal));

    private static async Task InvokeAsync(WebApplication application, HttpContext context)
    {
        var requestDelegate = FindEndpoint(application).RequestDelegate
            ?? throw new InvalidOperationException("Federation review endpoint has no request delegate.");
        await requestDelegate(context);
    }


    private sealed class CanHaveBodyRequestFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class FakeStateService : IRuntimeStackFederationStateService
    {
        public Task<RuntimeStackFederationStateResponse?> GetAsync(
            string slugOrId,
            CancellationToken ct) => Task.FromResult<RuntimeStackFederationStateResponse?>(null);
    }

    private sealed class FakeReviewService(RuntimeStackFederationReviewResponse? review)
        : IRuntimeStackFederationReviewService
    {
        public Task<RuntimeStackFederationReviewResponse?> ReviewAsync(
            string slugOrId,
            RuntimeStackFederationPolicyRequest request,
            CancellationToken ct) => Task.FromResult(review);
    }
}
