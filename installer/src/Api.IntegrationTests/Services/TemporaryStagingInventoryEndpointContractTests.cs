using System.Security.Claims;
using System.Text.Json;
using HostAgent.Runtime.Services.TemporaryStaging;
using HostAgent.Runtime.Services.TemporaryStaging.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Services;

public sealed class TemporaryStagingInventoryEndpointContractTests
{
    private const string Route = "/api/operator/services/temporary-staging";

    [Fact]
    public async Task Inventory_registers_only_GET_and_requires_safe_operator_status_policy()
    {
        await using var application = CreateApplication(false);
        var endpoint = FindEndpoint(application);
        Assert.Equal("GET", Assert.Single(endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods));
        Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            data => data.Policy == MemOperatorPolicies.ReadSafeStatus);
        Assert.Single(((IEndpointRouteBuilder)application).DataSources.SelectMany(source => source.Endpoints));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Inventory_is_no_store_read_only_and_filters_workspace_links_by_capability(bool allowWorkspace)
    {
        await using var application = CreateApplication(allowWorkspace);
        await using var scope = application.Services.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = "GET";
        context.Request.Path = Route;
        context.Response.Body = new MemoryStream();
        await FindEndpoint(application).RequestDelegate!(context);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
        Assert.Equal("no-cache", context.Response.Headers.Pragma.ToString());
        context.Response.Body.Position = 0;
        var result = await JsonSerializer.DeserializeAsync<TemporaryStagingInventoryResponse>(
            context.Response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(result);
        var item = Assert.Single(result!.Items);
        Assert.False(item.CanRetire);
        Assert.Equal(allowWorkspace ? "mst_one" : null, item.RetirementReview?.StagingRunId);
        Assert.NotNull(item.Owner);
        Assert.Equal(allowWorkspace ? "/migrations/mig_one" : null, item.Owner!.WorkspaceHref);
    }

    private static WebApplication CreateApplication(bool allowWorkspace)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IAuthorizationService>(new TestAuthorization(allowWorkspace));
        builder.Services.AddSingleton<ITemporaryStagingInventorySource>(new FixedSource());
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<TemporaryStagingInventoryService>();
        var application = builder.Build();
        new TemporaryStagingInventoryEndpoints().AddRoutes(application);
        return application;
    }

    private static RouteEndpoint FindEndpoint(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Single(endpoint => endpoint.RoutePattern.RawText == Route);

    private sealed class FixedSource : ITemporaryStagingInventorySource
    {
        public Task<StagingInventorySnapshot> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new StagingInventorySnapshot(true, true, true, true, [],
                [new StagingRecordedRuntime("stage-one", "migration-candidate", null, "ready", false, [])],
                [new StagingRecordedOwner("stage-one", "migration", "mig_one", "Example", "mca_one", true, false, "mst_one")], []));
    }

    private sealed class TestAuthorization(bool allow) : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(allow ? AuthorizationResult.Success() : AuthorizationResult.Failed());
    }
}
