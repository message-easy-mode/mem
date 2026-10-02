using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Endpoints;

namespace Api.IntegrationTests.Security;

public sealed class MemOperatorEnrollmentEndpointContractTests
{
    [Fact]
    public async Task SEC_AUTH_04C_registers_only_anonymous_scoped_enrolment_routes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();

        await using var app = builder.Build();

        new MemOperatorEnrollmentEndpoints().AddRoutes(app);

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint =>
                endpoint.RoutePattern.RawText?.Contains(
                    "api/auth/enrollment",
                    StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(6, routes.Length);

        Assert.All(routes, route =>
        {
            Assert.Contains(
                route.Metadata.GetOrderedMetadata<IAllowAnonymous>(),
                _ => true);
        });
    }
}
