using System.Net;
using System.Security.Cryptography;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Commands;
using HostAgent.Docker;
using HostAgent.Element.Provisioning;
using HostAgent.Element.Runtime;
using HostAgent.Matrix.Provisioning;
using HostAgent.Matrix.Runtime;
using HostAgent.Matrix.Users;
using HostAgent.Platform;
using HostAgent.Planning;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Databases;
using HostAgent.Runtime.Filesystem;
using HostAgent.Runtime.Ingress;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Readiness;
using HostAgent.Runtime.Secrets;
using HostAgent.Runtime.ServiceRuntime;
using HostAgent.Runtime.Stacks.Identity;
using HostAgent.Runtime.Stacks.Turn;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Infrastructure.Persistence;
using HostAgent.Runtime.Backups.Artifacts.PortableExports;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Catalog;
using Modules.Shared.RuntimeImages;

namespace HostAgent.Runtime.Backups.StandardRecreate;

public sealed class StandardRecreateService : IStandardRecreateExecutor
{
    private const string PostgresContainerName = "mem-postgres";

    private readonly IConfiguration _configuration;
    private readonly DockerClient _docker;
    private readonly IDockerHost _dockerHost;
    private readonly MemDbContext _db;
    private readonly PlatformDomainResolver _domainResolver;
    private readonly ChatStackRuntimePlanner _runtimePlanner;
    private readonly RuntimeDirectoryPreparer _directoryPreparer;
    private readonly RuntimeStackDatabaseService _databaseService;
    private readonly RuntimeStackSecretService _secretService;
    private readonly SynapseConfigGenerator _synapseConfigGenerator;
    private readonly ElementConfigGenerator _elementConfigGenerator;
    private readonly MatrixContainerStarter _matrixContainerStarter;
    private readonly ElementContainerStarter _elementContainerStarter;
    private readonly CoturnRuntimeService _coturnRuntimeService;
    private readonly IRoutePublisher _routePublisher;
    private readonly RuntimeReadinessVerifier _readinessVerifier;
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly RuntimeStackLogoService _logoService;
    private readonly StandardRecreateUserInventoryFinalizer _userInventoryFinalizer;
    private readonly StandardRecreateHistoryService _history;
    private readonly RestoreAttemptCoordinator _restoreAttemptCoordinator;
    private readonly BackupCatalogPayloadResolver _catalogPayloadResolver;
    private readonly IApprovedPostgresRuntimeProvider _approvedPostgresRuntimeProvider;
    private readonly IApprovedOperationalRuntimeImageProvider _approvedOperationalRuntimeImageProvider;
    private readonly RuntimeNetworkOptions _networkOptions;
    private readonly StandardRecreateOperationLifetime _operationLifetime;
    private readonly ILogger<StandardRecreateService> _logger;

    public StandardRecreateService(
        IConfiguration configuration,
        DockerClient docker,
        IDockerHost dockerHost,
        MemDbContext db,
        PlatformDomainResolver domainResolver,
        ChatStackRuntimePlanner runtimePlanner,
        RuntimeDirectoryPreparer directoryPreparer,
        RuntimeStackDatabaseService databaseService,
        RuntimeStackSecretService secretService,
        SynapseConfigGenerator synapseConfigGenerator,
        ElementConfigGenerator elementConfigGenerator,
        MatrixContainerStarter matrixContainerStarter,
        ElementContainerStarter elementContainerStarter,
        CoturnRuntimeService coturnRuntimeService,
        IRoutePublisher routePublisher,
        RuntimeReadinessVerifier readinessVerifier,
        RuntimeStackManifestStore manifestStore,
        RuntimeStackLogoService logoService,
        StandardRecreateUserInventoryFinalizer userInventoryFinalizer,
        StandardRecreateHistoryService history,
        RestoreAttemptCoordinator restoreAttemptCoordinator,
        BackupCatalogPayloadResolver catalogPayloadResolver,
        IApprovedPostgresRuntimeProvider approvedPostgresRuntimeProvider,
        IApprovedOperationalRuntimeImageProvider approvedOperationalRuntimeImageProvider,
        IOptions<RuntimeNetworkOptions> networkOptions,
        StandardRecreateOperationLifetime operationLifetime,
        ILogger<StandardRecreateService> logger)
    {
        _configuration = configuration;
        _docker = docker;
        _dockerHost = dockerHost;
        _db = db;
        _domainResolver = domainResolver;
        _runtimePlanner = runtimePlanner;
        _directoryPreparer = directoryPreparer;
        _databaseService = databaseService;
        _secretService = secretService;
        _synapseConfigGenerator = synapseConfigGenerator;
        _elementConfigGenerator = elementConfigGenerator;
        _matrixContainerStarter = matrixContainerStarter;
        _elementContainerStarter = elementContainerStarter;
        _coturnRuntimeService = coturnRuntimeService;
        _routePublisher = routePublisher;
        _readinessVerifier = readinessVerifier;
        _manifestStore = manifestStore;
        _logoService = logoService;
        _userInventoryFinalizer = userInventoryFinalizer;
        _history = history;
        _restoreAttemptCoordinator = restoreAttemptCoordinator;
        _catalogPayloadResolver = catalogPayloadResolver;
        _approvedPostgresRuntimeProvider = approvedPostgresRuntimeProvider;
        _approvedOperationalRuntimeImageProvider = approvedOperationalRuntimeImageProvider;
        _networkOptions = networkOptions.Value;
        _operationLifetime = operationLifetime;
        _logger = logger;
    }

    public Task<StandardRecreateResult> ExecuteCatalogAsync(
        string catalogEntryId,
        string restoreSessionId,
        StandardRecreateRequest request,
        CancellationToken ct) =>
        ExecuteFromCatalogAsync(
            catalogEntryId,
            restoreSessionId,
            request,
            ct);


    /// <summary>
    /// Executes Standard Recreate from a canonical Backup Catalog payload. The
    /// catalog identity is authoritative. Uploaded ZIP validation receipts are
    /// ingestion provenance only and are never used to locate source files or
    /// create a restore attempt.
    /// </summary>
    public async Task<StandardRecreateResult> ExecuteFromCatalogAsync(
        string catalogEntryId,
        string restoreSessionId,
        StandardRecreateRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(catalogEntryId))
        {
            throw new InvalidOperationException("Backup Catalog entry id is required.");
        }

        if (string.IsNullOrWhiteSpace(restoreSessionId) ||
            !IsSafePathSegment(restoreSessionId))
        {
            throw new InvalidOperationException(
                "Restore session id is required and must be a safe path segment.");
        }

        var material = await _catalogPayloadResolver
            .ResolveStandardRecreateExecutionMaterialAsync(
                catalogEntryId,
                ct);

        var source = StandardRecreateSourceMaterial.CreateCatalog(
            material,
            restoreSessionId);

        return await ExecuteCoreAsync(source, request, ct);
    }

    private async Task<StandardRecreateResult> ExecuteCoreAsync(
        StandardRecreateSourceMaterial source,
        StandardRecreateRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!request.ExecuteProductionRecreate)
        {
            throw new InvalidOperationException("executeProductionRecreate=true is required.");
        }

        var missingAcks = new List<string>();
        if (!request.AcknowledgeCreatesRealStack) missingAcks.Add("acknowledgeCreatesRealStack");
        if (!request.AcknowledgeMutatesProductionPostgres) missingAcks.Add("acknowledgeMutatesProductionPostgres");
        if (!request.AcknowledgeMutatesNpmRoutes) missingAcks.Add("acknowledgeMutatesNpmRoutes");
        if (!request.AcknowledgeNoAutomaticRollback) missingAcks.Add("acknowledgeNoAutomaticRollback");

        if (missingAcks.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required acknowledgements: {string.Join(", ", missingAcks)}");
        }

        var startedAtUtc = DateTimeOffset.UtcNow;
        var recreateId = CreateRunId();
        var warnings = new List<string>();
        var errors = new List<string>();
        var checks = new List<StandardRecreateCheck>();
        var evidence = new List<HostAgentEvidence>();

        var dataRoot = ResolveDataRoot();
        var workspacePath = Path.Combine(
            dataRoot,
            "production-recreate",
            "work",
            recreateId);
        var databaseDirectory = Path.Combine(workspacePath, "database");

        var stackId = Guid.NewGuid();
        var matrixInstanceId = Guid.NewGuid();
        var elementInstanceId = Guid.NewGuid();

        var sourceStackSlug = source.SourceStackSlug;
        var sourceServerName = source.MatrixServerName;

        if (!string.IsNullOrWhiteSpace(request.MatrixImage) ||
            !string.IsNullOrWhiteSpace(request.ElementImage))
        {
            throw new InvalidOperationException(
                "Caller-supplied Matrix/Element image overrides are no longer supported for Standard Recreate. " +
                "MEM uses release-approved immutable runtime image authorities.");
        }

        var approvedSynapseRuntime = await _approvedOperationalRuntimeImageProvider
            .PrepareSynapseAsync(ct);
        var approvedElementRuntime = await _approvedOperationalRuntimeImageProvider
            .PrepareElementAsync(ct);
        var matrixImage = approvedSynapseRuntime.ResolvedImageId;
        var elementImage = approvedElementRuntime.ResolvedImageId;

        string targetStackSlug = "unknown";
        string matrixHost = "unknown";
        string elementHost = "unknown";

        StandardRecreateDatabaseSummary? databaseSummary = null;
        StandardRecreateRuntimeSummary? runtimeSummary = null;
        StandardRecreateRouteSummary? routeSummary = null;
        StandardRecreateUserInventorySummary? userInventorySummary = null;
        StandardRecreateTurnSummary? turnSummary = null;
        Guid? restoreAttemptId = null;
        Guid? runtimeOperationId = null;
        var restoreOperationCompleted = false;
        var requestAborted = ct;
        StandardRecreateOperationScope? operationLifetime = null;

        try
        {
            sourceStackSlug = string.IsNullOrWhiteSpace(source.SourceStackSlug)
                ? "restored-stack"
                : source.SourceStackSlug.Trim();
            sourceServerName = StandardRecreateMatrixIdentity.NormalizeHost(
                source.MatrixServerName)
                ?? throw new InvalidDataException(
                    "The backup does not declare the original Matrix server identity required for a standard restore.");

            targetStackSlug = Slugify(string.IsNullOrWhiteSpace(request.TargetStackSlug)
                ? $"{sourceStackSlug}-restored"
                : request.TargetStackSlug);

            AddCheck(
                checks,
                errors,
                "production-recreate.source-material.resolved",
                "error",
                true,
                "Standard Recreate source material was resolved directly from the managed Backup Catalog payload.",
                source.CatalogEntryId);

            var existingManifest = await _manifestStore.FindAsync(
                targetStackSlug,
                ct);
            var existingStackRow = await _db.RuntimeStacks
                .AsNoTracking()
                .AnyAsync(x => x.Slug == targetStackSlug, ct);

            var targetAvailable = existingManifest is null && !existingStackRow;
            AddCheck(
                checks,
                errors,
                "production-recreate.target-stack.available",
                "error",
                targetAvailable,
                targetAvailable
                    ? $"Target stack slug '{targetStackSlug}' is available."
                    : $"Target stack slug '{targetStackSlug}' already exists as a MEM runtime stack.",
                targetStackSlug);

            if (!targetAvailable)
            {
                var attempt = await _restoreAttemptCoordinator.GetByRestoreSessionIdAsync(
                    source.RestoreSessionId,
                    ct);

                if (attempt is null)
                {
                    throw new InvalidOperationException(
                        "The canonical restore attempt was not found for this source.");
                }

                throw new RestoreAttemptConflictException(
                    new RestoreAttemptConflictResponse(
                        Code: "restore_target_occupied",
                        RestoreSessionId: attempt.RestoreSessionId,
                        ResourceType: RestoreTargetResourceTypes.StackSlug,
                        ResourceValue: targetStackSlug,
                        Detail: $"Target stack '{targetStackSlug}' is already owned by an existing MEM runtime stack. Choose another stack name; MEM will not replace it automatically."));
            }

            var requestedMatrixHost = StandardRecreateMatrixIdentity.NormalizeHost(
                request.MatrixHost);
            if (!StandardRecreateMatrixIdentity.Matches(
                    requestedMatrixHost,
                    sourceServerName))
            {
                throw new StandardRecreateIdentityMismatchException(
                    StandardRecreateMatrixIdentity.CreateMismatchMessage(
                        sourceServerName));
            }

            // A restored Synapse database retains user IDs, rooms, events, and
            // federation data for its original server_name. Never derive a new
            // Matrix host from the replacement stack name or platform domain.
            matrixHost = sourceServerName;

            var domain = await _domainResolver.ResolveMainPlatformDomainAsync(
                request.RequestedDomainId,
                ct);
            elementHost = NormalizeHost(request.ElementHost)
                ?? NormalizeHost(source.SourceElementHost)
                ?? $"element-{targetStackSlug}.{domain.BaseDomain}";

            // Only create recreate work files after the immutable Matrix identity
            // check has accepted the request. A rejected identity must not reserve
            // targets or leave a partial local recreate workspace.
            Directory.CreateDirectory(databaseDirectory);

            var matrixPublicBaseUrl = $"https://{matrixHost}";
            var elementPublicBaseUrl = $"https://{elementHost}";

            AddCheck(
                checks,
                errors,
                "production-recreate.matrix-host.present",
                "error",
                !string.IsNullOrWhiteSpace(matrixHost),
                $"Matrix public host resolved: {matrixHost}",
                matrixHost);
            AddCheck(
                checks,
                errors,
                "production-recreate.element-host.present",
                "error",
                !string.IsNullOrWhiteSpace(elementHost),
                $"Element public host resolved: {elementHost}",
                elementHost);

            var reservation = await _restoreAttemptCoordinator
                .ReserveTargetsAndStartStandardRecreateForCatalogAsync(
                    source.RestoreSessionId,
                    source.CatalogEntryDatabaseId,
                    targetStackSlug,
                    matrixHost,
                    elementHost,
                    recreateId,
                    request.Operator,
                    request.Note,
                    ct);

            restoreAttemptId = reservation.Attempt.Id;
            runtimeOperationId = reservation.RuntimeOperationId;

            // Target claims and the RuntimeOperation are now durable. From this
            // point forward the restore is owned by the server rather than by the
            // initiating browser connection. A client disconnect must not strand
            // a partially-created database/runtime with a permanently running
            // restore operation.
            operationLifetime = _operationLifetime.BeginAfterAcceptance(
                source.RestoreSessionId,
                recreateId,
                reservation.RuntimeOperationId,
                requestAborted);
            ct = operationLifetime.CancellationToken;

            AddCheck(
                checks,
                errors,
                "production-recreate.operation-lifetime.server-owned",
                "error",
                true,
                "Standard Recreate is running under a bounded server-owned lifetime independent of the initiating HTTP request.",
                $"operationId={reservation.RuntimeOperationId}; requestAbortCancelsMutation=false; operationTimeoutMinutes={StandardRecreateOperationLifetime.DefaultOperationTimeout.TotalMinutes}");

            AddCheck(
                checks,
                errors,
                "production-recreate.target-claims.reserved",
                "error",
                true,
                $"Restore target claims were reserved for stack '{targetStackSlug}', Matrix host '{matrixHost}', and Element host '{elementHost}'.",
                string.Join(
                    ", ",
                    reservation.Claims.Select(
                        claim => $"{claim.ResourceType}:{claim.ResourceValue}")));

            async Task ReportProgressAsync(
                string step,
                string summary)
            {
                if (!restoreAttemptId.HasValue || !runtimeOperationId.HasValue)
                {
                    return;
                }

                try
                {
                    await _restoreAttemptCoordinator.RecordStandardRecreateProgressAsync(
                        restoreAttemptId.Value,
                        runtimeOperationId.Value,
                        step,
                        summary,
                        ct);
                }
                catch (Exception progressException) when (
                    progressException is not OperationCanceledException &&
                    progressException is not StackOverflowException &&
                    progressException is not OutOfMemoryException)
                {
                    _logger.LogWarning(
                        progressException,
                        "Standard Recreate progress projection could not be updated. RestoreSessionId={RestoreSessionId} RecreateId={RecreateId} Step={Step}",
                        source.RestoreSessionId,
                        recreateId,
                        step);
                }
            }

            await ReportProgressAsync(
                "prepare-runtime",
                "Preparing the restored runtime directories and target workspace.");

            var runtimePlan = _runtimePlanner.Plan(
                stackId,
                matrixInstanceId,
                elementInstanceId,
                targetStackSlug);

            await _directoryPreparer.PrepareAsync(
                [runtimePlan.MatrixDataPath, runtimePlan.ElementDataPath],
                ct);

            // Keep resource identity in the failure result as soon as the
            // workspace exists. A later explicit cleanup can then remove only
            // this recreate's derived resources, even if a later stage fails.
            var elementConfigPath = Path.Combine(
                runtimePlan.ElementDataPath!,
                "config.json");
            runtimeSummary = new StandardRecreateRuntimeSummary(
                MatrixContainerName: runtimePlan.MatrixContainerName,
                MatrixContainerId: null,
                MatrixStarted: false,
                MatrixHealthPassed: false,
                MatrixHealthResponse: null,
                ElementContainerName: runtimePlan.ElementContainerName!,
                ElementContainerId: null,
                ElementStarted: false,
                ElementHealthPassed: false,
                ElementHealthResponse: null,
                RuntimeNetworkName: runtimePlan.RuntimeNetworkName,
                MatrixDataPath: runtimePlan.MatrixDataPath,
                ElementDataPath: runtimePlan.ElementDataPath!,
                HomeserverPath: Path.Combine(
                    runtimePlan.MatrixDataPath,
                    "homeserver.yaml"),
                ElementConfigPath: elementConfigPath,
                ManifestSaved: false,
                DatabaseOwnershipSaved: false,
                StackRegistered: false);

            routeSummary = new StandardRecreateRouteSummary(
                MatrixPublicHost: matrixHost,
                MatrixForwardHost: runtimePlan.MatrixInternalHost,
                MatrixForwardPort: 8008,
                MatrixRouteId: null,
                MatrixRouteReady: false,
                ElementPublicHost: elementHost,
                ElementForwardHost: runtimePlan.ElementInternalHost!,
                ElementForwardPort: 80,
                ElementRouteId: null,
                ElementRouteReady: false,
                PublicReadinessPassed: false);

            var databaseDumpPath = Path.Combine(databaseDirectory, "synapse.sql");

            await ReportProgressAsync(
                "restore-source-material",
                "Restoring the backup database dump, Matrix identity material, configuration, and media into the target runtime workspace.");

            var copiedSource = await source.CopyToRuntimeAsync(
                databaseDumpPath,
                runtimePlan.MatrixDataPath,
                elementConfigPath,
                ct);

            var extractedHomeserverPath = Path.Combine(
                runtimePlan.MatrixDataPath,
                "homeserver.yaml");
            var backedUpTurnSettings = CaptureBackedUpTurnSettings(
                extractedHomeserverPath);
            var coturnSynapseConfig = await _coturnRuntimeService.GetSynapseConfigAsync(ct);
            var turnPlan = StandardRecreateTurnPolicy.Resolve(
                source.BackupCoturn,
                backedUpTurnSettings.HasAny,
                coturnSynapseConfig is not null);

            AddCheck(
                checks,
                errors,
                "production-recreate.turn.restore-policy",
                "error",
                turnPlan.CanProceed,
                turnPlan.Detail,
                $"mode={turnPlan.Mode}; targetState={turnPlan.TargetState}; targetManagement={turnPlan.TargetManagement}");

            if (!turnPlan.CanProceed)
            {
                throw new InvalidDataException(turnPlan.Detail);
            }

                AddCheck(
                checks,
                errors,
                "production-recreate.backup-files.extracted",
                "error",
                true,
                $"Database, homeserver.yaml, signing key, and media store were {source.CopySummaryVerb}. Media files={copiedSource.MediaFiles}, bytes={copiedSource.MediaBytes}.",
                runtimePlan.MatrixDataPath);

            // A catalog payload may retain Element's original config.json, but the
            // restored client must always point at the immutable Matrix identity
            // selected for this recovered runtime. Keep the legacy create path's
            // generation step after either source copies its material.
            await _elementConfigGenerator.WriteHostConfigAsync(
                elementConfigPath,
                matrixPublicBaseUrl,
                matrixHost,
                ct);

            await ReportProgressAsync(
                "restore-database",
                "Provisioning the target Synapse database and importing the backup into mem-postgres.");

            var databaseProvisioning = await _databaseService.ProvisionMatrixDatabaseAsync(stackId, targetStackSlug, ct);
            var postgresSettings = databaseProvisioning.Provisioning.ToSynapseSettings(databaseProvisioning.Password);

            AddCheck(checks, errors, "production-recreate.production-postgres.provisioned", "error", true,
                $"Production Postgres database '{postgresSettings.DatabaseName}' and user '{postgresSettings.Username}' were created or verified in mem-postgres.",
                postgresSettings.DatabaseName);

            var import = await ImportDatabaseDumpAsync(
                databaseDirectory,
                databaseDumpPath,
                postgresSettings,
                ct);

            var importSucceeded = import.ExitCode == 0;
            AddCheck(checks, errors, "production-recreate.production-postgres.import", "error", importSucceeded,
                importSucceeded
                    ? "Backup SQL dump imported into production mem-postgres stack database."
                    : "Backup SQL dump failed to import into production mem-postgres stack database.",
                importSucceeded ? Trim(import.Stdout, 2000) : Trim(import.Stderr + Environment.NewLine + import.Stdout, 4000));

            if (!importSucceeded)
            {
                throw new InvalidOperationException("Production database import failed. See recreate result checks for details.");
            }

            var publicTableCount = await QueryIntAsync(postgresSettings.DatabaseName,
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';", ct);
            var knownTables = await QueryLinesAsync(postgresSettings.DatabaseName,
                """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public'
                AND table_name IN ('users', 'events', 'rooms', 'state_events', 'event_json', 'room_memberships')
                ORDER BY table_name;
                """, ct);

            var synapseKnownTableCount = knownTables.Count;
            var usersCount = await QueryOptionalSynapseUserCountAsync(postgresSettings.DatabaseName, ct);
            var eventsCount = await QueryOptionalTableCountAsync(postgresSettings.DatabaseName, "events", ct);
            var roomsCount = await QueryOptionalTableCountAsync(postgresSettings.DatabaseName, "rooms", ct);
            var stateEventsCount = await QueryOptionalTableCountAsync(postgresSettings.DatabaseName, "state_events", ct);

            databaseSummary = new StandardRecreateDatabaseSummary(
                Provisioned: true,
                Host: postgresSettings.Host,
                Port: postgresSettings.Port,
                DatabaseName: postgresSettings.DatabaseName,
                DatabaseUsername: postgresSettings.Username,
                ImportSucceeded: importSucceeded,
                PublicTableCount: publicTableCount,
                SynapseKnownTableCount: synapseKnownTableCount,
                UsersCount: usersCount,
                EventsCount: eventsCount,
                RoomsCount: roomsCount,
                StateEventsCount: stateEventsCount);

            AddCheck(checks, errors, "production-recreate.production-postgres.tables", "error", publicTableCount > 0 && synapseKnownTableCount > 0,
                $"Restored production database contains {publicTableCount} public tables and {synapseKnownTableCount} known Synapse tables.",
                string.Join(", ", knownTables));

            await ReportProgressAsync(
                "configure-matrix",
                "Applying the restored Matrix identity, Synapse configuration, and required TURN posture.");

            var matrixRegistrationSharedSecret = await _secretService.GetMatrixRegistrationSharedSecretAsync(stackId, ct);
            var generatedMatrixRegistrationSecret = false;
            if (string.IsNullOrWhiteSpace(matrixRegistrationSharedSecret))
            {
                matrixRegistrationSharedSecret = RuntimeStackSecretService.CreateSecretValue();
                generatedMatrixRegistrationSecret = true;
            }

            var homeserverPath = await _synapseConfigGenerator.GenerateAsync(
                instanceId: matrixInstanceId,
                serverName: matrixHost,
                dataPath: runtimePlan.MatrixDataPath,
                reportStats: false,
                cancellationToken: ct,
                registrationSharedSecret: matrixRegistrationSharedSecret,
                postgres: postgresSettings,
                coturn: turnPlan.GenerateWithPlatformCoturn
                    ? coturnSynapseConfig
                    : null,
                image: matrixImage);

            WriteProductionLogConfig(Path.Combine(runtimePlan.MatrixDataPath, "log.config"));
            PatchHomeserverTopLevel(homeserverPath, "server_name", matrixHost, quote: true);
            PatchHomeserverTopLevel(homeserverPath, "public_baseurl", matrixPublicBaseUrl + "/", quote: true);
            PatchHomeserverTopLevel(homeserverPath, "log_config", "/data/log.config", quote: true);

            var identityFidelity = StandardRecreateIdentityFidelity.NormalizeAndVerify(
                homeserverPath,
                runtimePlan.MatrixDataPath,
                copiedSource.SigningKeyBytes,
                copiedSource.SigningKeySha256);

            AddCheck(
                checks,
                errors,
                "production-recreate.matrix.signing-key-fidelity",
                "error",
                true,
                "The exact backed-up Matrix signing key was preserved and the restored Synapse configuration references its canonical runtime path.",
                $"signingKeyPath={identityFidelity.SigningKeyContainerPath}; signingKeyBytes={identityFidelity.SigningKeyBytes}; mediaStorePath={identityFidelity.MediaStoreContainerPath}");

            var turnSettingsApplied =
                turnPlan.ApplyBackedUpSettings &&
                ApplyBackedUpTurnSettings(
                    homeserverPath,
                    backedUpTurnSettings);

            // Read back the final file rather than reporting what we intended to write. This is the
            // persisted source of truth for the restored Synapse instance and prevents the stack UI
            // from presenting a stale or inferred TURN state after Production Recreate.
            var effectiveTurnSettings = CaptureEffectiveTurnSettings(homeserverPath);
            var effectiveTurnUsesCurrentPlatformCoturn = UsesCurrentPlatformCoturn(
                effectiveTurnSettings,
                coturnSynapseConfig);
            var turnFidelityPassed = turnPlan.Mode switch
            {
                StandardRecreateTurnModes.RestoreDisconnected =>
                    !effectiveTurnSettings.IsConfigured,
                StandardRecreateTurnModes.RebindPlatform =>
                    coturnSynapseConfig is not null &&
                    TurnSettingsMatchPlatform(
                        effectiveTurnSettings,
                        coturnSynapseConfig),
                StandardRecreateTurnModes.PreserveBackup =>
                    turnSettingsApplied &&
                    TurnSettingsMatch(
                        backedUpTurnSettings.Settings,
                        effectiveTurnSettings),
                _ => false
            };
            var effectiveTurnPublicHost = effectiveTurnSettings.TurnUris
                .Select(TryGetTurnHost)
                .FirstOrDefault(host => !string.IsNullOrWhiteSpace(host));
            var effectiveTurnRealm = ResolveTurnRealm(
                turnPlan,
                backedUpTurnSettings,
                source.BackupCoturn,
                coturnSynapseConfig);

            AddCheck(
                checks,
                errors,
                "production-recreate.turn.fidelity",
                "error",
                turnFidelityPassed,
                turnFidelityPassed
                    ? turnPlan.Mode switch
                    {
                        StandardRecreateTurnModes.RestoreDisconnected =>
                            "The recreated stack remained disconnected from TURN, matching the backup.",
                        StandardRecreateTurnModes.RebindPlatform =>
                            "The backed-up MEM-managed association was rebound to the current platform TURN service and verified.",
                        _ =>
                            "The backed-up external or legacy TURN settings were preserved and verified."
                    }
                    : "The recreated Synapse TURN state did not match the backup restore policy.",
                $"mode={turnPlan.Mode}; effectiveTurnUris={effectiveTurnSettings.TurnUris.Count}; credentialMechanism={effectiveTurnSettings.CredentialMechanism}");

            if (!turnFidelityPassed)
            {
                throw new InvalidOperationException(
                    "The recreated Synapse TURN state did not match the backup restore policy.");
            }

            turnSummary = new StandardRecreateTurnSummary(
                Mode: turnPlan.Mode,
                State: turnPlan.TargetState,
                Management: turnPlan.TargetManagement,
                PlatformTurnRequired: turnPlan.RequiresPlatformCoturn,
                PlatformTurnReady: turnPlan.RequiresPlatformCoturn
                    ? coturnSynapseConfig is not null
                    : null,
                PublicHost: effectiveTurnPublicHost,
                TurnUris: effectiveTurnSettings.TurnUris,
                Detail: turnPlan.Detail);

            var runtimeOwnership = StandardRecreateRuntimeOwnership.Normalize(
                runtimePlan.MatrixDataPath);

            AddCheck(
                checks,
                errors,
                "production-recreate.matrix.runtime-ownership",
                "error",
                true,
                runtimeOwnership.Detail,
                runtimeOwnership.ContainerUser is null
                    ? "platform=non-linux"
                    : $"containerUser={runtimeOwnership.ContainerUser}; ownershipChanged={runtimeOwnership.OwnershipChanged.ToString().ToLowerInvariant()}");

            await ReportProgressAsync(
                "start-matrix",
                "Starting the restored Matrix service and waiting for Synapse health and writable-media readiness.");

            var matrixStart = await _matrixContainerStarter.EnsureStartedAsync(
                runtimePlan.MatrixContainerName,
                matrixImage,
                runtimePlan.MatrixDataPath,
                runtimePlan.RuntimeNetworkName,
                runtimePlan.MatrixInternalBaseUrl,
                ct,
                allowPullIfMissing: false);

            runtimeSummary = runtimeSummary! with
            {
                MatrixContainerName = matrixStart.ContainerName,
                MatrixContainerId = matrixStart.ContainerId,
                MatrixStarted = matrixStart.Running
            };

            var matrixHealth = await WaitForMatrixHealthAsync(matrixStart.ContainerName, ct);
            AddCheck(checks, errors, "production-recreate.matrix.health", "error", matrixHealth.Passed,
                matrixHealth.Passed ? "Production recreated Matrix container health check passed." : "Production recreated Matrix container health check failed.",
                matrixHealth.Response);

            runtimeSummary = runtimeSummary! with
            {
                MatrixHealthPassed = matrixHealth.Passed,
                MatrixHealthResponse = matrixHealth.Response
            };

            if (!matrixHealth.Passed)
            {
                throw new InvalidOperationException(
                    "Production recreated Matrix health check failed before writable-media readiness could be verified.");
            }

            var mediaWriteReadiness = await VerifyMatrixMediaStoreWritableAsync(
                matrixStart.ContainerName,
                runtimeOwnership.ContainerUser,
                ct);

            AddCheck(
                checks,
                errors,
                "production-recreate.matrix.media-store-writable",
                "error",
                mediaWriteReadiness.Passed,
                mediaWriteReadiness.Passed
                    ? "The restored Synapse runtime identity can create and remove new media-store content."
                    : "The restored Synapse runtime identity cannot write new media-store content.",
                mediaWriteReadiness.Detail);

            if (!mediaWriteReadiness.Passed)
            {
                throw new InvalidOperationException(
                    "The restored Matrix media store is not writable by the Synapse runtime identity.");
            }

            await ReportProgressAsync(
                "start-element",
                "Starting the restored Element client and waiting for its internal health check.");

            var elementStart = await _elementContainerStarter.EnsureStartedAsync(
                runtimePlan.ElementContainerName!,
                elementImage,
                runtimePlan.ElementDataPath!,
                elementConfigPath,
                runtimePlan.RuntimeNetworkName,
                runtimePlan.ElementInternalBaseUrl!,
                ct,
                allowPullIfMissing: false);

            runtimeSummary = runtimeSummary! with
            {
                ElementContainerName = elementStart.ContainerName,
                ElementContainerId = elementStart.ContainerId,
                ElementStarted = elementStart.Running
            };

            var elementHealth = await WaitForElementHealthAsync(elementStart.ContainerName, ct);
            AddCheck(checks, errors, "production-recreate.element.health", "error", elementHealth.Passed,
                elementHealth.Passed ? "Production recreated Element container health check passed." : "Production recreated Element container health check failed.",
                elementHealth.Response);

            runtimeSummary = runtimeSummary! with
            {
                ElementHealthPassed = elementHealth.Passed,
                ElementHealthResponse = elementHealth.Response
            };

            await ReportProgressAsync(
                "publish-routes",
                "Publishing the restored Matrix and Element HTTPS routes through Nginx Proxy Manager.");

            var matrixPublicRoute = await _routePublisher.EnsureAsync(
                new RoutePublishRequest(
                    Domain: matrixHost,
                    ForwardHost: runtimePlan.MatrixInternalHost,
                    ForwardPort: 8008,
                    Kind: RouteKind.Matrix,
                    IsPublic: true,
                    RequireSsl: true,
                    CertificateId: domain.ActiveNpmCertificateId,
                    ForceSsl: true,
                    Http2: true),
                ct);

            routeSummary = routeSummary! with
            {
                MatrixForwardHost = matrixPublicRoute.ForwardHost,
                MatrixForwardPort = matrixPublicRoute.ForwardPort,
                MatrixRouteId = matrixPublicRoute.RouteId,
                MatrixRouteReady = matrixPublicRoute.Ready
            };

            var elementPublicRoute = await _routePublisher.EnsureAsync(
                new RoutePublishRequest(
                    Domain: elementHost,
                    ForwardHost: runtimePlan.ElementInternalHost!,
                    ForwardPort: 80,
                    Kind: RouteKind.ElementWeb,
                    IsPublic: true,
                    RequireSsl: true,
                    CertificateId: domain.ActiveNpmCertificateId,
                    ForceSsl: true,
                    Http2: true),
                ct);

            routeSummary = routeSummary! with
            {
                ElementForwardHost = elementPublicRoute.ForwardHost,
                ElementForwardPort = elementPublicRoute.ForwardPort,
                ElementRouteId = elementPublicRoute.RouteId,
                ElementRouteReady = elementPublicRoute.Ready
            };

            var cutoverCleanup = await DisconnectNpmFromConflictingCutoverIngressNetworksAsync(
                targetStackSlug,
                ct);

            foreach (var cleanupNote in cutoverCleanup)
            {
                warnings.Add(cleanupNote);
            }

            await ReportProgressAsync(
                "verify-readiness",
                "Checking internal services, public HTTPS routes, ingress, and restored server readiness.");

            var readiness = await _readinessVerifier.VerifyAsync(
                new RuntimeReadinessVerificationRequest(
                    MatrixInternalBaseUrl: runtimePlan.MatrixInternalBaseUrl,
                    ElementInternalBaseUrl: runtimePlan.ElementInternalBaseUrl!,
                    MatrixPublicBaseUrl: matrixPublicBaseUrl,
                    ElementPublicBaseUrl: elementPublicBaseUrl,
                    MatrixPublicHost: matrixHost,
                    ElementPublicHost: elementHost,
                    MatrixForwardHost: runtimePlan.MatrixInternalHost,
                    MatrixForwardPort: 8008,
                    ElementForwardHost: runtimePlan.ElementInternalHost!,
                    ElementForwardPort: 80,
                    ExpectedNpmCertificateId: domain.ActiveNpmCertificateId),
                ct);

            foreach (var check in readiness.Checks)
            {
                AddCheck(checks, errors, check.Code, check.Success ? "info" : "warning", check.Success,
                    check.Success ? $"{check.Name} passed." : $"{check.Name} failed.",
                    check.Detail ?? check.BodyPreview);
            }

            routeSummary = routeSummary! with
            {
                MatrixPublicHost = matrixHost,
                ElementPublicHost = elementHost,
                PublicReadinessPassed = readiness.AllPassed
            };

            runtimeSummary = new StandardRecreateRuntimeSummary(
                MatrixContainerName: matrixStart.ContainerName,
                MatrixContainerId: matrixStart.ContainerId,
                MatrixStarted: matrixStart.Running,
                MatrixHealthPassed: matrixHealth.Passed,
                MatrixHealthResponse: matrixHealth.Response,
                ElementContainerName: elementStart.ContainerName,
                ElementContainerId: elementStart.ContainerId,
                ElementStarted: elementStart.Running,
                ElementHealthPassed: elementHealth.Passed,
                ElementHealthResponse: elementHealth.Response,
                RuntimeNetworkName: runtimePlan.RuntimeNetworkName,
                MatrixDataPath: runtimePlan.MatrixDataPath,
                ElementDataPath: runtimePlan.ElementDataPath!,
                HomeserverPath: homeserverPath,
                ElementConfigPath: elementConfigPath,
                ManifestSaved: false,
                DatabaseOwnershipSaved: false,
                StackRegistered: false);

            var matrix = new HostAgentServiceRuntimeResult(
                InstanceId: matrixInstanceId,
                ServiceKey: ServiceKeys.Matrix,
                ContainerId: matrixStart.ContainerId,
                ContainerName: matrixStart.ContainerName,
                HostPort: 0,
                DataPath: runtimePlan.MatrixDataPath,
                ServerName: matrixHost,
                PublicHost: matrixHost,
                PublicBaseUrl: matrixPublicBaseUrl,
                InternalHost: runtimePlan.MatrixInternalHost,
                InternalBaseUrl: runtimePlan.MatrixInternalBaseUrl,
                PublicRouteId: matrixPublicRoute.RouteId,
                InternalRouteId: null,
                NpmCertificateId: matrixPublicRoute.CertificateId,
                RuntimeMetadata: new Dictionary<string, string?>
                {
                    ["restoreMode"] = "production-recreate",
                    ["sourceKind"] = source.SourceKind,
                    ["catalogEntryId"] = source.CatalogEntryId,
                    ["recreateId"] = recreateId,
                    ["sourceStackSlug"] = sourceStackSlug,
                    ["matrixImage"] = matrixStart.Image,
                    ["approvedMatrixImageReference"] = approvedSynapseRuntime.ApprovedReference,
                    ["homeserverPath"] = homeserverPath,
                    ["domainId"] = domain.DomainId.ToString(),
                    ["baseDomain"] = domain.BaseDomain,
                    ["activeCertificateId"] = domain.ActiveCertificateId?.ToString(),
                    ["activeNpmCertificateId"] = domain.ActiveNpmCertificateId?.ToString(),
                    ["runtimeNetworkName"] = runtimePlan.RuntimeNetworkName,
                    ["databaseEngine"] = databaseProvisioning.Provisioning.DatabaseEngine,
                    ["databaseHost"] = databaseProvisioning.Provisioning.DatabaseHost,
                    ["databasePort"] = databaseProvisioning.Provisioning.DatabasePort.ToString(),
                    ["databaseName"] = databaseProvisioning.Provisioning.DatabaseName,
                    ["databaseUsername"] = databaseProvisioning.Provisioning.DatabaseUsername,
                    ["databasePasswordSecretKind"] = databaseProvisioning.Provisioning.PasswordSecretKind,
                    ["databaseStatus"] = databaseProvisioning.Provisioning.Status,
                    ["publicRouteId"] = matrixPublicRoute.RouteId,
                    ["publicRouteReady"] = matrixPublicRoute.Ready ? "true" : "false",
                    ["npmCertificateId"] = matrixPublicRoute.CertificateId?.ToString(),
                    ["publicForwardHost"] = matrixPublicRoute.ForwardHost,
                    ["publicForwardPort"] = matrixPublicRoute.ForwardPort.ToString(),
                    ["readinessVerified"] = readiness.AllPassed ? "true" : "false",
                    ["matrixInternalReady"] = ReadinessSuccess(readiness, "host-agent.matrix.internal-http.reachable"),
                    ["matrixPublicReady"] = ReadinessSuccess(readiness, "host-agent.matrix.public-https.reachable"),
                    ["npmReady"] = ReadinessSuccess(readiness, "host-agent.npm.ready"),
                    ["matrixRouteConfigured"] = ReadinessSuccess(readiness, "host-agent.matrix.public-route.configured"),
                    ["turnConfigured"] = effectiveTurnSettings.IsConfigured ? "true" : "false",
                    ["turnPublicHost"] = effectiveTurnPublicHost,
                    ["turnRealm"] = effectiveTurnRealm,
                    ["turnUris"] = effectiveTurnSettings.TurnUris.Count > 0
                        ? string.Join(", ", effectiveTurnSettings.TurnUris)
                        : null,
                    ["turnRelayPortsPublished"] = effectiveTurnUsesCurrentPlatformCoturn
                        ? (coturnSynapseConfig!.RelayPortsPublished ? "true" : "false")
                        : null,
                    ["turnSharedSecretPresent"] = effectiveTurnSettings.SharedSecretPresent ? "true" : "false",
                    ["turnUserLifetime"] = effectiveTurnSettings.UserLifetime,
                    ["turnAllowGuests"] = effectiveTurnSettings.AllowGuests.HasValue
                        ? effectiveTurnSettings.AllowGuests.Value ? "true" : "false"
                        : null,
                    ["turnConfigurationSource"] = turnPlan.ConfigurationSource,
                    ["turnManagement"] = turnPlan.TargetManagement,
                    ["turnConfigurationSha256"] = RuntimeStackTurnConfigTransaction.Hash(
                        File.ReadAllBytes(homeserverPath)),
                    ["turnLastOperationMode"] = "restore",
                    ["turnLastInspectedAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["turnSettingsPreserved"] = turnPlan.ApplyBackedUpSettings
                        ? "true"
                        : null
                });

            var element = new HostAgentServiceRuntimeResult(
                InstanceId: elementInstanceId,
                ServiceKey: ServiceKeys.ElementWeb,
                ContainerId: elementStart.ContainerId,
                ContainerName: elementStart.ContainerName,
                HostPort: 0,
                DataPath: runtimePlan.ElementDataPath,
                ServerName: null,
                PublicHost: elementHost,
                PublicBaseUrl: elementPublicBaseUrl,
                InternalHost: runtimePlan.ElementInternalHost,
                InternalBaseUrl: runtimePlan.ElementInternalBaseUrl,
                PublicRouteId: elementPublicRoute.RouteId,
                InternalRouteId: null,
                NpmCertificateId: elementPublicRoute.CertificateId,
                RuntimeMetadata: new Dictionary<string, string?>
                {
                    ["restoreMode"] = "production-recreate",
                    ["sourceKind"] = source.SourceKind,
                    ["catalogEntryId"] = source.CatalogEntryId,
                    ["recreateId"] = recreateId,
                    ["sourceStackSlug"] = sourceStackSlug,
                    ["elementImage"] = elementStart.Image,
                    ["approvedElementImageReference"] = approvedElementRuntime.ApprovedReference,
                    ["elementConfigPath"] = elementConfigPath,
                    ["domainId"] = domain.DomainId.ToString(),
                    ["baseDomain"] = domain.BaseDomain,
                    ["activeCertificateId"] = domain.ActiveCertificateId?.ToString(),
                    ["activeNpmCertificateId"] = domain.ActiveNpmCertificateId?.ToString(),
                    ["runtimeNetworkName"] = runtimePlan.RuntimeNetworkName,
                    ["matrixPublicBaseUrl"] = matrixPublicBaseUrl,
                    ["matrixInternalBaseUrl"] = runtimePlan.MatrixInternalBaseUrl,
                    ["matrixDatabaseName"] = databaseProvisioning.Provisioning.DatabaseName,
                    ["matrixDatabaseUsername"] = databaseProvisioning.Provisioning.DatabaseUsername,
                    ["matrixDatabasePasswordSecretKind"] = databaseProvisioning.Provisioning.PasswordSecretKind,
                    ["publicRouteId"] = elementPublicRoute.RouteId,
                    ["publicRouteReady"] = elementPublicRoute.Ready ? "true" : "false",
                    ["npmCertificateId"] = elementPublicRoute.CertificateId?.ToString(),
                    ["publicForwardHost"] = elementPublicRoute.ForwardHost,
                    ["publicForwardPort"] = elementPublicRoute.ForwardPort.ToString(),
                    ["readinessVerified"] = readiness.AllPassed ? "true" : "false",
                    ["elementInternalReady"] = ReadinessSuccess(readiness, "host-agent.element.internal-http.reachable"),
                    ["elementPublicReady"] = ReadinessSuccess(readiness, "host-agent.element.public-https.reachable"),
                    ["npmReady"] = ReadinessSuccess(readiness, "host-agent.npm.ready"),
                    ["elementRouteConfigured"] = ReadinessSuccess(readiness, "host-agent.element.public-route.configured")
                });

            var stackStatus = readiness.AllPassed ? "public_routes_verified" : "public_routes_created";
            var createResult = new CreateChatStackRuntimeResult(
                StackId: stackId,
                Status: stackStatus,
                Message: $"Production Recreate restored a {source.SourceKind} backup into the normal MEM production runtime model, imported the database into mem-postgres, started normalized Matrix/Element containers, created/updated NPM routes, and saved the runtime stack manifest.",
                Matrix: matrix,
                Element: element,
                Warnings: warnings,
                Evidence: evidence);

            await ReportProgressAsync(
                "register-runtime",
                "Registering the restored stack, service ownership, and final runtime metadata with MEM.");

            await _manifestStore.SaveAsync(targetStackSlug, createResult, ct);
            await _databaseService.SaveOwnershipAsync(databaseProvisioning.Provisioning, databaseProvisioning.Password, ct);

            if (source.StackLogo is not null)
            {
                try
                {
                    var restoredManifest = await _manifestStore.FindAsync(
                        stackId.ToString(),
                        ct) ?? throw new InvalidOperationException(
                        $"Restored runtime stack '{targetStackSlug}' could not be reopened for logo recovery.");

                    await _logoService.RestoreFromBackupAsync(
                        restoredManifest,
                        source.StackLogo.Path,
                        new RuntimeStackLogoMetadata(
                            source.StackLogo.Sha256,
                            source.StackLogo.Bytes,
                            source.StackLogo.Width,
                            source.StackLogo.Height),
                        ct);

                    AddCheck(
                        checks,
                        errors,
                        "production-recreate.stack-logo.restored",
                        "info",
                        true,
                        "The custom stack logo was restored from the backup payload.",
                        source.StackLogo.Sha256);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
                {
                    warnings.Add(
                        $"The restored stack is usable, but its custom logo could not be recovered: {ex.Message}");
                    AddCheck(
                        checks,
                        errors,
                        "production-recreate.stack-logo.restored",
                        "warning",
                        false,
                        "The custom stack logo could not be restored from the backup payload.",
                        ex.Message);
                }
            }

            if (generatedMatrixRegistrationSecret && !string.IsNullOrWhiteSpace(matrixRegistrationSharedSecret))
            {
                await _secretService.UpsertMatrixRegistrationSharedSecretAsync(
                    stackId,
                    matrixRegistrationSharedSecret,
                    source: "production-recreate",
                    ct);
            }

            var userInventoryFinalization = await _userInventoryFinalizer.FinalizeAsync(
                RuntimeStackManifest.FromResult(
                    targetStackSlug,
                    createResult,
                    DateTimeOffset.UtcNow),
                databaseSummary?.UsersCount,
                ct);

            userInventorySummary = userInventoryFinalization.Summary;
            checks.Add(userInventoryFinalization.Check);

            if (!string.IsNullOrWhiteSpace(userInventoryFinalization.Warning))
            {
                warnings.Add(userInventoryFinalization.Warning);
            }

            runtimeSummary = runtimeSummary with
            {
                ManifestSaved = true,
                DatabaseOwnershipSaved = true,
                StackRegistered = true
            };

            AddCheck(checks, errors, "production-recreate.stack.registered", "error", true,
                $"Runtime stack '{targetStackSlug}' was saved into MEM runtime stack inventory.",
                stackId.ToString());

            var finishedAtUtc = DateTimeOffset.UtcNow;
            var status = readiness.AllPassed ? "production_recreate_verified" : "production_recreate_created_needs_verification";

            var result = new StandardRecreateResult(
                Source: "control-plane",
                Status: status,
                RecreateId: recreateId,
                RuntimeStackId: stackId,
                MatrixInstanceId: matrixInstanceId,
                ElementInstanceId: elementInstanceId,
                TargetStackSlug: targetStackSlug,
                MatrixHost: matrixHost,
                ElementHost: elementHost,
                StartedAtUtc: startedAtUtc,
                FinishedAtUtc: finishedAtUtc,
                Operator: request.Operator,
                Note: request.Note,
                Database: databaseSummary,
                Runtime: runtimeSummary,
                Routes: routeSummary,
                Mutations: BuildMutationSummary(),
                Checks: checks,
                Warnings: warnings,
                Errors: errors,
                Detail: readiness.AllPassed
                    ? "Production Recreate v1 completed and public readiness checks passed. The restored backup is now represented as a normal MEM runtime stack."
                    : "Production Recreate v1 completed but one or more public readiness checks need review. The restored backup is represented as a MEM runtime stack.",
                CatalogEntryId: source.CatalogEntryId)
            {
                UserInventory = userInventorySummary,
                Turn = turnSummary
            };

            if (restoreAttemptId.HasValue && runtimeOperationId.HasValue)
            {
                await _restoreAttemptCoordinator.CompleteStandardRecreateAsync(
                    restoreAttemptId.Value,
                    runtimeOperationId.Value,
                    stackId,
                    readiness.AllPassed,
                    result,
                    ct);
                restoreOperationCompleted = true;
            }

            // Completion is already durable at this point. Keep the auxiliary
            // recreate history write bounded and independent so an operation
            // timeout or shutdown racing with successful terminal persistence
            // cannot turn an already-completed restore back into a failure.
            using (var completionHistoryCancellation = new CancellationTokenSource(
                       TimeSpan.FromSeconds(20)))
            {
                try
                {
                    await _history.SaveAsync(result, completionHistoryCancellation.Token);
                }
                catch (Exception historyException) when (
                    historyException is not StackOverflowException and not OutOfMemoryException)
                {
                    _logger.LogError(
                        historyException,
                        "Standard Recreate completed but auxiliary history could not be saved. RecreateId={RecreateId}",
                        recreateId);
                }
            }

            return result;
        }
        catch (Exception ex) when (
            ex is not RestoreAttemptConflictException and not StandardRecreateIdentityMismatchException &&
            (ex is not OperationCanceledException || runtimeOperationId.HasValue))
        {
            _logger.LogError(
                ex,
                "Production recreate failed. RecreateId={RecreateId} SourceKind={SourceKind} CatalogEntryId={CatalogEntryId}",
                recreateId,
                source.SourceKind,
                source.CatalogEntryId);
            var failure = ClassifyStandardRecreateFailure(ex, operationLifetime);
            errors.Add(failure.OperatorSummary);

            var finishedAtUtc = DateTimeOffset.UtcNow;
            var fallbackSlug = string.IsNullOrWhiteSpace(request.TargetStackSlug) ? "unknown" : Slugify(request.TargetStackSlug);
            var fallbackMatrixHost = NormalizeHost(request.MatrixHost) ?? sourceServerName;
            var fallbackElementHost = NormalizeHost(request.ElementHost) ?? "unknown";

            var result = new StandardRecreateResult(
                Source: "control-plane",
                Status: "production_recreate_failed",
                RecreateId: recreateId,
                RuntimeStackId: stackId,
                MatrixInstanceId: matrixInstanceId,
                ElementInstanceId: elementInstanceId,
                TargetStackSlug: fallbackSlug,
                MatrixHost: fallbackMatrixHost,
                ElementHost: fallbackElementHost,
                StartedAtUtc: startedAtUtc,
                FinishedAtUtc: finishedAtUtc,
                Operator: request.Operator,
                Note: request.Note,
                Database: databaseSummary ?? new StandardRecreateDatabaseSummary(false, "mem-postgres", 5432, string.Empty, string.Empty, false, 0, 0, null, null, null, null),
                Runtime: runtimeSummary ?? new StandardRecreateRuntimeSummary(string.Empty, null, false, false, null, string.Empty, null, false, false, null, ResolveGatewayNetworkName(), string.Empty, string.Empty, string.Empty, string.Empty, false, false, false),
                Routes: routeSummary ?? new StandardRecreateRouteSummary(fallbackMatrixHost, string.Empty, 8008, null, false, fallbackElementHost, string.Empty, 80, null, false, false),
                Mutations: BuildMutationSummary(),
                Checks: checks,
                Warnings: warnings,
                Errors: errors,
                Detail: $"Production Recreate v1 failed: {failure.OperatorSummary}",
                CatalogEntryId: source.CatalogEntryId)
            {
                Turn = turnSummary
            };

            // Terminal journaling has its own bounded authority. If the
            // server-owned mutation token timed out or application shutdown
            // cancelled it, reusing that cancelled token would recreate the exact
            // stuck-running state this finalizer is meant to prevent.
            using var terminalEvidenceCancellation = new CancellationTokenSource(
                TimeSpan.FromSeconds(20));

            if (!restoreOperationCompleted && restoreAttemptId.HasValue && runtimeOperationId.HasValue)
            {
                try
                {
                    await _restoreAttemptCoordinator.RecordStandardRecreateFailureAsync(
                        restoreAttemptId.Value,
                        runtimeOperationId.Value,
                        "restore.standard-recreate.failed",
                        failure.Category,
                        failure.OperatorSummary,
                        new
                        {
                            RecreateId = recreateId,
                            CatalogEntryId = source.CatalogEntryId,
                            SourceKind = source.SourceKind,
                            TargetStackSlug = fallbackSlug,
                            MatrixHost = fallbackMatrixHost,
                            ElementHost = fallbackElementHost,
                            FailureCategory = failure.Category,
                            RequestAborted = operationLifetime?.RequestAborted == true,
                            OperationTimeoutRequested = operationLifetime?.OperationTimeoutRequested == true,
                            ApplicationStoppingRequested = operationLifetime?.ApplicationStoppingRequested == true,
                            RawException = ex.ToString()
                        },
                        terminalEvidenceCancellation.Token);
                }
                catch (Exception coordinationException) when (
                    coordinationException is not StackOverflowException and not OutOfMemoryException)
                {
                    _logger.LogError(
                        coordinationException,
                        "Standard Recreate failed and the restore attempt could not be updated with terminal evidence. RecreateId={RecreateId} RestoreAttemptId={RestoreAttemptId} RuntimeOperationId={RuntimeOperationId}",
                        recreateId,
                        restoreAttemptId,
                        runtimeOperationId);
                }
            }

            try
            {
                await _history.SaveAsync(result, terminalEvidenceCancellation.Token);
            }
            catch (Exception historyException) when (
                historyException is not StackOverflowException and not OutOfMemoryException)
            {
                _logger.LogError(
                    historyException,
                    "Standard Recreate failure history could not be saved after terminal operation journaling. RecreateId={RecreateId}",
                    recreateId);
            }

            return result;
        }
        finally
        {
            operationLifetime?.Dispose();
        }
    }

    private StandardRecreateMutationSummary BuildMutationSummary() =>
        new(
            RuntimeStackCreated: true,
            ProductionPostgresMutated: true,
            ProductionContainersTouched: true,
            NpmRoutesChanged: true,
            DnsChanged: false,
            CertificatesChanged: false,
            OldStacksDeleted: false,
            Notes:
            [
                "Production Recreate v1 creates a real MEM runtime stack from a managed Backup Catalog payload.",
                "The backup SQL dump is imported into the normal production mem-postgres service.",
                "Normalized Matrix and Element containers are started on the normal MEM runtime network.",
                "NPM routes are created or updated to point at the normalized production containers.",
                "DNS records are not created, updated, or deleted.",
                "Certificates are not requested, imported, renewed, or deleted.",
                "Existing old stacks are not deleted by this v1 executor.",
                "Private staging/cutover candidate resources are not cleaned up by this v1 executor."
            ]);

    private async Task<IReadOnlyList<string>> DisconnectNpmFromConflictingCutoverIngressNetworksAsync(
    string targetStackSlug,
    CancellationToken ct)
    {
        var notes = new List<string>();
        const string npmContainerName = "mem-npm";

        var networks = await _docker.Networks.ListNetworksAsync(
            new NetworksListParameters(),
            ct);

        foreach (var summary in networks.Where(x =>
                     x.Name.StartsWith("mem-cutover-ingress-", StringComparison.OrdinalIgnoreCase)))
        {
            NetworkResponse inspected;

            try
            {
                inspected = await _docker.Networks.InspectNetworkAsync(summary.ID, ct);
            }
            catch
            {
                continue;
            }

            var containers = inspected.Containers ?? new Dictionary<string, EndpointResource>();

            var npmEndpoint = containers.FirstOrDefault(kv =>
                string.Equals(kv.Value.Name, npmContainerName, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(npmEndpoint.Key))
            {
                continue;
            }

            await _docker.Networks.DisconnectNetworkAsync(
                inspected.ID,
                new NetworkDisconnectParameters
                {
                    Container = npmEndpoint.Key,
                    Force = true
                },
                ct);

            notes.Add(
                $"Disconnected '{npmContainerName}' from temporary cutover ingress network '{inspected.Name}' after production recreate for stack '{targetStackSlug}'.");
        }

        return notes;
    }

    private async Task<ExecResult> ImportDatabaseDumpAsync(
        string databaseDirectory,
        string databaseDumpPath,
        RuntimeStackPostgresSettings postgres,
        CancellationToken ct)
    {
        var containerName = $"mem-prod-recreate-import-{Guid.NewGuid():N}"[..48];
        var approvedPostgresRuntime = await _approvedPostgresRuntimeProvider
            .ResolveForOperationAsync(ct);
        var image = approvedPostgresRuntime.ResolvedImageId;

        var create = await _docker.Containers.CreateContainerAsync(
            new CreateContainerParameters
            {
                Name = containerName,
                Image = image,
                Env = [$"PGPASSWORD={postgres.Password}"],
                Cmd =
                [
                    "psql",
                    "-v", "ON_ERROR_STOP=1",
                    "-h", postgres.Host,
                    "-p", postgres.Port.ToString(),
                    "-U", postgres.Username,
                    "-d", postgres.DatabaseName,
                    "-f", "/restore/synapse.sql"
                ],
                HostConfig = new HostConfig
                {
                    Binds = [$"{databaseDirectory}:/restore:ro"],
                    AutoRemove = false
                },
                NetworkingConfig = new NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, EndpointSettings>
                    {
                        [ResolveGatewayNetworkName()] = new EndpointSettings()
                    }
                },
                Labels = new Dictionary<string, string>
                {
                    ["mem.component"] = "production-recreate",
                    ["mem.operation"] = "database-import"
                }
            },
            ct);

        try
        {
            await _docker.Containers.StartContainerAsync(create.ID, new ContainerStartParameters(), ct);
            var wait = await _docker.Containers.WaitContainerAsync(create.ID, ct);
            var logs = await _dockerHost.GetLogsAsync(create.ID, 5000, ct);

            return new ExecResult((long)wait.StatusCode, logs, string.Empty);
        }
        finally
        {
            try
            {
                await _docker.Containers.RemoveContainerAsync(
                    create.ID,
                    new ContainerRemoveParameters { Force = true, RemoveVolumes = false },
                    CancellationToken.None);
            }
            catch
            {
                // best effort cleanup only
            }
        }
    }

    private async Task<(bool Passed, string? Response)> WaitForMatrixHealthAsync(string containerName, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        string? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await ExecInContainerAsync(
                containerName,
                ["python", "-c", "import urllib.request,sys; print(urllib.request.urlopen('http://127.0.0.1:8008/health', timeout=3).read().decode('utf-8','replace'))"],
                ct,
                throwOnNonZeroExit: false);

            last = Trim(result.Stdout + result.Stderr, 1000);
            if (result.ExitCode == 0 && last.Contains("OK", StringComparison.OrdinalIgnoreCase))
            {
                return (true, last);
            }

            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        return (false, last);
    }

    private async Task<(bool Passed, string? Response)> WaitForElementHealthAsync(string containerName, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        string? last = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await ExecInContainerAsync(
                containerName,
                ["sh", "-lc", "test -s /app/config.json && test -s /app/index.html"],
                ct,
                throwOnNonZeroExit: false);

            last = Trim(result.Stdout + result.Stderr, 1000);
            if (result.ExitCode == 0)
            {
                return (true, "Element config.json and index.html exist inside the production container.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        return (false, last);
    }

    private async Task<int> QueryIntAsync(string databaseName, string sql, CancellationToken ct)
    {
        var lines = await QueryLinesAsync(databaseName, sql, ct);
        return lines.Count == 0 ? 0 : int.Parse(lines[0]);
    }

    private async Task<long?> QueryOptionalSynapseUserCountAsync(
        string databaseName,
        CancellationToken ct)
    {
        var exists = await QueryIntAsync(
            databaseName,
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = 'users';",
            ct);

        if (exists == 0)
        {
            return null;
        }

        var internalPrefix = $"@{MatrixManagedRecoveryAuthorityService.UsernamePrefix}";
        var lines = await QueryLinesAsync(
            databaseName,
            $"SELECT COUNT(*) FROM {QuoteIdentifier("users")} WHERE position({QuoteLiteral(internalPrefix)} in name) <> 1;",
            ct);

        return lines.Count == 0 ? null : long.Parse(lines[0]);
    }

    private async Task<long?> QueryOptionalTableCountAsync(string databaseName, string tableName, CancellationToken ct)
    {
        var exists = await QueryIntAsync(databaseName,
            $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = {QuoteLiteral(tableName)};",
            ct);

        if (exists == 0)
        {
            return null;
        }

        var lines = await QueryLinesAsync(databaseName, $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)};", ct);
        return lines.Count == 0 ? null : long.Parse(lines[0]);
    }

    private async Task<IReadOnlyList<string>> QueryLinesAsync(string databaseName, string sql, CancellationToken ct)
    {
        var result = await ExecInContainerAsync(
            PostgresContainerName,
            ["psql", "-v", "ON_ERROR_STOP=1", "-U", "postgres", "-d", databaseName, "-t", "-A", "-c", sql],
            ct);

        return result.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private async Task<ExecResult> ExecInContainerAsync(
        string containerName,
        IReadOnlyList<string> command,
        CancellationToken ct,
        bool throwOnNonZeroExit = true)
    {
        var exec = await _docker.Exec.ExecCreateContainerAsync(
            containerName,
            new ContainerExecCreateParameters
            {
                AttachStdout = true,
                AttachStderr = true,
                Cmd = command.ToList()
            },
            ct);

        using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(exec.ID, tty: false, cancellationToken: ct);
        var output = await stream.ReadOutputToEndAsync(ct);
        var inspect = await _docker.Exec.InspectContainerExecAsync(exec.ID, ct);

        var result = new ExecResult(inspect.ExitCode, output.stdout ?? string.Empty, output.stderr ?? string.Empty);
        if (throwOnNonZeroExit && result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command failed in container '{containerName}' with exit code {result.ExitCode}: {Trim(result.Stderr + Environment.NewLine + result.Stdout, 4000)}");
        }

        return result;
    }


    private string ResolveDataRoot() =>


        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private string ResolveGatewayNetworkName() =>
        string.IsNullOrWhiteSpace(_networkOptions.GatewayNetworkName)
            ? "mem-gateway"
            : _networkOptions.GatewayNetworkName.Trim();

    private async Task PullImageIfMissingAsync(string image, CancellationToken ct)
    {
        try
        {
            await _docker.Images.InspectImageAsync(image, ct);
            return;
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // pull below
        }

        var (repo, tag) = SplitImage(image);
        await _docker.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = repo, Tag = tag },
            authConfig: null,
            progress: new Progress<JSONMessage>(),
            cancellationToken: ct);
    }

    private static (string Repository, string Tag) SplitImage(string image)
    {
        var trimmed = image.Trim();
        var slashIndex = trimmed.LastIndexOf('/');
        var colonIndex = trimmed.LastIndexOf(':');
        return colonIndex > slashIndex
            ? (trimmed[..colonIndex], trimmed[(colonIndex + 1)..])
            : (trimmed, "latest");
    }

    private static void WriteProductionLogConfig(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            """
            version: 1
            formatters:
              precise:
                format: '%(asctime)s - %(name)s - %(lineno)d - %(levelname)s - %(request)s - %(message)s'
            handlers:
              console:
                class: logging.StreamHandler
                formatter: precise
            loggers:
                synapse:
                    level: INFO
            root:
                level: INFO
                handlers: [console]
            disable_existing_loggers: false
            """);
    }

    private static readonly string[] BackedUpTurnKeys =
    [
        "turn_uris",
        "turn_shared_secret",
        "turn_shared_secret_path",
        "turn_user_lifetime",
        "turn_allow_guests"
    ];

    private sealed record EffectiveTurnSettings(
        IReadOnlyList<string> TurnUris,
        string CredentialMechanism,
        bool SharedSecretPresent,
        string? SharedSecretValue,
        string? SharedSecretPath,
        string? UserLifetime,
        bool? AllowGuests)
    {
        public bool IsConfigured =>
            TurnUris.Count > 0 ||
            SharedSecretPresent;
    }

    private sealed record BackedUpTurnSettings(
        IReadOnlyDictionary<string, IReadOnlyList<string>> Blocks,
        EffectiveTurnSettings Settings,
        string? Realm)
    {
        public bool HasAny => Blocks.Count > 0;
        public int TurnUriCount => Settings.TurnUris.Count;
        public bool SharedSecretPresent => Settings.SharedSecretPresent;
    }

    private static BackedUpTurnSettings CaptureBackedUpTurnSettings(string homeserverPath)
    {
        if (!File.Exists(homeserverPath))
        {
            return new BackedUpTurnSettings(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
                new EffectiveTurnSettings([], "none", SharedSecretPresent: false, SharedSecretValue: null, SharedSecretPath: null, UserLifetime: null, AllowGuests: null),
                Realm: null);
        }

        var sourceLines = File.ReadAllLines(homeserverPath);
        var blocks = CaptureTurnBlocks(sourceLines);

        return new BackedUpTurnSettings(
            blocks,
            ReadTurnSettings(blocks),
            ReadTurnRealmComment(sourceLines));
    }

    private static EffectiveTurnSettings CaptureEffectiveTurnSettings(string homeserverPath)
    {
        if (!File.Exists(homeserverPath))
        {
            return new EffectiveTurnSettings([], "none", SharedSecretPresent: false, SharedSecretValue: null, SharedSecretPath: null, UserLifetime: null, AllowGuests: null);
        }

        return ReadTurnSettings(CaptureTurnBlocks(File.ReadAllLines(homeserverPath)));
    }

    private static Dictionary<string, IReadOnlyList<string>> CaptureTurnBlocks(IReadOnlyList<string> sourceLines)
    {
        var blocks = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var key in BackedUpTurnKeys)
        {
            var block = ExtractTopLevelYamlBlock(sourceLines, key);
            if (block.Count > 0)
            {
                blocks[key] = block;
            }
        }

        return blocks;
    }

    private static EffectiveTurnSettings ReadTurnSettings(
        IReadOnlyDictionary<string, IReadOnlyList<string>> blocks)
    {
        var turnUris = blocks.TryGetValue("turn_uris", out var turnUrisBlock)
            ? ReadTurnUris(turnUrisBlock)
            : [];
        var sharedSecretValue = blocks.TryGetValue("turn_shared_secret", out var secretBlock) &&
                                secretBlock.Count > 0
            ? ValueAfterYamlColon(secretBlock[0])
            : null;
        var sharedSecretPath = blocks.TryGetValue("turn_shared_secret_path", out var secretPathBlock) &&
                               secretPathBlock.Count > 0
            ? ValueAfterYamlColon(secretPathBlock[0])
            : null;
        var sharedSecretPresent =
            !string.IsNullOrWhiteSpace(sharedSecretValue) ||
            !string.IsNullOrWhiteSpace(sharedSecretPath);
        var credentialMechanism = !string.IsNullOrWhiteSpace(sharedSecretValue)
            ? "inline-shared-secret"
            : !string.IsNullOrWhiteSpace(sharedSecretPath)
                ? "shared-secret-path"
                : "none";
        var userLifetime = blocks.TryGetValue("turn_user_lifetime", out var lifetimeBlock) &&
                           lifetimeBlock.Count > 0
            ? ValueAfterYamlColon(lifetimeBlock[0])
            : null;
        bool? allowGuests = blocks.TryGetValue("turn_allow_guests", out var allowGuestsBlock) &&
                            allowGuestsBlock.Count > 0 &&
                            bool.TryParse(
                                ValueAfterYamlColon(allowGuestsBlock[0]),
                                out var parsedAllowGuests)
            ? parsedAllowGuests
            : null;

        return new EffectiveTurnSettings(
            turnUris,
            credentialMechanism,
            sharedSecretPresent,
            sharedSecretValue,
            sharedSecretPath,
            userLifetime,
            allowGuests);
    }

    private static IReadOnlyList<string> ReadTurnUris(IReadOnlyList<string> turnUrisBlock)
    {
        if (turnUrisBlock.Count == 0)
        {
            return [];
        }

        var inline = ValueAfterYamlColon(turnUrisBlock[0]);
        if (!string.IsNullOrWhiteSpace(inline))
        {
            return inline.Trim('[', ']')
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => value.Trim().Trim('"', '\''))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
        }

        return turnUrisBlock
            .Skip(1)
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("-", StringComparison.Ordinal))
            .Select(line => line[1..].Trim().Trim('"', '\''))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string? ReadTurnRealmComment(IReadOnlyList<string> lines)
    {
        const string prefix = "# TURN realm:";
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var realm = trimmed[prefix.Length..].Trim().Trim('"', '\'');
                return string.IsNullOrWhiteSpace(realm) ? null : realm;
            }
        }

        return null;
    }

    private static bool TurnSettingsMatch(
        EffectiveTurnSettings expected,
        EffectiveTurnSettings actual) =>
        expected.TurnUris.SequenceEqual(actual.TurnUris, StringComparer.Ordinal) &&
        string.Equals(
            expected.CredentialMechanism,
            actual.CredentialMechanism,
            StringComparison.Ordinal) &&
        string.Equals(
            expected.SharedSecretValue,
            actual.SharedSecretValue,
            StringComparison.Ordinal) &&
        string.Equals(
            expected.SharedSecretPath,
            actual.SharedSecretPath,
            StringComparison.Ordinal) &&
        string.Equals(expected.UserLifetime, actual.UserLifetime, StringComparison.Ordinal) &&
        expected.AllowGuests == actual.AllowGuests;

    private static bool TurnSettingsMatchPlatform(
        EffectiveTurnSettings actual,
        CoturnSynapseConfig platform) =>
        actual.TurnUris.SequenceEqual(platform.TurnUris, StringComparer.Ordinal) &&
        string.Equals(
            actual.CredentialMechanism,
            "inline-shared-secret",
            StringComparison.Ordinal) &&
        string.Equals(
            actual.SharedSecretValue,
            platform.SharedSecret,
            StringComparison.Ordinal) &&
        string.Equals(
            actual.UserLifetime,
            platform.UserLifetime,
            StringComparison.Ordinal) &&
        actual.AllowGuests == platform.AllowGuests;

    private static bool UsesCurrentPlatformCoturn(
        EffectiveTurnSettings effectiveTurnSettings,
        CoturnSynapseConfig? coturnSynapseConfig) =>
        coturnSynapseConfig is not null &&
        effectiveTurnSettings.TurnUris.SequenceEqual(coturnSynapseConfig.TurnUris, StringComparer.Ordinal);

    private static string? ResolveTurnRealm(
        StandardRecreateTurnPlan plan,
        BackedUpTurnSettings backedUpTurnSettings,
        MemStackExportCoturnManifest? backupCoturn,
        CoturnSynapseConfig? coturnSynapseConfig)
    {
        if (string.Equals(
                plan.Mode,
                StandardRecreateTurnModes.RebindPlatform,
                StringComparison.Ordinal))
        {
            return coturnSynapseConfig?.Realm;
        }

        if (string.Equals(
                plan.Mode,
                StandardRecreateTurnModes.PreserveBackup,
                StringComparison.Ordinal))
        {
            return backedUpTurnSettings.Realm ?? backupCoturn?.Realm;
        }

        return null;
    }

    private static string? TryGetTurnHost(string? turnUri)
    {
        if (string.IsNullOrWhiteSpace(turnUri))
        {
            return null;
        }

        var value = turnUri.Trim();
        if (value.StartsWith("turn:", StringComparison.OrdinalIgnoreCase)) value = value[5..];
        else if (value.StartsWith("turns:", StringComparison.OrdinalIgnoreCase)) value = value[6..];

        var withoutQuery = value.Split('?', 2)[0].Trim();
        if (withoutQuery.StartsWith("[", StringComparison.Ordinal))
        {
            var closing = withoutQuery.IndexOf(']');
            return closing > 1 ? withoutQuery[1..closing] : null;
        }

        var colon = withoutQuery.LastIndexOf(':');
        return colon > 0 ? withoutQuery[..colon] : withoutQuery;
    }

    private static bool ApplyBackedUpTurnSettings(
        string homeserverPath,
        BackedUpTurnSettings backedUpTurnSettings)
    {
        if (!backedUpTurnSettings.HasAny)
        {
            return false;
        }

        var generatedLines = File.ReadAllLines(homeserverPath).ToList();
        foreach (var key in BackedUpTurnKeys)
        {
            RemoveTopLevelYamlBlock(generatedLines, key);
        }

        if (generatedLines.Count > 0 && !string.IsNullOrWhiteSpace(generatedLines[^1]))
        {
            generatedLines.Add(string.Empty);
        }

        foreach (var key in BackedUpTurnKeys)
        {
            if (!backedUpTurnSettings.Blocks.TryGetValue(key, out var block))
            {
                continue;
            }

            generatedLines.AddRange(block);
        }

        File.WriteAllLines(homeserverPath, generatedLines);
        return true;
    }

    private static IReadOnlyList<string> ExtractTopLevelYamlBlock(
        IReadOnlyList<string> lines,
        string key)
    {
        var prefix = key + ":";
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var block = new List<string> { lines[index] };
            for (var cursor = index + 1; cursor < lines.Count; cursor++)
            {
                var line = lines[cursor];
                if (IsTopLevelYamlKey(line))
                {
                    break;
                }

                block.Add(line);
            }

            return block;
        }

        return Array.Empty<string>();
    }

    private static void RemoveTopLevelYamlBlock(List<string> lines, string key)
    {
        var prefix = key + ":";
        for (var index = 0; index < lines.Count; index++)
        {
            if (!lines[index].StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var end = index + 1;
            while (end < lines.Count && !IsTopLevelYamlKey(lines[end]))
            {
                end++;
            }

            lines.RemoveRange(index, end - index);
            return;
        }
    }

    private static bool IsTopLevelYamlKey(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || char.IsWhiteSpace(line[0]) || line.StartsWith("#", StringComparison.Ordinal))
        {
            return false;
        }

        return line.IndexOf(':') >= 0;
    }

    private static string? ValueAfterYamlColon(string line)
    {
        var separator = line.IndexOf(':');
        if (separator < 0 || separator == line.Length - 1)
        {
            return null;
        }

        return line[(separator + 1)..].Trim().Trim('"', '\'');
    }

    private static void PatchHomeserverTopLevel(string homeserverPath, string key, string value, bool quote)
    {
        var lines = File.ReadAllLines(homeserverPath).ToList();
        var rendered = quote
            ? $"{key}: \"{EscapeYaml(value)}\""
            : $"{key}: {value}";

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith(key + ":", StringComparison.Ordinal))
            {
                var indent = lines[i][..(lines[i].Length - trimmed.Length)];
                lines[i] = indent + rendered;
                File.WriteAllLines(homeserverPath, lines);
                return;
            }
        }

        lines.Add(rendered);
        File.WriteAllLines(homeserverPath, lines);
    }

    private static string EscapeYaml(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    internal static ContainerExecCreateParameters CreateMediaStoreWriteProbeExecParameters(
        string? containerUser,
        string probeName)
    {
        if (string.IsNullOrWhiteSpace(probeName) ||
            probeName.Any(character => !char.IsLetterOrDigit(character) && character != '-' && character != '_'))
        {
            throw new ArgumentException(
                "Media-store write probe name must contain only letters, numbers, '-' or '_'.",
                nameof(probeName));
        }

        var probePath = $"/data/media_store/.{probeName}";
        var command =
            $"set -eu; mkdir -p {probePath}; printf ok > {probePath}/probe; rm -rf {probePath}";

        return new ContainerExecCreateParameters
        {
            AttachStdout = true,
            AttachStderr = true,
            User = string.IsNullOrWhiteSpace(containerUser) ? null : containerUser,
            Cmd = ["sh", "-lc", command]
        };
    }

    private async Task<(bool Passed, string Detail)> VerifyMatrixMediaStoreWritableAsync(
        string containerName,
        string? containerUser,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerUser))
        {
            return (
                false,
                "MEM could not determine the restored Synapse runtime identity for the media-store write probe.");
        }

        var probeName = $"mem-restore-write-probe-{Guid.NewGuid():N}";

        try
        {
            var exec = await _docker.Exec.ExecCreateContainerAsync(
                containerName,
                CreateMediaStoreWriteProbeExecParameters(containerUser, probeName),
                ct);

            using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
                exec.ID,
                tty: false,
                cancellationToken: ct);
            var output = await stream.ReadOutputToEndAsync(ct);
            var inspect = await _docker.Exec.InspectContainerExecAsync(exec.ID, ct);

            var stderr = output.stderr ?? string.Empty;
            var stdout = output.stdout ?? string.Empty;
            var passed = inspect.ExitCode == 0;

            return (
                passed,
                passed
                    ? $"containerUser={containerUser}; probe=created-and-removed"
                    : $"containerUser={containerUser}; exitCode={inspect.ExitCode}; output={Trim(stderr + Environment.NewLine + stdout, 2000)}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (
                false,
                $"containerUser={containerUser}; probeError={Trim(ex.Message, 1500)}");
        }
    }

    private static void AddCheck(
        List<StandardRecreateCheck> checks,
        List<string> errors,
        string code,
        string severity,
        bool passed,
        string message,
        string? detail)
    {
        checks.Add(new StandardRecreateCheck(code, severity, passed, message, detail));
        if (!passed && string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(message);
        }
    }

    private static string? NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                return uri.Host.ToLowerInvariant();
            }
        }

        return trimmed.Trim('/').ToLowerInvariant();
    }

    private static string Slugify(string? value)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "restored-stack" : value.Trim();
        var chars = source
            .ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal)) normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        normalized = normalized.Trim('-');
        return normalized.Length == 0 ? "restored-stack" : normalized;
    }


    private static bool IsSafePathSegment(string value) =>
        value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static string CreateRunId()
    {
        var bytes = RandomNumberGenerator.GetBytes(4);
        return $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}Z-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string QuoteIdentifier(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static string QuoteLiteral(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string ReadinessSuccess(RuntimeReadinessVerificationResult readiness, string code) =>
        readiness.Checks.Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase) && x.Success)
            ? "true"
            : "false";

    private static StandardRecreateFailureClassification ClassifyStandardRecreateFailure(
        Exception exception,
        StandardRecreateOperationScope? operationLifetime)
    {
        if (exception is OperationCanceledException)
        {
            if (operationLifetime?.OperationTimeoutRequested == true)
            {
                return new StandardRecreateFailureClassification(
                    Category: "standard-recreate-operation-timeout",
                    OperatorSummary: "Standard Recreate exceeded its bounded server-owned operation timeout. Review the restore workspace and protected operation evidence before retrying.");
            }

            if (operationLifetime?.ApplicationStoppingRequested == true)
            {
                return new StandardRecreateFailureClassification(
                    Category: "standard-recreate-application-stopping",
                    OperatorSummary: "The MEM Control Plane stopped while Standard Recreate was in progress. Review the restore workspace and protected operation evidence before retrying.");
            }

            return new StandardRecreateFailureClassification(
                Category: "standard-recreate-operation-cancelled",
                OperatorSummary: "The accepted Standard Recreate operation was cancelled before reaching a terminal state. Review protected operation evidence before retrying.");
        }

        var dockerException = FindDockerApiException(exception);
        var diagnosticText = exception.ToString();

        if (dockerException is not null)
        {
            if (diagnosticText.Contains("failed to resolve reference", StringComparison.OrdinalIgnoreCase) ||
                diagnosticText.Contains("manifest unknown", StringComparison.OrdinalIgnoreCase) ||
                diagnosticText.Contains("no matching manifest", StringComparison.OrdinalIgnoreCase))
            {
                return new StandardRecreateFailureClassification(
                    Category: "container-image-unavailable",
                    OperatorSummary: "The restored Matrix server image could not be retrieved. Check the configured image reference and registry access.");
            }

            if (dockerException.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new StandardRecreateFailureClassification(
                    Category: "container-registry-access-denied",
                    OperatorSummary: "Docker was denied access to the restored service image. Check registry access before retrying.");
            }

            return new StandardRecreateFailureClassification(
                Category: "docker-operation-failed",
                OperatorSummary: "Docker could not create or start the restored chat server. Review protected operation evidence for diagnostic details.");
        }

        if (exception is InvalidDataException)
        {
            return new StandardRecreateFailureClassification(
                Category: "backup-content-invalid",
                OperatorSummary: "The backup content required for recreation was unavailable or invalid. Review protected operation evidence for diagnostic details.");
        }

        if (diagnosticText.Contains("Production database import failed", StringComparison.OrdinalIgnoreCase) ||
            diagnosticText.Contains("pg_restore", StringComparison.OrdinalIgnoreCase))
        {
            return new StandardRecreateFailureClassification(
                Category: "database-import-failed",
                OperatorSummary: "The backup database could not be imported into the restored production database. Review protected operation evidence for diagnostic details.");
        }

        return new StandardRecreateFailureClassification(
            Category: "standard-recreate-failed",
            OperatorSummary: "Standard recreate did not complete. Review protected operation evidence for diagnostic details.");
    }

    private static DockerApiException? FindDockerApiException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is DockerApiException dockerException)
            {
                return dockerException;
            }
        }

        return null;
    }

    private static string Trim(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private sealed class StandardRecreateIdentityMismatchException : InvalidOperationException
    {
        public StandardRecreateIdentityMismatchException(string message)
            : base(message)
        {
        }
    }

    private sealed record StandardRecreateFailureClassification(
        string Category,
        string OperatorSummary);

    private sealed record ExecResult(long ExitCode, string Stdout, string Stderr);
}
