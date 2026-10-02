using System.Reflection;
using Carter;
using HostAgent.Runtime.Migrations.Acceptance;
using HostAgent.Runtime.Migrations.Acceptance.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationAcceptanceEndpointContractTests
{
    [Fact]
    public async Task Acceptance_routes_are_migration_keyed_policy_protected_and_no_store()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<MigrationAcceptanceService>();
        builder.Services.AddScoped<MigrationFinishService>();
        await using var application = builder.Build();

        var module = new MigrationAcceptanceEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);

        var endpoints = ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => Normalize(endpoint.RoutePattern.RawText).StartsWith(
                "/api/operator/migrations/sessions/{migrationId}/acceptance",
                StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(4, endpoints.Length);
        Assert.All(endpoints, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                authorization => authorization.Policy == MemOperatorPolicies.MigrationIntakeOperate));

        var routes = endpoints.SelectMany(endpoint =>
            (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => $"{method} {Normalize(endpoint.RoutePattern.RawText)}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/acceptance", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/acceptance", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/acceptance/finish", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/acceptance/report", routes);
    }

    [Fact]
    public void Browser_acceptance_request_contains_decisions_not_server_owned_identifiers()
    {
        var prohibited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ExecutionId", "CandidateArtifactId", "StagingRunId", "CatalogEntryId",
            "RestoreSessionId", "HostPath", "DockerId", "ImageId"
        };
        var properties = typeof(AcceptMigrationRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(properties, property => prohibited.Contains(property));
    }


    [Fact]
    public void Guided_finish_request_contains_only_operator_decisions()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(FinishMigrationRequest.RetentionDays),
            nameof(FinishMigrationRequest.ConfirmVerifiedServerIsAuthoritative),
            nameof(FinishMigrationRequest.ConfirmRecoveryBoundaryChanges),
            nameof(FinishMigrationRequest.ConfirmRetainOldServerAndNoAutomaticDeletion),
        };
        var properties = typeof(FinishMigrationRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(allowed.SetEquals(properties));
    }

    private static string Normalize(string? rawText)
    {
        var route = string.IsNullOrWhiteSpace(rawText) ? "/" : rawText;
        return route.Length > 1 ? route.TrimEnd('/') : route;
    }
}
