using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.AdvancedCutover.Candidate.Endpoints;
using HostAgent.Runtime.Backups.AdvancedCutover.Confirmation.Catalog;
using HostAgent.Runtime.Backups.AdvancedCutover.Confirmation.Catalog.Endpoints;
using HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog;
using HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog.Endpoints;
using HostAgent.Runtime.Backups.AdvancedCutover.PostCutover;
using HostAgent.Runtime.Backups.AdvancedCutover.PostCutover.Endpoints;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview.Endpoints;
using HostAgent.Runtime.Backups.AdvancedCutover.Readiness;
using HostAgent.Runtime.Backups.AdvancedCutover.Readiness.Endpoints;
using HostAgent.Runtime.Migrations.Acceptance;
using HostAgent.Runtime.Migrations.Acceptance.Endpoints;
using HostAgent.Runtime.Migrations.Cutover;
using HostAgent.Runtime.Migrations.Cutover.Endpoints;
using HostAgent.Runtime.Migrations.Staging;
using HostAgent.Runtime.Migrations.Staging.Endpoints;
using HostAgent.Runtime.Migrations.Staging.Retirement;
using HostAgent.Runtime.Migrations.Staging.Retirement.Endpoints;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Identity;
using Modules.Operator.Migrations;
using Modules.Operator.Migrations.Conversion;
using Modules.Operator.Migrations.Workspace;

namespace Api.IntegrationTests.Migrations;

/// <summary>
/// Records the current migration architecture and its retired transitional
/// surfaces. Update these guardrails only when a later slice deliberately changes
/// the corresponding runtime, persistence, or route boundary.
/// </summary>
public sealed class MigrationCurrentStateGuardrailTests
{
    [Fact]
    public async Task Current_routes_use_secure_sessions_and_retire_legacy_neutral_and_artifact_binding_surfaces()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production",
        });
        builder.Services.AddAuthorization();

        // Register endpoint parameter types so Minimal API binding recognises
        // them as services. The guardrail enumerates routes only and never
        // resolves or executes these services.
        builder.Services.AddScoped<MigrationSessionProjectionService>();
        builder.Services.AddScoped<MigrationSessionInventoryService>();
        builder.Services.AddScoped<MigrationSessionLifecycleService>();
        builder.Services.AddScoped<MigrationWorkspaceProjectionService>();
        builder.Services.AddScoped<MigrationSourceRequestService>();
        builder.Services.AddScoped<SecureMigrationIntakeService>();
        builder.Services.AddScoped<MigrationFinalPackageRecipientService>();
        builder.Services.AddScoped<MigrationFinalPackageUploadService>();
        builder.Services.AddScoped<MigrationConversionOrchestrator>();
        builder.Services.AddScoped<IMigrationConversionWorkerRunner, MigrationConversionWorkerRunner>();
        builder.Services.AddScoped<MigrationStagingOrchestrator>();
        builder.Services.AddScoped<MigrationStagingRetirementService>();
        builder.Services.AddScoped<MigrationCutoverFacadeService>();
        builder.Services.AddScoped<MigrationAcceptanceService>();
        builder.Services.AddScoped<MigrationFinishService>();
        builder.Services.AddScoped<RuntimeStackBackupProductionCandidateService>();
        builder.Services.AddScoped<RuntimeStackBackupPublicCutoverPreviewService>();
        builder.Services.AddScoped<CatalogPublicCutoverConfirmationService>();
        builder.Services.AddScoped<CatalogPublicCutoverConfirmationHistoryService>();
        builder.Services.AddScoped<CatalogPreCutoverReadinessService>();
        builder.Services.AddScoped<CatalogPublicCutoverExecutionService>();
        builder.Services.AddScoped<CatalogPublicCutoverExecutionHistoryService>();
        builder.Services.AddScoped<CatalogPostCutoverProjectionService>();

        await using var application = builder.Build();

        new MigrationSessionEndpoints().AddRoutes(application);
        new MigrationWorkspaceEndpoints().AddRoutes(application);
        new SecureMigrationIntakeEndpoints().AddRoutes(application);
        new MigrationConversionEndpoints().AddRoutes(application);
        new MigrationStagingEndpoints().AddRoutes(application);
        new MigrationStagingRetirementEndpoints().AddRoutes(application);
        new MigrationCutoverEndpoints().AddRoutes(application);
        new MigrationAcceptanceEndpoints().AddRoutes(application);
        new HostAgentCatalogProductionCandidateEndpoints().AddRoutes(application);
        new HostAgentCatalogPublicCutoverPreviewEndpoints().AddRoutes(application);
        new HostAgentCatalogPublicCutoverConfirmationEndpoints().AddRoutes(application);
        new HostAgentCatalogPreCutoverReadinessEndpoints().AddRoutes(application);
        new HostAgentCatalogPublicCutoverExecutionEndpoints().AddRoutes(application);
        new HostAgentCatalogPostCutoverProjectionEndpoints().AddRoutes(application);

        var routes = GetRouteContracts(application);

        Assert.DoesNotContain(
            routes,
            route => route.StartsWith(
                "GET /api/operator/migrations/intakes",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            routes,
            route => route.StartsWith(
                "POST /api/operator/migrations/intakes",
                StringComparison.Ordinal));

        Assert.DoesNotContain("GET /api/operator/migrations/sessions", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/inventory", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/workspace", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/lifecycle", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/lifecycle/archive", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/lifecycle/unarchive", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/lifecycle/cancel", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/lifecycle/delete", routes);
        Assert.Contains(
            "GET /api/operator/migrations/sessions/{migrationId}/package-revisions/{purpose}/source-request",
            routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/package-revisions/final-recipient", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/package-revisions/final/package", routes);

        var sessionEndpoints = GetRouteEndpoints(application)
            .Where(endpoint =>
                NormalizeRoutePattern(endpoint.RoutePattern.RawText)
                    .StartsWith(
                        "/api/operator/migrations/sessions",
                        StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(29, sessionEndpoints.Length);
        Assert.All(sessionEndpoints, endpoint =>
            Assert.Contains(
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                authorization => authorization.Policy ==
                    MemOperatorPolicies.MigrationIntakeOperate));
        Assert.Contains(
            "GET /api/operator/migrations/sessions/{migrationId}/conversion-attempts",
            routes);
        Assert.Contains(
            "GET /api/operator/migrations/sessions/{migrationId}/conversion-attempts/options",
            routes);
        Assert.Contains(
            "POST /api/operator/migrations/sessions/{migrationId}/conversion-attempts",
            routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/staging-runs", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/staging-runs", routes);
        Assert.DoesNotContain("POST /api/operator/migrations/sessions/{migrationId}/staging-runs/{stagingRunId}/destroy", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/staging-runs/{stagingRunId}/retirement", routes);

        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/cutover", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/cutover/candidate", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/cutover/preview", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/cutover/confirmation", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/cutover/readiness", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/cutover/execution", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/cutover/post-cutover", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/acceptance", routes);
        Assert.Contains("POST /api/operator/migrations/sessions/{migrationId}/acceptance", routes);
        Assert.Contains("GET /api/operator/migrations/sessions/{migrationId}/acceptance/report", routes);

        Assert.Contains("POST /api/operator/migrations/secure-intakes", routes);
        Assert.Contains("GET /api/operator/migrations/secure-intakes/{intakeId}", routes);
        Assert.Contains("POST /api/operator/migrations/secure-intakes/{intakeId}/package", routes);
        Assert.DoesNotContain(
            "POST /api/operator/migrations/secure-intakes/{intakeId}/cancel",
            routes);
        Assert.DoesNotContain(
            routes,
            route => route.Contains(
                "/api/operator/migrations/plaintext-intakes",
                StringComparison.Ordinal));

        Assert.DoesNotContain(
            routes,
            route => route.Contains(
                "/internal/host-agent/migrations/intakes/",
                StringComparison.Ordinal) &&
                route.Contains("/bind-catalog", StringComparison.Ordinal));

        Assert.Contains(
            "POST /internal/host-agent/backups/catalog/{catalogEntryId}/production-restore/candidates/recreate-private",
            routes);
        Assert.Contains(
            "POST /internal/host-agent/backups/catalog/{catalogEntryId}/production-restore/cutover-preview",
            routes);
        Assert.Contains(
            "POST /internal/host-agent/backups/catalog/{catalogEntryId}/production-restore/cutover-confirmations/evaluate",
            routes);
        Assert.Contains(
            "GET /internal/host-agent/backups/catalog/{catalogEntryId}/production-restore/pre-cutover-readiness",
            routes);
        Assert.Contains(
            "POST /internal/host-agent/backups/catalog/{catalogEntryId}/production-restore/cutover-executions/execute",
            routes);
        Assert.Contains(
            "GET /internal/host-agent/backups/catalog/{catalogEntryId}/production-restore/post-cutover",
            routes);

        Assert.Contains(
            routes,
            route => route.StartsWith("GET /api/operator/migrations/", StringComparison.Ordinal) &&
                     route.Contains("/cutover", StringComparison.Ordinal));
        Assert.Contains(
            routes,
            route => route.StartsWith("POST /api/operator/migrations/", StringComparison.Ordinal) &&
                     route.Contains("/cutover", StringComparison.Ordinal));
    }

    [Fact]
    public void Transitional_neutral_persistence_symbols_are_retired()
    {
        var retiredRootProperties = new[]
        {
            "ContractVersion",
            "Status",
            "ManifestJson",
            "ManifestSha256",
            "PreviewedAtUtc",
            "CommittedAtUtc",
            "AcceptedAtUtc",
            "AcceptedBy",
            "AbandonedAtUtc",
            "AbandonedBy",
            "SecureIntakeCreatedAtUtc",
            "ExpiresAtUtc",
            "AgeRecipient",
            "ProtectedAgeIdentity",
            "RecipientFingerprint",
            "PackageFileName",
            "PackageSizeBytes",
            "EncryptedPackageSha256",
            "DecryptedArchiveSha256",
            "PackageUploadedAtUtc",
            "PackageValidatedAtUtc",
            "ArchiveMigrationId",
            "ArchiveSourceProduct",
            "ArchiveSourceVersion",
            "ArchiveStackCount",
            "Mappings",
            "Validations",
        };

        Assert.All(
            retiredRootProperties,
            propertyName => Assert.Null(
                typeof(MigrationIntakeEntity).GetProperty(propertyName)));

        Assert.Null(typeof(MemDbContext).GetProperty("MigrationImportMappings"));
        Assert.Null(typeof(MemDbContext).GetProperty("MigrationValidations"));
    }

    private static HashSet<string> GetRouteContracts(WebApplication application) =>
        GetRouteEndpoints(application)
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>())
                .Select(method => $"{method} {NormalizeRoutePattern(endpoint.RoutePattern.RawText)}"))
            .ToHashSet(StringComparer.Ordinal);

    private static IReadOnlyList<RouteEndpoint> GetRouteEndpoints(
        WebApplication application) =>
        ((IEndpointRouteBuilder)application).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();

    private static string NormalizeRoutePattern(string? rawText)
    {
        var route = string.IsNullOrWhiteSpace(rawText) ? "/" : rawText;
        return route.Length > 1 ? route.TrimEnd('/') : route;
    }
}
