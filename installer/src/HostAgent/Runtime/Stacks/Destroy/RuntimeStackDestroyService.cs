using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Ingress;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Operations;
using HostAgent.Runtime.ServiceRuntime;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Services;
using Shared.ControlPlane.Runtime;
using System.Text.Json;

namespace HostAgent.Runtime.Stacks.Destroy;

public sealed class RuntimeStackDestroyService
{
    private static readonly TimeSpan FailureJournalTimeout = TimeSpan.FromSeconds(5);

    private readonly DockerClient _docker;
    private readonly MemDbContext _db;
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly RuntimeOperationStore _operationStore;
    private readonly IRoutePublisher _routePublisher;
    private readonly NpmProxyHostService _npmProxyHostService;
    private readonly RuntimeStackDatabaseService _databaseService;
    private readonly RuntimeStackDestroyOperationLifetime _operationLifetime;
    private readonly MemControlPlaneRuntimeContext _runtimeContext;
    private readonly ILogger<RuntimeStackDestroyService> _logger;

    public RuntimeStackDestroyService(
        DockerClient docker,
        MemDbContext db,
        RuntimeStackManifestStore manifestStore,
        RuntimeOperationStore operationStore,
        IRoutePublisher routePublisher,
        NpmProxyHostService npmProxyHostService,
        RuntimeStackDatabaseService databaseService,
        RuntimeStackDestroyOperationLifetime operationLifetime,
        MemControlPlaneRuntimeContext runtimeContext,
        ILogger<RuntimeStackDestroyService> logger)
    {
        _docker = docker;
        _db = db;
        _manifestStore = manifestStore;
        _operationStore = operationStore;
        _routePublisher = routePublisher;
        _npmProxyHostService = npmProxyHostService;
        _databaseService = databaseService;
        _operationLifetime = operationLifetime;
        _runtimeContext = runtimeContext;
        _logger = logger;
    }

    internal async Task<RuntimeStackDestroyAcceptance> PrepareAsync(
        string slugOrId,
        RuntimeStackDestroyRequest? request,
        CancellationToken ct)
    {
        var effectiveRequest = NormalizeRequest(request);

        if (string.IsNullOrWhiteSpace(slugOrId))
        {
            throw new InvalidOperationException("Stack slug or id is required.");
        }

        var requestedIdentity = slugOrId.Trim();
        var manifest = await _manifestStore.FindAsync(
            requestedIdentity,
            ct);

        if (manifest is not null)
        {
            var stackRow = await _db.RuntimeStacks
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == manifest.StackId, ct);

            if (stackRow is not null && IsDestroyedRuntimeStack(stackRow))
            {
                throw new InvalidOperationException(
                    $"Runtime stack '{manifest.Slug}' is already recorded as destroyed.");
            }

            return new RuntimeStackDestroyAcceptance(
                RuntimeStackId: manifest.StackId,
                Slug: manifest.Slug,
                RuntimeStackRowExists: stackRow is not null,
                OwnershipSource: "manifest",
                OwnershipSnapshot: manifest,
                HostMutationLevel: BuildHostMutationLevel(effectiveRequest),
                Request: effectiveRequest);
        }

        var manifestlessStack = await FindRuntimeStackRowAsync(
            requestedIdentity,
            ct);

        if (manifestlessStack is null)
        {
            throw new InvalidOperationException(
                $"Runtime stack '{slugOrId}' was not found.");
        }

        if (IsDestroyedRuntimeStack(manifestlessStack))
        {
            throw new InvalidOperationException(
                $"Runtime stack '{manifestlessStack.Slug}' is already recorded as destroyed.");
        }

        if (!effectiveRequest.RemoveContainers ||
            !effectiveRequest.RemoveRoutes ||
            effectiveRequest.RemoveDatabase ||
            effectiveRequest.RemoveFiles ||
            effectiveRequest.Force)
        {
            throw new RuntimeStackDestroyOwnershipRefusedException(
                "Database-reconstructed destroy is limited to removing exact containers and routes while retaining database and files with force disabled.");
        }

        var reconstructed = BuildOwnershipSnapshotFromDatabase(manifestlessStack);

        return new RuntimeStackDestroyAcceptance(
            RuntimeStackId: reconstructed.StackId,
            Slug: reconstructed.Slug,
            RuntimeStackRowExists: true,
            OwnershipSource: "database-reconstruction",
            OwnershipSnapshot: reconstructed,
            HostMutationLevel: BuildHostMutationLevel(effectiveRequest),
            Request: effectiveRequest);
    }

    /// <summary>
    /// Compatibility entry point for trusted in-process callers. New HTTP
    /// endpoints should create the durable RuntimeOperation first and dispatch
    /// through <see cref="DestroyAcceptedAsync"/>.
    /// </summary>
    public async Task<RuntimeStackDestroyResult> DestroyAsync(
        string slugOrId,
        RuntimeStackDestroyRequest? request,
        CancellationToken ct)
    {
        var acceptance = await PrepareAsync(slugOrId, request, ct);
        var operationId = await _operationStore.StartAsync(
            runtimeStackId: acceptance.RuntimeStackRowExists
                ? acceptance.RuntimeStackId
                : null,
            operation: "destroy-stack-runtime",
            idempotencyKey: ResolveIdempotencyKey(acceptance.Request, acceptance.RuntimeStackId),
            requestedBy: "host-agent",
            hostMutationLevel: acceptance.HostMutationLevel,
            input: BuildOperationInput(acceptance),
            ct: ct);

        await _operationStore.UpdateStepAsync(operationId, "queued", ct);
        return await DestroyAcceptedAsync(acceptance, operationId);
    }

    internal async Task<RuntimeStackDestroyResult> DestroyAcceptedAsync(
        RuntimeStackDestroyAcceptance acceptance,
        Guid acceptedOperationId)
    {
        ArgumentNullException.ThrowIfNull(acceptance);

        using var operationScope = _operationLifetime.Begin();
        var ct = operationScope.CancellationToken;
        var currentStep = "validate";
        var steps = new List<RuntimeStackDestroyStepResult>();
        var warnings = new List<string>();

        try
        {
            await _operationStore.UpdateStepAsync(
                acceptedOperationId,
                currentStep,
                ct);

            var manifest = acceptance.OwnershipSnapshot;

            if (acceptance.OwnershipSource == "database-reconstruction")
            {
                var restoredManifest = await _manifestStore.FindAsync(
                    acceptance.RuntimeStackId.ToString("D"),
                    ct);

                if (restoredManifest is not null)
                {
                    throw new RuntimeStackDestroyOwnershipRefusedException(
                        $"Runtime stack '{acceptance.Slug}' regained an active manifest after reconciliation review. Use the normal Chat servers removal path instead.");
                }

                var currentStack = await FindRuntimeStackRowAsync(
                    acceptance.RuntimeStackId.ToString("D"),
                    ct) ?? throw new RuntimeStackDestroyOwnershipRefusedException(
                        $"Runtime stack '{acceptance.Slug}' no longer has the durable database record used for reconciliation review.");
                var currentSnapshot = BuildOwnershipSnapshotFromDatabase(currentStack);

                ValidateDatabaseReconstructionSnapshot(
                    acceptance.OwnershipSnapshot,
                    currentSnapshot);
                manifest = currentSnapshot;
            }

            if (manifest.StackId != acceptance.RuntimeStackId ||
                !string.Equals(
                    manifest.Slug,
                    acceptance.Slug,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The accepted destroy ownership snapshot does not match the accepted runtime stack identity.");
            }

            var request = acceptance.Request;
            var stackDataPath = ResolveStackDataPath(manifest);

            steps.Add(new RuntimeStackDestroyStepResult(
                Code: acceptance.OwnershipSource == "manifest"
                    ? "destroy.ownership.manifest"
                    : "destroy.ownership.database-reconstruction",
                Status: "completed",
                Message: acceptance.OwnershipSource == "manifest"
                    ? "Destroy ownership was frozen from the active runtime-stack manifest."
                    : "The active manifest was missing. Destroy ownership was reconstructed from the durable RuntimeStack, service-instance, and route records.",
                Data: new Dictionary<string, string?>
                {
                    ["ownershipSource"] = acceptance.OwnershipSource,
                    ["stackId"] = acceptance.RuntimeStackId.ToString(),
                    ["slug"] = acceptance.Slug
                }));

            if (acceptance.OwnershipSource == "database-reconstruction")
            {
                warnings.Add(
                    "The active runtime-stack manifest was missing. MEM recovered bounded destroy ownership from durable database records.");
            }

            steps.Add(new RuntimeStackDestroyStepResult(
                Code: "destroy.operation-lifetime.server-owned",
                Status: "completed",
                Message: "Destroy mutation is running under a bounded server-owned lifetime independent of the initiating HTTP request.",
                Data: new Dictionary<string, string?>
                {
                    ["requestAbortCancelsMutation"] = "false",
                    ["applicationStoppingCancelsMutation"] = "true",
                    ["operationTimeoutMinutes"] = RuntimeStackDestroyOperationLifetime
                        .DefaultOperationTimeout
                        .TotalMinutes
                        .ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));

            if (!acceptance.RuntimeStackRowExists)
            {
                warnings.Add(
                    $"RuntimeStack database row was not found for stack id '{manifest.StackId}'. This looks like an orphaned legacy/dev manifest. Destroy will continue without attaching the operation to a RuntimeStacks row.");

                steps.Add(new RuntimeStackDestroyStepResult(
                    Code: "runtime-stack.row.missing",
                    Status: "warning",
                    Message: "RuntimeStack database row was missing. Continuing cleanup from manifest data only.",
                    Data: new Dictionary<string, string?>
                    {
                        ["stackId"] = manifest.StackId.ToString(),
                        ["slug"] = manifest.Slug
                    }));
            }

            if (request.RemoveRoutes)
            {
                currentStep = "remove-matrix-route";
                await _operationStore.UpdateStepAsync(acceptedOperationId, currentStep, ct);
                await DestroyRouteAsync(
                    manifest.StackId,
                    manifest.Matrix,
                    requireDatabaseReconstructionOwnership: acceptance.OwnershipSource == "database-reconstruction",
                    steps,
                    warnings,
                    request.Force,
                    ct);

                if (manifest.Element is not null)
                {
                    currentStep = "remove-element-route";
                    await _operationStore.UpdateStepAsync(acceptedOperationId, currentStep, ct);
                    await DestroyRouteAsync(
                        manifest.StackId,
                        manifest.Element,
                        requireDatabaseReconstructionOwnership: acceptance.OwnershipSource == "database-reconstruction",
                        steps,
                        warnings,
                        request.Force,
                        ct);
                }
            }
            else
            {
                steps.Add(Skipped(
                    "routes.skipped",
                    "Route removal was not requested."));
            }

            if (request.RemoveContainers)
            {
                currentStep = "remove-matrix-container";
                await _operationStore.UpdateStepAsync(acceptedOperationId, currentStep, ct);
                await DestroyContainerAsync(
                    manifest.StackId,
                    manifest.Matrix,
                    requireDatabaseReconstructionOwnership: acceptance.OwnershipSource == "database-reconstruction",
                    steps,
                    warnings,
                    request.Force,
                    ct);

                if (manifest.Element is not null)
                {
                    currentStep = "remove-element-container";
                    await _operationStore.UpdateStepAsync(acceptedOperationId, currentStep, ct);
                    await DestroyContainerAsync(
                        manifest.StackId,
                        manifest.Element,
                        requireDatabaseReconstructionOwnership: acceptance.OwnershipSource == "database-reconstruction",
                        steps,
                        warnings,
                        request.Force,
                        ct);
                }
            }
            else
            {
                steps.Add(Skipped(
                    "containers.skipped",
                    "Container removal was not requested."));
            }

            if (request.RemoveDatabase)
            {
                currentStep = "remove-database";
                await _operationStore.UpdateStepAsync(acceptedOperationId, currentStep, ct);
                var dbResult = await _databaseService.DropMatrixDatabaseAsync(
                    manifest.StackId,
                    force: request.Force,
                    ct);

                steps.Add(new RuntimeStackDestroyStepResult(
                    Code: "database.drop",
                    Status: dbResult.Dropped ? "completed" : "skipped",
                    Message: dbResult.Detail,
                    Data: new Dictionary<string, string?>
                    {
                        ["databaseName"] = dbResult.DatabaseName,
                        ["databaseUsername"] = dbResult.DatabaseUsername,
                        ["dropped"] = dbResult.Dropped ? "true" : "false"
                    }));

                foreach (var warning in dbResult.Warnings)
                {
                    warnings.Add(warning);
                }
            }
            else
            {
                currentStep = "retain-database";
                await _operationStore.UpdateStepAsync(acceptedOperationId, currentStep, ct);
                steps.Add(Skipped(
                    "database.skipped",
                    "Database removal was not requested. Matrix Postgres database and role were kept."));
            }

            if (request.RemoveFiles)
            {
                currentStep = "remove-files";
                await _operationStore.UpdateStepAsync(acceptedOperationId, currentStep, ct);
                if (string.IsNullOrWhiteSpace(stackDataPath))
                {
                    steps.Add(Skipped(
                        "files.skipped",
                        "Stack data path could not be determined."));
                }
                else
                {
                    await DestroyFilesAsync(
                        manifest.StackId,
                        stackDataPath,
                        steps,
                        warnings,
                        request.Force,
                        ct);
                }
            }
            else
            {
                currentStep = "retain-files";
                await _operationStore.UpdateStepAsync(acceptedOperationId, currentStep, ct);
                steps.Add(new RuntimeStackDestroyStepResult(
                    Code: "files.kept",
                    Status: "kept",
                    Message: "Stack files were kept. Set removeFiles=true to delete local Matrix/Element data.",
                    Data: new Dictionary<string, string?>
                    {
                        ["path"] = stackDataPath
                    }));
            }

            currentStep = "mark-runtime-stack-destroyed";
            await _operationStore.UpdateStepAsync(acceptedOperationId, currentStep, ct);
            if (acceptance.RuntimeStackRowExists)
            {
                var rowChanged = await MarkDatabaseRowsDestroyedAsync(
                    manifest.StackId,
                    request,
                    ct);

                steps.Add(new RuntimeStackDestroyStepResult(
                    Code: "runtime-stack.row.destroyed",
                    Status: rowChanged ? "completed" : "skipped",
                    Message: rowChanged
                        ? "RuntimeStack database row was marked destroyed and its slug was released."
                        : "RuntimeStack database row was already marked destroyed.",
                    Data: new Dictionary<string, string?>
                    {
                        ["stackId"] = manifest.StackId.ToString(),
                        ["slug"] = manifest.Slug
                    }));
            }
            else
            {
                steps.Add(new RuntimeStackDestroyStepResult(
                    Code: "runtime-stack.row.skipped",
                    Status: "skipped",
                    Message: "RuntimeStack database row update was skipped because no row exists for this legacy/orphaned manifest.",
                    Data: new Dictionary<string, string?>
                    {
                        ["stackId"] = manifest.StackId.ToString(),
                        ["slug"] = manifest.Slug
                    }));
            }

            currentStep = "delete-manifest";
            await _operationStore.UpdateStepAsync(acceptedOperationId, currentStep, ct);
            var manifestDeleted = await _manifestStore.DeleteAsync(
                manifest.Slug,
                ct);

            steps.Add(new RuntimeStackDestroyStepResult(
                Code: "manifest.delete",
                Status: manifestDeleted ? "completed" : "skipped",
                Message: manifestDeleted
                    ? "Runtime stack manifest was removed from the local manifest store."
                    : "Runtime stack manifest was not found or was already removed.",
                Data: new Dictionary<string, string?>
                {
                    ["slug"] = manifest.Slug,
                    ["stackId"] = manifest.StackId.ToString()
                }));

            var result = new RuntimeStackDestroyResult(
                Source: "control-plane",
                Status: warnings.Count == 0 ? "destroyed" : "destroyed_with_warnings",
                OperationId: acceptedOperationId,
                RuntimeStackId: manifest.StackId,
                Slug: manifest.Slug,
                ContainersRequested: request.RemoveContainers,
                RoutesRequested: request.RemoveRoutes,
                DatabaseRequested: request.RemoveDatabase,
                FilesRequested: request.RemoveFiles,
                Steps: steps,
                Warnings: warnings,
                KeptDataPath: request.RemoveFiles ? null : stackDataPath,
                Detail: request.RemoveFiles
                    ? "Runtime stack destroy operation completed. Files were requested for deletion."
                    : "Runtime stack destroy operation completed. Local data files were kept.",
                DestroyedAtUtc: DateTimeOffset.UtcNow);

            currentStep = "complete";
            using var completionTimeout =
                new CancellationTokenSource(FailureJournalTimeout);
            await _operationStore.CompleteAsync(
                acceptedOperationId,
                status: "succeeded",
                currentStep: currentStep,
                result: result,
                evidence: steps,
                ct: completionTimeout.Token);

            return result;
        }
        catch (Exception ex)
        {
            await JournalFailureAsync(
                acceptedOperationId,
                currentStep,
                ex,
                steps,
                operationScope);
            throw;
        }
    }

    internal static bool CanContinueAfterStepFailure(
        bool force,
        Exception exception) =>
        force &&
        exception is not OperationCanceledException &&
        exception is not RuntimeStackDestroyOwnershipRefusedException &&
        exception is not StackOverflowException &&
        exception is not OutOfMemoryException;

    internal static string ResolveIdempotencyKey(
        RuntimeStackDestroyRequest request,
        Guid runtimeStackId)
    {
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var supplied = request.IdempotencyKey.Trim();
            if (supplied.Length > 200)
            {
                throw new InvalidOperationException(
                    "Destroy idempotency key must not exceed 200 characters.");
            }

            return supplied;
        }

        return $"destroy-stack:{runtimeStackId:N}:{Guid.NewGuid():N}";
    }

    internal static object BuildOperationInput(RuntimeStackDestroyAcceptance acceptance) => new
    {
        acceptance.RuntimeStackId,
        acceptance.Slug,
        acceptance.RuntimeStackRowExists,
        acceptance.OwnershipSource,
        acceptance.Request.RemoveContainers,
        acceptance.Request.RemoveRoutes,
        acceptance.Request.RemoveDatabase,
        acceptance.Request.RemoveFiles,
        acceptance.Request.Force,
        acceptance.Request.IdempotencyKey
    };

    private async Task<RuntimeStackEntity?> FindRuntimeStackRowAsync(
        string slugOrId,
        CancellationToken ct)
    {
        var query = _db.RuntimeStacks
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.ServiceInstances)
            .Include(x => x.Routes);

        if (Guid.TryParse(slugOrId, out var stackId))
        {
            return await query.FirstOrDefaultAsync(x => x.Id == stackId, ct);
        }

        return await query.FirstOrDefaultAsync(x => x.Slug == slugOrId, ct);
    }

    internal static RuntimeStackManifest BuildOwnershipSnapshotFromDatabase(
        RuntimeStackEntity stack)
    {
        ArgumentNullException.ThrowIfNull(stack);

        if (IsDestroyedRuntimeStack(stack))
        {
            throw new InvalidOperationException(
                $"Runtime stack '{stack.Slug}' is already recorded as destroyed.");
        }

        var matrix = BuildServiceSnapshot(
            stack,
            ServiceKeys.Matrix,
            required: true)!;
        var element = BuildServiceSnapshot(
            stack,
            ServiceKeys.ElementWeb,
            required: false);
        var lastVerifiedAt = stack.LastVerifiedAtUtc ?? stack.UpdatedAtUtc;

        return new RuntimeStackManifest(
            Source: "control-plane-database-reconstruction",
            StackId: stack.Id,
            Slug: stack.Slug,
            LastVerifiedStatus: stack.LastVerifiedStatus ?? stack.Status,
            LastVerifiedAtUtc: new DateTimeOffset(
                DateTime.SpecifyKind(lastVerifiedAt, DateTimeKind.Utc)),
            Matrix: matrix,
            Element: element,
            Warnings:
            [
                "The active runtime-stack manifest was missing. Ownership was reconstructed from durable database records for bounded destroy recovery."
            ],
            Metadata: new Dictionary<string, string?>
            {
                ["ownershipSource"] = "database-reconstruction",
                ["manifestPath"] = stack.ManifestPath,
                ["dataRoot"] = stack.DataRoot,
                ["runtimeNetworkName"] = stack.RuntimeNetworkName
            });
    }

    internal static void ValidateDatabaseReconstructionSnapshot(
        RuntimeStackManifest accepted,
        RuntimeStackManifest current)
    {
        ArgumentNullException.ThrowIfNull(accepted);
        ArgumentNullException.ThrowIfNull(current);

        if (accepted.StackId != current.StackId ||
            !string.Equals(
                accepted.Slug,
                current.Slug,
                StringComparison.OrdinalIgnoreCase) ||
            !ServiceOwnershipMatches(accepted.Matrix, current.Matrix) ||
            !ServiceOwnershipMatches(accepted.Element, current.Element))
        {
            throw new RuntimeStackDestroyOwnershipRefusedException(
                "The durable service or route ownership changed after reconciliation review. MEM refused to continue the destructive operation.");
        }
    }

    private static bool ServiceOwnershipMatches(
        RuntimeStackServiceManifest? accepted,
        RuntimeStackServiceManifest? current)
    {
        if (accepted is null || current is null)
        {
            return accepted is null && current is null;
        }

        return accepted.InstanceId == current.InstanceId &&
               string.Equals(
                   accepted.ServiceKey,
                   current.ServiceKey,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   accepted.ContainerId,
                   current.ContainerId,
                   StringComparison.Ordinal) &&
               string.Equals(
                   accepted.ContainerName,
                   current.ContainerName,
                   StringComparison.Ordinal) &&
               string.Equals(
                   accepted.DataPath,
                   current.DataPath,
                   StringComparison.Ordinal) &&
               string.Equals(
                   accepted.InternalHost,
                   current.InternalHost,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   accepted.PublicHost,
                   current.PublicHost,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   accepted.PublicRouteId,
                   current.PublicRouteId,
                   StringComparison.Ordinal) &&
               accepted.NpmCertificateId == current.NpmCertificateId &&
               MetadataValueMatches(
                   accepted.RuntimeMetadata,
                   current.RuntimeMetadata,
                   "publicForwardHost",
                   ignoreCase: true) &&
               MetadataValueMatches(
                   accepted.RuntimeMetadata,
                   current.RuntimeMetadata,
                   "publicForwardPort",
                   ignoreCase: false);
    }

    private static bool MetadataValueMatches(
        IReadOnlyDictionary<string, string?> accepted,
        IReadOnlyDictionary<string, string?> current,
        string key,
        bool ignoreCase)
    {
        accepted.TryGetValue(key, out var acceptedValue);
        current.TryGetValue(key, out var currentValue);

        return string.Equals(
            acceptedValue,
            currentValue,
            ignoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static RuntimeStackServiceManifest? BuildServiceSnapshot(
        RuntimeStackEntity stack,
        string serviceKey,
        bool required)
    {
        var services = stack.ServiceInstances
            .Where(x => string.Equals(
                x.ServiceKey,
                serviceKey,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (services.Length == 0)
        {
            if (!required)
            {
                return null;
            }

            throw new InvalidOperationException(
                $"Runtime stack '{stack.Slug}' cannot be recovered because its '{serviceKey}' service record is missing.");
        }

        if (services.Length != 1)
        {
            throw new InvalidOperationException(
                $"Runtime stack '{stack.Slug}' cannot be recovered because it has {services.Length} '{serviceKey}' service records.");
        }

        var service = services[0];
        var routes = stack.Routes
            .Where(x =>
                x.RuntimeServiceInstanceId == service.Id ||
                string.Equals(
                    x.ServiceKey,
                    serviceKey,
                    StringComparison.OrdinalIgnoreCase))
            .DistinctBy(x => x.Id)
            .ToArray();

        if (routes.Length > 1)
        {
            throw new InvalidOperationException(
                $"Runtime stack '{stack.Slug}' cannot be recovered because '{serviceKey}' has {routes.Length} route records.");
        }

        var route = routes.SingleOrDefault();
        if (route is not null &&
            !string.Equals(route.Provider, "npm", StringComparison.OrdinalIgnoreCase))
        {
            throw new RuntimeStackDestroyOwnershipRefusedException(
                $"Runtime stack '{stack.Slug}' cannot be recovered because '{serviceKey}' uses unsupported route provider '{route.Provider}'.");
        }

        var metadata = ParseMetadata(service.RuntimeMetadataJson);
        metadata["runtimeNetworkName"] = service.NetworkName ?? stack.RuntimeNetworkName;
        metadata["publicForwardHost"] = route?.ForwardHost;
        metadata["publicForwardPort"] = route?.ForwardPort.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        metadata["activeNpmCertificateId"] = (route?.NpmCertificateId ?? stack.ActiveNpmCertificateId)
            ?.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new RuntimeStackServiceManifest(
            InstanceId: service.InstanceId,
            ServiceKey: service.ServiceKey,
            ContainerId: service.ContainerId,
            ContainerName: service.ContainerName,
            HostPort: service.HostPort ?? 0,
            DataPath: service.DataPath,
            ServerName: service.ServerName,
            PublicHost: route?.PublicHost ?? service.PublicHost,
            PublicBaseUrl: route?.PublicBaseUrl ?? service.PublicBaseUrl,
            InternalHost: service.InternalHost,
            InternalBaseUrl: service.InternalBaseUrl,
            PublicRouteId: route?.ProviderRouteId,
            InternalRouteId: null,
            NpmCertificateId: route?.NpmCertificateId ?? stack.ActiveNpmCertificateId,
            RuntimeMetadata: metadata);
    }

    private static Dictionary<string, string?> ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json) is { } parsed
                ? new Dictionary<string, string?>(parsed, StringComparer.Ordinal)
                : new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }

    private static bool IsDestroyedRuntimeStack(RuntimeStackEntity stack) =>
        string.Equals(stack.Status, "destroyed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(stack.LastVerifiedStatus, "destroyed", StringComparison.OrdinalIgnoreCase) ||
        stack.Slug.Contains("--destroyed-", StringComparison.OrdinalIgnoreCase);

    private async Task JournalFailureAsync(
        Guid operationId,
        string currentStep,
        Exception exception,
        IReadOnlyList<RuntimeStackDestroyStepResult> steps,
        RuntimeStackDestroyOperationScope operationScope)
    {
        try
        {
            using var journalTimeout =
                new CancellationTokenSource(FailureJournalTimeout);
            await _operationStore.FailAsync(
                operationId,
                currentStep: currentStep,
                error: exception.Message,
                evidence: new
                {
                    failureKind = ResolveFailureKind(exception, operationScope),
                    exceptionType = exception.GetType().FullName,
                    operationScope.OperationTimeoutRequested,
                    operationScope.ApplicationStoppingRequested,
                    steps
                },
                ct: journalTimeout.Token);
        }
        catch (Exception journalException)
        {
            _logger.LogError(
                journalException,
                "Could not persist terminal failure for destroy-stack operation {OperationId}. OriginalExceptionType={OriginalExceptionType}",
                operationId,
                exception.GetType().FullName);
        }
    }

    private static string ResolveFailureKind(
        Exception exception,
        RuntimeStackDestroyOperationScope operationScope)
    {
        if (exception is OperationCanceledException)
        {
            if (operationScope.ApplicationStoppingRequested)
            {
                return "destroy-stack-application-stopping";
            }

            if (operationScope.OperationTimeoutRequested)
            {
                return "destroy-stack-operation-timeout";
            }

            return "destroy-stack-operation-cancelled";
        }

        return "destroy-stack-failed";
    }

    private static RuntimeStackDestroyRequest NormalizeRequest(
        RuntimeStackDestroyRequest? request)
    {
        var effective = request ?? new RuntimeStackDestroyRequest();
        return effective with
        {
            IdempotencyKey = string.IsNullOrWhiteSpace(effective.IdempotencyKey)
                ? null
                : effective.IdempotencyKey.Trim()
        };
    }

    private async Task DestroyRouteAsync(
        Guid runtimeStackId,
        RuntimeStackServiceManifest service,
        bool requireDatabaseReconstructionOwnership,
        List<RuntimeStackDestroyStepResult> steps,
        List<string> warnings,
        bool force,
        CancellationToken ct)
    {
        var publicRouteId = string.IsNullOrWhiteSpace(service.PublicRouteId)
            ? null
            : service.PublicRouteId.Trim();
        var publicHost = string.IsNullOrWhiteSpace(service.PublicHost)
            ? null
            : service.PublicHost.Trim();

        if (publicRouteId is null && publicHost is null)
        {
            steps.Add(Skipped(
                $"route.{service.ServiceKey}.skipped",
                $"No public route id or public host was recorded for {service.ServiceKey}."));
            return;
        }

        try
        {
            if (requireDatabaseReconstructionOwnership)
            {
                if (publicHost is null)
                {
                    throw new RuntimeStackDestroyOwnershipRefusedException(
                        $"Refusing database-reconstructed destroy for runtime stack '{runtimeStackId:D}' service '{service.ServiceKey}' because the durable route record does not contain a public host.");
                }

                var observed = await _npmProxyHostService.GetByDomainAsync(
                    publicHost,
                    ct);

                if (observed is null)
                {
                    steps.Add(Skipped(
                        $"route.{service.ServiceKey}.missing",
                        $"The recorded NPM route for {service.ServiceKey} was already absent. The destroy operation can safely continue."));
                    return;
                }

                RuntimeStackDestroyRouteOwnershipValidator
                    .ValidateDatabaseReconstruction(
                        runtimeStackId,
                        service,
                        observed);

                await _routePublisher.DeleteByDomainIfExistsAsync(
                    publicHost,
                    ct);

                steps.Add(new RuntimeStackDestroyStepResult(
                    Code: $"route.{service.ServiceKey}.delete",
                    Status: "completed",
                    Message: $"The ownership-verified NPM route for {service.ServiceKey} was deleted by its exact public host.",
                    Data: new Dictionary<string, string?>
                    {
                        ["publicRouteId"] = publicRouteId,
                        ["observedProxyHostId"] = observed.id.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        ["publicHost"] = publicHost,
                        ["deletionMode"] = "verified-domain"
                    }));
                return;
            }

            var deletionMode = "domain";

            if (publicRouteId is not null)
            {
                await _routePublisher.DeleteByIdIfExistsAsync(
                    publicRouteId,
                    ct);
                deletionMode = publicHost is null ? "id" : "id+domain";
            }

            if (publicHost is not null)
            {
                await _routePublisher.DeleteByDomainIfExistsAsync(
                    publicHost,
                    ct);
            }

            steps.Add(new RuntimeStackDestroyStepResult(
                Code: $"route.{service.ServiceKey}.delete",
                Status: "completed",
                Message: publicRouteId is null
                    ? $"Route deletion was requested for {service.ServiceKey} by public host."
                    : publicHost is null
                        ? $"Route deletion was requested for {service.ServiceKey} by route id."
                        : $"Route deletion was requested for {service.ServiceKey} by route id with a public-host fallback.",
                Data: new Dictionary<string, string?>
                {
                    ["publicRouteId"] = publicRouteId,
                    ["publicHost"] = publicHost,
                    ["deletionMode"] = deletionMode
                }));
        }
        catch (Exception ex) when (CanContinueAfterStepFailure(force, ex))
        {
            warnings.Add(
                $"Route deletion failed for {service.ServiceKey}, but force=true allowed destroy to continue: {ex.Message}");
            steps.Add(new RuntimeStackDestroyStepResult(
                Code: $"route.{service.ServiceKey}.delete",
                Status: "warning",
                Message: ex.Message,
                Data: new Dictionary<string, string?>
                {
                    ["publicRouteId"] = publicRouteId,
                    ["publicHost"] = publicHost
                }));
        }
    }

    private async Task DestroyContainerAsync(
        Guid runtimeStackId,
        RuntimeStackServiceManifest service,
        bool requireDatabaseReconstructionOwnership,
        List<RuntimeStackDestroyStepResult> steps,
        List<string> warnings,
        bool force,
        CancellationToken ct)
    {
        try
        {
            var resolved = await ResolveContainerAsync(
                service.ContainerId,
                service.ContainerName,
                ct);

            if (resolved is null)
            {
                steps.Add(Skipped(
                    $"container.{service.ServiceKey}.missing",
                    $"Container for {service.ServiceKey} was not found. The destroy operation can safely continue."));
                return;
            }

            if (requireDatabaseReconstructionOwnership)
            {
                RuntimeStackDestroyContainerOwnershipValidator
                    .ValidateDatabaseReconstruction(
                        runtimeStackId,
                        service,
                        new RuntimeStackDestroyContainerObservation(
                            resolved.Id,
                            resolved.Name,
                            resolved.Running,
                            resolved.Labels,
                            resolved.MountSources),
                        _runtimeContext);
            }

            if (resolved.Running)
            {
                await _docker.Containers.StopContainerAsync(
                    resolved.Id,
                    new ContainerStopParameters
                    {
                        WaitBeforeKillSeconds = 10
                    },
                    ct);
            }

            await _docker.Containers.RemoveContainerAsync(
                resolved.Id,
                new ContainerRemoveParameters
                {
                    Force = force,
                    RemoveVolumes = false
                },
                ct);

            steps.Add(new RuntimeStackDestroyStepResult(
                Code: $"container.{service.ServiceKey}.remove",
                Status: "completed",
                Message: resolved.Running
                    ? $"Container for {service.ServiceKey} was stopped and removed."
                    : $"Already-stopped container for {service.ServiceKey} was removed.",
                Data: new Dictionary<string, string?>
                {
                    ["containerId"] = resolved.Id,
                    ["containerName"] = resolved.Name,
                    ["wasRunning"] = resolved.Running ? "true" : "false",
                    ["ownershipVerified"] = requireDatabaseReconstructionOwnership ? "true" : "manifest"
                }));
        }
        catch (Exception ex) when (CanContinueAfterStepFailure(force, ex))
        {
            warnings.Add(
                $"Container removal failed for {service.ServiceKey}, but force=true allowed destroy to continue: {ex.Message}");
            steps.Add(new RuntimeStackDestroyStepResult(
                Code: $"container.{service.ServiceKey}.remove",
                Status: "warning",
                Message: ex.Message,
                Data: new Dictionary<string, string?>
                {
                    ["containerId"] = service.ContainerId,
                    ["containerName"] = service.ContainerName
                }));
        }
    }

    private async Task DestroyFilesAsync(
        Guid stackId,
        string stackDataPath,
        List<RuntimeStackDestroyStepResult> steps,
        List<string> warnings,
        bool force,
        CancellationToken ct)
    {
        try
        {
            if (!IsSafeStackDataPath(stackId, stackDataPath))
            {
                throw new InvalidOperationException(
                    $"Refusing to delete unsafe stack data path: {stackDataPath}");
            }

            if (!Directory.Exists(stackDataPath))
            {
                steps.Add(Skipped(
                    "files.missing",
                    $"Stack data path does not exist: {stackDataPath}"));
                return;
            }

            await Task.Run(
                () => Directory.Delete(stackDataPath, recursive: true),
                ct);

            steps.Add(new RuntimeStackDestroyStepResult(
                Code: "files.delete",
                Status: "completed",
                Message: "Stack local data directory was deleted.",
                Data: new Dictionary<string, string?>
                {
                    ["path"] = stackDataPath
                }));
        }
        catch (Exception ex) when (CanContinueAfterStepFailure(force, ex))
        {
            warnings.Add(
                $"File deletion failed, but force=true allowed destroy to continue: {ex.Message}");
            steps.Add(new RuntimeStackDestroyStepResult(
                Code: "files.delete",
                Status: "warning",
                Message: ex.Message,
                Data: new Dictionary<string, string?>
                {
                    ["path"] = stackDataPath
                }));
        }
    }

    private async Task<ResolvedContainer?> ResolveContainerAsync(
        string? containerId,
        string? containerName,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(containerId))
        {
            try
            {
                var info = await _docker.Containers.InspectContainerAsync(
                    containerId,
                    ct);

                return ToResolvedContainer(info);
            }
            catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Fall back to name lookup below. This is required for recovery
                // from a previous partially completed destroy operation.
            }
        }

        if (string.IsNullOrWhiteSpace(containerName))
        {
            return null;
        }

        var containers = await _docker.Containers.ListContainersAsync(
            new ContainersListParameters
            {
                All = true,
                Filters = new Dictionary<string, IDictionary<string, bool>>
                {
                    ["name"] = new Dictionary<string, bool>
                    {
                        [containerName] = true
                    }
                }
            },
            ct);

        var match = containers.FirstOrDefault(x =>
            x.Names.Any(name =>
                string.Equals(
                    name.TrimStart('/'),
                    containerName,
                    StringComparison.OrdinalIgnoreCase)));

        if (match is null)
        {
            return null;
        }

        var inspected = await _docker.Containers.InspectContainerAsync(
            match.ID,
            ct);

        return ToResolvedContainer(inspected);
    }

    private async Task<bool> MarkDatabaseRowsDestroyedAsync(
        Guid stackId,
        RuntimeStackDestroyRequest request,
        CancellationToken ct)
    {
        var stack = await _db.RuntimeStacks
            .AsSplitQuery()
            .Include(x => x.ServiceInstances)
            .Include(x => x.Routes)
            .FirstOrDefaultAsync(x => x.Id == stackId, ct);

        if (stack is null)
        {
            return false;
        }

        if (string.Equals(stack.Status, "destroyed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var originalSlug = stack.Slug;

        _db.RuntimeRoutes.RemoveRange(stack.Routes);
        _db.RuntimeServiceInstances.RemoveRange(stack.ServiceInstances);

        if (request.RemoveDatabase)
        {
            var databaseRows = await _db.RuntimeStackDatabases
                .Where(x => x.RuntimeStackId == stackId)
                .ToArrayAsync(ct);
            _db.RuntimeStackDatabases.RemoveRange(databaseRows);
        }

        stack.Slug = await GenerateReleasedDestroyedSlugAsync(
            originalSlug,
            now,
            ct);
        stack.DisplayName = string.IsNullOrWhiteSpace(stack.DisplayName)
            ? originalSlug
            : stack.DisplayName;
        stack.Status = "destroyed";
        stack.LastVerifiedStatus = "destroyed";
        stack.LastVerifiedAtUtc = now;
        stack.UpdatedAtUtc = now;
        stack.LastError = null;

        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static RuntimeStackDestroyStepResult Skipped(
        string code,
        string message) =>
        new(
            Code: code,
            Status: "skipped",
            Message: message,
            Data: new Dictionary<string, string?>());

    internal static string BuildHostMutationLevel(
        RuntimeStackDestroyRequest request)
    {
        var parts = new List<string>();
        if (request.RemoveContainers) parts.Add("docker");
        if (request.RemoveRoutes) parts.Add("ingress");
        if (request.RemoveDatabase) parts.Add("postgres");
        if (request.RemoveFiles) parts.Add("filesystem");
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }

    private static string? ResolveStackDataPath(
        RuntimeStackManifest manifest)
    {
        var matrixPath = manifest.Matrix.DataPath;
        if (string.IsNullOrWhiteSpace(matrixPath)) return null;
        return new DirectoryInfo(matrixPath).Parent?.FullName;
    }

    private static bool IsSafeStackDataPath(
        Guid stackId,
        string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var fullPath = Path.GetFullPath(path);
        var leaf = Path.GetFileName(fullPath);
        return string.Equals(
                   leaf,
                   stackId.ToString("N"),
                   StringComparison.OrdinalIgnoreCase) &&
               fullPath.Contains(
                   $"{Path.DirectorySeparatorChar}instances{Path.DirectorySeparatorChar}",
                   StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> GenerateReleasedDestroyedSlugAsync(
        string originalSlug,
        DateTime destroyedAtUtc,
        CancellationToken ct)
    {
        var safeOriginal = string.IsNullOrWhiteSpace(originalSlug)
            ? "stack"
            : originalSlug.Trim().ToLowerInvariant();
        var timestamp = destroyedAtUtc.ToString("yyyyMMddHHmmss");
        var baseSlug = $"{safeOriginal}--destroyed-{timestamp}";
        var candidate = baseSlug;
        var counter = 1;

        while (await _db.RuntimeStacks.AnyAsync(x => x.Slug == candidate, ct))
        {
            candidate = $"{baseSlug}-{counter}";
            counter++;
        }

        return candidate;
    }

    private static ResolvedContainer ToResolvedContainer(
        ContainerInspectResponse inspected)
    {
        var labels = inspected.Config?.Labels is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(
                inspected.Config.Labels,
                StringComparer.Ordinal);
        var mountSources = (inspected.Mounts ?? [])
            .Select(mount => mount.Source)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .ToArray();

        return new ResolvedContainer(
            Id: inspected.ID,
            Name: (inspected.Name ?? string.Empty).TrimStart('/'),
            Running: inspected.State?.Running ?? false,
            Labels: labels,
            MountSources: mountSources);
    }


    private sealed record ResolvedContainer(
        string Id,
        string Name,
        bool Running,
        IReadOnlyDictionary<string, string> Labels,
        IReadOnlyList<string> MountSources);
}
