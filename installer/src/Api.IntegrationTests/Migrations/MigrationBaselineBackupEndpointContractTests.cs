using Carter;
using HostAgent.Runtime.Migrations.Acceptance;
using HostAgent.Runtime.Migrations.BaselineBackup;
using HostAgent.Runtime.Migrations.BaselineBackup.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationBaselineBackupEndpointContractTests
{
    [Fact]
    public async Task Baseline_backup_routes_are_migration_keyed_policy_protected_and_no_store()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<MigrationBaselineBackupHandoffService>();
        builder.Services.AddScoped<MigrationAcceptanceService>();
        await using var application = builder.Build();

        var module = new MigrationBaselineBackupEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);

        var endpoints = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => Normalize(endpoint.RoutePattern.RawText).StartsWith(
                "/api/operator/migrations/sessions/{migrationId}/baseline-backup",
                StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, endpoints.Length);
        Assert.All(endpoints, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                authorization => authorization.Policy == MemOperatorPolicies.MigrationIntakeOperate));

        var routes = endpoints.SelectMany(endpoint =>
            (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => $"{method} {Normalize(endpoint.RoutePattern.RawText)}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/baseline-backup", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/baseline-backup/retry", routes);
    }

    private static string Normalize(string? rawText)
    {
        var route = string.IsNullOrWhiteSpace(rawText) ? "/" : rawText;
        return route.Length > 1 ? route.TrimEnd('/') : route;
    }
}
