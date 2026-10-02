using Carter;
using HostAgent.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.IntegrationTests.Runtime;

public sealed class HostAgentCreateStackAsyncEndpointContractTests
{
    [Theory]
    [InlineData("/internal/host-agent/runtime-images/policy", "GET")]
    [InlineData("/internal/host-agent/commands/create-chat-stack-runtime", "POST")]
    [InlineData("/internal/host-agent/operations/{operationId:guid}", "GET")]
    public async Task STACK_CREATE_REL_01C_registers_accept_and_poll_routes(
        string route,
        string method)
    {
        var builder = WebApplication.CreateBuilder();
        await using var application = builder.Build();

        new HostAgentCommandEndpoints().AddRoutes(application);

        var endpoint = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(candidate => string.Equals(
                candidate.RoutePattern.RawText,
                route,
                StringComparison.Ordinal));

        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        Assert.NotNull(methods);
        Assert.Equal(method, Assert.Single(methods!.HttpMethods));
    }
}
