using System.Globalization;
using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Commands;
using HostAgent.Docker;
using HostAgent.Element.Provisioning;
using HostAgent.Element.Runtime;
using HostAgent.Matrix.Provisioning;
using HostAgent.Matrix.Runtime;
using HostAgent.Planning;
using HostAgent.Runtime.Backups.StandardRecreate;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Filesystem;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Migrations.Cutover;
using HostAgent.Runtime.Secrets;
using HostAgent.Runtime.ServiceRuntime;
using HostAgent.Runtime.Stacks.Turn;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Shared.RuntimeImages;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Migrations.ProductionAdoption;

/// <summary>
/// Materialises a verified final Migration Candidate Artifact into the normal MEM runtime model
/// without creating or changing public routes. This is the private, reversible half of
/// MIG-PRODUCTION-01B; public cutover remains a separately gated mutation.
/// </summary>
public sealed class MigrationProductionRuntimeMaterializationService(
    MemDbContext db,
    MigrationCutoverContextResolver contextResolver,
    MigrationProductionAdoptionService adoptionService,
    ChatStackRuntimePlanner runtimePlanner,
    RuntimeDirectoryPreparer directoryPreparer,
    RuntimeStackDatabaseService databaseService,
    IRuntimeStackPostgresQueryExecutor queryExecutor,
    RuntimeStackSecretService secretService,
    SynapseConfigGenerator synapseConfigGenerator,
    ElementConfigGenerator elementConfigGenerator,
    MatrixContainerStarter matrixContainerStarter,
    ElementContainerStarter elementContainerStarter,
    RuntimeStackManifestStore manifestStore,
    StandardRecreateUserInventoryFinalizer userInventoryFinalizer,
    IApprovedPostgresRuntimeProvider approvedPostgresRuntimeProvider,
    IApprovedOperationalRuntimeImageProvider approvedOperationalRuntimeImageProvider,
    DockerClient docker,
    IDockerHost dockerHost,
    ILogger<MigrationProductionRuntimeMaterializationService> logger,
    IMemDiagnosticEventWriter? diagnostics = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<MigrationProductionAdoptionStateResponse> MaterializeAsync(
        string migrationId,
        MaterializeMigrationProductionRuntimeRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateAcknowledgements(request);

        var plan = await LoadPlanAsync(migrationId, tracking: true, ct)
            ?? throw new InvalidOperationException(
                "Prepare a collision-free production adoption plan before materialising the normal runtime.");

        if (string.Equals(plan.Status, "private-runtime-ready", StringComparison.OrdinalIgnoreCase) &&
            IsMaterializationComplete(plan))
        {
            return await adoptionService.GetStateAsync(migrationId, ct);
        }

        if (!string.Equals(plan.Status, "prepared", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Production adoption plan '{plan.AdoptionPlanId}' is in state '{plan.Status}' and cannot start private production materialisation.");
        }

        var staleReason = MigrationProductionAdoptionService.ResolveStaleReason(plan);
        if (staleReason is not null)
        {
            throw new InvalidOperationException(staleReason);
        }

        if (await db.MigrationAcceptances.AsNoTracking()
            .AnyAsync(x => x.MigrationIntakeEntityId == plan.MigrationIntakeEntityId, ct))
        {
            throw new InvalidOperationException(
                "The normal runtime cannot be materialised after migration acceptance.");
        }

        var context = await contextResolver.ResolveAsync(migrationId, ct);
        ValidatePlanBindings(plan, context);

        if (await db.RuntimeStacks.AsNoTracking()
            .AnyAsync(x => x.Id == plan.RuntimeStackId || x.Slug == plan.TargetStackSlug, ct))
        {
            throw new InvalidOperationException(
                "A normal MEM runtime stack already owns the planned production identity.");
        }

        await EnsureProductionContainerNamesUnclaimedAsync(plan, ct);

        var synapseRuntime = await approvedOperationalRuntimeImageProvider.ResolveSynapseForOperationAsync(ct);
        var elementRuntime = await approvedOperationalRuntimeImageProvider.ResolveElementForOperationAsync(ct);
        ValidateApprovedImages(plan, synapseRuntime, elementRuntime);

        var materializationId = string.IsNullOrWhiteSpace(plan.MaterializationId)
            ? CreateMaterializationId()
            : plan.MaterializationId;
        var startedAtUtc = DateTime.UtcNow;

        plan.MaterializationId = materializationId;
        plan.MaterializationStatus = "materializing";
        plan.MaterializationStartedAtUtc = startedAtUtc;
        plan.MaterializationCompletedAtUtc = null;
        plan.MaterializationFailureCode = null;
        plan.MaterializationFailureSummary = null;
        plan.Status = "materializing";
        plan.UpdatedAtUtc = startedAtUtc;
        await db.SaveChangesAsync(ct);
        await MigrationProductionDiagnosticEvents.RecordAsync(
            diagnostics,
            migrationId,
            eventCode: "migration.production.materialization.started",
            severity: MemDiagnosticSeverities.Information,
            stage: "production-materialization",
            message: "Private production runtime materialization started.",
            targetStackSlug: plan.TargetStackSlug,
            service: "synapse",
            details: new Dictionary<string, string?>
            {
                ["adoptionPlanId"] = plan.AdoptionPlanId,
                ["materializationId"] = materializationId
            });

        try
        {
            var runtime = runtimePlanner.Plan(
                plan.RuntimeStackId,
                plan.MatrixInstanceId,
                plan.ElementInstanceId,
                plan.TargetStackSlug);
            ValidateRuntimePlan(plan, runtime);

            EnsureUnclaimedRuntimePaths(plan, runtime);
            await directoryPreparer.PrepareAsync(
                [runtime.MatrixDataPath, runtime.ElementDataPath],
                ct);

            var workspaceRoot = Path.Combine(
                plan.RuntimeDataRoot,
                "migration-production-materialization",
                materializationId);
            var databaseDirectory = Path.Combine(workspaceRoot, "database");
            Directory.CreateDirectory(databaseDirectory);

            var databaseImportPlan = PrivateStagingDatabaseImportPolicy.Resolve(
                PrivateStagingDatabaseImportPolicy.ResolveMigrationCandidateDumpFormat(
                    context.CandidateArtifact.ArtifactKind));
            var databaseDumpPath = Path.Combine(
                databaseDirectory,
                databaseImportPlan.WorkspaceFileName);

            await CopySourceMaterialAsync(
                context,
                databaseDumpPath,
                runtime.MatrixDataPath,
                runtime.ElementDataPath!,
                ct);
            PrivateStagingDatabaseImportPolicy.ValidateCopiedDump(
                databaseImportPlan,
                databaseDumpPath);

            var databaseProvisioning = await databaseService.ProvisionMatrixDatabaseAsync(
                plan.RuntimeStackId,
                plan.TargetStackSlug,
                ct);
            ValidateDatabaseIdentity(plan, databaseProvisioning.Provisioning);
            var postgresSettings = databaseProvisioning.Provisioning.ToSynapseSettings(
                databaseProvisioning.Password);

            await ImportDatabaseAsync(
                workspaceRoot,
                databaseImportPlan,
                postgresSettings,
                plan.RuntimeNetworkName,
                materializationId,
                ct);
            plan.ProductionDatabaseImported = true;

            var counts = await ReadDatabaseCountsAsync(plan.DatabaseName, ct);
            ValidateExpectedCounts(plan, counts);

            var registrationSecret = await secretService.GetMatrixRegistrationSharedSecretAsync(
                plan.RuntimeStackId,
                ct);
            var generatedRegistrationSecret = string.IsNullOrWhiteSpace(registrationSecret);
            registrationSecret ??= RuntimeStackSecretService.CreateSecretValue();

            var authoritativeSourceHomeserver = context.SourceSummary.HomeserverConfigPath
                ?? throw new FileNotFoundException(
                    "The authoritative migration source homeserver.yaml path was not recorded.");
            var turnReader = new SynapseTurnConfigReader();
            var sourceTurn = await turnReader.ReadAsync(
                authoritativeSourceHomeserver,
                ct);
            if (!sourceTurn.Supported)
            {
                throw new InvalidDataException(
                    sourceTurn.Detail ??
                    "The authoritative migration source TURN configuration could not be classified safely.");
            }

            // Migration preserves the source TURN posture. A source with no
            // TURN settings remains disconnected even when platform Coturn is
            // Ready; existing source settings are preserved exactly and are
            // classified as external until the operator explicitly adopts them.
            var homeserverPath = await synapseConfigGenerator.GenerateAsync(
                plan.MatrixInstanceId,
                plan.MatrixServerName,
                runtime.MatrixDataPath,
                reportStats: false,
                cancellationToken: ct,
                registrationSharedSecret: registrationSecret,
                postgres: postgresSettings,
                coturn: null,
                image: plan.MatrixImageId);
            PatchProductionHomeserver(
                homeserverPath,
                plan.MatrixServerName,
                plan.MatrixPublicBaseUrl);

            if (sourceTurn.AnyTurnSettings)
            {
                var turnEditor = new SynapseTurnConfigEditor();
                var preserved = turnEditor.RenderPreserve(
                    await File.ReadAllBytesAsync(homeserverPath, ct),
                    await File.ReadAllBytesAsync(authoritativeSourceHomeserver, ct));
                await File.WriteAllBytesAsync(homeserverPath, preserved, ct);
            }

            var effectiveTurn = await turnReader.ReadAsync(homeserverPath, ct);
            if (!effectiveTurn.Supported ||
                !MigrationTurnSettingsMatch(sourceTurn, effectiveTurn))
            {
                throw new InvalidOperationException(
                    "The normal production migration runtime did not preserve the authoritative source TURN posture.");
            }

            EnsureProductionLogConfig(runtime.MatrixDataPath);

            var elementConfigPath = Path.Combine(runtime.ElementDataPath!, "config.json");
            await elementConfigGenerator.WriteHostConfigAsync(
                elementConfigPath,
                plan.MatrixPublicBaseUrl,
                plan.MatrixServerName,
                ct);

            var matrixStart = await matrixContainerStarter.EnsureStartedAsync(
                runtime.MatrixContainerName,
                synapseRuntime.ResolvedImageId,
                runtime.MatrixDataPath,
                runtime.RuntimeNetworkName,
                runtime.MatrixInternalBaseUrl,
                ct,
                allowPullIfMissing: false);
            plan.MatrixProductionContainerStarted = matrixStart.Running;

            var matrixHealth = await WaitForMatrixHealthAsync(matrixStart.ContainerName, ct);
            if (!matrixHealth.Passed)
            {
                throw new InvalidOperationException(
                    "The normal production Matrix container did not pass its private health check.");
            }
            plan.MatrixProductionHealthPassed = true;

            var elementStart = await elementContainerStarter.EnsureStartedAsync(
                runtime.ElementContainerName!,
                elementRuntime.ResolvedImageId,
                runtime.ElementDataPath!,
                elementConfigPath,
                runtime.RuntimeNetworkName,
                runtime.ElementInternalBaseUrl!,
                ct,
                allowPullIfMissing: false);
            plan.ElementProductionContainerStarted = elementStart.Running;

            var elementHealth = await WaitForElementHealthAsync(
                elementStart.ContainerName,
                plan.MatrixPublicBaseUrl,
                runtime.MatrixInternalBaseUrl,
                ct);
            if (!elementHealth.Passed)
            {
                throw new InvalidOperationException(
                    "The normal production Element container did not pass its private health check.");
            }
            plan.ElementProductionHealthPassed = true;

            var matrix = CreateMatrixRuntimeResult(
                plan,
                runtime,
                matrixStart,
                databaseProvisioning.Provisioning,
                matrixHealth.Response,
                effectiveTurn);
            var element = CreateElementRuntimeResult(
                plan,
                runtime,
                elementStart,
                elementHealth.Response,
                elementConfigPath);
            var createResult = new CreateChatStackRuntimeResult(
                StackId: plan.RuntimeStackId,
                Status: "migration_private_production_ready",
                Message: "The final migration candidate was materialised into the normal MEM runtime model without creating public routes.",
                Matrix: matrix,
                Element: element,
                Warnings: [],
                Evidence:
                [
                    new HostAgentEvidence(
                        "migration-production.private-runtime.materialized",
                        "Normal MEM database, Matrix, Element, manifest, and ownership records were materialised privately.",
                        plan.AdoptionPlanId),
                    new HostAgentEvidence(
                        "migration-production.public-routes.absent",
                        "No NPM route was created or changed by private production materialisation.")
                ]);

            await manifestStore.SaveAsync(plan.TargetStackSlug, createResult, ct);
            plan.RuntimeManifestSaved = true;
            plan.RuntimeRecordsCreated = true;

            await databaseService.SaveOwnershipAsync(
                databaseProvisioning.Provisioning,
                databaseProvisioning.Password,
                ct);
            plan.DatabaseOwnershipSaved = true;

            if (generatedRegistrationSecret)
            {
                await secretService.UpsertMatrixRegistrationSharedSecretAsync(
                    plan.RuntimeStackId,
                    registrationSecret,
                    source: "migration-production-materialization",
                    ct);
            }

            var userInventory = await userInventoryFinalizer.FinalizeAsync(
                RuntimeStackManifest.FromResult(
                    plan.TargetStackSlug,
                    createResult,
                    DateTimeOffset.UtcNow),
                counts.Users,
                ct);
            plan.UserInventorySynchronized = string.Equals(
                userInventory.Summary.Status,
                "synchronized",
                StringComparison.OrdinalIgnoreCase);

            var routesCreated = await db.RuntimeRoutes.AsNoTracking()
                .AnyAsync(x => x.RuntimeStackId == plan.RuntimeStackId, ct);
            if (routesCreated)
            {
                throw new InvalidOperationException(
                    "Private production materialisation unexpectedly created normal runtime route ownership.");
            }

            var completedAtUtc = DateTime.UtcNow;
            plan.MaterializationStatus = "private-runtime-ready";
            plan.MaterializationCompletedAtUtc = completedAtUtc;
            plan.Status = "private-runtime-ready";
            plan.UpdatedAtUtc = completedAtUtc;
            plan.BlockerSummary = null;
            plan.MaterializationEvidenceJson = JsonSerializer.Serialize(new
            {
                migrationId,
                plan.AdoptionPlanId,
                materializationId,
                plan.RuntimeStackId,
                plan.TargetStackSlug,
                packageRevisionId = plan.PackageRevision.PackageRevisionId,
                candidateArtifactId = plan.CandidateArtifact.CandidateArtifactId,
                stagingRunId = plan.StagingRun.StagingRunId,
                database = new
                {
                    plan.DatabaseHost,
                    plan.DatabasePort,
                    plan.DatabaseName,
                    plan.DatabaseUsername,
                    imported = true,
                    dumpFormat = databaseImportPlan.DumpFormat,
                    importMechanism = databaseImportPlan.ImportMechanism,
                    counts.PublicTables,
                    counts.KnownSynapseTables,
                    counts.Users,
                    counts.Rooms,
                    counts.Events,
                },
                matrix = new
                {
                    runtime.MatrixContainerName,
                    imageReference = synapseRuntime.ApprovedReference,
                    imageId = synapseRuntime.ResolvedImageId,
                    started = matrixStart.Running,
                    healthPassed = true,
                },
                element = new
                {
                    runtime.ElementContainerName,
                    imageReference = elementRuntime.ApprovedReference,
                    imageId = elementRuntime.ResolvedImageId,
                    started = elementStart.Running,
                    healthPassed = true,
                },
                runtime.RuntimeNetworkName,
                publicRoutesCreated = false,
                userInventoryStatus = userInventory.Summary.Status,
                completedAtUtc,
            }, JsonOptions);

            await db.SaveChangesAsync(ct);
            await MigrationProductionDiagnosticEvents.RecordAsync(
                diagnostics,
                migrationId,
                eventCode: "migration.production.materialization.completed",
                severity: MemDiagnosticSeverities.Information,
                stage: "production-materialization",
                message: "Private production runtime materialization completed successfully.",
                targetStackSlug: plan.TargetStackSlug,
                service: "synapse",
                observed: new Dictionary<string, string?>
                {
                    ["status"] = plan.MaterializationStatus,
                    ["publicRoutesCreated"] = "false"
                },
                details: new Dictionary<string, string?>
                {
                    ["adoptionPlanId"] = plan.AdoptionPlanId,
                    ["materializationId"] = materializationId
                });
            return await adoptionService.GetStateAsync(migrationId, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Migration production runtime materialisation failed. MigrationId={MigrationId} AdoptionPlanId={AdoptionPlanId} MaterializationId={MaterializationId}",
                migrationId,
                plan.AdoptionPlanId,
                materializationId);

            var failure = ClassifyFailure(ex);
            plan.MaterializationStatus = "failed";
            plan.Status = "materialization-failed";
            plan.MaterializationCompletedAtUtc = DateTime.UtcNow;
            plan.MaterializationFailureCode = failure.Code;
            plan.MaterializationFailureSummary = failure.Summary;
            plan.BlockerSummary = failure.Summary;
            plan.UpdatedAtUtc = DateTime.UtcNow;
            plan.MaterializationEvidenceJson = JsonSerializer.Serialize(new
            {
                migrationId,
                plan.AdoptionPlanId,
                materializationId,
                failure.Code,
                failure.Summary,
                plan.ProductionDatabaseImported,
                plan.MatrixProductionContainerStarted,
                plan.MatrixProductionHealthPassed,
                plan.ElementProductionContainerStarted,
                plan.ElementProductionHealthPassed,
                plan.RuntimeManifestSaved,
                plan.DatabaseOwnershipSaved,
                plan.RuntimeRecordsCreated,
            }, JsonOptions);
            var failurePersistenceToken = ct.IsCancellationRequested
                ? CancellationToken.None
                : ct;
            await db.SaveChangesAsync(failurePersistenceToken);
            await MigrationProductionDiagnosticEvents.RecordAsync(
                diagnostics,
                migrationId,
                eventCode: "migration.production.materialization.failed",
                severity: MemDiagnosticSeverities.Error,
                stage: "production-materialization",
                message: "Private production runtime materialization failed.",
                targetStackSlug: plan.TargetStackSlug,
                createIncident: true,
                exception: ex,
                service: "synapse",
                expected: new Dictionary<string, string?>
                {
                    ["status"] = "private-runtime-ready"
                },
                observed: new Dictionary<string, string?>
                {
                    ["status"] = plan.MaterializationStatus,
                    ["failureCode"] = failure.Code
                },
                details: new Dictionary<string, string?>
                {
                    ["adoptionPlanId"] = plan.AdoptionPlanId,
                    ["materializationId"] = materializationId,
                    ["failureSummary"] = failure.Summary
                },
                retryable: true,
                suggestedAction: "Open the Migration Workspace and review private production materialization before retrying.");

            throw new InvalidOperationException(failure.Summary, ex);
        }
    }

    internal static IReadOnlyList<string> GetMissingAcknowledgements(
        MaterializeMigrationProductionRuntimeRequest request)
    {
        var missing = new List<string>();
        if (!request.ExecutePrivateProductionMaterialization) missing.Add("executePrivateProductionMaterialization");
        if (!request.AcknowledgeCreatesNormalRuntimeRecords) missing.Add("acknowledgeCreatesNormalRuntimeRecords");
        if (!request.AcknowledgeMutatesProductionPostgres) missing.Add("acknowledgeMutatesProductionPostgres");
        if (!request.AcknowledgeStartsProductionContainers) missing.Add("acknowledgeStartsProductionContainers");
        if (!request.AcknowledgeNoPublicRoutes) missing.Add("acknowledgeNoPublicRoutes");
        if (!request.AcknowledgeNoAutomaticRollback) missing.Add("acknowledgeNoAutomaticRollback");
        return missing;
    }

    private static void ValidateAcknowledgements(
        MaterializeMigrationProductionRuntimeRequest request)
    {
        var missing = GetMissingAcknowledgements(request);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required acknowledgements: {string.Join(", ", missing)}");
        }
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

    private static bool IsMaterializationComplete(MigrationProductionAdoptionEntity plan) =>
        plan.ProductionDatabaseImported &&
        plan.MatrixProductionContainerStarted &&
        plan.MatrixProductionHealthPassed &&
        plan.ElementProductionContainerStarted &&
        plan.ElementProductionHealthPassed &&
        plan.RuntimeManifestSaved &&
        plan.DatabaseOwnershipSaved &&
        plan.RuntimeRecordsCreated;

    private static void ValidatePlanBindings(
        MigrationProductionAdoptionEntity plan,
        MigrationCutoverContext context)
    {
        if (plan.MigrationPackageRevisionEntityId != context.PackageRevision.Id ||
            plan.MigrationCandidateArtifactEntityId != context.CandidateArtifact.Id ||
            plan.MigrationStagingRunEntityId != context.StagingRun.Id)
        {
            throw new InvalidOperationException(
                "The production adoption plan is stale because its final package, candidate, or staging binding changed.");
        }
    }

    private static void ValidateApprovedImages(
        MigrationProductionAdoptionEntity plan,
        ApprovedOperationalRuntimeImageDescriptor synapse,
        ApprovedOperationalRuntimeImageDescriptor element)
    {
        if (!string.Equals(plan.MatrixImageReference, synapse.ApprovedReference, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.MatrixImageId, synapse.ResolvedImageId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.ElementImageReference, element.ApprovedReference, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.ElementImageId, element.ResolvedImageId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The approved Synapse or Element runtime image changed after the adoption plan was prepared.");
        }
    }

    private static void ValidateRuntimePlan(
        MigrationProductionAdoptionEntity plan,
        ChatStackRuntimePlan runtime)
    {
        if (!string.Equals(plan.RuntimeNetworkName, runtime.RuntimeNetworkName, StringComparison.Ordinal) ||
            !string.Equals(plan.MatrixContainerName, runtime.MatrixContainerName, StringComparison.Ordinal) ||
            !string.Equals(plan.ElementContainerName, runtime.ElementContainerName, StringComparison.Ordinal) ||
            !string.Equals(plan.MatrixDataPath, runtime.MatrixDataPath, StringComparison.Ordinal) ||
            !string.Equals(plan.ElementDataPath, runtime.ElementDataPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The server-derived normal runtime plan no longer matches the durable adoption plan.");
        }
    }

    private static void ValidateDatabaseIdentity(
        MigrationProductionAdoptionEntity plan,
        RuntimeStackDatabaseProvisioningResult database)
    {
        if (!string.Equals(plan.DatabaseHost, database.DatabaseHost, StringComparison.Ordinal) ||
            plan.DatabasePort != database.DatabasePort ||
            !string.Equals(plan.DatabaseName, database.DatabaseName, StringComparison.Ordinal) ||
            !string.Equals(plan.DatabaseUsername, database.DatabaseUsername, StringComparison.Ordinal) ||
            !string.Equals(plan.DatabasePasswordSecretKind, database.PasswordSecretKind, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The provisioned production database identity does not match the durable adoption plan.");
        }
    }

    private async Task EnsureProductionContainerNamesUnclaimedAsync(
        MigrationProductionAdoptionEntity plan,
        CancellationToken ct)
    {
        var containers = await docker.Containers.ListContainersAsync(
            new ContainersListParameters { All = true },
            ct);
        var plannedNames = new HashSet<string>(
            [plan.MatrixContainerName, plan.ElementContainerName],
            StringComparer.OrdinalIgnoreCase);

        var collision = containers.FirstOrDefault(container =>
            (container.Names ?? []).Any(name => plannedNames.Contains(name.TrimStart('/'))));
        if (collision is not null)
        {
            throw new InvalidOperationException(
                "A Docker container already owns one of the server-planned production container names.");
        }
    }

    private static void EnsureUnclaimedRuntimePaths(
        MigrationProductionAdoptionEntity plan,
        ChatStackRuntimePlan runtime)
    {
        foreach (var path in new[] { runtime.MatrixDataPath, runtime.ElementDataPath })
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                continue;
            }

            if (Directory.EnumerateFileSystemEntries(path).Any())
            {
                throw new InvalidOperationException(
                    $"The planned normal runtime data path is not empty for adoption plan '{plan.AdoptionPlanId}'.");
            }
        }
    }

    private static async Task CopySourceMaterialAsync(
        MigrationCutoverContext context,
        string databaseDumpPath,
        string matrixDataPath,
        string elementDataPath,
        CancellationToken ct)
    {
        await CopyRequiredFileAsync(
            context.SourceSummary.DatabaseDumpPath,
            databaseDumpPath,
            ct);
        await CopyRequiredFileAsync(
            context.SourceSummary.HomeserverConfigPath,
            Path.Combine(matrixDataPath, "homeserver.yaml"),
            ct);
        await CopyRequiredFileAsync(
            context.SourceSummary.SigningKeyPath,
            Path.Combine(matrixDataPath, "signing.key"),
            ct);

        var logConfigSource = Path.Combine(context.PrivateStaging.MatrixDataPath, "log.config");
        if (File.Exists(logConfigSource))
        {
            await CopyRequiredFileAsync(
                logConfigSource,
                Path.Combine(matrixDataPath, "log.config"),
                ct);
        }

        var mediaTarget = Path.Combine(matrixDataPath, "media_store");
        Directory.CreateDirectory(mediaTarget);
        var mediaSource = Path.Combine(context.PrivateStaging.MatrixDataPath, "media_store");
        if (context.SourceSummary.MediaStorePresent && Directory.Exists(mediaSource))
        {
            await CopyDirectoryAsync(
                mediaSource,
                mediaTarget,
                ct);
        }

        Directory.CreateDirectory(elementDataPath);
        await CopyRequiredFileAsync(
            context.SourceSummary.ElementConfigPath,
            Path.Combine(elementDataPath, "config.json"),
            ct);
    }

    private static async Task CopyRequiredFileAsync(
        string? sourcePath,
        string targetPath,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                "Required authoritative migration source material was not found.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        await using var source = File.OpenRead(sourcePath);
        await using var target = File.Create(targetPath);
        await source.CopyToAsync(target, ct);
    }

    private static async Task CopyDirectoryAsync(
        string sourceRoot,
        string targetRoot,
        CancellationToken ct)
    {
        foreach (var sourcePath in Directory.EnumerateFiles(
                     sourceRoot,
                     "*",
                     SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, sourcePath);
            if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            {
                throw new InvalidDataException(
                    "Migration media material resolved outside its trusted source root.");
            }

            await CopyRequiredFileAsync(
                sourcePath,
                Path.Combine(targetRoot, relative),
                ct);
        }
    }

    private async Task ImportDatabaseAsync(
        string workspaceRoot,
        PrivateStagingDatabaseImportPlan importPlan,
        RuntimeStackPostgresSettings postgres,
        string networkName,
        string materializationId,
        CancellationToken ct)
    {
        var approvedPostgres = await approvedPostgresRuntimeProvider.ResolveForOperationAsync(ct);
        var containerName = $"mem-migration-prod-import-{Guid.NewGuid():N}"[..48];
        var create = await docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = containerName,
                Image = approvedPostgres.ResolvedImageId,
                Env =
                [
                    $"PGPASSWORD={postgres.Password}",
                    $"PGHOST={postgres.Host}",
                    $"PGPORT={postgres.Port.ToString(CultureInfo.InvariantCulture)}",
                    $"PGUSER={postgres.Username}",
                    $"PGDATABASE={postgres.DatabaseName}",
                ],
                Cmd = importPlan.BuildCommand(
                    postgres.Username,
                    postgres.DatabaseName).ToArray(),
                HostConfig = new HostConfig
                {
                    Binds = [$"{workspaceRoot}:/restore:ro"],
                    AutoRemove = false,
                },
                NetworkingConfig = new NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, EndpointSettings>
                    {
                        [networkName] = new EndpointSettings(),
                    },
                },
                Labels = new Dictionary<string, string>
                {
                    ["mem.component"] = "migration-production-adoption",
                    ["mem.operation"] = "database-import",
                    ["mem.materialization-id"] = materializationId,
                },
            },
            ct);

        try
        {
            await docker.Containers.StartContainerAsync(create.ID, new ContainerStartParameters(), ct);
            var wait = await docker.Containers.WaitContainerAsync(create.ID, ct);
            if (wait.StatusCode != 0)
            {
                var logs = await dockerHost.GetLogsAsync(create.ID, 500, ct);
                logger.LogError(
                    "Migration production database import failed. MaterializationId={MaterializationId} ExitCode={ExitCode} Logs={Logs}",
                    materializationId,
                    wait.StatusCode,
                    logs);
                throw new InvalidOperationException(
                    "The final migration database could not be imported into the normal production PostgreSQL service.");
            }
        }
        finally
        {
            try
            {
                await docker.Containers.RemoveContainerAsync(
                    create.ID,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = false },
                    CancellationToken.None);
            }
            catch
            {
                // Best-effort cleanup of the migration-owned import helper only.
            }
        }
    }

    private async Task<DatabaseCounts> ReadDatabaseCountsAsync(
        string databaseName,
        CancellationToken ct)
    {
        var publicTables = await ReadLongAsync(
            databaseName,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';",
            ct);
        var knownSynapseTables = await ReadLongAsync(
            databaseName,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name IN ('users','events','rooms','state_events','event_json','room_memberships');",
            ct);
        var users = await ReadOptionalTableCountAsync(databaseName, "users", ct);
        var rooms = await ReadOptionalTableCountAsync(databaseName, "rooms", ct);
        var events = await ReadOptionalTableCountAsync(databaseName, "events", ct);

        if (publicTables <= 0 || knownSynapseTables <= 0)
        {
            throw new InvalidDataException(
                "The imported normal production database does not contain the expected Synapse schema.");
        }

        return new DatabaseCounts(publicTables, knownSynapseTables, users, rooms, events);
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

    private static void ValidateExpectedCounts(
        MigrationProductionAdoptionEntity plan,
        DatabaseCounts actual)
    {
        ValidateExpectedCount("users", plan.ExpectedUsersCount, actual.Users);
        ValidateExpectedCount("rooms", plan.ExpectedRoomsCount, actual.Rooms);
        ValidateExpectedCount("events", plan.ExpectedEventsCount, actual.Events);
    }

    private static void ValidateExpectedCount(
        string label,
        long? expected,
        long? actual)
    {
        if (expected.HasValue && actual != expected)
        {
            throw new InvalidDataException(
                $"The normal production database {label} count does not match verified private staging evidence.");
        }
    }

    private static void PatchProductionHomeserver(
        string homeserverPath,
        string serverName,
        string publicBaseUrl)
    {
        var lines = File.ReadAllLines(homeserverPath).ToList();
        EnsureOrReplaceTopLevelScalar(lines, "server_name", $"\"{EscapeYaml(serverName)}\"");
        EnsureOrReplaceTopLevelScalar(lines, "public_baseurl", $"\"{EscapeYaml(publicBaseUrl.TrimEnd('/') + "/")}\"");
        EnsureOrReplaceTopLevelScalar(lines, "signing_key_path", "\"/data/signing.key\"");
        EnsureOrReplaceTopLevelScalar(lines, "media_store_path", "\"/data/media_store\"");
        EnsureOrReplaceTopLevelScalar(lines, "log_config", "\"/data/log.config\"");
        EnsureOrReplaceTopLevelScalar(lines, "enable_registration", "false");
        EnsureOrReplaceTopLevelScalar(lines, "enable_registration_without_verification", "false");
        File.WriteAllLines(homeserverPath, lines);
    }

    private static void EnsureOrReplaceTopLevelScalar(
        List<string> lines,
        string key,
        string value)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].Length > 0 && char.IsWhiteSpace(lines[index][0]))
            {
                continue;
            }

            if (lines[index].StartsWith(key + ":", StringComparison.Ordinal))
            {
                lines[index] = $"{key}: {value}";
                return;
            }
        }

        lines.Add($"{key}: {value}");
    }

    private static string EscapeYaml(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static void EnsureProductionLogConfig(string matrixDataPath)
    {
        var path = Path.Combine(matrixDataPath, "log.config");
        if (File.Exists(path))
        {
            return;
        }

        File.WriteAllText(
            path,
            "version: 1\nformatters:\n  precise:\n    format: '%(asctime)s - %(name)s - %(lineno)d - %(levelname)s - %(message)s'\nhandlers:\n  console:\n    class: logging.StreamHandler\n    formatter: precise\nroot:\n  level: INFO\n  handlers: [console]\ndisable_existing_loggers: false\n");
    }

    private async Task<HealthResult> WaitForMatrixHealthAsync(
        string containerName,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        string? last = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await ExecInContainerAsync(
                containerName,
                ["python", "-c", "import urllib.request; print(urllib.request.urlopen('http://127.0.0.1:8008/health', timeout=3).read().decode('utf-8','replace'))"],
                ct);
            last = Trim(result.Stdout + result.Stderr, 1000);
            if (result.ExitCode == 0 && last.Contains("OK", StringComparison.OrdinalIgnoreCase))
            {
                return new HealthResult(true, last);
            }
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
        return new HealthResult(false, last);
    }

    private async Task<HealthResult> WaitForElementHealthAsync(
        string containerName,
        string expectedMatrixPublicBaseUrl,
        string matrixInternalBaseUrl,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        string? last = null;
        var expectedConfigValue = expectedMatrixPublicBaseUrl.TrimEnd('/');
        var matrixVersionsUrl = matrixInternalBaseUrl.TrimEnd('/') + "/_matrix/client/versions";

        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await ExecInContainerAsync(
                containerName,
                [
                    "sh",
                    "-lc",
                    $"(wget -qO- http://127.0.0.1/config.json || curl -fsS http://127.0.0.1/config.json) | grep -Fq '{EscapeShellSingleQuoted(expectedConfigValue)}' && " +
                    "(wget -qO- http://127.0.0.1/ || curl -fsS http://127.0.0.1/) | grep -qi '<html' && " +
                    $"(wget -qO- '{EscapeShellSingleQuoted(matrixVersionsUrl)}' || curl -fsS '{EscapeShellSingleQuoted(matrixVersionsUrl)}') | grep -q 'versions'"
                ],
                ct);
            last = Trim(result.Stdout + Environment.NewLine + result.Stderr, 1500);
            if (result.ExitCode == 0)
            {
                return new HealthResult(
                    true,
                    "Element served the intended production config and static page, then reached Matrix through the normal private runtime network.");
            }
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
        return new HealthResult(false, last);
    }

    private static string EscapeShellSingleQuoted(string value) =>
        value.Replace("'", "'\"'\"'", StringComparison.Ordinal);

    private async Task<ExecResult> ExecInContainerAsync(
        string containerName,
        IReadOnlyList<string> command,
        CancellationToken ct)
    {
        var exec = await docker.Exec.ExecCreateContainerAsync(
            containerName,
            new ContainerExecCreateParameters
            {
                AttachStdout = true,
                AttachStderr = true,
                Cmd = command.ToList(),
            },
            ct);
        using var stream = await docker.Exec.StartAndAttachContainerExecAsync(
            exec.ID,
            tty: false,
            cancellationToken: ct);
        var output = await stream.ReadOutputToEndAsync(ct);
        var inspect = await docker.Exec.InspectContainerExecAsync(exec.ID, ct);
        return new ExecResult(inspect.ExitCode, output.stdout ?? string.Empty, output.stderr ?? string.Empty);
    }


    private static bool MigrationTurnSettingsMatch(
        SynapseTurnConfigReadResult source,
        SynapseTurnConfigReadResult effective)
    {
        if (source.AnyTurnSettings != effective.AnyTurnSettings)
        {
            return false;
        }

        if (!source.AnyTurnSettings)
        {
            return true;
        }

        return source.TurnUris.SequenceEqual(
                   effective.TurnUris,
                   StringComparer.Ordinal) &&
               string.Equals(
                   source.CredentialMechanism,
                   effective.CredentialMechanism,
                   StringComparison.Ordinal) &&
               string.Equals(
                   source.SharedSecretValue,
                   effective.SharedSecretValue,
                   StringComparison.Ordinal) &&
               string.Equals(
                   source.SharedSecretPath,
                   effective.SharedSecretPath,
                   StringComparison.Ordinal) &&
               string.Equals(
                   source.UserLifetime,
                   effective.UserLifetime,
                   StringComparison.Ordinal) &&
               source.AllowGuests == effective.AllowGuests;
    }

    private static string? TryGetTurnHost(IReadOnlyList<string> turnUris)
    {
        foreach (var turnUri in turnUris)
        {
            if (string.IsNullOrWhiteSpace(turnUri))
            {
                continue;
            }

            var value = turnUri.Trim();
            if (value.StartsWith("turn:", StringComparison.OrdinalIgnoreCase))
            {
                value = value[5..];
            }
            else if (value.StartsWith("turns:", StringComparison.OrdinalIgnoreCase))
            {
                value = value[6..];
            }

            var withoutQuery = value.Split('?', 2)[0].Trim();
            if (withoutQuery.StartsWith("[", StringComparison.Ordinal))
            {
                var closing = withoutQuery.IndexOf(']');
                if (closing > 1)
                {
                    return withoutQuery[1..closing];
                }

                continue;
            }

            var colon = withoutQuery.LastIndexOf(':');
            return colon > 0 ? withoutQuery[..colon] : withoutQuery;
        }

        return null;
    }

    private static HostAgentServiceRuntimeResult CreateMatrixRuntimeResult(
        MigrationProductionAdoptionEntity plan,
        ChatStackRuntimePlan runtime,
        MatrixContainerStartResult start,
        RuntimeStackDatabaseProvisioningResult database,
        string? healthResponse,
        SynapseTurnConfigReadResult turn) =>
        new(
            InstanceId: plan.MatrixInstanceId,
            ServiceKey: ServiceKeys.Matrix,
            ContainerId: start.ContainerId,
            ContainerName: start.ContainerName,
            HostPort: 0,
            DataPath: runtime.MatrixDataPath,
            ServerName: plan.MatrixServerName,
            PublicHost: null,
            PublicBaseUrl: null,
            InternalHost: runtime.MatrixInternalHost,
            InternalBaseUrl: runtime.MatrixInternalBaseUrl,
            PublicRouteId: null,
            InternalRouteId: null,
            NpmCertificateId: null,
            RuntimeMetadata: new Dictionary<string, string?>
            {
                ["migrationId"] = plan.MigrationIntake.IntakeId,
                ["adoptionPlanId"] = plan.AdoptionPlanId,
                ["materializationId"] = plan.MaterializationId,
                ["runtimeNetworkName"] = runtime.RuntimeNetworkName,
                ["matrixImage"] = plan.MatrixImageId,
                ["approvedMatrixImageReference"] = plan.MatrixImageReference,
                ["matrixStarted"] = "true",
                ["matrixHealthPassed"] = "true",
                ["matrixHealthResponse"] = healthResponse,
                ["databaseHost"] = database.DatabaseHost,
                ["databasePort"] = database.DatabasePort.ToString(CultureInfo.InvariantCulture),
                ["databaseName"] = database.DatabaseName,
                ["databaseUsername"] = database.DatabaseUsername,
                ["databasePasswordSecretKind"] = database.PasswordSecretKind,
                ["intendedPublicHost"] = plan.MatrixPublicHost,
                ["intendedPublicBaseUrl"] = plan.MatrixPublicBaseUrl,
                ["publicRouteReady"] = "false",
                ["readinessVerified"] = "false",
                ["privateProductionMaterialization"] = "true",
                ["turnConfigured"] = turn.AnyTurnSettings ? "true" : "false",
                ["turnPublicHost"] = TryGetTurnHost(turn.TurnUris),
                ["turnRealm"] = turn.CommentRealm,
                ["turnUris"] = turn.TurnUris.Count > 0
                    ? string.Join(", ", turn.TurnUris)
                    : null,
                ["turnRelayPortsPublished"] = null,
                ["turnSharedSecretPresent"] = turn.SharedSecretPresent ? "true" : "false",
                ["turnUserLifetime"] = turn.UserLifetime,
                ["turnAllowGuests"] = turn.AllowGuests.HasValue
                    ? turn.AllowGuests.Value ? "true" : "false"
                    : null,
                ["turnConfigurationSource"] = turn.AnyTurnSettings
                    ? "migration-source-preserved"
                    : "migration-source-disconnected",
                ["turnManagement"] = turn.AnyTurnSettings
                    ? RuntimeStackTurnManagementKinds.ExternalObserved
                    : RuntimeStackTurnManagementKinds.None,
                ["turnConfigurationSha256"] = turn.FileSha256,
                ["turnLastOperationMode"] = "migration-materialization",
                ["turnLastInspectedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["turnSettingsPreserved"] = turn.AnyTurnSettings ? "true" : null,
            });

    private static HostAgentServiceRuntimeResult CreateElementRuntimeResult(
        MigrationProductionAdoptionEntity plan,
        ChatStackRuntimePlan runtime,
        ElementContainerStartResult start,
        string? healthResponse,
        string configPath) =>
        new(
            InstanceId: plan.ElementInstanceId,
            ServiceKey: ServiceKeys.ElementWeb,
            ContainerId: start.ContainerId,
            ContainerName: start.ContainerName,
            HostPort: 0,
            DataPath: runtime.ElementDataPath,
            ServerName: null,
            PublicHost: null,
            PublicBaseUrl: null,
            InternalHost: runtime.ElementInternalHost,
            InternalBaseUrl: runtime.ElementInternalBaseUrl,
            PublicRouteId: null,
            InternalRouteId: null,
            NpmCertificateId: null,
            RuntimeMetadata: new Dictionary<string, string?>
            {
                ["migrationId"] = plan.MigrationIntake.IntakeId,
                ["adoptionPlanId"] = plan.AdoptionPlanId,
                ["materializationId"] = plan.MaterializationId,
                ["runtimeNetworkName"] = runtime.RuntimeNetworkName,
                ["elementImage"] = plan.ElementImageId,
                ["approvedElementImageReference"] = plan.ElementImageReference,
                ["elementStarted"] = "true",
                ["elementHealthPassed"] = "true",
                ["elementHealthResponse"] = healthResponse,
                ["elementConfigPath"] = configPath,
                ["intendedPublicHost"] = plan.ElementPublicHost,
                ["intendedPublicBaseUrl"] = plan.ElementPublicBaseUrl,
                ["publicRouteReady"] = "false",
                ["readinessVerified"] = "false",
                ["privateProductionMaterialization"] = "true",
            });

    internal static MaterializationFailure ClassifyFailure(Exception exception) =>
        exception switch
        {
            FileNotFoundException => new(
                "migration_production_source_material_missing",
                "Required authoritative migration source material was not found."),
            InvalidDataException => new(
                "migration_production_evidence_invalid",
                "Authoritative migration evidence or restored production data did not pass verification."),
            OperationCanceledException => new(
                "migration_production_materialization_cancelled",
                "Private server creation did not complete before the bounded operation ended. No public route was created."),
            DockerApiException => new(
                "migration_production_docker_operation_failed",
                "Docker could not complete private production runtime materialisation."),
            _ => new(
                "migration_production_materialization_failed",
                "Private production runtime materialisation did not complete. No public route was created by this operation."),
        };

    private static string CreateMaterializationId() =>
        $"mpm_{DateTime.UtcNow:yyyyMMdd-HHmmss}Z_{Guid.NewGuid():N}";

    private static string Trim(string? value, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private sealed record DatabaseCounts(
        long PublicTables,
        long KnownSynapseTables,
        long? Users,
        long? Rooms,
        long? Events);

    private sealed record HealthResult(bool Passed, string? Response);
    private sealed record ExecResult(long ExitCode, string Stdout, string Stderr);
    internal sealed record MaterializationFailure(string Code, string Summary);
}
