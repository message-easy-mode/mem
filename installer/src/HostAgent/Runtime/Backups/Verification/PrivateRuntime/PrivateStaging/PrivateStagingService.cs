using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Docker;
using HostAgent.Docker.Models;
using HostAgent.Element.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using HostAgent.Runtime.Backups.Catalog;
using Modules.Shared.RuntimeImages;

namespace HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

public sealed class PrivateStagingService
{
    private const string DatabaseUser = "mem_restore_staging";
    private const string DatabaseNamePrefix = "synapse_restore_staging";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;
    private readonly DockerClient _docker;
    private readonly IDockerHost _dockerHost;
    private readonly BackupCatalogPayloadResolver _catalogPayloadResolver;
    private readonly IApprovedPostgresRuntimeProvider _approvedPostgresRuntimeProvider;
    private readonly IApprovedOperationalRuntimeImageProvider _approvedOperationalRuntimeImageProvider;
    private readonly ElementContainerStarter _elementContainerStarter;
    private readonly ILogger<PrivateStagingService> _logger;

    public PrivateStagingService(
        IConfiguration configuration,
        DockerClient docker,
        IDockerHost dockerHost,
        BackupCatalogPayloadResolver catalogPayloadResolver,
        IApprovedPostgresRuntimeProvider approvedPostgresRuntimeProvider,
        IApprovedOperationalRuntimeImageProvider approvedOperationalRuntimeImageProvider,
        ElementContainerStarter elementContainerStarter,
        ILogger<PrivateStagingService> logger)
    {
        _configuration = configuration;
        _docker = docker;
        _dockerHost = dockerHost;
        _catalogPayloadResolver = catalogPayloadResolver;
        _approvedPostgresRuntimeProvider = approvedPostgresRuntimeProvider;
        _approvedOperationalRuntimeImageProvider = approvedOperationalRuntimeImageProvider;
        _elementContainerStarter = elementContainerStarter;
        _logger = logger;
    }

    public async Task<PrivateStagingRunResult> CreatePrivateSynapseFromCatalogAsync(
        string sourceIdentity,
        PrivateStagingRunRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceIdentity))
        {
            throw new InvalidOperationException("Backup Catalog entry id is required.");
        }

        if (!IsSafePathSegment(sourceIdentity))
        {
            throw new InvalidOperationException("Backup Catalog entry id contains unsafe characters.");
        }

        var sourceMaterial = await _catalogPayloadResolver
            .ResolvePrivateStagingSourceMaterialAsync(
                sourceIdentity,
                ct);

        return await CreatePrivateSynapseFromMaterialAsync(
            sourceMaterial.CatalogEntryId!, sourceMaterial, request, ct);
    }

    public Task<PrivateStagingRunResult> CreatePrivateSynapseFromMaterialAsync(
        string sourceIdentity,
        PrivateStagingSourceMaterial sourceMaterial,
        PrivateStagingRunRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceIdentity) || !IsSafePathSegment(sourceIdentity))
            throw new InvalidOperationException("Private staging source identity is invalid.");
        return CreatePrivateSynapseCoreAsync(sourceIdentity, ResolveDataRoot(), sourceMaterial, request, ct);
    }

    private async Task<PrivateStagingRunResult> CreatePrivateSynapseCoreAsync(
        string sourceIdentity,
        string dataRoot,
        PrivateStagingSourceMaterial sourceMaterial,
        PrivateStagingRunRequest request,
        CancellationToken ct)
    {
        var sourceKind = sourceMaterial.Kind;

        var startedAtUtc = DateTimeOffset.UtcNow;
        var stagingId = CreateRunId();

        var checks = new List<PrivateStagingCheck>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var cleanupWarnings = new List<string>();

        var workspacePath = Path.Combine(dataRoot, "restore-staging", "runs", stagingId);
        var runtimePath = Path.Combine(workspacePath, "runtime");
        var matrixDataPath = Path.Combine(runtimePath, "matrix");
        var databaseDirectory = Path.Combine(workspacePath, "database");
        var databaseDumpPath = Path.Combine(databaseDirectory, "synapse.dump");
        var elementDataPath = Path.Combine(runtimePath, "element");

        Directory.CreateDirectory(matrixDataPath);
        Directory.CreateDirectory(databaseDirectory);
        Directory.CreateDirectory(elementDataPath);

        var approvedPostgresRuntime = await _approvedPostgresRuntimeProvider
            .ResolveForOperationAsync(ct);
        var postgresImage = approvedPostgresRuntime.ResolvedImageId;

        if (!string.IsNullOrWhiteSpace(request.SynapseImage))
        {
            throw new InvalidOperationException(
                "Caller-supplied Synapse image overrides are no longer supported for private staging. " +
                "MEM uses the release-approved immutable Synapse runtime authority.");
        }

        var approvedSynapseRuntime = request.RequireApprovedRuntimeImages
            ? await _approvedOperationalRuntimeImageProvider.ResolveSynapseForOperationAsync(ct)
            : await _approvedOperationalRuntimeImageProvider.PrepareSynapseAsync(ct);
        var approvedElementRuntime = request.RequireApprovedRuntimeImages
            ? await _approvedOperationalRuntimeImageProvider.ResolveElementForOperationAsync(ct)
            : await _approvedOperationalRuntimeImageProvider.PrepareElementAsync(ct);

        var synapseImage = approvedSynapseRuntime.ResolvedImageId;
        var elementImage = approvedElementRuntime.ResolvedImageId;

        var databaseName = $"{DatabaseNamePrefix}_{stagingId.Replace("-", "_", StringComparison.Ordinal)}";
        var databasePassword = CreatePassword();

        var networkName = $"mem-restore-staging-{stagingId}";
        string? networkId = null;

        var postgresContainerName = $"mem-restore-staging-postgres-{stagingId}";
        string? postgresContainerId = null;

        var synapseContainerName = $"mem-restore-staging-synapse-{stagingId}";
        string? synapseContainerId = null;

        var elementContainerName = $"mem-restore-staging-element-{stagingId}";
        string? elementContainerId = null;

        string? matrixServerName = null;
        string? sourceStackSlug = null;
        string? targetStackSlug = null;

        var importSucceeded = false;
        PrivateStagingDatabaseImportPlan? databaseImportPlan = null;
        var publicTableCount = 0;
        var synapseKnownTableCount = 0;
        long? usersCount = null;
        long? eventsCount = null;
        long? roomsCount = null;
        long? stateEventsCount = null;

        var homeserverConfigExtracted = false;
        var homeserverConfigPatched = false;
        var signingKeyExtracted = false;
        var mediaStoreExtracted = false;
        var elementConfigExtracted = false;
        var elementConfigPatched = false;
        string? elementConfigSha256 = null;
        var elementContainerStarted = false;
        var elementHealthPassed = false;
        var elementSynapseConnectivityPassed = false;
        var elementNetworkAttached = false;
        string? elementHealthResponse = null;
        string? elementLogsTail = null;
        var mediaFiles = 0L;
        var mediaBytes = 0L;
        var postgresContainerStarted = false;
        var synapseContainerStarted = false;
        var synapseHealthPassed = false;
        string? healthResponse = null;
        string? synapseLogsTail = null;

        PrivateStagingDestroySummary? destroySummary = null;

        try
        {
            if (sourceKind is not (PrivateStagingSourceKinds.BackupCatalog or PrivateStagingSourceKinds.MigrationCandidate))
                throw new InvalidOperationException("Private staging source kind is not supported.");
            if (sourceKind == PrivateStagingSourceKinds.BackupCatalog &&
                !string.Equals(sourceIdentity, sourceMaterial.CatalogEntryId, StringComparison.Ordinal))
                throw new InvalidOperationException("Catalog private staging source identity did not match the resolved payload.");

            matrixServerName = sourceMaterial.MatrixServerName;
            sourceStackSlug = sourceMaterial.SourceStackSlug;

            AddCheck(
                checks,
                errors,
                "restore-staging.source-material.resolved",
                "error",
                true,
                "Private staging source material was resolved by the trusted server-side source adapter.",
                sourceIdentity);

            var sourceIdentifier = sourceIdentity;

            targetStackSlug = string.IsNullOrWhiteSpace(request.TargetStackSlug)
                ? Slugify(string.IsNullOrWhiteSpace(sourceStackSlug)
                    ? $"restore-staging-{sourceIdentifier}"
                    : $"{sourceStackSlug}-staging")
                : Slugify(request.TargetStackSlug);

            AddCheck(
                checks,
                errors,
                "restore-staging.server-name.present",
                "error",
                !string.IsNullOrWhiteSpace(matrixServerName),
                !string.IsNullOrWhiteSpace(matrixServerName)
                    ? $"Matrix server_name was found: {matrixServerName}"
                    : "Matrix server_name is missing from private staging source material.",
                matrixServerName);

            databaseImportPlan = PrivateStagingDatabaseImportPolicy.Resolve(
                sourceMaterial.DatabaseDumpFormat);
            databaseDumpPath = Path.Combine(
                databaseDirectory,
                databaseImportPlan.WorkspaceFileName);

            await CopySourceFileAsync(
                sourceMaterial.DatabaseDumpPath,
                databaseDumpPath,
                ct);

            PrivateStagingDatabaseImportPolicy.ValidateCopiedDump(
                databaseImportPlan,
                databaseDumpPath);

            AddCheck(
                checks,
                errors,
                "restore-staging.database-dump.extracted",
                "error",
                true,
                "Synapse database dump was copied and its trusted format was verified in the private staging workspace.",
                databaseDumpPath);

            AddCheck(
                checks,
                errors,
                "restore-staging.database.import-mechanism",
                "error",
                true,
                $"Private staging selected {databaseImportPlan.ImportMechanism} for the trusted {databaseImportPlan.DumpFormat} database artifact.",
                databaseImportPlan.ImportMechanism);

            var privateHomeserverPath = Path.Combine(matrixDataPath, "homeserver.yaml");

            await CopySourceFileAsync(
                sourceMaterial.HomeserverPath,
                privateHomeserverPath,
                ct);

            homeserverConfigExtracted = true;

            AddCheck(
                checks,
                errors,
                "restore-staging.homeserver-config.extracted",
                "error",
                true,
                "homeserver.yaml was copied from trusted source material.",
                privateHomeserverPath);

            var privateSigningKeyPath = Path.Combine(matrixDataPath, "signing.key");

            await CopySourceFileAsync(
                sourceMaterial.SigningKeyPath,
                privateSigningKeyPath,
                ct);

            signingKeyExtracted = true;

            AddCheck(
                checks,
                errors,
                "restore-staging.signing-key.extracted",
                "error",
                true,
                "Matrix signing key was copied from trusted source material into private staging data.",
                privateSigningKeyPath);

            var catalogMediaResult = await CopyMediaStoreAsync(
                sourceMaterial.MediaStorePath,
                matrixDataPath,
                ct);

            mediaFiles = catalogMediaResult.Files;
            mediaBytes = catalogMediaResult.Bytes;
            mediaStoreExtracted = true;

            AddCheck(
                checks,
                errors,
                "restore-staging.media-store.extracted",
                "warning",
                true,
                sourceMaterial.MediaStorePath is null
                    ? "No media_store directory was present in trusted source material. Private Synapse staging can continue."
                    : $"media_store was copied from trusted source material. Files: {mediaFiles}. Bytes: {mediaBytes}.",
                Path.Combine(matrixDataPath, "media_store"));

            if (!string.IsNullOrWhiteSpace(sourceMaterial.ElementConfigPath))
            {
                await CopySourceFileAsync(
                    sourceMaterial.ElementConfigPath,
                    Path.Combine(elementDataPath, "config.json"),
                    ct);

                elementConfigExtracted = true;
            }

            AddCheck(
                checks,
                errors,
                "restore-staging.element-config.extracted",
                request.RequireElementRuntime ? "error" : "warning",
                elementConfigExtracted,
                elementConfigExtracted
                    ? "Element config.json was copied from trusted source material."
                    : request.RequireElementRuntime
                        ? "Element config.json is required for a complete migration staging restore."
                        : "Element config.json was not found in trusted source material. Private Synapse staging can continue.",
                elementConfigExtracted
                    ? Path.Combine(elementDataPath, "config.json")
                    : null);

            if (request.RequireElementRuntime && !elementConfigExtracted)
            {
                throw new InvalidDataException(
                    "Element config.json is required for a complete migration staging restore.");
            }

            var logConfigPath = Path.Combine(matrixDataPath, "log.config");

            WritePrivateStagingLogConfig(logConfigPath);

            PatchHomeserverYamlForPrivateStaging(
                Path.Combine(matrixDataPath, "homeserver.yaml"),
                matrixServerName!,
                databaseName,
                DatabaseUser,
                databasePassword,
                postgresHost: "restore-staging-postgres",
                postgresPort: 5432,
                logConfigPath: "/data/log.config");

            EnsurePrivateRuntimePermissions(matrixDataPath);

            homeserverConfigPatched = true;

            AddCheck(
                checks,
                errors,
                "restore-staging.homeserver-config.patched",
                "error",
                true,
                "homeserver.yaml was patched for private staging Postgres and local runtime paths.",
                Path.Combine(matrixDataPath, "homeserver.yaml"));

            var network = await _docker.Networks.CreateNetworkAsync(
                new NetworksCreateParameters
                {
                    Name = networkName,
                    Driver = "bridge",
                    Internal = true,
                    CheckDuplicate = true,
                    Labels = CreateStagingLabels(
                        stagingId,
                        sourceKind,
                        sourceIdentity,
                        service: null)
                },
                ct);

            networkId = network.ID;

            AddCheck(
                checks,
                errors,
                "restore-staging.network.created",
                "error",
                !string.IsNullOrWhiteSpace(networkId),
                "Internal-only Docker network was created for private staging.",
                networkName);

            postgresContainerId = await _dockerHost.CreateContainerAsync(
                new DockerContainerSpec(
                    Name: postgresContainerName,
                    Image: postgresImage,
                    Env: new Dictionary<string, string>
                    {
                        ["POSTGRES_DB"] = databaseName,
                        ["POSTGRES_USER"] = DatabaseUser,
                        ["POSTGRES_PASSWORD"] = databasePassword,
                        ["POSTGRES_INITDB_ARGS"] = "--encoding=UTF8 --locale=C",
                        ["LANG"] = "C",
                        ["LC_ALL"] = "C"
                    },
                    Labels: CreateStagingLabels(
                        stagingId,
                        sourceKind,
                        sourceIdentity,
                        service: "postgres"),
                    BindMounts:
                    [
                        new BindMount(
                            HostPath: workspacePath,
                            ContainerPath: "/restore",
                            ReadOnly: true)
                    ],
                    RestartPolicy: new DockerRestartPolicy(DockerRestartPolicyName.UnlessStopped),
                    NetworkName: networkName,
                    NetworkAliases:
                    [
                        "restore-staging-postgres",
                        "postgres"
                    ]),
                ct);

            await _dockerHost.StartContainerAsync(
                postgresContainerId,
                ct);

            postgresContainerStarted = true;

            AddCheck(
                checks,
                errors,
                "restore-staging.postgres.started",
                "error",
                true,
                "Private staging Postgres container was started.",
                postgresContainerName);

            await WaitForPostgresAsync(
                postgresContainerName,
                databaseName,
                DatabaseUser,
                ct);

            AddCheck(
                checks,
                errors,
                "restore-staging.postgres.ready",
                "error",
                true,
                "Private staging Postgres is stably accepting SQL connections.",
                null);

            var databaseCollation = await QueryLinesAsync(
                postgresContainerName,
                databaseName,
                "SELECT datcollate FROM pg_database WHERE datname = current_database();",
                ct);

            var lcCollate = databaseCollation.FirstOrDefault();

            var collationIsSafe =
                string.Equals(lcCollate, "C", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lcCollate, "C.UTF-8", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lcCollate, "C.UTF8", StringComparison.OrdinalIgnoreCase);

            AddCheck(
                checks,
                errors,
                "restore-staging.database.collation",
                "error",
                collationIsSafe,
                collationIsSafe
                    ? $"Private staging database uses Synapse-compatible collation '{lcCollate}'."
                    : $"Private staging database collation is '{lcCollate ?? "unknown"}', but Synapse expects 'C' or a compatible C locale.",
                lcCollate);

            var import = await ExecInContainerAsync(
                postgresContainerName,
                databaseImportPlan.BuildCommand(DatabaseUser, databaseName),
                ct,
                throwOnNonZeroExit: false);

            importSucceeded = import.ExitCode == 0;

            AddCheck(
                checks,
                errors,
                "restore-staging.database.import",
                "error",
                importSucceeded,
                importSucceeded
                    ? $"Synapse database dump imported into private staging Postgres with {databaseImportPlan.ImportMechanism}."
                    : $"Synapse database dump failed to import into private staging Postgres with {databaseImportPlan.ImportMechanism}.",
                importSucceeded
                    ? Trim(import.Stdout, 2000)
                    : Trim(import.Stderr + Environment.NewLine + import.Stdout, 4000));

            PrivateStagingDatabaseImportPolicy.EnsureImportSucceeded(importSucceeded);

            if (importSucceeded)
            {
                publicTableCount = await QueryIntAsync(
                    postgresContainerName,
                    databaseName,
                    "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public';",
                    ct);

                var knownTables = await QueryLinesAsync(
                    postgresContainerName,
                    databaseName,
                    """
                    SELECT table_name
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                    AND table_name IN ('users', 'events', 'rooms', 'state_events', 'event_json', 'room_memberships')
                    ORDER BY table_name;
                    """,
                    ct);

                synapseKnownTableCount = knownTables.Count;

                usersCount = await QueryOptionalTableCountAsync(
                    postgresContainerName,
                    databaseName,
                    "users",
                    ct);

                eventsCount = await QueryOptionalTableCountAsync(
                    postgresContainerName,
                    databaseName,
                    "events",
                    ct);

                roomsCount = await QueryOptionalTableCountAsync(
                    postgresContainerName,
                    databaseName,
                    "rooms",
                    ct);

                stateEventsCount = await QueryOptionalTableCountAsync(
                    postgresContainerName,
                    databaseName,
                    "state_events",
                    ct);

                AddCheck(
                    checks,
                    errors,
                    "restore-staging.database.tables",
                    "error",
                    publicTableCount > 0 && synapseKnownTableCount > 0,
                    $"Restored database contains {publicTableCount} public tables and {synapseKnownTableCount} known Synapse tables.",
                    string.Join(", ", knownTables));
            }

            synapseContainerId = await _dockerHost.CreateContainerAsync(
                new DockerContainerSpec(
                    Name: synapseContainerName,
                    Image: synapseImage,
                    Env: new Dictionary<string, string>
                    {
                        ["SYNAPSE_CONFIG_PATH"] = "/data/homeserver.yaml"
                    },
                    Labels: CreateStagingLabels(
                        stagingId,
                        sourceKind,
                        sourceIdentity,
                        service: "synapse"),
                    PortBindings: null,
                    BindMounts:
                    [
                        new BindMount(
                            HostPath: matrixDataPath,
                            ContainerPath: "/data",
                            ReadOnly: false)
                    ],
                    RestartPolicy: new DockerRestartPolicy(DockerRestartPolicyName.UnlessStopped),
                    NetworkName: networkName,
                    NetworkAliases:
                    [
                        "restore-staging-synapse",
                        "synapse",
                        "matrix"
                    ]),
                ct);

            await _dockerHost.StartContainerAsync(
                synapseContainerId,
                ct);

            synapseContainerStarted = true;

            AddCheck(
                checks,
                errors,
                "restore-staging.synapse.started",
                "error",
                true,
                "Private staging Synapse container was started.",
                synapseContainerName);

            var health = await WaitForSynapseHealthAsync(
                synapseContainerName,
                ct);

            synapseHealthPassed = health.Passed;
            healthResponse = health.Response;

            AddCheck(
                checks,
                errors,
                "restore-staging.synapse.health",
                "error",
                synapseHealthPassed,
                synapseHealthPassed
                    ? "Private staging Synapse health check passed."
                    : "Private staging Synapse health check did not pass.",
                healthResponse);

            try
            {
                synapseLogsTail = await _dockerHost.GetLogsAsync(
                    synapseContainerId,
                    tail: 300,
                    ct);
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not read Synapse logs tail: {ex.Message}");
            }

            if (elementConfigExtracted && synapseHealthPassed)
            {
                var elementConfigPath = Path.Combine(elementDataPath, "config.json");
                PatchElementConfigForPrivateStaging(
                    elementConfigPath,
                    matrixServerName!,
                    "http://restore-staging-synapse:8008");
                elementConfigPatched = true;
                elementConfigSha256 = await ComputeSha256Async(elementConfigPath, ct);

                AddCheck(
                    checks,
                    errors,
                    "restore-staging.element-config.patched",
                    request.RequireElementRuntime ? "error" : "warning",
                    true,
                    "Element config.json was patched to use the private Synapse service alias.",
                    elementConfigSha256);

                var elementStart = await _elementContainerStarter.EnsureStartedAsync(
                    elementContainerName,
                    elementImage,
                    elementDataPath,
                    elementConfigPath,
                    networkName,
                    "http://restore-staging-element",
                    ct,
                    allowPullIfMissing: false);

                elementContainerId = elementStart.ContainerId;
                elementContainerStarted = elementStart.Running;

                AddCheck(
                    checks,
                    errors,
                    "restore-staging.element.started",
                    request.RequireElementRuntime ? "error" : "warning",
                    elementContainerStarted,
                    elementContainerStarted
                        ? "Private staging Element container was started with a read-only config mount and no host port binding."
                        : "Private staging Element container did not start.",
                    elementContainerName);

                var elementInspect = await _docker.Containers.InspectContainerAsync(
                    elementContainerId,
                    ct);
                elementNetworkAttached = elementInspect.NetworkSettings.Networks.ContainsKey(networkName);
                var elementHasNoPublishedPorts =
                    elementInspect.HostConfig.PortBindings is null ||
                    elementInspect.HostConfig.PortBindings.Count == 0;

                AddCheck(
                    checks,
                    errors,
                    "restore-staging.element.network",
                    request.RequireElementRuntime ? "error" : "warning",
                    elementNetworkAttached && elementHasNoPublishedPorts,
                    elementNetworkAttached && elementHasNoPublishedPorts
                        ? "Element is attached only to the private staging network and publishes no host ports."
                        : "Element private network attachment or no-host-port evidence did not pass.",
                    networkName);

                var elementHealth = await WaitForElementHealthAsync(
                    elementContainerName,
                    ct);
                elementHealthPassed = elementHealth.StaticContentPassed;
                elementSynapseConnectivityPassed = elementHealth.SynapseConnectivityPassed;
                elementHealthResponse = elementHealth.Response;

                AddCheck(
                    checks,
                    errors,
                    "restore-staging.element.health",
                    request.RequireElementRuntime ? "error" : "warning",
                    elementHealthPassed,
                    elementHealthPassed
                        ? "Private staging Element served config.json and static web content."
                        : "Private staging Element did not serve its expected config and static web content.",
                    elementHealthResponse);

                AddCheck(
                    checks,
                    errors,
                    "restore-staging.element.synapse-connectivity",
                    request.RequireElementRuntime ? "error" : "warning",
                    elementSynapseConnectivityPassed,
                    elementSynapseConnectivityPassed
                        ? "Element reached the private Synapse Matrix client endpoint through the staging network."
                        : "Element could not reach the private Synapse Matrix client endpoint.",
                    elementHealthResponse);

                try
                {
                    elementLogsTail = await _dockerHost.GetLogsAsync(
                        elementContainerId,
                        tail: 200,
                        ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    warnings.Add($"Could not read Element logs tail: {ex.Message}");
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Private restore staging failed. StagingId={StagingId} SourceKind={SourceKind} CatalogEntryId={CatalogEntryId}",
                stagingId,
                sourceKind,
                sourceIdentity);

            errors.Add(ex.Message);

            if (synapseContainerId is not null)
            {
                try
                {
                    synapseLogsTail = await _dockerHost.GetLogsAsync(
                        synapseContainerId,
                        tail: 300,
                        ct);
                }
                catch
                {
                    // Best effort only.
                }
            }

            if (elementContainerId is not null)
            {
                try
                {
                    elementLogsTail = await _dockerHost.GetLogsAsync(
                        elementContainerId,
                        tail: 200,
                        ct);
                }
                catch
                {
                    // Best effort only.
                }
            }

            if (!request.KeepOnFailure)
            {
                destroySummary = await DestroyResourcesAsync(
                    elementContainerId,
                    elementContainerName,
                    synapseContainerId,
                    synapseContainerName,
                    postgresContainerId,
                    postgresContainerName,
                    networkId,
                    networkName,
                    workspacePath,
                    cleanupWarnings,
                    ct);
            }
            else
            {
                warnings.Add("Private staging failed and resources were kept because keepOnFailure was requested.");
            }
        }

        var finishedAtUtc = DateTimeOffset.UtcNow;

        var database = new PrivateStagingDatabaseSummary(
            ImportSucceeded: importSucceeded,
            PublicTableCount: publicTableCount,
            SynapseKnownTableCount: synapseKnownTableCount,
            UsersCount: usersCount,
            EventsCount: eventsCount,
            RoomsCount: roomsCount,
            StateEventsCount: stateEventsCount,
            DumpFormat: databaseImportPlan?.DumpFormat,
            ImportMechanism: databaseImportPlan?.ImportMechanism);

        var runtime = new PrivateStagingRuntimeSummary(
            HomeserverConfigExtracted: homeserverConfigExtracted,
            HomeserverConfigPatched: homeserverConfigPatched,
            SigningKeyExtracted: signingKeyExtracted,
            MediaStoreExtracted: mediaStoreExtracted,
            MediaFiles: mediaFiles,
            MediaBytes: mediaBytes,
            ElementConfigExtracted: elementConfigExtracted,
            PostgresContainerStarted: postgresContainerStarted,
            SynapseContainerStarted: synapseContainerStarted,
            SynapseHealthPassed: synapseHealthPassed,
            HealthResponse: healthResponse,
            SynapseLogsTail: Trim(synapseLogsTail ?? string.Empty, 8000),
            ElementConfigPatched: elementConfigPatched,
            ElementContainerStarted: elementContainerStarted,
            ElementHealthPassed: elementHealthPassed,
            ElementSynapseConnectivityPassed: elementSynapseConnectivityPassed,
            ElementNetworkAttached: elementNetworkAttached,
            ElementHealthResponse: Trim(elementHealthResponse ?? string.Empty, 2000),
            ElementLogsTail: Trim(elementLogsTail ?? string.Empty, 8000));

        var safety = CreateSafetySummary();

        var elementRequirementPassed = !request.RequireElementRuntime ||
            (elementConfigExtracted &&
             elementConfigPatched &&
             elementContainerStarted &&
             elementHealthPassed &&
             elementSynapseConnectivityPassed &&
             elementNetworkAttached);

        var status = errors.Count == 0 &&
                     importSucceeded &&
                     synapseHealthPassed &&
                     elementRequirementPassed
            ? "ready"
            : "failed";

        if (destroySummary is not null && status == "failed")
        {
            status = "failed-cleaned";
        }

        var result = new PrivateStagingRunResult(
            Source: "control-plane",
            Status: status,
            Mode: request.RequireElementRuntime
                ? "private-stack-staging"
                : "private-synapse-staging",
            StagingId: stagingId,
            ValidationId: null,
            StartedAtUtc: startedAtUtc,
            FinishedAtUtc: finishedAtUtc,
            UploadedZipPath: null,
            WorkspacePath: workspacePath,
            RuntimePath: runtimePath,
            MatrixDataPath: matrixDataPath,
            ElementDataPath: elementConfigExtracted ? elementDataPath : null,
            DatabaseDumpPath: databaseDumpPath,
            NetworkName: networkName,
            NetworkId: networkId,
            PostgresContainerName: postgresContainerName,
            PostgresContainerId: postgresContainerId,
            SynapseContainerName: synapseContainerName,
            SynapseContainerId: synapseContainerId,
            PostgresImage: postgresImage,
            SynapseImage: synapseImage,
            DatabaseName: databaseName,
            DatabaseUser: DatabaseUser,
            MatrixServerName: matrixServerName,
            TargetStackSlug: targetStackSlug ?? $"restore-staging-{stagingId}",
            Safety: safety,
            Database: database,
            Runtime: runtime,
            Checks: checks,
            Warnings: warnings,
            Errors: errors.Distinct(StringComparer.Ordinal).ToList(),
            Destroy: destroySummary,
            Detail: status == "ready"
                ? request.RequireElementRuntime
                    ? "Full private staging restore is ready. PostgreSQL, Synapse, and Element are running on the private network with no public routes or host ports. Use the destroy endpoint when finished."
                    : "Private staging restore is ready. It is persistent, private-only, and has not published DNS, NPM routes, certificates, federation, or production replacement. Use the destroy endpoint when finished."
                : "Private staging restore did not become ready. Inspect checks, errors, cleanup/destroy state, and runtime logs.",
            SourceKind: sourceKind,
            CatalogEntryId: string.Equals(sourceKind, PrivateStagingSourceKinds.BackupCatalog, StringComparison.Ordinal) ? sourceIdentity : null,
            SynapseApprovedReference: approvedSynapseRuntime?.ApprovedReference,
            ElementContainerName: elementConfigExtracted ? elementContainerName : null,
            ElementContainerId: elementContainerId,
            ElementImage: elementConfigExtracted ? elementImage : null,
            ElementApprovedReference: approvedElementRuntime?.ApprovedReference,
            ElementConfigSha256: elementConfigSha256);

        await SaveResultAsync(
            result,
            ct);

        return result;
    }

    /// <summary>
    /// Reads one durable private-staging result by its server-owned identifier.
    /// This exposes retained evidence to trusted orchestration services without
    /// accepting browser paths or Docker identifiers.
    /// </summary>
    public Task<PrivateStagingRunResult?> GetAsync(
        string stagingId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stagingId))
        {
            throw new InvalidOperationException("Private staging id is required.");
        }

        return LoadResultAsync(stagingId.Trim(), ct);
    }

    public async Task<PrivateStagingRunResult> DestroyAsync(
        string stagingId,
        CancellationToken ct)
    {
        var existing = await LoadResultAsync(
            stagingId,
            ct);

        if (existing is null)
        {
            throw new FileNotFoundException($"Restore Staging run '{stagingId}' was not found.");
        }

        if (IsDestroyComplete(existing))
        {
            return existing;
        }

        var warnings = new List<string>();

        var destroy = await DestroyResourcesAsync(
            existing.ElementContainerId,
            existing.ElementContainerName ?? $"mem-restore-staging-element-{existing.StagingId}",
            existing.SynapseContainerId,
            existing.SynapseContainerName,
            existing.PostgresContainerId,
            existing.PostgresContainerName,
            existing.NetworkId,
            existing.NetworkName,
            existing.WorkspacePath,
            warnings,
            ct);

        var destroyComplete = IsDestroyComplete(destroy);
        var result = existing with
        {
            Status = destroyComplete ? "destroyed" : "destroy-needs-attention",
            FinishedAtUtc = DateTimeOffset.UtcNow,
            Destroy = destroy,
            Detail = destroyComplete
                ? "Private staging resources were destroyed. Production resources were not touched."
                : "Private staging retirement needs attention because one or more disposable resources could not be removed. Production resources were not touched."
        };

        await SaveResultAsync(
            result,
            ct);

        if (!destroyComplete)
        {
            throw new PrivateStagingDestroyIncompleteException(
                "Private staging retirement did not remove every disposable resource.",
                result);
        }

        return result;
    }

    internal static bool IsDestroyComplete(
        PrivateStagingRunResult result) =>
        string.Equals(
            result.Status,
            "destroyed",
            StringComparison.OrdinalIgnoreCase) ||
        IsDestroyComplete(result.Destroy);

    internal static bool IsDestroyComplete(
        PrivateStagingDestroySummary? destroy) =>
        destroy is not null &&
        destroy.ElementContainerRemoved &&
        destroy.SynapseContainerRemoved &&
        destroy.PostgresContainerRemoved &&
        destroy.NetworkRemoved &&
        destroy.WorkspaceRemoved;

    private async Task<PrivateStagingRunResult?> LoadResultAsync(
        string stagingId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stagingId))
        {
            throw new InvalidOperationException("Staging id is required.");
        }

        if (!IsSafePathSegment(stagingId))
        {
            throw new InvalidOperationException("Staging id contains unsafe characters.");
        }

        var resultPath = Path.Combine(
            ResolveHistoryRoot(),
            stagingId,
            "restore-staging-result.json");

        if (!File.Exists(resultPath))
        {
            return null;
        }

        await using var stream = File.OpenRead(resultPath);

        return await JsonSerializer.DeserializeAsync<PrivateStagingRunResult>(
            stream,
            JsonOptions,
            ct);
    }

    private async Task SaveResultAsync(
        PrivateStagingRunResult result,
        CancellationToken ct)
    {
        var runDirectory = Path.Combine(
            ResolveHistoryRoot(),
            result.StagingId);

        Directory.CreateDirectory(runDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(runDirectory, "restore-staging-result.json"),
            JsonSerializer.Serialize(result, JsonOptions),
            ct);
    }

    private string ResolveHistoryRoot()
    {
        return Path.Combine(
            ResolveDataRoot(),
            "restore-staging",
            "history");
    }

    private async Task<PrivateStagingDestroySummary> DestroyResourcesAsync(
        string? elementContainerId,
        string elementContainerName,
        string? synapseContainerId,
        string synapseContainerName,
        string? postgresContainerId,
        string postgresContainerName,
        string? networkId,
        string networkName,
        string workspacePath,
        List<string> warnings,
        CancellationToken ct)
    {
        var elementContainerRemoved = await RemoveContainerIfPresentAsync(
            elementContainerId,
            elementContainerName,
            warnings,
            ct);

        var synapseContainerRemoved = await RemoveContainerIfPresentAsync(
            synapseContainerId,
            synapseContainerName,
            warnings,
            ct);

        var postgresContainerRemoved = await RemoveContainerIfPresentAsync(
            postgresContainerId,
            postgresContainerName,
            warnings,
            ct);

        var networkRemoved = await RemoveNetworkIfPresentAsync(
            networkId,
            networkName,
            warnings,
            ct);

        var workspaceRemoved = RemoveWorkspaceIfPresent(
            workspacePath,
            warnings);

        return new PrivateStagingDestroySummary(
            DestroyedAtUtc: DateTimeOffset.UtcNow,
            SynapseContainerRemoved: synapseContainerRemoved,
            PostgresContainerRemoved: postgresContainerRemoved,
            NetworkRemoved: networkRemoved,
            WorkspaceRemoved: workspaceRemoved,
            Warnings: warnings,
            ElementContainerRemoved: elementContainerRemoved);
    }

    private async Task<bool> RemoveContainerIfPresentAsync(
        string? containerId,
        string containerName,
        List<string> warnings,
        CancellationToken ct)
    {
        var containerReference = string.IsNullOrWhiteSpace(containerId)
            ? containerName
            : containerId.Trim();

        if (string.IsNullOrWhiteSpace(containerReference))
        {
            return true;
        }

        try
        {
            await _dockerHost.RemoveContainerAsync(
                containerReference,
                force: true,
                ct);

            return true;
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Retirement is deliberately idempotent. A prior interrupted attempt may
            // already have removed this disposable container.
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(
                $"Could not remove private staging container '{containerName}': {ex.Message}");
            return false;
        }
    }

    private async Task<bool> RemoveNetworkIfPresentAsync(
        string? networkId,
        string networkName,
        List<string> warnings,
        CancellationToken ct)
    {
        var networkReference = string.IsNullOrWhiteSpace(networkId)
            ? networkName
            : networkId.Trim();

        if (string.IsNullOrWhiteSpace(networkReference))
        {
            return true;
        }

        try
        {
            await _docker.Networks.DeleteNetworkAsync(
                networkReference,
                ct);

            return true;
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // A previous partially completed retirement may already have removed it.
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            warnings.Add(
                $"Could not remove private staging Docker network '{networkName}': {ex.Message}");
            return false;
        }
    }

    private static bool RemoveWorkspaceIfPresent(
        string workspacePath,
        List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) ||
            !Directory.Exists(workspacePath))
        {
            return true;
        }

        try
        {
            Directory.Delete(
                workspacePath,
                recursive: true);

            return true;
        }
        catch (Exception ex)
        {
            warnings.Add(
                $"Could not remove private staging workspace '{workspacePath}': {ex.Message}");
            return false;
        }
    }

    internal static void PatchElementConfigForPrivateStaging(
        string elementConfigPath,
        string matrixServerName,
        string privateSynapseBaseUrl)
    {
        if (!File.Exists(elementConfigPath))
        {
            throw new FileNotFoundException(
                $"Element config.json was expected but not found: {elementConfigPath}");
        }

        var node = JsonNode.Parse(File.ReadAllText(elementConfigPath)) as JsonObject
            ?? throw new InvalidDataException(
                "Element config.json could not be parsed as a JSON object.");

        var defaultServerConfig = node["default_server_config"] as JsonObject ?? new JsonObject();
        var homeserver = defaultServerConfig["m.homeserver"] as JsonObject ?? new JsonObject();
        homeserver["base_url"] = privateSynapseBaseUrl.TrimEnd('/');
        homeserver["server_name"] = matrixServerName.Trim();
        defaultServerConfig["m.homeserver"] = homeserver;
        node["default_server_config"] = defaultServerConfig;
        node["default_server_name"] = matrixServerName.Trim();
        node.Remove("default_hs_url");
        node.Remove("default_is_url");
        node["disable_custom_urls"] = true;

        File.WriteAllText(
            elementConfigPath,
            node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<PrivateElementHealthResult> WaitForElementHealthAsync(
        string elementContainerName,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        var staticContentPassed = false;
        var synapseConnectivityPassed = false;
        string? lastOutput = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var staticResult = await ExecInContainerAsync(
                    elementContainerName,
                    [
                        "sh",
                        "-c",
                        "(wget -qO- http://127.0.0.1/config.json || curl -fsS http://127.0.0.1/config.json) | grep -q 'restore-staging-synapse:8008' && (wget -qO- http://127.0.0.1/ || curl -fsS http://127.0.0.1/) | grep -qi '<html'"
                    ],
                    ct,
                    throwOnNonZeroExit: false);

                staticContentPassed = staticResult.ExitCode == 0;

                var synapseResult = await ExecInContainerAsync(
                    elementContainerName,
                    [
                        "sh",
                        "-c",
                        "(wget -qO- http://restore-staging-synapse:8008/_matrix/client/versions || curl -fsS http://restore-staging-synapse:8008/_matrix/client/versions) | grep -q 'versions'"
                    ],
                    ct,
                    throwOnNonZeroExit: false);

                synapseConnectivityPassed = synapseResult.ExitCode == 0;
                lastOutput = Trim(
                    staticResult.Stderr + Environment.NewLine + staticResult.Stdout + Environment.NewLine +
                    synapseResult.Stderr + Environment.NewLine + synapseResult.Stdout,
                    2000);

                if (staticContentPassed && synapseConnectivityPassed)
                {
                    return new PrivateElementHealthResult(
                        StaticContentPassed: true,
                        SynapseConnectivityPassed: true,
                        Response: "Element served its patched config and static page, then reached private Synapse through the internal staging network.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastOutput = ex.Message;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        return new PrivateElementHealthResult(
            StaticContentPassed: staticContentPassed,
            SynapseConnectivityPassed: synapseConnectivityPassed,
            Response: lastOutput);
    }

    private async Task<PrivateSynapseHealthResult> WaitForSynapseHealthAsync(
        string synapseContainerName,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        string? lastOutput = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await ExecInContainerAsync(
                    synapseContainerName,
                    [
                        "python",
                        "-c",
                        """
                        import urllib.request
                        import sys
                        try:
                            body = urllib.request.urlopen("http://127.0.0.1:8008/health", timeout=3).read().decode("utf-8", "replace")
                            print(body)
                            sys.exit(0)
                        except Exception as e:
                            print(str(e))
                            sys.exit(1)
                        """
                    ],
                    ct,
                    throwOnNonZeroExit: false);

                lastOutput = Trim(
                    result.Stderr + Environment.NewLine + result.Stdout,
                    2000);

                if (result.ExitCode == 0)
                {
                    return new PrivateSynapseHealthResult(
                        Passed: true,
                        Response: Trim(result.Stdout, 2000));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastOutput = ex.Message;
            }

            await Task.Delay(
                TimeSpan.FromSeconds(2),
                ct);
        }

        return new PrivateSynapseHealthResult(
            Passed: false,
            Response: lastOutput);
    }

    private async Task WaitForPostgresAsync(
        string containerName,
        string databaseName,
        string databaseUser,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        var consecutiveStableReads = 0;
        string? lastDetail = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var readiness = await ExecInContainerAsync(
                    containerName,
                    [
                        "pg_isready",
                        "-U", databaseUser,
                        "-d", databaseName
                    ],
                    ct,
                    throwOnNonZeroExit: false);

                if (readiness.ExitCode != 0)
                {
                    consecutiveStableReads = 0;
                    lastDetail = Trim(
                        readiness.Stderr + Environment.NewLine + readiness.Stdout,
                        2000);

                    await Task.Delay(
                        TimeSpan.FromSeconds(1),
                        ct);

                    continue;
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(1),
                    ct);

                var sqlReadiness = await ExecInContainerAsync(
                    containerName,
                    [
                        "psql",
                        "-v", "ON_ERROR_STOP=1",
                        "-U", databaseUser,
                        "-d", databaseName,
                        "-t",
                        "-A",
                        "-c", "SELECT 1;"
                    ],
                    ct,
                    throwOnNonZeroExit: false);

                if (sqlReadiness.ExitCode == 0 &&
                    sqlReadiness.Stdout
                        .Split(
                            '\n',
                            StringSplitOptions.RemoveEmptyEntries |
                            StringSplitOptions.TrimEntries)
                        .Any(line => line == "1"))
                {
                    consecutiveStableReads++;

                    if (consecutiveStableReads >= 3)
                    {
                        return;
                    }
                }
                else
                {
                    consecutiveStableReads = 0;
                    lastDetail = Trim(
                        sqlReadiness.Stderr + Environment.NewLine + sqlReadiness.Stdout,
                        2000);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                consecutiveStableReads = 0;
                lastDetail = ex.Message;
            }

            await Task.Delay(
                TimeSpan.FromSeconds(1),
                ct);
        }

        throw new InvalidOperationException(
            $"Private staging Postgres did not become stably ready in time. Last output: {lastDetail ?? "none"}");
    }

    private async Task<int> QueryIntAsync(
        string containerName,
        string databaseName,
        string sql,
        CancellationToken ct)
    {
        var lines = await QueryLinesAsync(
            containerName,
            databaseName,
            sql,
            ct);

        return lines.Count == 0
            ? 0
            : int.Parse(lines[0]);
    }

    private async Task<long?> QueryOptionalTableCountAsync(
        string containerName,
        string databaseName,
        string tableName,
        CancellationToken ct)
    {
        var exists = await QueryIntAsync(
            containerName,
            databaseName,
            $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = {QuoteLiteral(tableName)};",
            ct);

        if (exists == 0)
        {
            return null;
        }

        var lines = await QueryLinesAsync(
            containerName,
            databaseName,
            $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)};",
            ct);

        return lines.Count == 0
            ? null
            : long.Parse(lines[0]);
    }

    private async Task<IReadOnlyList<string>> QueryLinesAsync(
        string containerName,
        string databaseName,
        string sql,
        CancellationToken ct)
    {
        var result = await ExecInContainerAsync(
            containerName,
            [
                "psql",
                "-v", "ON_ERROR_STOP=1",
                "-U", DatabaseUser,
                "-d", databaseName,
                "-t",
                "-A",
                "-c", sql
            ],
            ct);

        return result.Stdout
            .Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
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

        using var stream = await _docker.Exec.StartAndAttachContainerExecAsync(
            exec.ID,
            tty: false,
            cancellationToken: ct);

        var output = await stream.ReadOutputToEndAsync(ct);

        var inspect = await _docker.Exec.InspectContainerExecAsync(
            exec.ID,
            ct);

        var result = new ExecResult(
            ExitCode: inspect.ExitCode,
            Stdout: output.stdout ?? string.Empty,
            Stderr: output.stderr ?? string.Empty);

        if (throwOnNonZeroExit && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command failed in container '{containerName}' with exit code {result.ExitCode}: {Trim(result.Stderr + Environment.NewLine + result.Stdout, 4000)}");
        }

        return result;
    }

    private static async Task CopySourceFileAsync(
        string sourcePath,
        string targetPath,
        CancellationToken ct)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException(
                $"Private staging source material was not found: {sourcePath}",
                sourcePath);
        }

        var targetDirectory = Path.GetDirectoryName(targetPath);

        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        await using var input = File.OpenRead(sourcePath);
        await using var output = File.Create(targetPath);

        await input.CopyToAsync(output, ct);
    }

    private static async Task<MediaExtractionResult> CopyMediaStoreAsync(
        string? sourceMediaStorePath,
        string matrixDataPath,
        CancellationToken ct)
    {
        var targetMediaStorePath = Path.Combine(
            matrixDataPath,
            "media_store");

        Directory.CreateDirectory(targetMediaStorePath);

        if (string.IsNullOrWhiteSpace(sourceMediaStorePath))
        {
            return new MediaExtractionResult(
                Files: 0,
                Bytes: 0);
        }

        if (!Directory.Exists(sourceMediaStorePath))
        {
            throw new DirectoryNotFoundException(
                $"Private staging media_store directory was not found: {sourceMediaStorePath}");
        }

        var files = 0L;
        var bytes = 0L;

        foreach (var sourceFilePath in Directory.EnumerateFiles(
                     sourceMediaStorePath,
                     "*",
                     SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(
                sourceMediaStorePath,
                sourceFilePath);

            if (!IsSafeRelativePath(relativePath))
            {
                throw new InvalidDataException(
                    $"Unsafe media_store source path: {sourceFilePath}");
            }

            var targetPath = Path.Combine(
                targetMediaStorePath,
                relativePath);

            await CopySourceFileAsync(
                sourceFilePath,
                targetPath,
                ct);

            files++;
            bytes += new FileInfo(sourceFilePath).Length;
        }

        return new MediaExtractionResult(
            Files: files,
            Bytes: bytes);
    }

    private static void PatchHomeserverYamlForPrivateStaging(
        string homeserverPath,
        string serverName,
        string databaseName,
        string databaseUser,
        string databasePassword,
        string postgresHost,
        int postgresPort,
        string logConfigPath)
    {
        var lines = File.ReadAllLines(homeserverPath).ToList();

        EnsureOrReplaceScalar(
            lines,
            "server_name",
            $"\"{EscapeYaml(serverName)}\"");

        EnsureOrReplaceScalar(
            lines,
            "signing_key_path",
            "\"/data/signing.key\"");

        EnsureOrReplaceScalar(
            lines,
            "media_store_path",
            "\"/data/media_store\"");

        EnsureOrReplaceScalar(
            lines,
            "log_config",
            $"\"{EscapeYaml(logConfigPath)}\"");

        EnsureOrReplaceScalar(
            lines,
            "enable_registration",
            "false");

        EnsureOrReplaceScalar(
            lines,
            "enable_registration_without_verification",
            "false");

        EnsureOrReplaceScalar(
            lines,
            "serve_server_wellknown",
            "false");

        EnsureOrReplaceScalar(
            lines,
            "allow_public_rooms_without_auth",
            "false");

        EnsureOrReplaceScalar(
            lines,
            "allow_public_rooms_over_federation",
            "false");

        RemoveTopLevelBlock(
            lines,
            "database");

        lines.Add(string.Empty);
        lines.Add("# MEM private restore staging database override.");
        lines.Add("# This staging homeserver must remain private and must not be published publicly.");
        lines.Add("database:");
        lines.Add("  name: psycopg2");
        lines.Add("  args:");
        lines.Add($"    user: \"{EscapeYaml(databaseUser)}\"");
        lines.Add($"    password: \"{EscapeYaml(databasePassword)}\"");
        lines.Add($"    database: \"{EscapeYaml(databaseName)}\"");
        lines.Add($"    host: \"{EscapeYaml(postgresHost)}\"");
        lines.Add($"    port: {postgresPort}");
        lines.Add("    cp_min: 5");
        lines.Add("    cp_max: 10");

        File.WriteAllLines(
            homeserverPath,
            lines);
    }

    private static void EnsureOrReplaceScalar(
        List<string> lines,
        string key,
        string value)
    {
        var replacement = $"{key}: {value}";

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.StartsWith(
                    key + ":",
                    StringComparison.Ordinal))
            {
                var indent = lines[i][..(lines[i].Length - trimmed.Length)];
                lines[i] = indent + replacement;
                return;
            }
        }

        lines.Add(replacement);
    }

    private static void RemoveTopLevelBlock(
        List<string> lines,
        string key)
    {
        var startIndex = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(
                    key + ":",
                    StringComparison.Ordinal))
            {
                startIndex = i;
                break;
            }
        }

        if (startIndex < 0)
        {
            return;
        }

        var endIndex = lines.Count;

        for (var i = startIndex + 1; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.Trim().Length == 0)
            {
                continue;
            }

            var leadingSpaces = line.TakeWhile(char.IsWhiteSpace).Count();

            if (leadingSpaces == 0)
            {
                endIndex = i;
                break;
            }
        }

        lines.RemoveRange(
            startIndex,
            endIndex - startIndex);
    }

    private string ResolveDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static Dictionary<string, string> CreateStagingLabels(
        string stagingId,
        string sourceKind,
        string? sourceIdentity,
        string? service)
    {
        var labels = new Dictionary<string, string>
        {
            ["mem.component"] = "restore-staging",
            ["mem.restore-staging.id"] = stagingId,
            ["mem.restore-staging.source-kind"] = sourceKind,
            ["mem.managed-by"] = "mem-host-agent"
        };

        if (!string.IsNullOrWhiteSpace(sourceIdentity))
        {
            labels[sourceKind == PrivateStagingSourceKinds.BackupCatalog
                ? "mem.restore-staging.catalog-entry-id"
                : "mem.migration-staging.candidate-id"] = sourceIdentity;
        }

        if (!string.IsNullOrWhiteSpace(service))
        {
            labels["mem.service"] = service;
        }

        return labels;
    }

    private static PrivateStagingSafetySummary CreateSafetySummary()
    {
        return new PrivateStagingSafetySummary(
            PrivateOnly: true,
            DockerNetworkInternal: true,
            PublicRoutesCreated: false,
            DnsChanged: false,
            CertificatesChanged: false,
            ProductionContainersTouched: false,
            ProductionDatabasesTouched: false,
            RequiresExplicitDestroy: true,
            Notes:
            [
                "No NPM routes are created.",
                "No DNS records are changed.",
                "No certificates are requested or imported.",
                "No production stack is stopped, deleted, replaced, or modified.",
                "The Docker network is internal-only and staging containers have no host port bindings.",
                "Destroy this staging runtime explicitly when testing is complete."
            ]);
    }

    private static void WritePrivateStagingLogConfig(
        string logConfigPath)
    {
        var directory = Path.GetDirectoryName(logConfigPath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            logConfigPath,
            """
            version: 1

            formatters:
              precise:
                format: '%(asctime)s - %(name)s - %(lineno)d - %(levelname)s - %(request)s - %(message)s'

            filters:
              context:
                (): synapse.logging.context.LoggingContextFilter
                request: ""

            handlers:
              console:
                class: logging.StreamHandler
                formatter: precise
                filters: [context]
                level: INFO

            loggers:
              synapse:
                level: INFO

              synapse.storage.SQL:
                level: WARNING

            root:
              level: INFO
              handlers: [console]

            disable_existing_loggers: false
            """);
    }

    private static void EnsurePrivateRuntimePermissions(
        string matrixDataPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            SetDirectoryMode(matrixDataPath);

            foreach (var directory in Directory.EnumerateDirectories(
                         matrixDataPath,
                         "*",
                         SearchOption.AllDirectories))
            {
                SetDirectoryMode(directory);
            }

            foreach (var file in Directory.EnumerateFiles(
                         matrixDataPath,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(
                    file,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead |
                    UnixFileMode.GroupWrite |
                    UnixFileMode.OtherRead);
            }
        }
        catch
        {
            // Permission normalization is best-effort.
        }
    }

    private static void SetDirectoryMode(
        string path)
    {
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupWrite |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherWrite |
            UnixFileMode.OtherExecute);
    }

    private static void AddCheck(
        List<PrivateStagingCheck> checks,
        List<string> errors,
        string code,
        string severity,
        bool passed,
        string message,
        string? detail)
    {
        checks.Add(new PrivateStagingCheck(
            Code: code,
            Severity: severity,
            Passed: passed,
            Message: message,
            Detail: detail));

        if (!passed && severity == "error")
        {
            errors.Add(message);
        }
    }

    private static string CreateRunId()
    {
        return DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss'Z'") +
               "-" +
               Guid.NewGuid().ToString("N")[..8];
    }

    private static string CreatePassword()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);

        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool IsSafePathSegment(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return value
            .Split(
                '.',
                StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment != "." && segment != "..");
    }

    private static bool IsSafeRelativePath(
        string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        if (Path.IsPathRooted(relativePath))
        {
            return false;
        }

        var normalized = NormalizeZipPath(relativePath);

        if (normalized.Contains(
                "../",
                StringComparison.Ordinal) ||
            normalized.Contains(
                "/..",
                StringComparison.Ordinal) ||
            normalized == "..")
        {
            return false;
        }

        return true;
    }

    private static string NormalizeZipPath(
        string value)
    {
        return value.Replace('\\', '/').Trim();
    }

    private static string Slugify(
        string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();

        var slug = new string(chars);

        while (slug.Contains(
                   "--",
                   StringComparison.Ordinal))
        {
            slug = slug.Replace(
                "--",
                "-",
                StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug.Trim('-'))
            ? "restore-staging"
            : slug.Trim('-');
    }

    private static string QuoteLiteral(
        string value)
    {
        return "'" + value.Replace(
            "'",
            "''",
            StringComparison.Ordinal) + "'";
    }

    private static string QuoteIdentifier(
        string value)
    {
        return "\"" + value.Replace(
            "\"",
            "\"\"",
            StringComparison.Ordinal) + "\"";
    }

    private static string EscapeYaml(
        string value)
    {
        return value
            .Replace(
                "\\",
                "\\\\",
                StringComparison.Ordinal)
            .Replace(
                "\"",
                "\\\"",
                StringComparison.Ordinal);
    }

    private static string Trim(
        string value,
        int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= max
            ? value
            : value[..max] + "...";
    }

    private sealed record ExecResult(
        long ExitCode,
        string Stdout,
        string Stderr);

    private sealed record MediaExtractionResult(
        long Files,
        long Bytes);

    private sealed record PrivateElementHealthResult(
        bool StaticContentPassed,
        bool SynapseConnectivityPassed,
        string? Response);

    private sealed record PrivateSynapseHealthResult(
        bool Passed,
        string? Response);

}