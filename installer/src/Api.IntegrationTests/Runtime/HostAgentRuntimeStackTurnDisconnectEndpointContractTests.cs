using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Carter;
using HostAgent.Endpoints;
using HostAgent.Runtime.Stacks.Turn;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Runtime;

public sealed class HostAgentRuntimeStackTurnDisconnectEndpointContractTests
{
    private const string ReviewRoute = "/internal/host-agent/runtime-stacks/{slugOrId}/turn/disconnect/review";
    private const string ApplyRoute = "/internal/host-agent/runtime-stacks/{slugOrId}/turn/disconnect";

    [Theory]
    [InlineData(ReviewRoute)]
    [InlineData(ApplyRoute)]
    public async Task Stack_turn_disconnect_routes_are_post_only(string route)
    {
        await using var application = await BuildAsync(new FakeDisconnectionService());
        var methods = FindEndpoint(application, route).Metadata.GetMetadata<HttpMethodMetadata>();

        Assert.NotNull(methods);
        Assert.Equal(HttpMethods.Post, Assert.Single(methods!.HttpMethods));
    }

    [Theory]
    [InlineData(ReviewRoute)]
    [InlineData(ApplyRoute)]
    public async Task Stack_turn_disconnect_routes_require_a_current_operator_session(string route)
    {
        await using var application = await BuildAsync(new FakeDisconnectionService());
        await using var scope = application.Services.CreateAsyncScope();
        var context = Context(
            scope.ServiceProvider,
            new ClaimsPrincipal(new ClaimsIdentity()),
            route,
            route == ApplyRoute ? RequestJson() : null);

        await InvokeAsync(application, context, route);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Review_returns_restart_consequences_without_secret_material()
    {
        var service = new FakeDisconnectionService();
        await using var application = await BuildAsync(service);
        await using var scope = application.Services.CreateAsyncScope();
        var context = Context(scope.ServiceProvider, PlatformOwner(), ReviewRoute, null);

        await InvokeAsync(application, context, ReviewRoute);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        var body = await ReadBodyAsync(context);
        var response = JsonSerializer.Deserialize<RuntimeStackTurnDisconnectReviewResponse>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(response);
        Assert.True(response!.ConfigurationChangeRequired);
        Assert.True(response.RestartRequired);
        Assert.DoesNotContain("platform-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("turn_shared_secret", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("homeserver.yaml", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Apply_accepts_confirmation_hash_and_idempotency_without_step_up()
    {
        var service = new FakeDisconnectionService();
        await using var application = await BuildAsync(service);
        await using var scope = application.Services.CreateAsyncScope();
        var context = Context(
            scope.ServiceProvider,
            PlatformOwner(),
            ApplyRoute,
            RequestJson());

        await InvokeAsync(application, context, ApplyRoute);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.NotNull(service.LastRequest);
        Assert.True(service.LastRequest!.ConfirmDisconnectFromPlatformTurn);
        Assert.Equal("disconnect-review-hash", service.LastRequest.ReviewHash);
        Assert.Equal("turn-disconnect-demo-1", service.LastRequest.IdempotencyKey);
        var body = await ReadBodyAsync(context);
        Assert.DoesNotContain("platform-secret", body, StringComparison.Ordinal);
    }

    private static async Task<WebApplication> BuildAsync(
        IRuntimeStackTurnDisconnectionService disconnectionService)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(disconnectionService);
        builder.Services.AddSingleton<IRuntimeStackTurnConnectionService>(
            new FakeConnectionService());
        builder.Services.AddSingleton<IRuntimeStackTurnInspectionService>(
            new FakeInspectionService());
        var application = builder.Build();
        var module = new HostAgentRuntimeStackTurnEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);
        await Task.CompletedTask;
        return application;
    }

    private static DefaultHttpContext Context(
        IServiceProvider services,
        ClaimsPrincipal user,
        string route,
        string? json)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = user
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = route.Replace("{slugOrId}", "demo-stack", StringComparison.Ordinal);
        context.Request.RouteValues["slugOrId"] = "demo-stack";
        if (json is not null)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(json);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bodyBytes.Length;
            context.Features.Set<IHttpRequestBodyDetectionFeature>(
                new RequestBodyDetectionFeature());
            context.Request.Body = new MemoryStream(bodyBytes);
        }
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string RequestJson() =>
        JsonSerializer.Serialize(
            new RuntimeStackTurnDisconnectRequest(
                ReviewHash: "disconnect-review-hash",
                IdempotencyKey: "turn-disconnect-demo-1",
                ConfirmDisconnectFromPlatformTurn: true),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static async Task<string> ReadBodyAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await new StreamReader(context.Response.Body).ReadToEndAsync();
    }

    private static ClaimsPrincipal PlatformOwner() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Name, "owner"),
            new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
        ],
        IdentityConstants.ApplicationScheme));

    private static RouteEndpoint FindEndpoint(
        WebApplication application,
        string route) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => string.Equals(
                endpoint.RoutePattern.RawText,
                route,
                StringComparison.Ordinal));

    private static Task InvokeAsync(
        WebApplication application,
        HttpContext context,
        string route) =>
        (FindEndpoint(application, route).RequestDelegate
            ?? throw new InvalidOperationException("Stack TURN disconnect endpoint has no request delegate."))(context);

    private sealed class RequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class FakeDisconnectionService : IRuntimeStackTurnDisconnectionService
    {
        public RuntimeStackTurnDisconnectRequest? LastRequest { get; private set; }

        public Task<RuntimeStackTurnDisconnectReviewResponse?> ReviewAsync(
            string slugOrId,
            CancellationToken ct) =>
            Task.FromResult<RuntimeStackTurnDisconnectReviewResponse?>(new(
                Source: "control-plane",
                Status: RuntimeStackTurnDisconnectStatuses.Ready,
                RuntimeStackId: Guid.Parse("7085b97d-3d30-434e-976a-62df0178be16"),
                Slug: "demo-stack",
                MatrixServerName: "matrix.example.test",
                ConfigurationChangeRequired: true,
                RestartRequired: true,
                PlatformPublicHost: "turn.example.test",
                TurnUris:
                [
                    "turn:turn.example.test:3478?transport=udp",
                    "turn:turn.example.test:3478?transport=tcp"
                ],
                CurrentConfigurationSha256: "sha256:before",
                ReviewHash: "disconnect-review-hash",
                ConfirmationText: "Disconnect and restart Matrix.",
                Consequences: ["Matrix restarts."]));

        public Task<RuntimeStackTurnDisconnectResponse?> DisconnectAsync(
            string slugOrId,
            RuntimeStackTurnDisconnectRequest request,
            string requestedBy,
            Guid? actorOperatorId,
            CancellationToken requestCancellation)
        {
            LastRequest = request;
            return Task.FromResult<RuntimeStackTurnDisconnectResponse?>(new(
                Source: "control-plane",
                Status: RuntimeStackTurnDisconnectStatuses.Succeeded,
                OperationId: Guid.Parse("5a213f1d-6dca-4a36-8b80-b1d503ab44be"),
                RuntimeStackId: Guid.Parse("7085b97d-3d30-434e-976a-62df0178be16"),
                Slug: "demo-stack",
                ConfigurationChanged: true,
                MatrixRestarted: true,
                RollbackAttempted: false,
                RollbackSucceeded: null,
                StateAfter: RuntimeStackTurnStates.NotConnected,
                ErrorCode: null,
                Detail: "Disconnected."));
        }
    }

    private sealed class FakeConnectionService : IRuntimeStackTurnConnectionService
    {
        public Task<RuntimeStackTurnConnectReviewResponse?> ReviewAsync(
            string slugOrId,
            CancellationToken ct) =>
            Task.FromResult<RuntimeStackTurnConnectReviewResponse?>(null);

        public Task<RuntimeStackTurnConnectResponse?> ConnectAsync(
            string slugOrId,
            RuntimeStackTurnConnectRequest request,
            string requestedBy,
            Guid? actorOperatorId,
            CancellationToken requestCancellation) =>
            Task.FromResult<RuntimeStackTurnConnectResponse?>(null);
    }

    private sealed class FakeInspectionService : IRuntimeStackTurnInspectionService
    {
        public Task<RuntimeStackTurnInspectionResponse?> InspectAsync(
            string slugOrId,
            CancellationToken ct) =>
            Task.FromResult<RuntimeStackTurnInspectionResponse?>(null);
    }
}
