using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Endpoints;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Security;

public sealed class MemOperatorAdministrationEndpointContractTests
{
    [Fact]
    public async Task SEC_AUTH_04B_02_binds_every_operator_lifecycle_route_to_the_platform_owner_policy()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();

        await using var app = builder.Build();

        new MemOperatorAdministrationEndpoints().AddRoutes(app);

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                endpoint.RoutePattern.RawText?.Contains(
                    "api/security/operators",
                    StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(6, routes.Length);

        Assert.All(routes, route =>
        {
            var authorization = route.Metadata.GetOrderedMetadata<IAuthorizeData>();

            Assert.Contains(
                authorization,
                data => string.Equals(
                    data.Policy,
                    MemOperatorPolicies.ManagePlatform,
                    StringComparison.Ordinal));
        });
    }
}
