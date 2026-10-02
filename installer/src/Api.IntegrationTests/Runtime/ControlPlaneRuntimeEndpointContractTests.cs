using System.Security.Claims;
using System.Text.Json;
using Carter;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Modules.Operator.Runtime;
using Modules.Shared.Docker;
using Shared.ControlPlane.Runtime;

namespace Api.IntegrationTests.Runtime;

public sealed class ControlPlaneRuntimeEndpointContractTests
{
    [Fact]
    public async Task Runtime_context_endpoint_is_authenticated_no_store_and_path_safe()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-runtime-endpoint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var runtimeContext = TestRuntimeContext.Create(
                root,
                mode: MemRuntimeModes.LocalDevelopment,
                uiDeliveryMode: MemUiDeliveryModes.Vite);
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(runtimeContext);
            builder.Services.AddSingleton<IControlPlaneDockerOwnershipGuard>(
                new StubOwnershipGuard(new MemDockerOwnershipProjection(
                    State: "exclusive",
                    MutationsAllowed: true,
                    DevelopmentOverrideActive: false,
                    CompetingContainers: [],
                    WarningCode: null)));
            builder.Services.AddSingleton<IControlPlaneExposureInspector>(
                new StubExposureInspector(MemControlPlaneExposureProjection.NotApplicable));
            await using var application = builder.Build();

            var module = new ControlPlaneRuntimeEndpoints();
            Assert.IsAssignableFrom<ICarterModule>(module);
            module.AddRoutes(application);

            var endpoint = FindEndpoint(application, "/api/operator/runtime-context");
            var authorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            var policy = Assert.Single(authorization);
            Assert.Equal(MemOperatorPolicies.ReadSafeStatus, policy.Policy);

            var context = new DefaultHttpContext
            {
                RequestServices = application.Services,
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D"))
                ],
                "test"))
            };
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/api/operator/runtime-context";
            context.Response.Body = new MemoryStream();

            await (endpoint.RequestDelegate ??
                   throw new InvalidOperationException("Runtime endpoint delegate missing."))(
                context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
            Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());

            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            var projection = JsonSerializer.Deserialize<MemControlPlaneRuntimeContextProjection>(
                body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.NotNull(projection);
            Assert.Equal(MemRuntimeModes.LocalDevelopment, projection!.RuntimeMode);
            Assert.Equal(runtimeContext.ControlPlaneInstanceId, projection.ControlPlaneInstanceId);
            Assert.DoesNotContain(runtimeContext.ContentRootPath, body, StringComparison.Ordinal);
            Assert.DoesNotContain(runtimeContext.StateRootPath, body, StringComparison.Ordinal);
            Assert.DoesNotContain(runtimeContext.DockerEndpoint.ToString(), body, StringComparison.Ordinal);
            Assert.Equal("exclusive", projection.DockerOwnership.State);
            Assert.Equal(MemControlPlaneExposureStates.NotApplicable, projection.ControlPlaneExposure.State);
            Assert.Equal(MemControlPlaneAccessModes.LocalDevelopment, projection.ControlPlaneExposure.AccessMode);
            Assert.Equal(MemRestartKinds.DeveloperProcess, projection.Restart.Kind);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Runtime_preflight_endpoint_is_anonymous_no_store_and_bounded()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-runtime-preflight-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var runtimeContext = TestRuntimeContext.Create(
                root,
                mode: MemRuntimeModes.ContainerizedDevelopment,
                runningInContainer: true,
                containerName: "mem-control-plane-dev",
                uiDeliveryMode: MemUiDeliveryModes.EmbeddedSpa);
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddAuthorization();
            builder.Services.AddSingleton(runtimeContext);
            builder.Services.AddSingleton<IControlPlaneDockerOwnershipGuard>(
                new StubOwnershipGuard(MemDockerOwnershipProjection.Unchecked));
            builder.Services.AddSingleton<IControlPlaneExposureInspector>(
                new StubExposureInspector(new MemControlPlaneExposureProjection(
                    MemControlPlaneExposureStates.Private,
                    MemControlPlaneAccessModes.SshTunnel,
                    HostAddress: "127.0.0.1",
                    HostPort: 8443,
                    BindingCount: 1,
                    WarningCode: null)));
            await using var application = builder.Build();

            new ControlPlaneRuntimeEndpoints().AddRoutes(application);
            var endpoint = FindEndpoint(application, "/health/runtime");

            Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
            Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());

            var context = new DefaultHttpContext
            {
                RequestServices = application.Services
            };
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = "/health/runtime";
            context.Response.Body = new MemoryStream();

            await (endpoint.RequestDelegate ??
                   throw new InvalidOperationException("Runtime preflight endpoint delegate missing."))(
                context);

            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
            Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
            Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());

            context.Response.Body.Position = 0;
            using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            var projection = JsonSerializer.Deserialize<MemControlPlaneRuntimePreflightProjection>(
                body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.NotNull(projection);
            Assert.Equal(MemRuntimeModes.ContainerizedDevelopment, projection!.RuntimeMode);
            Assert.Equal(runtimeContext.ControlPlaneInstanceId, projection.ControlPlaneInstanceId);
            Assert.Equal(runtimeContext.ApiProcessInstanceId, projection.ApiProcessInstanceId);
            Assert.DoesNotContain(runtimeContext.ContentRootPath, body, StringComparison.Ordinal);
            Assert.DoesNotContain(runtimeContext.StateRootPath, body, StringComparison.Ordinal);
            Assert.DoesNotContain("dockerOwnership", body, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class StubOwnershipGuard(
        MemDockerOwnershipProjection projection) : IControlPlaneDockerOwnershipGuard
    {
        public Task<MemDockerOwnershipProjection> InspectAsync(
            CancellationToken cancellationToken) => Task.FromResult(projection);

        public Task EnsureMutationAllowedAsync(
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubExposureInspector(
        MemControlPlaneExposureProjection projection) : IControlPlaneExposureInspector
    {
        public Task<MemControlPlaneExposureProjection> InspectAsync(
            CancellationToken cancellationToken) => Task.FromResult(projection);
    }

    private static RouteEndpoint FindEndpoint(
        WebApplication application,
        string route) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate =>
                string.Equals(
                    candidate.RoutePattern.RawText,
                    route,
                    StringComparison.Ordinal) &&
                candidate.Metadata.OfType<HttpMethodMetadata>()
                    .Any(metadata => metadata.HttpMethods.Contains(HttpMethods.Get)));
}
