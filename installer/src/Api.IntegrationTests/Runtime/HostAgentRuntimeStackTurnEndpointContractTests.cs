using System.Security.Claims;
using System.Text.Json;
using Carter;
using HostAgent.Endpoints;
using HostAgent.Runtime.Stacks.Turn;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Runtime;

public sealed class HostAgentRuntimeStackTurnEndpointContractTests
{
    private const string Route = "/internal/host-agent/runtime-stacks/{slugOrId}/turn";

    [Fact]
    public async Task Stack_turn_inspection_is_a_read_only_get_route()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IRuntimeStackTurnInspectionService>(new FakeInspectionService(Response()));
        builder.Services.AddSingleton<IRuntimeStackTurnConnectionService>(new FakeConnectionService());
        builder.Services.AddSingleton<IRuntimeStackTurnDisconnectionService>(new FakeDisconnectionService());
        await using var application = builder.Build();

        var module = new HostAgentRuntimeStackTurnEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);

        var methods = FindEndpoint(application).Metadata.GetMetadata<HttpMethodMetadata>();
        Assert.NotNull(methods);
        Assert.Equal(HttpMethods.Get, Assert.Single(methods!.HttpMethods));
    }

    [Fact]
    public async Task Stack_turn_inspection_requires_a_current_operator_session()
    {
        await using var application = await BuildAsync(Response());
        await using var scope = application.Services.CreateAsyncScope();
        var context = Context(scope.ServiceProvider, new ClaimsPrincipal(new ClaimsIdentity()));
        await InvokeAsync(application, context);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Stack_turn_inspection_returns_no_store_safe_json_without_secrets_or_paths()
    {
        await using var application = await BuildAsync(Response());
        await using var scope = application.Services.CreateAsyncScope();
        var context = Context(scope.ServiceProvider, PlatformOwner());
        await InvokeAsync(application, context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var response = JsonSerializer.Deserialize<RuntimeStackTurnInspectionResponse>(
            body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal(RuntimeStackTurnStates.NotConnected, response!.State);
        Assert.DoesNotContain("super-secret", body, StringComparison.Ordinal);
        Assert.DoesNotContain("homeserver.yaml", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("coturn-secret.json", body, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<WebApplication> BuildAsync(RuntimeStackTurnInspectionResponse? response)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IRuntimeStackTurnInspectionService>(new FakeInspectionService(response));
        builder.Services.AddSingleton<IRuntimeStackTurnConnectionService>(new FakeConnectionService());
        builder.Services.AddSingleton<IRuntimeStackTurnDisconnectionService>(new FakeDisconnectionService());
        var application = builder.Build();
        new HostAgentRuntimeStackTurnEndpoints().AddRoutes(application);
        await Task.CompletedTask;
        return application;
    }

    private static DefaultHttpContext Context(IServiceProvider services, ClaimsPrincipal user)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = user
        };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/internal/host-agent/runtime-stacks/demo-stack/turn";
        context.Request.RouteValues["slugOrId"] = "demo-stack";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static RuntimeStackTurnInspectionResponse Response() =>
        new(
            Source: "control-plane",
            Status: "ok",
            RuntimeStackId: Guid.Parse("7085b97d-3d30-434e-976a-62df0178be16"),
            Slug: "demo-stack",
            InspectedAtUtc: DateTimeOffset.Parse("2026-07-26T03:00:00Z"),
            State: RuntimeStackTurnStates.NotConnected,
            Management: RuntimeStackTurnManagementKinds.None,
            LiveConfiguration: new RuntimeStackTurnLiveConfigurationResponse(
                Supported: true,
                AnyTurnSettings: false,
                MemManagedMarkerPresent: false,
                TurnUris: [],
                CredentialMechanism: "none",
                SharedSecretPresent: false,
                SharedSecretMatchesPlatform: null,
                UserLifetime: null,
                AllowGuests: null,
                PublicHost: null,
                Realm: null,
                FileSha256: "sha256:safe",
                ProblemCode: null,
                Detail: null),
            PersistedMetadata: new RuntimeStackTurnPersistedMetadataResponse(
                Recorded: false,
                Configured: null,
                TurnUris: [],
                PublicHost: null,
                Realm: null,
                ConfigurationSource: null,
                RelayPortsPublished: null,
                SharedSecretPresent: null,
                UserLifetime: null,
                AllowGuests: null,
                MatchesLiveConfiguration: null),
            Platform: null,
            MatrixRuntime: new RuntimeStackTurnMatrixRuntimeResponse(true, true, true, null, null),
            Diagnostics: [],
            Warnings: [],
            Detail: "This stack is not configured to use a TURN service.");

    private static ClaimsPrincipal PlatformOwner() =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, MemOperatorRoles.PlatformOwner)
        ],
        IdentityConstants.ApplicationScheme));

    private static RouteEndpoint FindEndpoint(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => string.Equals(endpoint.RoutePattern.RawText, Route, StringComparison.Ordinal));

    private static Task InvokeAsync(WebApplication application, HttpContext context) =>
        (FindEndpoint(application).RequestDelegate
            ?? throw new InvalidOperationException("Stack TURN endpoint has no request delegate."))(context);

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

    private sealed class FakeDisconnectionService : IRuntimeStackTurnDisconnectionService
    {
        public Task<RuntimeStackTurnDisconnectReviewResponse?> ReviewAsync(
            string slugOrId,
            CancellationToken ct) =>
            Task.FromResult<RuntimeStackTurnDisconnectReviewResponse?>(null);

        public Task<RuntimeStackTurnDisconnectResponse?> DisconnectAsync(
            string slugOrId,
            RuntimeStackTurnDisconnectRequest request,
            string requestedBy,
            Guid? actorOperatorId,
            CancellationToken requestCancellation) =>
            Task.FromResult<RuntimeStackTurnDisconnectResponse?>(null);
    }

    private sealed class FakeInspectionService(RuntimeStackTurnInspectionResponse? response)
        : IRuntimeStackTurnInspectionService
    {
        public Task<RuntimeStackTurnInspectionResponse?> InspectAsync(
            string slugOrId,
            CancellationToken ct) => Task.FromResult(response);
    }
}
