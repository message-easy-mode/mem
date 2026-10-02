using Carter;
using HostAgent.Runtime.Migrations.Qualification;
using HostAgent.Runtime.Migrations.Qualification.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationTwoServerQualificationEndpointContractTests
{
    [Fact]
    public async Task Qualification_routes_are_migration_keyed_and_policy_protected()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<MigrationTwoServerQualificationService>();
        builder.Services.AddScoped<MigrationTwoServerQualificationClosureService>();
        builder.Services.AddScoped<IMigrationTargetHostIdentityProbe, StubProbe>();
        await using var application = builder.Build();

        var module = new MigrationTwoServerQualificationEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);

        var endpoints = GetEndpoints(application);
        Assert.Equal(5, endpoints.Length);
        Assert.All(endpoints, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                authorization => authorization.Policy == MemOperatorPolicies.MigrationIntakeOperate));

        var routes = endpoints.SelectMany(endpoint =>
            (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => $"{method} {Normalize(endpoint.RoutePattern.RawText)}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(
            "GET /api/operator/migrations/sessions/{migrationId}/two-server-qualification",
            routes);
        Assert.Contains(
            "POST /api/operator/migrations/sessions/{migrationId}/two-server-qualification/source-evidence",
            routes);
        Assert.Contains(
            "GET /api/operator/migrations/sessions/{migrationId}/two-server-qualification/closure",
            routes);
        Assert.Contains(
            "POST /api/operator/migrations/sessions/{migrationId}/two-server-qualification/closure",
            routes);
        Assert.Contains(
            "GET /api/operator/migrations/sessions/{migrationId}/two-server-qualification/closure/report",
            routes);
    }

    private static RouteEndpoint[] GetEndpoints(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => Normalize(endpoint.RoutePattern.RawText).StartsWith(
                "/api/operator/migrations/sessions/{migrationId}/two-server-qualification",
                StringComparison.Ordinal))
            .ToArray();

    private static string Normalize(string? rawText)
    {
        var route = string.IsNullOrWhiteSpace(rawText) ? "/" : rawText;
        return route.Length > 1 ? route.TrimEnd('/') : route;
    }

    private sealed class StubProbe : IMigrationTargetHostIdentityProbe
    {
        public Task<MigrationTwoServerHostIdentity> ObserveAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(new MigrationTwoServerHostIdentity(
                new string('a', 64),
                "target",
                "Linux",
                "X64",
                new string('b', 64),
                "target-docker",
                "27.0.0"));
    }
}
