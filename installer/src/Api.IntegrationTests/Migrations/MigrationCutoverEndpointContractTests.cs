using System.Reflection;
using Carter;
using HostAgent.Runtime.Migrations.Cutover;
using HostAgent.Runtime.Migrations.Cutover.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationCutoverEndpointContractTests
{
    [Fact]
    public async Task Migration_cutover_routes_are_migration_keyed_and_policy_protected()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<MigrationCutoverFacadeService>();
        await using var application = builder.Build();

        var module = new MigrationCutoverEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);

        var endpoints = GetEndpoints(application);
        Assert.Equal(7, endpoints.Length);
        Assert.All(endpoints, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                authorization => authorization.Policy == MemOperatorPolicies.MigrationIntakeOperate));

        var routes = endpoints.SelectMany(endpoint =>
            (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => $"{method} {Normalize(endpoint.RoutePattern.RawText)}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/cutover", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/cutover/candidate", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/cutover/preview", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/cutover/confirmation", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/cutover/readiness", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/cutover/execution", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/cutover/post-cutover", routes);
    }

    [Theory]
    [InlineData(typeof(PrepareMigrationCutoverCandidateRequest))]
    [InlineData(typeof(CreateMigrationCutoverPreviewRequest))]
    [InlineData(typeof(ConfirmMigrationCutoverRequest))]
    [InlineData(typeof(ExecuteMigrationCutoverRequest))]
    public void Browser_request_contracts_do_not_accept_server_owned_cutover_identifiers(Type requestType)
    {
        var prohibited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CatalogEntryId",
            "RestoreSessionId",
            "CandidateId",
            "PreviewId",
            "ConfirmationId",
            "StagingRunId",
            "PrivateRuntimeStagingId",
            "HostPath",
            "DockerId",
            "ImageId"
        };

        var properties = requestType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(properties, property => prohibited.Contains(property));
    }

    private static RouteEndpoint[] GetEndpoints(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => Normalize(endpoint.RoutePattern.RawText).StartsWith(
                "/api/operator/migrations/sessions/{migrationId}/cutover",
                StringComparison.Ordinal))
            .ToArray();

    private static string Normalize(string? rawText)
    {
        var route = string.IsNullOrWhiteSpace(rawText) ? "/" : rawText;
        return route.Length > 1 ? route.TrimEnd('/') : route;
    }
}
