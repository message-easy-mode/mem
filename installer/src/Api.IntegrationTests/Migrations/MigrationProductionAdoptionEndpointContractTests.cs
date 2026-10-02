using System.Reflection;
using Carter;
using HostAgent.Runtime.Migrations.ProductionAdoption;
using HostAgent.Runtime.Migrations.ProductionAdoption.Endpoints;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationProductionAdoptionEndpointContractTests
{
    [Fact]
    public async Task Production_adoption_routes_are_migration_keyed_and_policy_protected()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddScoped<MigrationProductionAdoptionService>();
        builder.Services.AddScoped<MigrationProductionRuntimeMaterializationService>();
        builder.Services.AddScoped<MigrationPrivateServerCreationService>();
        builder.Services.AddScoped<MigrationPrivateServerTargetReviewService>();
        builder.Services.AddScoped<MigrationGoLiveService>();
        builder.Services.AddScoped<MigrationProductionCutoverService>();
        builder.Services.AddScoped<MigrationProductionVerificationService>();
        builder.Services.AddScoped<MigrationProductionRollbackService>();
        builder.Services.AddScoped<MigrationProductionRollbackCompletionService>();
        await using var application = builder.Build();

        var module = new MigrationProductionAdoptionEndpoints();
        Assert.IsAssignableFrom<ICarterModule>(module);
        module.AddRoutes(application);

        var endpoints = GetEndpoints(application);
        Assert.Equal(13, endpoints.Length);
        Assert.All(endpoints, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                authorization => authorization.Policy == MemOperatorPolicies.MigrationIntakeOperate));

        var routes = endpoints.SelectMany(endpoint =>
            (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])
                .Select(method => $"{method} {Normalize(endpoint.RoutePattern.RawText)}"))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/production-adoption", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/production-adoption/plan", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/production-adoption/materialize", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/production-adoption/private-server/review", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/production-adoption/private-server", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/production-adoption/go-live", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/production-adoption/cutover/preview", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/production-adoption/cutover/execution", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/production-adoption/verification", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/production-adoption/rollback/preview", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/production-adoption/rollback/execution", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/production-adoption/rollback/source-handoff", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/production-adoption/rollback/source-completion", routes);
    }

    [Fact]
    public void Public_cutover_mutations_require_recent_step_up_while_review_remains_read_only()
    {
        var executeCutover = typeof(MigrationProductionAdoptionEndpoints)
            .GetMethod("ExecuteCutoverAsync", BindingFlags.NonPublic | BindingFlags.Static);
        var materialize = typeof(MigrationProductionAdoptionEndpoints)
            .GetMethod("MaterializeAsync", BindingFlags.NonPublic | BindingFlags.Static);
        var reviewPrivateServerTarget = typeof(MigrationProductionAdoptionEndpoints)
            .GetMethod("ReviewPrivateServerTargetAsync", BindingFlags.NonPublic | BindingFlags.Static);
        var createPrivateServer = typeof(MigrationProductionAdoptionEndpoints)
            .GetMethod("CreatePrivateServerAsync", BindingFlags.NonPublic | BindingFlags.Static);
        var makeServerLive = typeof(MigrationProductionAdoptionEndpoints)
            .GetMethod("MakeServerLiveAsync", BindingFlags.NonPublic | BindingFlags.Static);
        var executeRollback = typeof(MigrationProductionAdoptionEndpoints)
            .GetMethod("ExecuteRollbackAsync", BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(executeCutover);
        Assert.Contains(
            executeCutover!.GetParameters(),
            parameter => parameter.ParameterType == typeof(IAuthorizationService));
        Assert.Contains(
            executeCutover.GetParameters(),
            parameter => parameter.ParameterType == typeof(MigrationProductionCutoverService));

        Assert.NotNull(materialize);
        Assert.Contains(
            materialize!.GetParameters(),
            parameter => parameter.ParameterType == typeof(IAuthorizationService));

        Assert.NotNull(reviewPrivateServerTarget);
        Assert.DoesNotContain(
            reviewPrivateServerTarget!.GetParameters(),
            parameter => parameter.ParameterType == typeof(IAuthorizationService));
        Assert.Contains(
            reviewPrivateServerTarget.GetParameters(),
            parameter => parameter.ParameterType == typeof(MigrationPrivateServerTargetReviewService));

        Assert.NotNull(createPrivateServer);
        Assert.Contains(
            createPrivateServer!.GetParameters(),
            parameter => parameter.ParameterType == typeof(IAuthorizationService));
        Assert.Contains(
            createPrivateServer.GetParameters(),
            parameter => parameter.ParameterType == typeof(MigrationPrivateServerCreationService));

        Assert.NotNull(makeServerLive);
        Assert.Contains(
            makeServerLive!.GetParameters(),
            parameter => parameter.ParameterType == typeof(IAuthorizationService));
        Assert.Contains(
            makeServerLive.GetParameters(),
            parameter => parameter.ParameterType == typeof(MigrationGoLiveService));

        Assert.NotNull(executeRollback);
        Assert.Contains(
            executeRollback!.GetParameters(),
            parameter => parameter.ParameterType == typeof(IAuthorizationService));
    }

    [Fact]
    public void Browser_request_cannot_supply_runtime_or_source_owned_identifiers()
    {
        var prohibited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "PackageRevisionId",
            "CandidateArtifactId",
            "StagingRunId",
            "RuntimeStackId",
            "MatrixInstanceId",
            "ElementInstanceId",
            "DatabaseName",
            "DatabaseUsername",
            "ContainerId",
            "ContainerName",
            "HostPath",
            "DockerId",
            "ImageId",
            "MatrixHost",
            "ElementHost",
        };

        var planningProperties = typeof(PrepareMigrationProductionAdoptionRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        var reviewProperties = typeof(ReviewMigrationPrivateServerTargetRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        var privateServerProperties = typeof(CreateMigrationPrivateServerRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        var materializationProperties = typeof(MaterializeMigrationProductionRuntimeRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        var goLiveProperties = typeof(MakeMigrationServerLiveRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        var cutoverProperties = typeof(ExecuteMigrationProductionCutoverRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        var verificationProperties = typeof(RunMigrationProductionVerificationRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();
        var rollbackProperties = typeof(ExecuteMigrationProductionRollbackRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(["TargetStackSlug", "ElementPublicHost"], planningProperties);
        Assert.Equal(["TargetStackSlug", "ElementPublicHost"], reviewProperties);
        Assert.DoesNotContain(reviewProperties, property => prohibited.Contains(property));
        Assert.Contains("ElementPublicHost", planningProperties);
        Assert.DoesNotContain(planningProperties, property => prohibited.Contains(property));
        Assert.Equal(["TargetStackSlug", "ElementPublicHost", "ConfirmVerifiedSnapshotIsAuthoritative", "ConfirmLaterSourceWritesAreNotIncluded", "ConfirmCreatePrivateServer"], privateServerProperties);
        Assert.DoesNotContain(privateServerProperties, property => prohibited.Contains(property));
        Assert.DoesNotContain(materializationProperties, property => prohibited.Contains(property));
        Assert.Equal(["ConfirmMovePublicTraffic", "ConfirmStopUsingOldServer", "ConfirmRunLiveVerification"], goLiveProperties);
        Assert.DoesNotContain(goLiveProperties, property => prohibited.Contains(property));
        Assert.Contains("ExecutePrivateProductionMaterialization", materializationProperties);
        Assert.Contains("AcknowledgeNoPublicRoutes", materializationProperties);
        Assert.DoesNotContain(cutoverProperties, property => prohibited.Contains(property));
        Assert.Contains("PreviewId", cutoverProperties);
        Assert.Contains("ExecuteNpmRouteMutation", cutoverProperties);
        Assert.Contains("AcknowledgeRollbackIsNextSlice", cutoverProperties);
        Assert.DoesNotContain(verificationProperties, property => prohibited.Contains(property));
        Assert.Equal(["Operator", "Note", "FreshnessMinutes"], verificationProperties);
        Assert.DoesNotContain(rollbackProperties, property => prohibited.Contains(property));
        Assert.Contains("PreviewId", rollbackProperties);
        Assert.Contains("ExecuteTargetRollback", rollbackProperties);
        Assert.Contains("AcknowledgeNoAutomaticSourceHostMutation", rollbackProperties);
    }

    private static RouteEndpoint[] GetEndpoints(WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => Normalize(endpoint.RoutePattern.RawText).StartsWith(
                "/api/operator/migrations/sessions/{migrationId}/production-adoption",
                StringComparison.Ordinal))
            .ToArray();

    private static string Normalize(string? rawText)
    {
        var route = string.IsNullOrWhiteSpace(rawText) ? "/" : rawText;
        return route.Length > 1 ? route.TrimEnd('/') : route;
    }
}
