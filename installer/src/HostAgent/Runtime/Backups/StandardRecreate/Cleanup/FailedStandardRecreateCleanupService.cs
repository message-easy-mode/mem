using System.Text.Json;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Planning;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Ingress;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.StandardRecreate.Cleanup;

/// <summary>
/// Removes resources from a failed Standard Recreate only after its restore has
/// been deliberately cancelled. Resource identity is derived from the immutable
/// recreate history record and runtime IDs, never from caller-supplied paths,
/// database names, container names, or route IDs.
/// </summary>
public sealed class FailedStandardRecreateCleanupService
{
    private const string MatrixComponent = "matrix";
    private const string ElementComponent = "element-web";

    private readonly StandardRecreateHistoryService _history;
    private readonly ChatStackRuntimePlanner _runtimePlanner;
    private readonly RuntimeStackDatabaseService _databaseService;
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly RuntimeOperationStore _operationStore;
    private readonly RestoreStructuredLogService _restoreLogs;
    private readonly IRoutePublisher _routePublisher;
    private readonly DockerClient _docker;
    private readonly MemDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FailedStandardRecreateCleanupService> _logger;

    public FailedStandardRecreateCleanupService(
        StandardRecreateHistoryService history,
        ChatStackRuntimePlanner runtimePlanner,
        RuntimeStackDatabaseService databaseService,
        RuntimeStackManifestStore manifestStore,
        RuntimeOperationStore operationStore,
        RestoreStructuredLogService restoreLogs,
        IRoutePublisher routePublisher,
        DockerClient docker,
        MemDbContext db,
        IConfiguration configuration,
        ILogger<FailedStandardRecreateCleanupService> logger)
    {
        _history = history;
        _runtimePlanner = runtimePlanner;
        _databaseService = databaseService;
        _manifestStore = manifestStore;
        _operationStore = operationStore;
        _restoreLogs = restoreLogs;
        _routePublisher = routePublisher;
        _docker = docker;
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<FailedStandardRecreateCleanupAssessment> AssessAsync(
        string recreateId,
        CancellationToken ct)
    {
        var recreate = await LoadFailedRecreateAsync(recreateId, ct);
        var checks = new List<FailedStandardRecreateCleanupCheck>();
        var warnings = new List<string>();
        var errors = new List<string>();

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.recreate.status",
            "error",
            string.Equals(recreate.Status, "production_recreate_failed", StringComparison.OrdinalIgnoreCase),
            "The Standard Recreate run is recorded as failed.",
            recreate.Status);

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.stack.not-registered",
            "error",
            !recreate.Runtime.StackRegistered &&
            !recreate.Runtime.ManifestSaved &&
            !recreate.Runtime.DatabaseOwnershipSaved,
            "The failed recreate did not become a normal MEM runtime stack.",
            $"stackRegistered={recreate.Runtime.StackRegistered}; manifestSaved={recreate.Runtime.ManifestSaved}; databaseOwnershipSaved={recreate.Runtime.DatabaseOwnershipSaved}");

        var attempt = await _db.RestoreAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                !string.IsNullOrWhiteSpace(recreate.CatalogEntryId) &&
                x.SourceCatalogEntryIdSnapshot == recreate.CatalogEntryId, ct);

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.restore-attempt.present",
            "error",
            attempt is not null,
            "A catalog-bound restore attempt is linked to the source backup.",
            attempt?.RestoreSessionId);

        var restoreCancelled = attempt is not null &&
                              string.Equals(attempt.Status, RestoreAttemptStatuses.Cancelled, StringComparison.OrdinalIgnoreCase);

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.restore.cancelled",
            "error",
            restoreCancelled,
            "The failed restore was deliberately cancelled before destructive cleanup.",
            attempt is null ? null : $"status={attempt.Status}; stage={attempt.CurrentStage}");

        RuntimeOperationEntity? recreateOperation = null;
        if (attempt is not null)
        {
            recreateOperation = await FindRecreateOperationAsync(attempt.Id, recreate.RecreateId, ct);
        }

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.recreate-operation.failed",
            "error",
            recreateOperation is not null &&
            string.Equals(recreateOperation.Status, "failed", StringComparison.OrdinalIgnoreCase),
            "The linked Standard Recreate operation is recorded as failed.",
            recreateOperation is null ? null : $"operationId={recreateOperation.Id}; status={recreateOperation.Status}");

        var activeCleanup = attempt is null
            ? null
            : await _db.RuntimeOperations
                .AsNoTracking()
                .Where(x => x.RestoreAttemptId == attempt.Id &&
                            x.Operation == "restore.standard-recreate.cleanup" &&
                            (x.Status == "queued" || x.Status == "running"))
                .OrderByDescending(x => x.RequestedAtUtc)
                .FirstOrDefaultAsync(ct);

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.no-cleanup-in-progress",
            "error",
            activeCleanup is null,
            "No other failed-recreate cleanup operation is running for this restore.",
            activeCleanup is null ? null : activeCleanup.Id.ToString());

        var runtimeStackExists = await _db.RuntimeStacks
            .AsNoTracking()
            .AnyAsync(x => x.Id == recreate.RuntimeStackId || x.Slug == recreate.TargetStackSlug, ct);

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.runtime-stack.absent",
            "error",
            !runtimeStackExists,
            "No normal runtime stack record owns the failed recreate target.",
            recreate.TargetStackSlug);

        var manifest = await _manifestStore.FindAsync(recreate.TargetStackSlug, ct);
        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.manifest.absent",
            "error",
            manifest is null,
            "No normal runtime manifest owns the failed recreate target.",
            manifest?.Slug);

        var plan = _runtimePlanner.Plan(
            recreate.RuntimeStackId,
            recreate.MatrixInstanceId,
            recreate.ElementInstanceId,
            recreate.TargetStackSlug);

        var matrixRuntimeMatches = string.IsNullOrWhiteSpace(recreate.Runtime.MatrixContainerName) ||
                                   string.Equals(recreate.Runtime.MatrixContainerName, plan.MatrixContainerName, StringComparison.Ordinal);
        var elementRuntimeMatches = string.IsNullOrWhiteSpace(recreate.Runtime.ElementContainerName) ||
                                    string.Equals(recreate.Runtime.ElementContainerName, plan.ElementContainerName, StringComparison.Ordinal);

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.matrix-container.identity",
            "error",
            matrixRuntimeMatches,
            "Matrix container identity matches the deterministic runtime plan.",
            $"expected={plan.MatrixContainerName}; recorded={recreate.Runtime.MatrixContainerName}");

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.element-container.identity",
            "error",
            elementRuntimeMatches,
            "Element container identity matches the deterministic runtime plan.",
            $"expected={plan.ElementContainerName}; recorded={recreate.Runtime.ElementContainerName}");

        var expectedDatabase = _databaseService.GetExpectedMatrixDatabaseIdentity(
            recreate.RuntimeStackId,
            recreate.TargetStackSlug);

        var databaseIdentityMatches = !recreate.Database.Provisioned ||
                                      (string.Equals(recreate.Database.DatabaseName, expectedDatabase.DatabaseName, StringComparison.Ordinal) &&
                                       string.Equals(recreate.Database.DatabaseUsername, expectedDatabase.DatabaseUsername, StringComparison.Ordinal));

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.database.identity",
            "error",
            databaseIdentityMatches,
            "Recorded failed-recreate database identity matches the deterministic runtime stack identity.",
            recreate.Database.Provisioned
                ? $"expected={expectedDatabase.DatabaseName}/{expectedDatabase.DatabaseUsername}; recorded={recreate.Database.DatabaseName}/{recreate.Database.DatabaseUsername}"
                : "No database was recorded as provisioned.");

        var matrixContainer = await InspectContainerAsync(plan.MatrixContainerName, MatrixComponent, ct);
        var elementContainer = await InspectContainerAsync(plan.ElementContainerName!, ElementComponent, ct);

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.matrix-container.managed",
            "error",
            !matrixContainer.Exists || matrixContainer.ManagedByMem,
            "Any Matrix container at the deterministic failed-recreate name is MEM-managed and safe to remove.",
            matrixContainer.Detail);

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.element-container.managed",
            "error",
            !elementContainer.Exists || elementContainer.ManagedByMem,
            "Any Element container at the deterministic failed-recreate name is MEM-managed and safe to remove.",
            elementContainer.Detail);

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.matrix-route.id",
            "error",
            IsSafeRouteId(recreate.Routes.MatrixRouteId),
            "The recorded Matrix route id is empty or a valid NPM numeric identifier.",
            recreate.Routes.MatrixRouteId);

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.element-route.id",
            "error",
            IsSafeRouteId(recreate.Routes.ElementRouteId),
            "The recorded Element route id is empty or a valid NPM numeric identifier.",
            recreate.Routes.ElementRouteId);

        var instanceDirectory = Path.GetFullPath(Path.GetDirectoryName(plan.MatrixDataPath) ?? throw new InvalidOperationException("Runtime plan did not provide a Matrix instance directory."));
        var workspaceDirectory = GetWorkspaceDirectoryPath(recreate.RecreateId);

        AddCheck(
            checks,
            errors,
            "failed-recreate.cleanup.instance-directory.identity",
            "error",
            IsExpectedInstanceDirectory(plan, instanceDirectory, recreate.RuntimeStackId),
            "Instance directory is derived from the failed recreate runtime identity.",
            instanceDirectory);

        var canCleanup = errors.Count == 0;
        var status = canCleanup ? "cleanup_ready" : "cleanup_blocked";

        return new FailedStandardRecreateCleanupAssessment(
            Source: "control-plane",
            Status: status,
            RecreateId: recreate.RecreateId,
            CatalogEntryId: recreate.CatalogEntryId,
            RestoreSessionId: attempt?.RestoreSessionId,
            RestoreAttemptStatus: attempt?.Status,
            RuntimeStackId: recreate.RuntimeStackId,
            TargetStackSlug: recreate.TargetStackSlug,
            RequiresCancelledRestore: true,
            CanCleanup: canCleanup,
            MatrixContainerPresent: matrixContainer.Exists,
            ElementContainerPresent: elementContainer.Exists,
            MatrixRouteId: recreate.Routes.MatrixRouteId,
            ElementRouteId: recreate.Routes.ElementRouteId,
            DatabaseProvisioned: recreate.Database.Provisioned,
            DatabaseName: recreate.Database.Provisioned ? recreate.Database.DatabaseName : null,
            DatabaseUsername: recreate.Database.Provisioned ? recreate.Database.DatabaseUsername : null,
            InstanceDirectoryPath: instanceDirectory,
            InstanceDirectoryPresent: Directory.Exists(instanceDirectory),
            WorkspaceDirectoryPath: workspaceDirectory,
            WorkspaceDirectoryPresent: Directory.Exists(workspaceDirectory),
            Checks: checks,
            Warnings: warnings,
            Errors: errors,
            Detail: canCleanup
                ? "The failed recreate was cancelled and its recorded partial resources can be removed safely."
                : "Failed-recreate cleanup is blocked until the listed safety checks are resolved.");
    }

    public async Task<FailedStandardRecreateCleanupResult> ExecuteAsync(
        string recreateId,
        FailedStandardRecreateCleanupRequest request,
        CancellationToken ct)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var cleanupId = BuildCleanupId();
        var assessment = await AssessAsync(recreateId, ct);
        var warnings = assessment.Warnings.ToList();
        var errors = assessment.Errors.ToList();
        var steps = new List<FailedStandardRecreateCleanupStep>();

        if (!assessment.CanCleanup)
        {
            return BuildResult(
                "cleanup_blocked",
                cleanupId,
                null,
                assessment,
                startedAtUtc,
                false, false, false, false, false, false, false,
                steps,
                warnings,
                errors,
                "Failed-recreate cleanup did not run because its safety assessment is blocked.");
        }

        if (!request.AcknowledgeCleanup)
        {
            return BuildResult(
                "operator_confirmation_required",
                cleanupId,
                null,
                assessment,
                startedAtUtc,
                false, false, false, false, false, false, false,
                steps,
                warnings,
                errors,
                "Explicit acknowledgement is required before partial failed-recreate resources are removed.");
        }

        var recreate = await LoadFailedRecreateAsync(recreateId, ct);
        var attempt = await _db.RestoreAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                !string.IsNullOrWhiteSpace(recreate.CatalogEntryId) &&
                x.SourceCatalogEntryIdSnapshot == recreate.CatalogEntryId, ct)
            ?? throw new InvalidOperationException("The failed recreate no longer has a restore attempt.");

        var runningCleanup = await _db.RuntimeOperations
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RestoreAttemptId == attempt.Id &&
                                      x.Operation == "restore.standard-recreate.cleanup" &&
                                      (x.Status == "queued" || x.Status == "running"), ct);

        if (runningCleanup is not null)
        {
            throw new FailedStandardRecreateCleanupConflictException(
                new FailedStandardRecreateCleanupConflictResponse(
                    Code: "failed_recreate_cleanup_in_progress",
                    RecreateId: recreate.RecreateId,
                    RestoreSessionId: attempt.RestoreSessionId,
                    Detail: $"Failed recreate cleanup '{runningCleanup.Id}' is already running for restore '{attempt.RestoreSessionId}'."));
        }

        var operationId = await _operationStore.StartAsync(
            runtimeStackId: null,
            operation: "restore.standard-recreate.cleanup",
            idempotencyKey: $"restore.standard-recreate.cleanup:{recreate.RecreateId}",
            requestedBy: NormalizeOperator(request.Operator),
            hostMutationLevel: "filesystem,postgres-write,docker,npm",
            input: new
            {
                RecreateId = recreate.RecreateId,
                CatalogEntryId = recreate.CatalogEntryId,
                RestoreSessionId = attempt.RestoreSessionId,
                RuntimeStackId = recreate.RuntimeStackId,
                TargetStackSlug = recreate.TargetStackSlug,
                DatabaseName = assessment.DatabaseName,
                DatabaseUsername = assessment.DatabaseUsername,
                MatrixRouteId = assessment.MatrixRouteId,
                ElementRouteId = assessment.ElementRouteId,
                InstanceDirectoryPath = assessment.InstanceDirectoryPath,
                WorkspaceDirectoryPath = assessment.WorkspaceDirectoryPath
            },
            ct: ct,
            restoreAttemptId: attempt.Id);

        var matrixRouteRemoved = false;
        var elementRouteRemoved = false;
        var matrixContainerRemoved = false;
        var elementContainerRemoved = false;
        var databaseDropped = false;
        var instanceDirectoryDeleted = false;
        var workspaceDirectoryDeleted = false;

        try
        {
            matrixRouteRemoved = await DeleteRouteAsync("matrix", assessment.MatrixRouteId, steps, ct);
            elementRouteRemoved = await DeleteRouteAsync("element-web", assessment.ElementRouteId, steps, ct);

            var plan = _runtimePlanner.Plan(
                recreate.RuntimeStackId,
                recreate.MatrixInstanceId,
                recreate.ElementInstanceId,
                recreate.TargetStackSlug);

            matrixContainerRemoved = await RemoveManagedContainerAsync(
                "matrix",
                plan.MatrixContainerName,
                MatrixComponent,
                steps,
                ct);

            elementContainerRemoved = await RemoveManagedContainerAsync(
                "element-web",
                plan.ElementContainerName!,
                ElementComponent,
                steps,
                ct);

            if (recreate.Database.Provisioned)
            {
                var drop = await _databaseService.DropUnregisteredMatrixDatabaseAsync(
                    recreate.RuntimeStackId,
                    recreate.TargetStackSlug,
                    recreate.Database.DatabaseName,
                    recreate.Database.DatabaseUsername,
                    ct);

                databaseDropped = drop.Dropped;
                warnings.AddRange(drop.Warnings);
                steps.Add(Completed(
                    "database.drop",
                    drop.Detail,
                    new Dictionary<string, string?>
                    {
                        ["databaseName"] = drop.DatabaseName,
                        ["databaseUsername"] = drop.DatabaseUsername,
                        ["dropped"] = drop.Dropped ? "true" : "false"
                    }));
            }
            else
            {
                steps.Add(Skipped("database.drop", "No failed-recreate database was recorded as provisioned."));
            }

            instanceDirectoryDeleted = await DeleteDirectoryAsync(
                "instance-directory.delete",
                assessment.InstanceDirectoryPath,
                steps,
                ct);

            workspaceDirectoryDeleted = await DeleteDirectoryAsync(
                "workspace-directory.delete",
                assessment.WorkspaceDirectoryPath,
                steps,
                ct);

            var result = BuildResult(
                "cleanup_completed",
                cleanupId,
                operationId,
                assessment,
                startedAtUtc,
                matrixRouteRemoved,
                elementRouteRemoved,
                matrixContainerRemoved,
                elementContainerRemoved,
                databaseDropped,
                instanceDirectoryDeleted,
                workspaceDirectoryDeleted,
                steps,
                warnings,
                errors,
                "Failed Standard Recreate resources recorded for this cancelled restore were removed.");

            await _operationStore.CompleteAsync(
                operationId,
                status: "succeeded",
                currentStep: "cleanup-completed",
                result: result,
                evidence: steps,
                ct);

            await _restoreLogs.RecordAsync(
                attempt.Id,
                operationId,
                stage: "cleanup",
                severity: RestoreLogSeverities.Information,
                eventCode: "restore.standard-recreate.cleanup.completed",
                message: "Partial resources recorded for the cancelled failed recreate were removed.",
                details: new Dictionary<string, string?>
                {
                    ["recreateId"] = recreate.RecreateId,
                    ["databaseDropped"] = databaseDropped ? "true" : "false",
                    ["instanceDirectoryDeleted"] = instanceDirectoryDeleted ? "true" : "false",
                    ["workspaceDirectoryDeleted"] = workspaceDirectoryDeleted ? "true" : "false"
                },
                ct: ct);

            _logger.LogInformation(
                "Failed Standard Recreate cleanup completed. CleanupId={CleanupId} RecreateId={RecreateId} RestoreSessionId={RestoreSessionId}",
                cleanupId,
                recreate.RecreateId,
                attempt.RestoreSessionId);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(ex.Message);
            steps.Add(Failed("cleanup.failed", ex.Message));

            var result = BuildResult(
                "cleanup_failed",
                cleanupId,
                operationId,
                assessment,
                startedAtUtc,
                matrixRouteRemoved,
                elementRouteRemoved,
                matrixContainerRemoved,
                elementContainerRemoved,
                databaseDropped,
                instanceDirectoryDeleted,
                workspaceDirectoryDeleted,
                steps,
                warnings,
                errors,
                "Failed Standard Recreate cleanup did not complete. Review the recorded steps and retry only after resolving the error.");

            await _operationStore.FailAsync(
                operationId,
                currentStep: "cleanup-failed",
                error: ex.Message,
                evidence: steps,
                ct);

            await _restoreLogs.RecordAsync(
                attempt.Id,
                operationId,
                stage: "cleanup",
                severity: RestoreLogSeverities.Error,
                eventCode: "restore.standard-recreate.cleanup.failed",
                message: ex.Message,
                details: new Dictionary<string, string?>
                {
                    ["recreateId"] = recreate.RecreateId
                },
                ct: ct);

            _logger.LogError(
                ex,
                "Failed Standard Recreate cleanup failed. CleanupId={CleanupId} RecreateId={RecreateId} RestoreSessionId={RestoreSessionId}",
                cleanupId,
                recreate.RecreateId,
                attempt.RestoreSessionId);

            return result;
        }
    }

    private async Task<StandardRecreateResult> LoadFailedRecreateAsync(string recreateId, CancellationToken ct)
    {
        var detail = await _history.GetAsync(recreateId, ct)
            ?? throw new FileNotFoundException($"Production recreate run '{recreateId}' was not found.");

        return detail.Recreate;
    }

    private async Task<RuntimeOperationEntity?> FindRecreateOperationAsync(
        Guid restoreAttemptId,
        string recreateId,
        CancellationToken ct)
    {
        var operations = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(x => x.RestoreAttemptId == restoreAttemptId &&
                        x.Operation == "restore.standard-recreate")
            .OrderByDescending(x => x.RequestedAtUtc)
            .Take(20)
            .ToListAsync(ct);

        return operations.FirstOrDefault(x => OperationReferencesRecreate(x.InputJson, recreateId));
    }

    private static bool OperationReferencesRecreate(string? inputJson, string recreateId)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(inputJson);
            return document.RootElement.TryGetProperty("recreateId", out var value) &&
                   string.Equals(value.GetString(), recreateId, StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<ContainerAssessment> InspectContainerAsync(
        string expectedName,
        string expectedComponent,
        CancellationToken ct)
    {
        var containers = await _docker.Containers.ListContainersAsync(
            new ContainersListParameters { All = true },
            ct);

        var matched = containers.FirstOrDefault(x => x.Names.Any(name =>
            string.Equals(name.TrimStart('/'), expectedName, StringComparison.OrdinalIgnoreCase)));

        if (matched is null)
        {
            return new ContainerAssessment(false, false, null, expectedName, "Container is not present.");
        }

        var inspect = await _docker.Containers.InspectContainerAsync(matched.ID, ct);
        var labels = inspect.Config.Labels ?? new Dictionary<string, string>();
        var managed = labels.TryGetValue("mem.managed-by", out var managedBy) &&
                      string.Equals(managedBy, "host-agent", StringComparison.OrdinalIgnoreCase) &&
                      labels.TryGetValue("mem.component", out var component) &&
                      string.Equals(component, expectedComponent, StringComparison.OrdinalIgnoreCase);

        return new ContainerAssessment(
            Exists: true,
            ManagedByMem: managed,
            Id: inspect.ID,
            Name: expectedName,
            Detail: $"containerId={inspect.ID}; managedByMem={managed}");
    }

    private async Task<bool> DeleteRouteAsync(
        string serviceKey,
        string? routeId,
        List<FailedStandardRecreateCleanupStep> steps,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(routeId))
        {
            steps.Add(Skipped($"route.{serviceKey}.delete", $"No recorded route id exists for {serviceKey}."));
            return false;
        }

        await _routePublisher.DeleteByIdIfExistsAsync(routeId, ct);
        steps.Add(Completed(
            $"route.{serviceKey}.delete",
            $"Recorded route deletion was requested for {serviceKey}.",
            new Dictionary<string, string?> { ["routeId"] = routeId }));
        return true;
    }

    private async Task<bool> RemoveManagedContainerAsync(
        string serviceKey,
        string expectedName,
        string expectedComponent,
        List<FailedStandardRecreateCleanupStep> steps,
        CancellationToken ct)
    {
        var assessment = await InspectContainerAsync(expectedName, expectedComponent, ct);
        if (!assessment.Exists)
        {
            steps.Add(Skipped($"container.{serviceKey}.remove", $"Container '{expectedName}' was not present."));
            return false;
        }

        if (!assessment.ManagedByMem || string.IsNullOrWhiteSpace(assessment.Id))
        {
            throw new InvalidOperationException(
                $"Refusing to remove container '{expectedName}' because it is not the expected MEM-managed {serviceKey} resource.");
        }

        var inspect = await _docker.Containers.InspectContainerAsync(assessment.Id, ct);
        if (inspect.State.Running)
        {
            await _docker.Containers.StopContainerAsync(
                assessment.Id,
                new ContainerStopParameters { WaitBeforeKillSeconds = 10 },
                ct);
        }

        await _docker.Containers.RemoveContainerAsync(
            assessment.Id,
            new ContainerRemoveParameters { Force = false, RemoveVolumes = false },
            ct);

        steps.Add(Completed(
            $"container.{serviceKey}.remove",
            $"MEM-managed {serviceKey} container was stopped and removed.",
            new Dictionary<string, string?>
            {
                ["containerId"] = assessment.Id,
                ["containerName"] = expectedName
            }));
        return true;
    }

    private static async Task<bool> DeleteDirectoryAsync(
        string code,
        string path,
        List<FailedStandardRecreateCleanupStep> steps,
        CancellationToken ct)
    {
        if (!Directory.Exists(path))
        {
            steps.Add(Skipped(code, $"Directory was not present: {path}"));
            return false;
        }

        var info = new DirectoryInfo(path);
        if (!string.IsNullOrWhiteSpace(info.LinkTarget))
        {
            throw new InvalidOperationException($"Refusing to delete symbolic-link directory '{path}'.");
        }

        await Task.Run(() => Directory.Delete(path, recursive: true), ct);
        steps.Add(Completed(
            code,
            "Recorded failed-recreate directory was deleted.",
            new Dictionary<string, string?> { ["path"] = path }));
        return true;
    }

    private string GetWorkspaceDirectoryPath(string recreateId)
    {
        if (!IsSafePathSegment(recreateId))
        {
            throw new InvalidOperationException("Production recreate id is invalid.");
        }

        return Path.GetFullPath(Path.Combine(ResolveDataRoot(), "production-recreate", "work", recreateId));
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static bool IsExpectedInstanceDirectory(
        ChatStackRuntimePlan plan,
        string instanceDirectory,
        Guid runtimeStackId)
    {
        var expectedMatrix = Path.GetFullPath(plan.MatrixDataPath);
        var expectedElement = Path.GetFullPath(plan.ElementDataPath ?? string.Empty);
        var expectedRoot = Path.GetFullPath(Path.GetDirectoryName(expectedMatrix) ?? string.Empty);
        var separator = Path.DirectorySeparatorChar.ToString();

        return !string.IsNullOrWhiteSpace(expectedRoot) &&
               string.Equals(instanceDirectory, expectedRoot, StringComparison.Ordinal) &&
               Path.GetFileName(instanceDirectory).Equals(runtimeStackId.ToString("N"), StringComparison.OrdinalIgnoreCase) &&
               expectedMatrix.StartsWith(expectedRoot + separator, StringComparison.Ordinal) &&
               expectedElement.StartsWith(expectedRoot + separator, StringComparison.Ordinal);
    }

    private static bool IsSafeRouteId(string? routeId) =>
        string.IsNullOrWhiteSpace(routeId) ||
        (int.TryParse(routeId, out var parsed) && parsed > 0);

    private static bool IsSafePathSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static string NormalizeOperator(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "host-agent" : value.Trim()[..Math.Min(value.Trim().Length, 100)];

    private static string BuildCleanupId() =>
        DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss'Z'") + "-" + Guid.NewGuid().ToString("N")[..8];

    private static FailedStandardRecreateCleanupResult BuildResult(
        string status,
        string cleanupId,
        Guid? runtimeOperationId,
        FailedStandardRecreateCleanupAssessment assessment,
        DateTimeOffset startedAtUtc,
        bool matrixRouteRemoved,
        bool elementRouteRemoved,
        bool matrixContainerRemoved,
        bool elementContainerRemoved,
        bool databaseDropped,
        bool instanceDirectoryDeleted,
        bool workspaceDirectoryDeleted,
        IReadOnlyList<FailedStandardRecreateCleanupStep> steps,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> errors,
        string detail) =>
        new(
            Source: "control-plane",
            Status: status,
            CleanupId: cleanupId,
            RuntimeOperationId: runtimeOperationId,
            RecreateId: assessment.RecreateId,
            CatalogEntryId: assessment.CatalogEntryId,
            RestoreSessionId: assessment.RestoreSessionId,
            StartedAtUtc: startedAtUtc,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            MatrixRouteRemoved: matrixRouteRemoved,
            ElementRouteRemoved: elementRouteRemoved,
            MatrixContainerRemoved: matrixContainerRemoved,
            ElementContainerRemoved: elementContainerRemoved,
            DatabaseDropped: databaseDropped,
            InstanceDirectoryDeleted: instanceDirectoryDeleted,
            WorkspaceDirectoryDeleted: workspaceDirectoryDeleted,
            Assessment: assessment,
            Steps: steps,
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray(),
            Errors: errors.Distinct(StringComparer.Ordinal).ToArray(),
            Detail: detail);

    private static FailedStandardRecreateCleanupStep Completed(
        string code,
        string message,
        IReadOnlyDictionary<string, string?> data) =>
        new(code, "completed", message, data);

    private static FailedStandardRecreateCleanupStep Skipped(string code, string message) =>
        new(code, "skipped", message, new Dictionary<string, string?>());

    private static FailedStandardRecreateCleanupStep Failed(string code, string message) =>
        new(code, "failed", message, new Dictionary<string, string?>());

    private static void AddCheck(
        List<FailedStandardRecreateCleanupCheck> checks,
        List<string> errors,
        string code,
        string severity,
        bool passed,
        string message,
        string? detail)
    {
        checks.Add(new FailedStandardRecreateCleanupCheck(code, severity, passed, message, detail));
        if (!passed && string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"{message}{(string.IsNullOrWhiteSpace(detail) ? string.Empty : $" Detail: {detail}")}");
        }
    }

    private sealed record ContainerAssessment(
        bool Exists,
        bool ManagedByMem,
        string? Id,
        string Name,
        string Detail);
}
