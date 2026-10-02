using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostAgent.Planning;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Migrations.Cutover;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Migrations.ProductionAdoption;

public sealed class MigrationProductionAdoptionService(
    MemDbContext db,
    MigrationCutoverContextResolver contextResolver,
    ChatStackRuntimePlanner runtimePlanner,
    RuntimeStackDatabaseService databaseService,
    RuntimeStackManifestStore manifestStore,
    IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MigrationProductionAdoptionStateResponse> GetStateAsync(
        string migrationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(migrationId))
        {
            throw new InvalidOperationException("Migration id is required.");
        }

        var intakeExists = await db.MigrationIntakes
            .AsNoTracking()
            .AnyAsync(x => x.IntakeId == migrationId, ct);
        if (!intakeExists)
        {
            throw new FileNotFoundException($"Migration Session '{migrationId}' was not found.");
        }

        var plan = await LoadPlanAsync(migrationId, tracking: false, ct);
        if (plan is null)
        {
            return new MigrationProductionAdoptionStateResponse(
                Source: "control-plane",
                Status: "not-prepared",
                MigrationId: migrationId,
                PlanPrepared: false,
                Plan: null,
                Detail: "No production adoption plan has been prepared. Preparing a plan records normal MEM runtime identities but creates no runtime records or public routes.");
        }

        var nowUtc = DateTime.UtcNow;
        var staleReason = ResolveStaleReason(plan);
        if (staleReason is not null)
        {
            var stalePlan = ToDto(plan, nowUtc) with
            {
                Status = "stale",
                CollisionFree = false,
                BlockerSummary = staleReason,
            };
            return new MigrationProductionAdoptionStateResponse(
                Source: "control-plane",
                Status: "stale",
                MigrationId: migrationId,
                PlanPrepared: true,
                Plan: stalePlan,
                Detail: "The production adoption plan remains durable but its authoritative package, candidate, or private staging binding is no longer current. Prepare the plan again before cutover.");
        }

        var effectiveStatus = ResolveEffectiveStatus(plan, nowUtc);
        return new MigrationProductionAdoptionStateResponse(
            Source: "control-plane",
            Status: effectiveStatus,
            MigrationId: migrationId,
            PlanPrepared: true,
            Plan: ToDto(plan, nowUtc),
            Detail: effectiveStatus switch
            {
                "prepared" => "The production adoption plan is collision-free and ready for private normal-runtime materialisation.",
                "private-runtime-ready" => "The normal MEM runtime stack is materialised and privately healthy. Prepare a fresh route snapshot before controlled public cutover.",
                "cutover-preview-ready" => "A fresh route snapshot is ready for controlled public cutover.",
                "cutover-executing" => "Controlled public cutover is executing. Refresh for durable evidence.",
                "public-awaiting-verification" => "The normal MEM runtime owns the public Matrix and Element routes and awaits production verification. Acceptance remains blocked.",
                "production-verification-running" => "Production verification is running against the normal MEM runtime, owned database, public routes, and public Matrix and Element endpoints.",
                "production-verification-passed" => "Production verification passed and remains fresh for the recorded validity window. Migration acceptance remains a separate operator boundary.",
                "production-verification-failed" => "Production verification failed. Review the durable failed checks, correct the runtime, and run verification again or use coordinated rollback.",
                "cutover-failed" => "Controlled public cutover failed. Review route compensation and rollback checkpoint evidence before retry or coordinated rollback.",
                "materialization-failed" => "Private production runtime materialisation failed. Review the durable failure evidence before retry or cleanup.",
                _ => "The production adoption plan is blocked. Resolve every recorded ownership collision before production materialisation continues."
            });
    }

    public async Task<MigrationProductionAdoptionStateResponse> PrepareAsync(
        string migrationId,
        PrepareMigrationProductionAdoptionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = await contextResolver.ResolveAsync(migrationId, ct);
        if (await db.MigrationAcceptances.AsNoTracking()
            .AnyAsync(x => x.MigrationIntakeEntityId == context.Intake.Id, ct))
        {
            throw new InvalidOperationException(
                "A production adoption plan cannot be prepared after migration acceptance.");
        }

        var existing = await LoadPlanAsync(migrationId, tracking: true, ct);
        if (existing is not null &&
            !string.Equals(existing.Status, "prepared", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(existing.Status, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Production adoption plan '{existing.AdoptionPlanId}' is already in state '{existing.Status}' and cannot be rewritten.");
        }

        var targetStackSlug = NormalizeSlug(
            string.IsNullOrWhiteSpace(request.TargetStackSlug)
                ? context.Capture.SourceStackSlug
                : request.TargetStackSlug);
        var targetDisplayName = string.IsNullOrWhiteSpace(context.SourceSummary.SourceDisplayName)
            ? targetStackSlug
            : Truncate(context.SourceSummary.SourceDisplayName.Trim(), 200);

        var matrixServerName = NormalizeHost(context.Capture.MatrixServerName)
            ?? throw new InvalidDataException("The authoritative migration package does not contain a valid Matrix server identity.");
        var matrixPublicHost = NormalizeHost(context.Capture.MatrixPublicUrl) ?? matrixServerName;
        if (!string.Equals(matrixServerName, matrixPublicHost, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The authoritative migration package Matrix public host does not match the preserved Matrix server identity.");
        }

        var elementPublicHost = ResolveElementPublicHost(
            request.ElementPublicHost,
            context.Capture.ElementPublicUrl,
            matrixServerName);

        var runtimeStackId = existing?.RuntimeStackId ?? Guid.NewGuid();
        var matrixInstanceId = existing?.MatrixInstanceId ?? Guid.NewGuid();
        var elementInstanceId = existing?.ElementInstanceId ?? Guid.NewGuid();
        var runtime = runtimePlanner.Plan(
            runtimeStackId,
            matrixInstanceId,
            elementInstanceId,
            targetStackSlug);
        var database = databaseService.GetExpectedMatrixDatabaseIdentity(
            runtimeStackId,
            targetStackSlug);

        var matrixImageReference = RequireEvidence(
            context.StagingRun.SynapseImageReference,
            "The verified staging run does not contain the approved Synapse image reference.");
        var matrixImageId = RequireEvidence(
            context.StagingRun.SynapseImageId,
            "The verified staging run does not contain the resolved Synapse image identity.");
        var elementImageReference = RequireEvidence(
            context.StagingRun.ElementImageReference,
            "The verified staging run does not contain the approved Element image reference.");
        var elementImageId = RequireEvidence(
            context.StagingRun.ElementImageId,
            "The verified staging run does not contain the resolved Element image identity.");

        var runtimeDataRoot = ResolveDataRoot();
        var manifestPath = Path.Combine(
            runtimeDataRoot,
            "control-plane",
            "runtime-stacks",
            $"{runtimeStackId:N}.json");

        var routes = new[]
        {
            new MigrationProductionAdoptionRoutePlan(
                ServiceKey: "matrix",
                PublicHost: matrixPublicHost,
                PublicBaseUrl: $"https://{matrixPublicHost}",
                ForwardScheme: "http",
                ForwardHost: runtime.MatrixContainerName,
                ForwardPort: 8008,
                Provider: "npm",
                PublicMutationDeferred: true),
            new MigrationProductionAdoptionRoutePlan(
                ServiceKey: "element-web",
                PublicHost: elementPublicHost,
                PublicBaseUrl: $"https://{elementPublicHost}",
                ForwardScheme: "http",
                ForwardHost: runtime.ElementContainerName!,
                ForwardPort: 80,
                Provider: "npm",
                PublicMutationDeferred: true),
        };

        var planFingerprint = new AdoptionPlanFingerprint(
            MigrationId: migrationId,
            ProductionAuthorityId: context.ProductionAuthority?.ProductionAuthorityId,
            ProductionAuthorityType: context.ProductionAuthority?.AuthorityType ??
                MigrationProductionAuthorityTypes.FinalFrozen,
            ProductionAuthorityEvidenceSha256: context.ProductionAuthority?.EvidenceSha256,
            PackageRevisionId: context.PackageRevision.PackageRevisionId,
            PackageRevisionSha256: context.PackageRevision.DecryptedArchiveSha256!,
            CandidateArtifactId: context.CandidateArtifact.CandidateArtifactId,
            CandidateArtifactSha256: context.CandidateArtifact.ArtifactSha256,
            StagingRunId: context.StagingRun.StagingRunId,
            RuntimeStackId: runtimeStackId,
            TargetStackSlug: targetStackSlug,
            MatrixInstanceId: matrixInstanceId,
            ElementInstanceId: elementInstanceId,
            MatrixServerName: matrixServerName,
            MatrixPublicHost: matrixPublicHost,
            ElementPublicHost: elementPublicHost,
            RuntimeNetworkName: runtime.RuntimeNetworkName,
            MatrixContainerName: runtime.MatrixContainerName,
            ElementContainerName: runtime.ElementContainerName!,
            MatrixImageReference: matrixImageReference,
            MatrixImageId: matrixImageId,
            ElementImageReference: elementImageReference,
            ElementImageId: elementImageId,
            DatabaseName: database.DatabaseName,
            DatabaseUsername: database.DatabaseUsername,
            Routes: routes);
        var planSha256 = ComputePlanSha256(planFingerprint);

        var planId = existing?.AdoptionPlanId ?? CreatePlanId();
        var entityId = existing?.Id ?? Guid.NewGuid();
        var collisions = await FindCollisionsAsync(
            entityId,
            context.Intake.Id,
            runtimeStackId,
            targetStackSlug,
            matrixInstanceId,
            elementInstanceId,
            runtime,
            database,
            matrixPublicHost,
            elementPublicHost,
            manifestPath,
            ct);
        var now = DateTime.UtcNow;
        var status = collisions.Count == 0 ? "prepared" : "blocked";
        var blockerSummary = collisions.Count == 0
            ? null
            : $"{collisions.Count} production ownership collision(s) must be resolved before public cutover.";

        var revisionNumber = existing is null
            ? 1
            : string.Equals(existing.PlanSha256, planSha256, StringComparison.OrdinalIgnoreCase)
                ? existing.RevisionNumber
                : existing.RevisionNumber + 1;

        var plan = existing ?? new MigrationProductionAdoptionEntity
        {
            Id = entityId,
            AdoptionPlanId = planId,
            MigrationIntakeEntityId = context.Intake.Id,
            CreatedAtUtc = now,
            RuntimeStackId = runtimeStackId,
            MatrixInstanceId = matrixInstanceId,
            ElementInstanceId = elementInstanceId,
        };

        plan.MigrationPackageRevisionEntityId = context.PackageRevision.Id;
        plan.MigrationCandidateArtifactEntityId = context.CandidateArtifact.Id;
        plan.MigrationStagingRunEntityId = context.StagingRun.Id;
        plan.Status = status;
        plan.RevisionNumber = revisionNumber;
        plan.PlanSha256 = planSha256;
        plan.UpdatedAtUtc = now;
        plan.PreparedAtUtc = now;
        plan.TargetStackSlug = targetStackSlug;
        plan.TargetDisplayName = targetDisplayName;
        plan.MatrixServerName = matrixServerName;
        plan.MatrixPublicHost = matrixPublicHost;
        plan.MatrixPublicBaseUrl = $"https://{matrixPublicHost}";
        plan.ElementPublicHost = elementPublicHost;
        plan.ElementPublicBaseUrl = $"https://{elementPublicHost}";
        plan.RuntimeNetworkName = runtime.RuntimeNetworkName;
        plan.RuntimeDataRoot = runtimeDataRoot;
        plan.ManifestPath = manifestPath;
        plan.MatrixContainerName = runtime.MatrixContainerName;
        plan.MatrixDataPath = runtime.MatrixDataPath;
        plan.ElementContainerName = runtime.ElementContainerName!;
        plan.ElementDataPath = runtime.ElementDataPath!;
        plan.MatrixImageReference = matrixImageReference;
        plan.MatrixImageId = matrixImageId;
        plan.ElementImageReference = elementImageReference;
        plan.ElementImageId = elementImageId;
        plan.DatabaseEngine = "postgres";
        plan.DatabaseHost = "mem-postgres";
        plan.DatabasePort = 5432;
        plan.DatabaseName = database.DatabaseName;
        plan.DatabaseUsername = database.DatabaseUsername;
        plan.DatabasePasswordSecretKind = RuntimeStackDatabaseService.MatrixPostgresPasswordSecretKind;
        plan.ExpectedUsersCount = context.StagingRun.UsersCount;
        plan.ExpectedRoomsCount = context.StagingRun.RoomsCount;
        plan.ExpectedEventsCount = context.StagingRun.EventsCount;
        plan.RoutePlanJson = JsonSerializer.Serialize(routes, JsonOptions);
        plan.ProvenanceJson = JsonSerializer.Serialize(new
        {
            migrationId,
            productionAuthorityId = context.ProductionAuthority?.ProductionAuthorityId,
            productionAuthorityType = context.ProductionAuthority?.AuthorityType ??
                MigrationProductionAuthorityTypes.FinalFrozen,
            productionAuthorityEvidenceSha256 = context.ProductionAuthority?.EvidenceSha256,
            packageRevisionId = context.PackageRevision.PackageRevisionId,
            packageSha256 = context.PackageRevision.DecryptedArchiveSha256,
            candidateArtifactId = context.CandidateArtifact.CandidateArtifactId,
            candidateArtifactSha256 = context.CandidateArtifact.ArtifactSha256,
            stagingRunId = context.StagingRun.StagingRunId,
            privateRuntimeStagingId = context.StagingRun.PrivateRuntimeStagingId,
            captureKind = context.Capture.Kind,
            sourceFrozen = context.Capture.SourceFrozen,
            rehearsalOnly = context.Capture.RehearsalOnly,
        }, JsonOptions);
        plan.CollisionEvidenceJson = JsonSerializer.Serialize(collisions, JsonOptions);
        plan.BlockerSummary = blockerSummary;

        if (existing is null)
        {
            db.MigrationProductionAdoptions.Add(plan);
        }

        await db.SaveChangesAsync(ct);
        var loaded = await LoadPlanAsync(migrationId, tracking: false, ct)
            ?? throw new InvalidOperationException("The saved production adoption plan could not be reloaded.");

        return new MigrationProductionAdoptionStateResponse(
            Source: "control-plane",
            Status: loaded.Status,
            MigrationId: migrationId,
            PlanPrepared: true,
            Plan: ToDto(loaded),
            Detail: loaded.Status == "prepared"
                ? "Normal MEM runtime identities were reserved in a durable migration adoption plan. No runtime stack, service, database, manifest, secret, or public route was created."
                : "The durable adoption preview was recorded without creating normal runtime state. Resolve the listed collisions and prepare the plan again.");
    }

    internal static string NormalizeSlug(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "migrated-stack" : value.Trim();
        var chars = source
            .ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }
        normalized = normalized.Trim('-');
        if (normalized.Length == 0)
        {
            normalized = "migrated-stack";
        }
        return normalized.Length <= 100 ? normalized : normalized[..100].TrimEnd('-');
    }

    internal static string ComputePlanSha256(AdoptionPlanFingerprint fingerprint)
    {
        var json = JsonSerializer.Serialize(fingerprint, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private async Task<List<MigrationProductionAdoptionCollision>> FindCollisionsAsync(
        Guid planEntityId,
        Guid migrationIntakeEntityId,
        Guid runtimeStackId,
        string targetStackSlug,
        Guid matrixInstanceId,
        Guid elementInstanceId,
        ChatStackRuntimePlan runtime,
        RuntimeStackDatabaseIdentity database,
        string matrixPublicHost,
        string elementPublicHost,
        string manifestPath,
        CancellationToken ct)
    {
        var collisions = new List<MigrationProductionAdoptionCollision>();

        if (await db.RuntimeStacks.AsNoTracking().AnyAsync(
                x => x.Id == runtimeStackId || x.Slug == targetStackSlug,
                ct))
        {
            collisions.Add(new(
                "runtime_stack_collision",
                "runtime-stack",
                targetStackSlug,
                "A normal MEM runtime stack already owns the planned stack identity or slug."));
        }

        if (await db.RuntimeServiceInstances.AsNoTracking().AnyAsync(
                x => x.InstanceId == matrixInstanceId ||
                     x.InstanceId == elementInstanceId ||
                     x.ContainerName == runtime.MatrixContainerName ||
                     x.ContainerName == runtime.ElementContainerName,
                ct))
        {
            collisions.Add(new(
                "runtime_service_collision",
                "runtime-service",
                $"{runtime.MatrixContainerName}, {runtime.ElementContainerName}",
                "A normal MEM runtime service already owns one of the planned service or container identities."));
        }

        if (await db.RuntimeStackDatabases.AsNoTracking().AnyAsync(
                x => x.RuntimeStackId == runtimeStackId ||
                     x.DatabaseName == database.DatabaseName ||
                     x.DatabaseUsername == database.DatabaseUsername,
                ct))
        {
            collisions.Add(new(
                "runtime_database_collision",
                "runtime-database",
                $"{database.DatabaseName}/{database.DatabaseUsername}",
                "A normal MEM runtime database already owns the planned database or role identity."));
        }

        var routeHosts = new[] { matrixPublicHost, elementPublicHost };
        var existingRouteHosts = await db.RuntimeRoutes.AsNoTracking()
            .Where(x => routeHosts.Contains(x.PublicHost))
            .Select(x => x.PublicHost)
            .Distinct()
            .ToArrayAsync(ct);
        foreach (var host in existingRouteHosts)
        {
            collisions.Add(new(
                "runtime_route_collision",
                "runtime-route",
                host,
                "A normal MEM runtime route already owns this intended public host."));
        }

        var otherPlan = await MigrationProductionReservationQuery
            .CurrentReservations(db)
            .Where(x => x.Id != planEntityId &&
                        x.MigrationIntakeEntityId != migrationIntakeEntityId)
            .Where(x => x.RuntimeStackId == runtimeStackId ||
                        x.TargetStackSlug == targetStackSlug ||
                        x.MatrixInstanceId == matrixInstanceId ||
                        x.ElementInstanceId == elementInstanceId ||
                        x.DatabaseName == database.DatabaseName ||
                        x.DatabaseUsername == database.DatabaseUsername ||
                        x.MatrixPublicHost == matrixPublicHost ||
                        x.ElementPublicHost == elementPublicHost)
            .Select(x => x.AdoptionPlanId)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(otherPlan))
        {
            collisions.Add(new(
                "migration_adoption_plan_collision",
                "migration-adoption-plan",
                otherPlan,
                "Another Migration Session currently reserves one or more of these normal runtime identities."));
        }

        if (await manifestStore.FindAsync(targetStackSlug, ct) is not null || File.Exists(manifestPath))
        {
            collisions.Add(new(
                "runtime_manifest_collision",
                "runtime-manifest",
                targetStackSlug,
                "A normal MEM runtime manifest already exists for the planned target identity."));
        }

        if (Directory.Exists(runtime.MatrixDataPath) ||
            (!string.IsNullOrWhiteSpace(runtime.ElementDataPath) && Directory.Exists(runtime.ElementDataPath)))
        {
            collisions.Add(new(
                "runtime_data_path_collision",
                "runtime-data-root",
                runtimeStackId.ToString(),
                "One or more planned normal runtime data directories already exist."));
        }

        return collisions;
    }

    internal static string? ResolveStaleReason(MigrationProductionAdoptionEntity plan) =>
        ResolveStaleReason(plan, plan.MigrationIntake.ProductionAuthorities);

    internal static string? ResolveStaleReason(
        MigrationProductionAdoptionEntity plan,
        IEnumerable<MigrationProductionAuthorityEntity> productionAuthorities)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(productionAuthorities);

        var activeAuthorities = productionAuthorities
            .Where(x =>
                x.Status == MigrationProductionAuthorityStatuses.Active &&
                x.ActiveMigrationKey == plan.MigrationIntake.IntakeId)
            .ToArray();

        if (activeAuthorities.Length > 1)
        {
            return "The Migration Session has more than one active production authority.";
        }

        var authority = activeAuthorities.SingleOrDefault();
        if (authority is null)
        {
            if (!string.Equals(plan.PackageRevision.Purpose, "final", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(plan.PackageRevision.Status, "package-validated", StringComparison.OrdinalIgnoreCase) ||
                plan.PackageRevision.SourceFrozen is not true ||
                plan.PackageRevision.RehearsalOnly is not false)
            {
                return "The production adoption plan no longer has active production authority.";
            }
        }
        else
        {
            if (!MigrationProductionAuthorityTypes.IsKnown(authority.AuthorityType) ||
                authority.MigrationPackageRevisionEntityId != plan.MigrationPackageRevisionEntityId ||
                authority.MigrationCandidateArtifactEntityId != plan.MigrationCandidateArtifactEntityId ||
                authority.MigrationStagingRunEntityId != plan.MigrationStagingRunEntityId)
            {
                return "The active production authority no longer matches the adoption plan package, candidate, and staging binding.";
            }

            var expectedPurpose = authority.AuthorityType ==
                MigrationProductionAuthorityTypes.OperatorAttestedSnapshot
                    ? "preview"
                    : "final";
            var expectedSourceFrozen = authority.AuthorityType ==
                MigrationProductionAuthorityTypes.FinalFrozen;
            var expectedRehearsalOnly = !expectedSourceFrozen;

            if (!string.Equals(plan.PackageRevision.Purpose, expectedPurpose, StringComparison.Ordinal) ||
                !string.Equals(plan.PackageRevision.Status, "package-validated", StringComparison.Ordinal) ||
                !string.Equals(plan.PackageRevision.RetentionState, "active", StringComparison.Ordinal) ||
                !string.Equals(plan.PackageRevision.ActivePurposeKey, $"{plan.MigrationIntake.IntakeId}:{expectedPurpose}", StringComparison.Ordinal) ||
                !string.Equals(plan.PackageRevision.CaptureKind, expectedPurpose, StringComparison.OrdinalIgnoreCase) ||
                plan.PackageRevision.SourceFrozen != expectedSourceFrozen ||
                plan.PackageRevision.RehearsalOnly != expectedRehearsalOnly ||
                !string.Equals(authority.DecryptedArchiveSha256, plan.PackageRevision.DecryptedArchiveSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(authority.EncryptedPackageSha256, plan.PackageRevision.EncryptedPackageSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(authority.SourceMigrationId, plan.PackageRevision.ArchiveMigrationId, StringComparison.Ordinal) ||
                !string.Equals(plan.CandidateArtifact.SourcePackageSha256, plan.PackageRevision.DecryptedArchiveSha256, StringComparison.OrdinalIgnoreCase))
            {
                return "The active production authority package evidence no longer matches the adoption plan.";
            }
        }

        if (!string.Equals(plan.CandidateArtifact.VerificationStatus, "verified", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.CandidateArtifact.RetentionState, "active", StringComparison.OrdinalIgnoreCase))
        {
            return "The bound Migration Candidate Artifact is no longer active and verified.";
        }

        if (plan.StagingRun.DestroyedAtUtc is not null ||
            !string.Equals(plan.StagingRun.Status, "verified", StringComparison.OrdinalIgnoreCase) ||
            !plan.StagingRun.PrivateOnly ||
            plan.StagingRun.PublicRoutesCreated ||
            !plan.StagingRun.DatabaseImportSucceeded ||
            !plan.StagingRun.SynapseHealthPassed ||
            !plan.StagingRun.ElementContainerStarted ||
            !plan.StagingRun.ElementHealthPassed ||
            !plan.StagingRun.ElementSynapseConnectivityPassed ||
            !plan.StagingRun.ElementNetworkAttached)
        {
            return "The bound full private staging runtime is no longer active and fully verified.";
        }

        return null;
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

    internal static DateTimeOffset? ToUtcOffset(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        var utc = value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
        };
        return new DateTimeOffset(utc);
    }

    internal static string ResolveCutoverPreviewStatus(
        MigrationProductionAdoptionEntity plan,
        DateTime nowUtc)
    {
        _ = nowUtc;
        return plan.CutoverPreviewStatus ?? "not-prepared";
    }

    internal static string ResolveEffectiveStatus(
        MigrationProductionAdoptionEntity plan,
        DateTime nowUtc)
    {
        _ = nowUtc;
        return plan.Status;
    }

    private MigrationProductionAdoptionPlanDto ToDto(
        MigrationProductionAdoptionEntity plan,
        DateTime? nowUtc = null)
    {
        var projectionTimeUtc = nowUtc ?? DateTime.UtcNow;
        var effectiveStatus = ResolveEffectiveStatus(plan, projectionTimeUtc);
        var previewStatus = ResolveCutoverPreviewStatus(plan, projectionTimeUtc);
        var routes = DeserializeList<MigrationProductionAdoptionRoutePlan>(plan.RoutePlanJson);
        var collisions = DeserializeList<MigrationProductionAdoptionCollision>(plan.CollisionEvidenceJson);
        return new MigrationProductionAdoptionPlanDto(
            AdoptionPlanId: plan.AdoptionPlanId,
            MigrationId: plan.MigrationIntake.IntakeId,
            PackageRevisionId: plan.PackageRevision.PackageRevisionId,
            CandidateArtifactId: plan.CandidateArtifact.CandidateArtifactId,
            StagingRunId: plan.StagingRun.StagingRunId,
            Status: effectiveStatus,
            RevisionNumber: plan.RevisionNumber,
            PlanSha256: plan.PlanSha256,
            CreatedAtUtc: plan.CreatedAtUtc,
            UpdatedAtUtc: plan.UpdatedAtUtc,
            PreparedAtUtc: plan.PreparedAtUtc,
            RuntimeStackId: plan.RuntimeStackId,
            TargetStackSlug: plan.TargetStackSlug,
            TargetDisplayName: plan.TargetDisplayName,
            MatrixInstanceId: plan.MatrixInstanceId,
            ElementInstanceId: plan.ElementInstanceId,
            MatrixServerName: plan.MatrixServerName,
            MatrixPublicHost: plan.MatrixPublicHost,
            MatrixPublicBaseUrl: plan.MatrixPublicBaseUrl,
            ElementPublicHost: plan.ElementPublicHost,
            ElementPublicBaseUrl: plan.ElementPublicBaseUrl,
            RuntimeNetworkName: plan.RuntimeNetworkName,
            MatrixContainerName: plan.MatrixContainerName,
            ElementContainerName: plan.ElementContainerName,
            MatrixImageReference: plan.MatrixImageReference,
            MatrixImageId: plan.MatrixImageId,
            ElementImageReference: plan.ElementImageReference,
            ElementImageId: plan.ElementImageId,
            DatabaseEngine: plan.DatabaseEngine,
            DatabaseHost: plan.DatabaseHost,
            DatabasePort: plan.DatabasePort,
            DatabaseName: plan.DatabaseName,
            DatabaseUsername: plan.DatabaseUsername,
            DatabasePasswordSecretKind: plan.DatabasePasswordSecretKind,
            ExpectedUsersCount: plan.ExpectedUsersCount,
            ExpectedRoomsCount: plan.ExpectedRoomsCount,
            ExpectedEventsCount: plan.ExpectedEventsCount,
            Routes: routes,
            Collisions: collisions,
            CollisionFree: collisions.Count == 0,
            RuntimeRecordsCreated: plan.RuntimeRecordsCreated,
            PublicRoutesCreated: plan.PublicRoutesCreated,
            Materialization: new MigrationProductionMaterializationDto(
                MaterializationId: plan.MaterializationId,
                Status: plan.MaterializationStatus ?? "not-started",
                StartedAtUtc: plan.MaterializationStartedAtUtc,
                CompletedAtUtc: plan.MaterializationCompletedAtUtc,
                ProductionDatabaseImported: plan.ProductionDatabaseImported,
                MatrixContainerStarted: plan.MatrixProductionContainerStarted,
                MatrixHealthPassed: plan.MatrixProductionHealthPassed,
                ElementContainerStarted: plan.ElementProductionContainerStarted,
                ElementHealthPassed: plan.ElementProductionHealthPassed,
                RuntimeManifestSaved: plan.RuntimeManifestSaved,
                DatabaseOwnershipSaved: plan.DatabaseOwnershipSaved,
                RuntimeRecordsCreated: plan.RuntimeRecordsCreated,
                UserInventorySynchronized: plan.UserInventorySynchronized,
                PublicRoutesCreated: false,
                FailureCode: plan.MaterializationFailureCode,
                FailureSummary: plan.MaterializationFailureSummary),
            Cutover: new MigrationProductionCutoverDto(
                Preview: new MigrationProductionCutoverPreviewDto(
                    PreviewId: plan.CutoverPreviewId,
                    Status: previewStatus,
                    CreatedAtUtc: ToUtcOffset(plan.CutoverPreviewCreatedAtUtc),
                    ExpiresAtUtc: ToUtcOffset(plan.CutoverPreviewExpiresAtUtc),
                    SnapshotSha256: plan.CutoverPreviewSha256,
                    Routes: DeserializePreviewRoutes(plan.CutoverPreviewJson),
                    Blockers: DeserializePreviewBlockers(plan.CutoverPreviewJson)),
                Execution: new MigrationProductionCutoverExecutionDto(
                    ExecutionId: plan.CutoverExecutionId,
                    Status: plan.CutoverStatus ?? "not-started",
                    StartedAtUtc: plan.CutoverStartedAtUtc,
                    CompletedAtUtc: plan.CutoverCompletedAtUtc,
                    TargetPublicAtUtc: plan.TargetPublicAtUtc,
                    MatrixRouteId: plan.MatrixNpmRouteId,
                    ElementRouteId: plan.ElementNpmRouteId,
                    RuntimePromotionCompleted: plan.RuntimePromotionCompleted,
                    PublicRoutesCreated: plan.PublicRoutesCreated,
                    RouteCompensationAttempted: plan.RouteCompensationAttempted,
                    RouteCompensationCompleted: plan.RouteCompensationCompleted,
                    FailureCode: plan.CutoverFailureCode,
                    FailureSummary: plan.CutoverFailureSummary)),
            ProductionVerification: new MigrationProductionVerificationDto(
                VerificationId: plan.ProductionVerificationId,
                Status: plan.ProductionVerificationStatus ?? "not-started",
                StartedAtUtc: plan.ProductionVerificationStartedAtUtc,
                CompletedAtUtc: plan.ProductionVerificationCompletedAtUtc,
                ValidUntilUtc: plan.ProductionVerificationValidUntilUtc,
                Fresh: string.Equals(plan.ProductionVerificationStatus, "passed", StringComparison.OrdinalIgnoreCase),
                Passed: string.Equals(plan.ProductionVerificationStatus, "passed", StringComparison.OrdinalIgnoreCase),
                CheckCount: plan.ProductionVerificationCheckCount,
                FailedCheckCount: plan.ProductionVerificationFailedCheckCount,
                EvidenceSha256: plan.ProductionVerificationEvidenceSha256,
                ReadinessReportId: plan.ProductionVerificationReadinessReportId,
                Checks: DeserializeVerificationChecks(plan.ProductionVerificationEvidenceJson),
                FailureCode: plan.ProductionVerificationFailureCode,
                FailureSummary: plan.ProductionVerificationFailureSummary),
            Rollback: new MigrationProductionRollbackDto(
                Preview: new MigrationProductionRollbackPreviewDto(
                    PreviewId: plan.RollbackPreviewId,
                    Status: plan.RollbackPreviewStatus ?? "not-prepared",
                    CreatedAtUtc: plan.RollbackPreviewCreatedAtUtc,
                    ExpiresAtUtc: plan.RollbackPreviewExpiresAtUtc,
                    SnapshotSha256: plan.RollbackPreviewSha256,
                    Routes: DeserializeRollbackPreviewRoutes(plan.RollbackPreviewJson),
                    Blockers: DeserializePreviewBlockers(plan.RollbackPreviewJson)),
                Execution: new MigrationProductionRollbackExecutionDto(
                    ExecutionId: plan.RollbackExecutionId,
                    Status: plan.RollbackStatus ?? "not-started",
                    StartedAtUtc: plan.RollbackStartedAtUtc,
                    CompletedAtUtc: plan.RollbackCompletedAtUtc,
                    RoutesRestored: plan.RollbackRoutesRestored,
                    RuntimeRoutesRemoved: plan.RollbackRuntimeRoutesRemoved,
                    TargetContainersStopped: plan.RollbackTargetContainersStopped,
                    TargetRouteCompensationAttempted: plan.RollbackTargetRouteCompensationAttempted,
                    TargetRouteCompensationCompleted: plan.RollbackTargetRouteCompensationCompleted,
                    SourceHandoffId: plan.RollbackSourceHandoffId,
                    SourceHandoffSha256: plan.RollbackSourceHandoffSha256,
                    FailureCode: plan.RollbackFailureCode,
                    FailureSummary: plan.RollbackFailureSummary),
                Completion: new MigrationProductionCoordinatedRollbackDto(
                    Status: plan.RollbackCompletionStatus ?? "not-received",
                    RestorationAttemptId: plan.RollbackSourceCompletionAttemptId,
                    CompletionSha256: plan.RollbackSourceCompletionSha256,
                    ImportedAtUtc: plan.RollbackSourceCompletionImportedAtUtc,
                    CompletedAtUtc: plan.CoordinatedRollbackCompletedAtUtc,
                    SourceRestored: plan.RollbackSourceRestored,
                    RestartPoliciesRestored: plan.RollbackRestartPoliciesRestored,
                    OriginalRunningStatesRestored: plan.RollbackOriginalRunningStatesRestored,
                    MatrixVerified: plan.RollbackMatrixVerified,
                    ElementVerified: plan.RollbackElementVerified,
                    TargetRollbackAuthorityVerified: plan.RollbackTargetAuthorityVerified,
                    TargetRollbackStillIntact: plan.RollbackTargetIntegrityVerified,
                    DevelopmentExternalControlPlane: plan.RollbackDevelopmentExternalControlPlane)),
            BlockerSummary: plan.BlockerSummary);
    }

    private static IReadOnlyList<MigrationProductionVerificationCheckDto> DeserializeVerificationChecks(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("checks", out var checks) ||
                checks.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return JsonSerializer.Deserialize<MigrationProductionVerificationCheckDto[]>(
                checks.GetRawText(),
                JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<MigrationProductionCutoverRouteSnapshot> DeserializePreviewRoutes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("routes", out var routes) ||
                routes.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return JsonSerializer.Deserialize<MigrationProductionCutoverRouteSnapshot[]>(
                routes.GetRawText(),
                JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<MigrationProductionRollbackRouteState> DeserializeRollbackPreviewRoutes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("routes", out var routes) ||
                routes.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return JsonSerializer.Deserialize<MigrationProductionRollbackRouteState[]>(
                routes.GetRawText(),
                JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> DeserializePreviewBlockers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("blockers", out var blockers) ||
                blockers.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return blockers.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyList<T> DeserializeList<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T[]>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(configuration);

    internal static string ResolveElementPublicHost(
        string? requestedElementPublicHost,
        string? capturedElementPublicUrl,
        string matrixServerName)
    {
        var selected = string.IsNullOrWhiteSpace(requestedElementPublicHost)
            ? NormalizeHost(capturedElementPublicUrl)
            : NormalizeTargetHost(requestedElementPublicHost);
        if (string.IsNullOrWhiteSpace(selected))
        {
            throw new InvalidDataException(
                "The authoritative migration package does not contain the Element public host required for production adoption planning. Enter a replacement Element public host.");
        }

        var normalizedMatrixServerName = NormalizeTargetHost(matrixServerName);
        if (string.Equals(selected, normalizedMatrixServerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Matrix and Element public host names must be different for a migrated chat server.");
        }

        return selected;
    }

    internal static string NormalizeTargetHost(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Element public host is required.");
        }

        var normalized = value.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalized.Contains('/', StringComparison.Ordinal) ||
            normalized.Contains('\\', StringComparison.Ordinal) ||
            normalized.Contains(':', StringComparison.Ordinal) ||
            normalized.Any(char.IsWhiteSpace) ||
            Uri.CheckHostName(normalized) == UriHostNameType.Unknown)
        {
            throw new InvalidOperationException($"'{value}' is not a valid public hostname.");
        }

        return normalized;
    }

    private static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return NormalizeTargetHost(uri.Host);
        }
        return NormalizeTargetHost(trimmed.Trim('/'));
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    private static string RequireEvidence(string? value, string message) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidDataException(message) : value.Trim();

    private static string CreatePlanId() =>
        $"madp_{DateTime.UtcNow:yyyyMMdd-HHmmss}Z_{Guid.NewGuid():N}";

    internal sealed record AdoptionPlanFingerprint(
        string MigrationId,
        string? ProductionAuthorityId,
        string ProductionAuthorityType,
        string? ProductionAuthorityEvidenceSha256,
        string PackageRevisionId,
        string PackageRevisionSha256,
        string CandidateArtifactId,
        string CandidateArtifactSha256,
        string StagingRunId,
        Guid RuntimeStackId,
        string TargetStackSlug,
        Guid MatrixInstanceId,
        Guid ElementInstanceId,
        string MatrixServerName,
        string MatrixPublicHost,
        string ElementPublicHost,
        string RuntimeNetworkName,
        string MatrixContainerName,
        string ElementContainerName,
        string MatrixImageReference,
        string MatrixImageId,
        string ElementImageReference,
        string ElementImageId,
        string DatabaseName,
        string DatabaseUsername,
        IReadOnlyList<MigrationProductionAdoptionRoutePlan> Routes);
}
