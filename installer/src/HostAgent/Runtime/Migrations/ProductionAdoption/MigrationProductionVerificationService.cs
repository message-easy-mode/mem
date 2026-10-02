using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Commands;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Readiness;
using HostAgent.Runtime.ServiceRuntime;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Npm.Services;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Migrations.ProductionAdoption;

/// <summary>
/// Runs and durably records the explicit post-cutover verification gate for a migrated normal
/// MEM runtime. Verification is read-only with respect to Matrix data and public routing; only
/// control-plane evidence, readiness reports, and normal runtime observation state are updated.
/// </summary>
public sealed class MigrationProductionVerificationService(
    MemDbContext db,
    MigrationProductionAdoptionService adoptionService,
    RuntimeReadinessVerifier readinessVerifier,
    RuntimeReadinessReportStore readinessReportStore,
    RuntimeStackManifestStore manifestStore,
    IRuntimeStackPostgresQueryExecutor queryExecutor,
    NpmProxyHostService npmProxyHostService,
    DockerClient docker,
    TimeProvider timeProvider,
    IMemDiagnosticEventWriter? diagnostics = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<MigrationProductionAdoptionStateResponse> VerifyAsync(
        string migrationId,
        RunMigrationProductionVerificationRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = NormalizeRetainedFreshnessRequest(request.FreshnessMinutes);

        var plan = await LoadPlanAsync(migrationId, tracking: true, ct)
            ?? throw new InvalidOperationException(
                "Complete normal-runtime production adoption and controlled public cutover before verification.");
        var accepted = await db.MigrationAcceptances.AsNoTracking()
            .AnyAsync(x => x.MigrationIntakeEntityId == plan.MigrationIntakeEntityId, ct);
        var blockers = BuildEligibilityBlockers(
            plan,
            accepted,
            MigrationProductionAdoptionService.ResolveStaleReason(plan));
        if (blockers.Count > 0)
        {
            throw new InvalidOperationException(
                $"Production verification is blocked: {string.Join("; ", blockers)}");
        }

        var startedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var verificationId = CreateId("mpvf", startedAtUtc);
        plan.ProductionVerificationId = verificationId;
        plan.ProductionVerificationStatus = "running";
        plan.ProductionVerificationStartedAtUtc = startedAtUtc;
        plan.ProductionVerificationCompletedAtUtc = null;
        plan.ProductionVerificationValidUntilUtc = null;
        plan.ProductionVerificationCheckCount = 0;
        plan.ProductionVerificationFailedCheckCount = 0;
        plan.ProductionVerificationEvidenceSha256 = null;
        plan.ProductionVerificationEvidenceJson = null;
        plan.ProductionVerificationReadinessReportId = null;
        plan.ProductionVerificationFailureCode = null;
        plan.ProductionVerificationFailureSummary = null;
        plan.Status = "production-verification-running";
        plan.BlockerSummary = null;
        plan.UpdatedAtUtc = startedAtUtc;
        await db.SaveChangesAsync(ct);
        await MigrationProductionDiagnosticEvents.RecordAsync(
            diagnostics,
            migrationId,
            eventCode: "migration.production.verification.started",
            severity: MemDiagnosticSeverities.Information,
            stage: "production-verification",
            message: "Migration production verification started.",
            targetStackSlug: plan.TargetStackSlug,
            service: "synapse",
            details: new Dictionary<string, string?>
            {
                ["adoptionPlanId"] = plan.AdoptionPlanId,
                ["verificationId"] = verificationId
            });

        try
        {
            var checks = new List<MigrationProductionVerificationCheckDto>();
            var runtime = await LoadRuntimeOwnershipAsync(plan, ct);
            checks.AddRange(runtime.Checks);

            RuntimeStackManifest? manifest = null;
            if (runtime.IdentityPassed)
            {
                manifest = await manifestStore.FindAsync(plan.RuntimeStackId.ToString(), ct);
                checks.Add(VerifyManifest(plan, manifest));
                checks.Add(await VerifyContainerAsync(
                    ServiceKeys.Matrix,
                    plan.MatrixContainerName,
                    plan.MatrixImageId,
                    runtime.MatrixService?.ContainerId,
                    ct));
                checks.Add(await VerifyContainerAsync(
                    ServiceKeys.ElementWeb,
                    plan.ElementContainerName,
                    plan.ElementImageId,
                    runtime.ElementService?.ContainerId,
                    ct));
                checks.AddRange(await VerifyLiveRoutesAsync(plan, runtime.Routes, ct));
                checks.AddRange(await VerifyDatabaseCountsAsync(plan, ct));
            }
            else
            {
                checks.Add(new MigrationProductionVerificationCheckDto(
                    "migration-production.runtime-probes.skipped",
                    "Runtime probes",
                    false,
                    "Container, database, route, and HTTP probes were not run because durable normal-runtime ownership did not match the adoption plan."));
            }

            RuntimeReadinessVerificationResult? readiness = null;
            Guid? readinessReportId = null;
            var safeToProbeReadiness = runtime.IdentityPassed &&
                manifest is not null &&
                checks.Where(x =>
                        x.Code.StartsWith("migration-production.container.", StringComparison.Ordinal) ||
                        x.Code.StartsWith("migration-production.route.live.", StringComparison.Ordinal))
                    .All(x => x.Success);
            if (safeToProbeReadiness)
            {
                readiness = await readinessVerifier.VerifyAsync(
                    new RuntimeReadinessVerificationRequest(
                        MatrixInternalBaseUrl: $"http://{plan.MatrixContainerName}:8008",
                        ElementInternalBaseUrl: $"http://{plan.ElementContainerName}",
                        MatrixPublicBaseUrl: plan.MatrixPublicBaseUrl,
                        ElementPublicBaseUrl: plan.ElementPublicBaseUrl,
                        MatrixPublicHost: plan.MatrixPublicHost,
                        ElementPublicHost: plan.ElementPublicHost,
                        MatrixForwardHost: plan.MatrixContainerName,
                        MatrixForwardPort: 8008,
                        ElementForwardHost: plan.ElementContainerName,
                        ElementForwardPort: 80,
                        ExpectedNpmCertificateId: null),
                    ct);
                checks.AddRange(readiness.Checks.Select(ToVerificationCheck));

                try
                {
                    readinessReportId = await readinessReportStore.SaveAsync(
                        manifest!,
                        readiness,
                        "migration-production-verification",
                        NormalizeOperator(request.Operator),
                        operationId: null,
                        ct: ct);
                    checks.Add(new MigrationProductionVerificationCheckDto(
                        "migration-production.readiness-report.saved",
                        "Runtime readiness report persistence",
                        true,
                        $"Runtime readiness report '{readinessReportId}' was saved."));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    checks.Add(new MigrationProductionVerificationCheckDto(
                        "migration-production.readiness-report.saved",
                        "Runtime readiness report persistence",
                        false,
                        SafeDetail(ex.Message)));
                }
            }
            else
            {
                checks.Add(new MigrationProductionVerificationCheckDto(
                    "migration-production.public-readiness.skipped",
                    "Public runtime readiness probes",
                    false,
                    "Public Matrix and Element readiness probes were not run because runtime identity, container, route, or manifest authority was incomplete."));
            }

            var preliminaryPassed = checks.Count > 0 && checks.All(x => x.Success);
            if (manifest is not null)
            {
                checks.Add(await PersistManifestObservationAsync(
                    plan,
                    manifest,
                    verificationId,
                    preliminaryPassed,
                    request.Note,
                    ct));
            }

            var completedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            var passed = checks.Count > 0 && checks.All(x => x.Success);
            DateTime? validUntilUtc = null;
            var failedChecks = checks.Where(x => !x.Success).ToArray();
            var failureSummary = failedChecks.Length == 0
                ? null
                : SafeDetail($"Failed checks: {string.Join(", ", failedChecks.Select(x => x.Code))}");
            var evidence = new ProductionVerificationEvidence(
                VerificationId: verificationId,
                MigrationId: migrationId,
                AdoptionPlanId: plan.AdoptionPlanId,
                CutoverExecutionId: plan.CutoverExecutionId!,
                PlanSha256: plan.PlanSha256,
                PackageRevisionId: plan.PackageRevision.PackageRevisionId,
                CandidateArtifactId: plan.CandidateArtifact.CandidateArtifactId,
                StagingRunId: plan.StagingRun.StagingRunId,
                RuntimeStackId: plan.RuntimeStackId,
                TargetStackSlug: plan.TargetStackSlug,
                MatrixServerName: plan.MatrixServerName,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: completedAtUtc,
                ValidUntilUtc: validUntilUtc,
                Operator: NormalizeOperator(request.Operator),
                Note: NormalizeNote(request.Note),
                Passed: passed,
                ReadinessReportId: readinessReportId,
                Checks: checks);
            var evidenceJson = JsonSerializer.Serialize(evidence, JsonOptions);
            var evidenceSha256 = ComputeEvidenceSha256(evidenceJson);

            plan.ProductionVerificationStatus = passed ? "passed" : "failed";
            plan.ProductionVerificationCompletedAtUtc = completedAtUtc;
            plan.ProductionVerificationValidUntilUtc = validUntilUtc;
            plan.ProductionVerificationCheckCount = checks.Count;
            plan.ProductionVerificationFailedCheckCount = failedChecks.Length;
            plan.ProductionVerificationEvidenceSha256 = evidenceSha256;
            plan.ProductionVerificationEvidenceJson = evidenceJson;
            plan.ProductionVerificationReadinessReportId = readinessReportId;
            plan.ProductionVerificationFailureCode = passed ? null : "production_verification_failed";
            plan.ProductionVerificationFailureSummary = failureSummary;
            plan.Status = passed
                ? "production-verification-passed"
                : "production-verification-failed";
            plan.BlockerSummary = failureSummary;
            plan.UpdatedAtUtc = completedAtUtc;

            if (runtime.Stack is not null)
            {
                runtime.Stack.Status = passed
                    ? "migration-production-verified"
                    : "migration-production-verification-failed";
                runtime.Stack.LastVerifiedStatus = runtime.Stack.Status;
                runtime.Stack.LastVerifiedAtUtc = completedAtUtc;
                runtime.Stack.UpdatedAtUtc = completedAtUtc;
                foreach (var service in runtime.Stack.ServiceInstances)
                {
                    service.Status = runtime.Stack.Status;
                    service.LastObservedAtUtc = completedAtUtc;
                    service.UpdatedAtUtc = completedAtUtc;
                }
                foreach (var route in runtime.Stack.Routes)
                {
                    route.Status = passed ? "verified" : "verification-failed";
                    route.LastVerifiedAtUtc = completedAtUtc;
                    route.LastError = passed ? null : failureSummary;
                }
            }

            await db.SaveChangesAsync(ct);
            await MigrationProductionDiagnosticEvents.RecordAsync(
                diagnostics,
                migrationId,
                eventCode: passed
                    ? "migration.production.verification.passed"
                    : "migration.production.verification.failed",
                severity: passed
                    ? MemDiagnosticSeverities.Information
                    : MemDiagnosticSeverities.Error,
                stage: "production-verification",
                message: passed
                    ? "Migration production verification passed."
                    : "Migration production verification failed.",
                targetStackSlug: plan.TargetStackSlug,
                createIncident: !passed,
                service: "synapse",
                expected: new Dictionary<string, string?>
                {
                    ["failedCheckCount"] = "0"
                },
                observed: new Dictionary<string, string?>
                {
                    ["status"] = plan.ProductionVerificationStatus,
                    ["failedCheckCount"] = failedChecks.Length.ToString(CultureInfo.InvariantCulture)
                },
                details: new Dictionary<string, string?>
                {
                    ["adoptionPlanId"] = plan.AdoptionPlanId,
                    ["verificationId"] = verificationId,
                    ["failureSummary"] = failureSummary
                },
                retryable: !passed,
                suggestedAction: !passed
                    ? "Open the Migration Workspace and review the failed production checks before retrying verification."
                    : null);
            return await adoptionService.GetStateAsync(migrationId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await PersistUnexpectedFailureAsync(
                plan,
                verificationId,
                startedAtUtc,
                "production_verification_cancelled",
                "Production verification was cancelled before completion.",
                CancellationToken.None);
            await MigrationProductionDiagnosticEvents.RecordAsync(
                diagnostics,
                migrationId,
                eventCode: "migration.production.verification.cancelled",
                severity: MemDiagnosticSeverities.Warning,
                stage: "production-verification",
                message: "Migration production verification was cancelled before completion.",
                targetStackSlug: plan.TargetStackSlug,
                service: "synapse",
                observed: new Dictionary<string, string?>
                {
                    ["status"] = plan.ProductionVerificationStatus
                },
                details: new Dictionary<string, string?>
                {
                    ["adoptionPlanId"] = plan.AdoptionPlanId,
                    ["verificationId"] = verificationId
                });
            throw;
        }
        catch (Exception ex)
        {
            await PersistUnexpectedFailureAsync(
                plan,
                verificationId,
                startedAtUtc,
                "production_verification_failed",
                SafeDetail(ex.Message),
                CancellationToken.None);
            await MigrationProductionDiagnosticEvents.RecordAsync(
                diagnostics,
                migrationId,
                eventCode: "migration.production.verification.failed",
                severity: MemDiagnosticSeverities.Error,
                stage: "production-verification",
                message: "Migration production verification failed unexpectedly.",
                targetStackSlug: plan.TargetStackSlug,
                createIncident: true,
                exception: ex,
                service: "synapse",
                expected: new Dictionary<string, string?>
                {
                    ["status"] = "passed"
                },
                observed: new Dictionary<string, string?>
                {
                    ["status"] = plan.ProductionVerificationStatus,
                    ["failureCode"] = plan.ProductionVerificationFailureCode
                },
                details: new Dictionary<string, string?>
                {
                    ["adoptionPlanId"] = plan.AdoptionPlanId,
                    ["verificationId"] = verificationId,
                    ["failureSummary"] = plan.ProductionVerificationFailureSummary
                },
                retryable: true,
                suggestedAction: "Open the Migration Workspace and review the retained production verification evidence before retrying.");
            return await adoptionService.GetStateAsync(migrationId, CancellationToken.None);
        }
    }

    internal static IReadOnlyList<string> BuildEligibilityBlockers(
        MigrationProductionAdoptionEntity plan,
        bool accepted,
        string? staleReason)
    {
        var blockers = new List<string>();
        if (accepted)
        {
            blockers.Add("Migration production verification cannot be rerun after acceptance.");
        }
        if (!string.IsNullOrWhiteSpace(staleReason))
        {
            blockers.Add(staleReason);
        }
        var statusAllowed =
            string.Equals(plan.Status, "public-awaiting-verification", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plan.Status, "production-verification-running", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plan.Status, "production-verification-passed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(plan.Status, "production-verification-failed", StringComparison.OrdinalIgnoreCase);
        if (!statusAllowed ||
            !string.Equals(plan.CutoverStatus, "public-awaiting-verification", StringComparison.OrdinalIgnoreCase) ||
            !plan.PublicRoutesCreated ||
            !plan.RuntimePromotionCompleted ||
            string.IsNullOrWhiteSpace(plan.CutoverExecutionId) ||
            plan.TargetPublicAtUtc is null)
        {
            blockers.Add("A completed controlled public cutover owned by the normal MEM runtime is required.");
        }
        if (!string.IsNullOrWhiteSpace(plan.RollbackExecutionId) ||
            !string.IsNullOrWhiteSpace(plan.RollbackStatus) ||
            !string.IsNullOrWhiteSpace(plan.RollbackCompletionStatus))
        {
            blockers.Add("Production verification is unavailable after the coordinated rollback lifecycle begins.");
        }
        return blockers.Distinct(StringComparer.Ordinal).ToArray();
    }

    internal static int? NormalizeRetainedFreshnessRequest(int? requested)
    {
        // Retained only for wire compatibility with older Web clients. Migration verification
        // remains valid until superseded or invalidated by a relevant lifecycle mutation.
        return requested;
    }

    internal static string ComputeEvidenceSha256(string evidenceJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(evidenceJson))).ToLowerInvariant();

    internal static MigrationProductionVerificationCheckDto BuildCountCheck(
        string label,
        long? expected,
        long? actual)
    {
        var success = actual.HasValue && (!expected.HasValue || actual.Value == expected.Value);
        var detail = success
            ? expected.HasValue
                ? $"The production database {label} count is {actual} and matches verified private staging evidence."
                : $"The production database {label} count is {actual}. No staging count was available for comparison."
            : $"The production database {label} count was '{actual?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}' but expected '{expected?.ToString(CultureInfo.InvariantCulture) ?? "available"}'.";
        return new MigrationProductionVerificationCheckDto(
            $"migration-production.database.{label}-count",
            $"Production database {label} count",
            success,
            detail);
    }

    private async Task<RuntimeOwnershipResult> LoadRuntimeOwnershipAsync(
        MigrationProductionAdoptionEntity plan,
        CancellationToken ct)
    {
        var checks = new List<MigrationProductionVerificationCheckDto>();
        var stack = await db.RuntimeStacks
            .Include(x => x.ServiceInstances)
            .Include(x => x.Routes)
            .SingleOrDefaultAsync(x => x.Id == plan.RuntimeStackId, ct);
        checks.Add(new MigrationProductionVerificationCheckDto(
            "migration-production.runtime-stack.identity",
            "Normal Runtime Stack identity",
            stack is not null &&
                string.Equals(stack.Slug, plan.TargetStackSlug, StringComparison.Ordinal) &&
                stack.MatrixInstanceId == plan.MatrixInstanceId &&
                stack.ElementInstanceId == plan.ElementInstanceId,
            stack is null
                ? "The normal Runtime Stack record was not found."
                : $"Runtime Stack '{stack.Id}' is bound to slug '{stack.Slug}'."));

        var matrix = stack?.ServiceInstances.SingleOrDefault(x => x.ServiceKey == ServiceKeys.Matrix);
        var element = stack?.ServiceInstances.SingleOrDefault(x => x.ServiceKey == ServiceKeys.ElementWeb);
        checks.Add(VerifyServiceOwnership(
            "matrix",
            matrix,
            plan.MatrixInstanceId,
            plan.MatrixContainerName));
        checks.Add(VerifyServiceOwnership(
            "element",
            element,
            plan.ElementInstanceId,
            plan.ElementContainerName));

        var database = await db.RuntimeStackDatabases
            .SingleOrDefaultAsync(x => x.RuntimeStackId == plan.RuntimeStackId, ct);
        checks.Add(new MigrationProductionVerificationCheckDto(
            "migration-production.database.ownership",
            "Normal runtime database ownership",
            database is not null &&
                string.Equals(database.DatabaseEngine, plan.DatabaseEngine, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(database.DatabaseHost, plan.DatabaseHost, StringComparison.OrdinalIgnoreCase) &&
                database.DatabasePort == plan.DatabasePort &&
                string.Equals(database.DatabaseName, plan.DatabaseName, StringComparison.Ordinal) &&
                string.Equals(database.DatabaseUsername, plan.DatabaseUsername, StringComparison.Ordinal) &&
                string.Equals(database.PasswordSecretKind, plan.DatabasePasswordSecretKind, StringComparison.Ordinal),
            database is null
                ? "The normal runtime database ownership record was not found."
                : $"Database '{database.DatabaseName}' is owned by Runtime Stack '{database.RuntimeStackId}'."));

        var routes = stack?.Routes.ToArray() ?? [];
        checks.Add(VerifyRouteOwnership(
            "matrix",
            routes.SingleOrDefault(x => x.ServiceKey == ServiceKeys.Matrix),
            plan.MatrixPublicHost,
            plan.MatrixContainerName,
            8008,
            plan.MatrixNpmRouteId));
        checks.Add(VerifyRouteOwnership(
            "element",
            routes.SingleOrDefault(x => x.ServiceKey == ServiceKeys.ElementWeb),
            plan.ElementPublicHost,
            plan.ElementContainerName,
            80,
            plan.ElementNpmRouteId));

        return new RuntimeOwnershipResult(
            stack,
            matrix,
            element,
            routes,
            checks,
            checks.All(x => x.Success));
    }

    private static MigrationProductionVerificationCheckDto VerifyServiceOwnership(
        string label,
        RuntimeServiceInstanceEntity? service,
        Guid expectedInstanceId,
        string expectedContainerName)
    {
        var success = service is not null &&
            service.InstanceId == expectedInstanceId &&
            string.Equals(service.ContainerName, expectedContainerName, StringComparison.Ordinal);
        return new MigrationProductionVerificationCheckDto(
            $"migration-production.service.{label}.ownership",
            $"{label} service ownership",
            success,
            service is null
                ? $"The normal runtime {label} service record was not found."
                : $"Service instance '{service.InstanceId}' owns container '{service.ContainerName}'.");
    }

    private static MigrationProductionVerificationCheckDto VerifyRouteOwnership(
        string label,
        RuntimeRouteEntity? route,
        string expectedPublicHost,
        string expectedForwardHost,
        int expectedForwardPort,
        string? expectedProviderRouteId)
    {
        var success = route is not null &&
            route.IsPublic &&
            string.Equals(route.Provider, "npm", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(route.PublicHost, expectedPublicHost, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(route.ForwardScheme, "http", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(route.ForwardHost, expectedForwardHost, StringComparison.Ordinal) &&
            route.ForwardPort == expectedForwardPort &&
            string.Equals(route.ProviderRouteId, expectedProviderRouteId, StringComparison.Ordinal);
        return new MigrationProductionVerificationCheckDto(
            $"migration-production.route.{label}.ownership",
            $"{label} RuntimeRoute ownership",
            success,
            route is null
                ? $"The normal runtime {label} public route record was not found."
                : $"RuntimeRoute '{route.Id}' owns NPM route '{route.ProviderRouteId}' for '{route.PublicHost}'.");
    }

    private static MigrationProductionVerificationCheckDto VerifyManifest(
        MigrationProductionAdoptionEntity plan,
        RuntimeStackManifest? manifest)
    {
        var success = manifest is not null &&
            manifest.StackId == plan.RuntimeStackId &&
            string.Equals(manifest.Slug, plan.TargetStackSlug, StringComparison.Ordinal) &&
            manifest.Matrix.InstanceId == plan.MatrixInstanceId &&
            manifest.Element?.InstanceId == plan.ElementInstanceId &&
            string.Equals(manifest.Matrix.PublicRouteId, plan.MatrixNpmRouteId, StringComparison.Ordinal) &&
            string.Equals(manifest.Element?.PublicRouteId, plan.ElementNpmRouteId, StringComparison.Ordinal);
        return new MigrationProductionVerificationCheckDto(
            "migration-production.runtime-manifest.identity",
            "Normal runtime manifest identity",
            success,
            manifest is null
                ? "The normal runtime manifest was not found."
                : $"Runtime manifest for Stack '{manifest.StackId}' retains both public route identities.");
    }

    private async Task<MigrationProductionVerificationCheckDto> VerifyContainerAsync(
        string serviceKey,
        string containerName,
        string expectedImageId,
        string? expectedContainerId,
        CancellationToken ct)
    {
        try
        {
            var inspect = await docker.Containers.InspectContainerAsync(containerName, ct);
            var problems = new List<string>();
            if (inspect.State?.Running is not true)
            {
                problems.Add("container is not running");
            }
            if (!string.IsNullOrWhiteSpace(expectedContainerId) &&
                !string.Equals(inspect.ID, expectedContainerId, StringComparison.Ordinal))
            {
                problems.Add("container id differs from normal runtime ownership");
            }
            if (!string.Equals(inspect.Image, expectedImageId, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add("resolved image id differs from the approved production adoption identity");
            }
            if ((inspect.HostConfig?.PortBindings?.Count ?? 0) > 0)
            {
                problems.Add("container unexpectedly publishes host ports");
            }
            if (inspect.State?.Health is not null &&
                !string.Equals(inspect.State.Health.Status, "healthy", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"container health is '{inspect.State.Health.Status ?? "unknown"}'");
            }
            return new MigrationProductionVerificationCheckDto(
                $"migration-production.container.{serviceKey}.identity",
                $"{serviceKey} production container identity",
                problems.Count == 0,
                problems.Count == 0
                    ? $"Container '{containerName}' is running with the approved image identity and no host ports."
                    : $"Container '{containerName}': {string.Join("; ", problems)}.");
        }
        catch (DockerApiException ex)
        {
            return new MigrationProductionVerificationCheckDto(
                $"migration-production.container.{serviceKey}.identity",
                $"{serviceKey} production container identity",
                false,
                SafeDetail(ex.Message));
        }
    }

    private async Task<IReadOnlyList<MigrationProductionVerificationCheckDto>> VerifyLiveRoutesAsync(
        MigrationProductionAdoptionEntity plan,
        IReadOnlyList<RuntimeRouteEntity> routes,
        CancellationToken ct)
    {
        var checks = new List<MigrationProductionVerificationCheckDto>();
        checks.Add(await VerifyLiveRouteAsync(
            "matrix",
            plan.MatrixPublicHost,
            plan.MatrixContainerName,
            8008,
            plan.MatrixNpmRouteId,
            routes.SingleOrDefault(x => x.ServiceKey == ServiceKeys.Matrix)?.NpmCertificateId,
            ct));
        checks.Add(await VerifyLiveRouteAsync(
            "element",
            plan.ElementPublicHost,
            plan.ElementContainerName,
            80,
            plan.ElementNpmRouteId,
            routes.SingleOrDefault(x => x.ServiceKey == ServiceKeys.ElementWeb)?.NpmCertificateId,
            ct));
        return checks;
    }

    private async Task<MigrationProductionVerificationCheckDto> VerifyLiveRouteAsync(
        string label,
        string publicHost,
        string expectedForwardHost,
        int expectedForwardPort,
        string? expectedRouteId,
        int? expectedCertificateId,
        CancellationToken ct)
    {
        try
        {
            var current = await npmProxyHostService.GetByDomainAsync(publicHost, ct);
            var success = current is not null &&
                string.Equals(current.id.ToString(CultureInfo.InvariantCulture), expectedRouteId, StringComparison.Ordinal) &&
                string.Equals(current.forward_scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(current.forward_host, expectedForwardHost, StringComparison.OrdinalIgnoreCase) &&
                current.forward_port == expectedForwardPort &&
                current.enabled == true &&
                current.ssl_forced == true &&
                current.meta?.nginx_online != false &&
                (!expectedCertificateId.HasValue || current.certificate_id == expectedCertificateId.Value);
            return new MigrationProductionVerificationCheckDto(
                $"migration-production.route.live.{label}",
                $"Live {label} NPM route",
                success,
                current is null
                    ? $"No live NPM route exists for '{publicHost}'."
                    : $"NPM route '{current.id}' forwards '{publicHost}' to '{current.forward_host}:{current.forward_port}'.",
                $"npm://proxy-hosts/{publicHost}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MigrationProductionVerificationCheckDto(
                $"migration-production.route.live.{label}",
                $"Live {label} NPM route",
                false,
                SafeDetail(ex.Message),
                $"npm://proxy-hosts/{publicHost}");
        }
    }

    private async Task<IReadOnlyList<MigrationProductionVerificationCheckDto>> VerifyDatabaseCountsAsync(
        MigrationProductionAdoptionEntity plan,
        CancellationToken ct)
    {
        try
        {
            var publicTables = await ReadLongAsync(
                plan.DatabaseName,
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';",
                ct);
            var synapseTables = await ReadLongAsync(
                plan.DatabaseName,
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('users','events','rooms','state_events','event_json','room_memberships');",
                ct);
            var users = await ReadOptionalTableCountAsync(plan.DatabaseName, "users", ct);
            var rooms = await ReadOptionalTableCountAsync(plan.DatabaseName, "rooms", ct);
            var events = await ReadOptionalTableCountAsync(plan.DatabaseName, "events", ct);
            return
            [
                new MigrationProductionVerificationCheckDto(
                    "migration-production.database.synapse-schema",
                    "Production Synapse database schema",
                    publicTables > 0 && synapseTables > 0,
                    $"The production database contains {publicTables} public tables and {synapseTables} known Synapse tables."),
                BuildCountCheck("users", plan.ExpectedUsersCount, users),
                BuildCountCheck("rooms", plan.ExpectedRoomsCount, rooms),
                BuildCountCheck("events", plan.ExpectedEventsCount, events),
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return
            [
                new MigrationProductionVerificationCheckDto(
                    "migration-production.database.probe",
                    "Production database verification",
                    false,
                    SafeDetail(ex.Message)),
            ];
        }
    }

    private async Task<long?> ReadOptionalTableCountAsync(
        string databaseName,
        string tableName,
        CancellationToken ct)
    {
        var exists = await ReadLongAsync(
            databaseName,
            $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = '{tableName}';",
            ct);
        return exists == 0
            ? null
            : await ReadLongAsync(databaseName, $"SELECT COUNT(*) FROM {tableName};", ct);
    }

    private async Task<long> ReadLongAsync(
        string databaseName,
        string sql,
        CancellationToken ct)
    {
        var result = await queryExecutor.QueryAsync(databaseName, sql, ct);
        var value = result.Stdout
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidDataException(
                "The production PostgreSQL verification query returned an invalid count.");
    }

    private async Task<MigrationProductionVerificationCheckDto> PersistManifestObservationAsync(
        MigrationProductionAdoptionEntity plan,
        RuntimeStackManifest manifest,
        string verificationId,
        bool passed,
        string? note,
        CancellationToken ct)
    {
        try
        {
            var observedAt = timeProvider.GetUtcNow();
            var status = passed
                ? "migration-production-verified"
                : "migration-production-verification-failed";
            var metadata = manifest.Metadata
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            metadata["migrationProductionVerificationId"] = verificationId;
            metadata["migrationProductionVerificationStatus"] = passed ? "passed" : "failed";
            metadata["migrationAcceptancePending"] = "true";
            metadata["migrationVerificationNote"] = NormalizeNote(note);

            var matrixMetadata = manifest.Matrix.RuntimeMetadata
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            matrixMetadata["readinessVerified"] = passed ? "true" : "false";
            matrixMetadata["productionVerificationId"] = verificationId;
            matrixMetadata["acceptancePending"] = "true";
            RuntimeStackServiceManifest? element = null;
            if (manifest.Element is not null)
            {
                var elementMetadata = manifest.Element.RuntimeMetadata
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                elementMetadata["readinessVerified"] = passed ? "true" : "false";
                elementMetadata["productionVerificationId"] = verificationId;
                elementMetadata["acceptancePending"] = "true";
                element = manifest.Element with { RuntimeMetadata = elementMetadata };
            }
            var updated = manifest with
            {
                LastVerifiedStatus = status,
                LastVerifiedAtUtc = observedAt,
                Matrix = manifest.Matrix with { RuntimeMetadata = matrixMetadata },
                Element = element,
                Warnings = passed
                    ? ["Migration acceptance remains pending. Coordinated pre-acceptance rollback is still available."]
                    : ["Production verification failed. Migration acceptance remains blocked."],
                Metadata = metadata,
            };
            var path = Path.GetFullPath(plan.ManifestPath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The normal runtime manifest file was not found.", path);
            }
            await File.WriteAllTextAsync(
                path,
                JsonSerializer.Serialize(updated, new JsonSerializerOptions(JsonOptions) { WriteIndented = true }),
                ct);
            return new MigrationProductionVerificationCheckDto(
                "migration-production.runtime-manifest.observation-saved",
                "Runtime manifest verification observation",
                true,
                $"Runtime manifest '{path}' records verification '{verificationId}'.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new MigrationProductionVerificationCheckDto(
                "migration-production.runtime-manifest.observation-saved",
                "Runtime manifest verification observation",
                false,
                SafeDetail(ex.Message));
        }
    }

    private async Task PersistUnexpectedFailureAsync(
        MigrationProductionAdoptionEntity plan,
        string verificationId,
        DateTime startedAtUtc,
        string failureCode,
        string failureSummary,
        CancellationToken ct)
    {
        var completedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        var check = new MigrationProductionVerificationCheckDto(
            "migration-production.verification.unexpected-failure",
            "Production verification execution",
            false,
            failureSummary);
        var evidence = new ProductionVerificationEvidence(
            verificationId,
            plan.MigrationIntake.IntakeId,
            plan.AdoptionPlanId,
            plan.CutoverExecutionId ?? "unavailable",
            plan.PlanSha256,
            plan.PackageRevision.PackageRevisionId,
            plan.CandidateArtifact.CandidateArtifactId,
            plan.StagingRun.StagingRunId,
            plan.RuntimeStackId,
            plan.TargetStackSlug,
            plan.MatrixServerName,
            startedAtUtc,
            completedAtUtc,
            null,
            "operator",
            null,
            false,
            null,
            [check]);
        var evidenceJson = JsonSerializer.Serialize(evidence, JsonOptions);
        plan.ProductionVerificationStatus = "failed";
        plan.ProductionVerificationCompletedAtUtc = completedAtUtc;
        plan.ProductionVerificationValidUntilUtc = null;
        plan.ProductionVerificationCheckCount = 1;
        plan.ProductionVerificationFailedCheckCount = 1;
        plan.ProductionVerificationEvidenceJson = evidenceJson;
        plan.ProductionVerificationEvidenceSha256 = ComputeEvidenceSha256(evidenceJson);
        plan.ProductionVerificationFailureCode = failureCode;
        plan.ProductionVerificationFailureSummary = failureSummary;
        plan.Status = "production-verification-failed";
        plan.BlockerSummary = failureSummary;
        plan.UpdatedAtUtc = completedAtUtc;
        await db.SaveChangesAsync(ct);
    }

    private async Task<MigrationProductionAdoptionEntity?> LoadPlanAsync(
        string migrationId,
        bool tracking,
        CancellationToken ct)
    {
        var query = db.MigrationProductionAdoptions
            .Include(x => x.MigrationIntake)
                .ThenInclude(x => x.ProductionAuthorities)
            .Include(x => x.PackageRevision)
            .Include(x => x.CandidateArtifact)
            .Include(x => x.StagingRun)
            .AsSplitQuery();
        if (!tracking)
        {
            query = query.AsNoTracking();
        }
        return await query.SingleOrDefaultAsync(
            x => x.MigrationIntake.IntakeId == migrationId,
            ct);
    }

    private static MigrationProductionVerificationCheckDto ToVerificationCheck(
        RuntimeReadinessCheckResult check) =>
        new(
            check.Code,
            check.Name,
            check.Success,
            check.Detail ?? (check.Success ? "Check passed." : "Check failed."),
            check.Url,
            check.StatusCode);

    private static string NormalizeOperator(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "operator" : value.Trim()[..Math.Min(value.Trim().Length, 200)];

    private static string? NormalizeNote(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 1000)];

    private static string SafeDetail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Production verification failed without additional safe detail.";
        }
        var normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized[..Math.Min(normalized.Length, 2000)];
    }

    private static string CreateId(string prefix, DateTime utc) =>
        $"{prefix}_{utc:yyyyMMdd-HHmmssZ}_{Guid.NewGuid():N}";

    private sealed record RuntimeOwnershipResult(
        RuntimeStackEntity? Stack,
        RuntimeServiceInstanceEntity? MatrixService,
        RuntimeServiceInstanceEntity? ElementService,
        IReadOnlyList<RuntimeRouteEntity> Routes,
        IReadOnlyList<MigrationProductionVerificationCheckDto> Checks,
        bool IdentityPassed);

    private sealed record ProductionVerificationEvidence(
        string VerificationId,
        string MigrationId,
        string AdoptionPlanId,
        string CutoverExecutionId,
        string PlanSha256,
        string PackageRevisionId,
        string CandidateArtifactId,
        string StagingRunId,
        Guid RuntimeStackId,
        string TargetStackSlug,
        string MatrixServerName,
        DateTime StartedAtUtc,
        DateTime CompletedAtUtc,
        DateTime? ValidUntilUtc,
        string Operator,
        string? Note,
        bool Passed,
        Guid? ReadinessReportId,
        IReadOnlyList<MigrationProductionVerificationCheckDto> Checks);
}
