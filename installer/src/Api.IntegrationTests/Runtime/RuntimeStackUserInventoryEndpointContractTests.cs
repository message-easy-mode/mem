using HostAgent.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackUserInventoryEndpointContractTests
{
    [Fact]
    public async Task RuntimeStackUserInventory_registers_narrow_list_synchronize_and_create_routes()
    {
        var builder = WebApplication.CreateBuilder();

        await using var application = builder.Build();
        new RuntimeStackUsersEndpoint().AddRoutes(application);

        var routes = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.Contains(
                "/internal/host-agent/runtime-stacks/{slugOrId}/users",
                StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(8, routes.Length);

        var synchronize = Assert.Single(routes, route =>
            string.Equals(route.RoutePattern.RawText,
                "/internal/host-agent/runtime-stacks/{slugOrId}/users/synchronize",
                StringComparison.Ordinal));

        Assert.Contains(
            HttpMethods.Post,
            synchronize.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);

        var authority = Assert.Single(routes, route =>
            string.Equals(route.RoutePattern.RawText,
                "/internal/host-agent/runtime-stacks/{slugOrId}/users/admin-authority",
                StringComparison.Ordinal));
        var password = Assert.Single(routes, route =>
            string.Equals(route.RoutePattern.RawText,
                "/internal/host-agent/runtime-stacks/{slugOrId}/users/{userId:guid}/password",
                StringComparison.Ordinal));

        var deactivate = Assert.Single(routes, route =>
            string.Equals(route.RoutePattern.RawText,
                "/internal/host-agent/runtime-stacks/{slugOrId}/users/{userId:guid}/deactivate",
                StringComparison.Ordinal));
        var reactivate = Assert.Single(routes, route =>
            string.Equals(route.RoutePattern.RawText,
                "/internal/host-agent/runtime-stacks/{slugOrId}/users/{userId:guid}/reactivate",
                StringComparison.Ordinal));

        Assert.Contains(HttpMethods.Post, authority.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.Contains(HttpMethods.Post, password.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.Contains(HttpMethods.Post, deactivate.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
        Assert.Contains(HttpMethods.Post, reactivate.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods);
    }
}
