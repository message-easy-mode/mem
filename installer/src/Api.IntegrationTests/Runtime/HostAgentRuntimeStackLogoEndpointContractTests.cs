using HostAgent.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Api.IntegrationTests.Runtime;

public sealed class HostAgentRuntimeStackLogoEndpointContractTests
{
    private const string Route = "/internal/host-agent/runtime-stacks/{slugOrId}/logo";

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Stack_logo_contract_registers_only_the_expected_http_methods(string method)
    {
        var builder = WebApplication.CreateBuilder();
        await using var application = builder.Build();

        new HostAgentRuntimeStacksEndpoint().AddRoutes(application);

        var matches = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(candidate => string.Equals(
                candidate.RoutePattern.RawText,
                Route,
                StringComparison.Ordinal))
            .Where(candidate => candidate.Metadata
                .GetMetadata<HttpMethodMetadata>()?
                .HttpMethods
                .Contains(method, StringComparer.OrdinalIgnoreCase) == true)
            .ToArray();

        var endpoint = Assert.Single(matches);
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        Assert.NotNull(methods);
        Assert.Equal(method, Assert.Single(methods!.HttpMethods));
    }
}
