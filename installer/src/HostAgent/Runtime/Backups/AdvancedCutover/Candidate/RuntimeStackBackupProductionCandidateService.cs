using System.Text.Json.Nodes;
using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Docker;
using HostAgent.Docker.Models;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.Backups.Catalog;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Candidate;

public sealed class RuntimeStackBackupProductionCandidateService
{
    private readonly PrivateStagingService _stagingService;
    private readonly BackupCatalogPayloadResolver _catalogPayloadResolver;
    private readonly CatalogRestoreSessionService _catalogRestoreSessions;
    private readonly RuntimeStackBackupProductionCandidateHistoryService _candidateHistory;
    private readonly DockerClient _docker;
    private readonly IDockerHost _dockerHost;
    private readonly ILogger<RuntimeStackBackupProductionCandidateService> _logger;

    public RuntimeStackBackupProductionCandidateService(
        PrivateStagingService stagingService,
        BackupCatalogPayloadResolver catalogPayloadResolver,
        CatalogRestoreSessionService catalogRestoreSessions,
        RuntimeStackBackupProductionCandidateHistoryService candidateHistory,
        DockerClient docker,
        IDockerHost dockerHost,
        ILogger<RuntimeStackBackupProductionCandidateService> logger)
    {
        _stagingService = stagingService;
        _catalogPayloadResolver = catalogPayloadResolver;
        _catalogRestoreSessions = catalogRestoreSessions;
        _candidateHistory = candidateHistory;
        _docker = docker;
        _dockerHost = dockerHost;
        _logger = logger;
    }

    /// <summary>
    /// Creates or resumes a private-only Production Restore candidate from
    /// canonical Backup Catalog material. This path deliberately never reads
    /// the legacy uploaded-ZIP store and never exposes public routes.
    /// </summary>
    public async Task<CatalogProductionCandidateResponse>
        CreateRecreateProductionPrivateCandidateFromCatalogAsync(
            string catalogEntryId,
            CatalogProductionCandidateRequest request,
            CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrWhiteSpace(request.SynapseImage) ||
            !string.IsNullOrWhiteSpace(request.ElementImage))
        {
            throw new InvalidOperationException(
                "Caller-supplied Matrix/Element image overrides are no longer supported for production restore candidates. " +
                "MEM uses release-approved immutable runtime image authorities.");
        }

        if (string.IsNullOrWhiteSpace(catalogEntryId))
        {
            throw new InvalidOperationException("Backup Catalog entry id is required.");
        }

        if (!IsSafePathSegment(catalogEntryId))
        {
            throw new InvalidOperationException("Backup Catalog entry id contains unsafe characters.");
        }

        // Resolve before any Docker work. This validates that the candidate
        // will come exclusively from complete managed catalog material.
        var payload = await _catalogPayloadResolver
            .ResolveProductionRestorePlanPayloadAsync(
                catalogEntryId,
                ct);

        var restoreMode = ResolveSupportedRestoreMode(request.RestoreMode);

        var targetStackSlug = string.IsNullOrWhiteSpace(request.TargetStackSlug)
            ? Slugify($"{payload.Manifest.Stack.Slug}-production-candidate")
            : Slugify(request.TargetStackSlug);

        // Durable audit/workspace state only. No target claim is taken here.
        var restoreSession = await _catalogRestoreSessions.PrepareAsync(
            payload.CatalogEntryId,
            ct);

        var existing = await _candidateHistory.FindActiveCatalogCandidateAsync(
            payload.CatalogEntryId,
            targetStackSlug,
            ct);

        if (existing is not null)
        {
            return new CatalogProductionCandidateResponse(
                Source: "control-plane",
                Status: existing.Status,
                CatalogEntryId: payload.CatalogEntryId,
                RestoreSessionId: restoreSession.RestoreSessionId,
                RestoreAttemptCreated: restoreSession.RestoreAttemptCreated,
                RestoreAttemptResumed: restoreSession.RestoreAttemptResumed,
                CandidateCreated: false,
                CandidateResumed: true,
                PayloadState: BackupCatalogPayloadStates.Available,
                IntegrityStatus: payload.IntegrityStatus,
                WarningCount: payload.WarningCount,
                Candidate: existing,
                Detail: "The existing active private candidate was resumed for the Backup Catalog entry. Public cutover remains locked.");
        }

        var stagingRequest = new PrivateStagingRunRequest(
    KeepOnFailure: request.KeepOnFailure ?? false,
    PostgresImage: request.PostgresImage,
    SynapseImage: null,
    TargetStackSlug: targetStackSlug);

        var stagingResult = await _stagingService
            .CreatePrivateSynapseFromCatalogAsync(
                payload.CatalogEntryId,
                stagingRequest,
                ct);

        var candidate = ToCandidateResult(
            stagingResult,
            restoreMode,
            targetStackSlug,
            stagingResult.ElementImage);

        // CatalogEntryId plus SourceKind define the canonical source identity
        // for this candidate. The history model's legacy-shaped ValidationId
        // field remains empty for all new catalog-native candidates.
        candidate = candidate with
        {
            CatalogEntryId = payload.CatalogEntryId,
            SourceKind = "backup-catalog",
            RestoreSessionId = restoreSession.RestoreSessionId,
            Warnings = candidate.Warnings
                .Concat(
                [
                    "This private Production Restore candidate was sourced exclusively from the managed Backup Catalog payload.",
                    "The original uploaded ZIP was not read and no public cutover route is available from this candidate."
                ])
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };

        candidate = await TryStartPrivateElementRuntimeAsync(
            candidate,
            ct);

        await _candidateHistory.SaveCandidateAsync(
            candidate,
            ct);

        _logger.LogInformation(
            "Created private catalog Production Restore candidate. CandidateId={CandidateId} CatalogEntryId={CatalogEntryId} RestoreSessionId={RestoreSessionId} TargetStackSlug={TargetStackSlug} Status={Status} ElementStarted={ElementStarted} ElementHealth={ElementHealth}",
            candidate.CandidateId,
            candidate.CatalogEntryId,
            candidate.RestoreSessionId,
            candidate.TargetStackSlug,
            candidate.Status,
            candidate.Runtime.ElementContainerStarted,
            candidate.Runtime.ElementHealthPassed);

        return new CatalogProductionCandidateResponse(
            Source: "control-plane",
            Status: candidate.Status,
            CatalogEntryId: payload.CatalogEntryId,
            RestoreSessionId: restoreSession.RestoreSessionId,
            RestoreAttemptCreated: restoreSession.RestoreAttemptCreated,
            RestoreAttemptResumed: restoreSession.RestoreAttemptResumed,
            CandidateCreated: true,
            CandidateResumed: false,
            PayloadState: BackupCatalogPayloadStates.Available,
            IntegrityStatus: payload.IntegrityStatus,
            WarningCount: payload.WarningCount,
            Candidate: candidate,
            Detail: candidate.Status == "private_candidate_ready"
                ? "Private Production Restore candidate is ready from the managed Backup Catalog payload. Public cutover remains locked."
                : "Private Production Restore candidate was created from the managed Backup Catalog payload, but did not become ready. Inspect checks and errors.");
    }

    /// <summary>
    /// Adopts an already verified Migration-owned private staging runtime as a
    /// Production Restore candidate. The underlying PostgreSQL and Synapse
    /// runtime is reused; only the private Element runtime and durable cutover
    /// candidate evidence are added. No Backup Catalog item or Restore Session
    /// is created.
    /// </summary>
    public async Task<RuntimeStackBackupProductionCandidateResult>
        CreateOrResumeFromMigrationStagingAsync(
            string migrationId,
            string privateStagingId,
            string? targetStackSlug,
            CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(migrationId) || !IsSafePathSegment(migrationId))
        {
            throw new InvalidOperationException("Migration id is required and must be a safe identifier.");
        }

        if (string.IsNullOrWhiteSpace(privateStagingId) || !IsSafePathSegment(privateStagingId))
        {
            throw new InvalidOperationException("Verified private staging id is required and must be a safe identifier.");
        }

        var staging = await _stagingService.GetAsync(privateStagingId, ct)
            ?? throw new FileNotFoundException(
                $"Private staging runtime '{privateStagingId}' was not found.");

        var stagingReady =
            string.Equals(staging.SourceKind, "migration-candidate", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(staging.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
            staging.Destroy is null &&
            staging.Safety.PrivateOnly &&
            staging.Safety.DockerNetworkInternal &&
            !staging.Safety.PublicRoutesCreated &&
            staging.Database.ImportSucceeded &&
            staging.Runtime.SynapseHealthPassed;

        if (!stagingReady)
        {
            throw new InvalidOperationException(
                "The selected Migration staging runtime is not verified, active, private-only, and healthy.");
        }

        var resolvedTargetStackSlug = string.IsNullOrWhiteSpace(targetStackSlug)
            ? Slugify($"{staging.TargetStackSlug}-production-candidate")
            : Slugify(targetStackSlug);

        var existing = await _candidateHistory.FindActiveMigrationCandidateAsync(
            migrationId,
            targetStackSlug: null,
            ct: ct);

        if (existing is not null)
        {
            return existing;
        }

        var candidate = ToCandidateResult(
            staging,
            "recreate-production",
            resolvedTargetStackSlug,
            staging.ElementImage) with
        {
            ValidationId = migrationId,
            SourceKind = "migration-session",
            CatalogEntryId = null,
            RestoreSessionId = null,
            Warnings = staging.Warnings
                .Concat(
                [
                    "This private Production Restore candidate was adopted from a verified Migration Staging Run.",
                    "No Backup Catalog item or normal Restore Session was created.",
                    "Public routing remains locked until migration-keyed preview, confirmation, and readiness gates pass."
                ])
                .Distinct(StringComparer.Ordinal)
                .ToList()
        };

        candidate = await TryStartPrivateElementRuntimeAsync(candidate, ct);
        await _candidateHistory.SaveCandidateAsync(candidate, ct);

        _logger.LogInformation(
            "Adopted Migration staging runtime as private production candidate. MigrationId={MigrationId} CandidateId={CandidateId} PrivateRuntimeId={PrivateRuntimeId} Status={Status}",
            migrationId,
            candidate.CandidateId,
            candidate.PrivateRuntimeId,
            candidate.Status);

        return candidate;
    }

    public async Task<RuntimeStackBackupProductionCandidateResult> DestroyAsync(
        string candidateId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
        {
            throw new InvalidOperationException("Production Restore candidate id is required.");
        }

        if (!IsSafePathSegment(candidateId))
        {
            throw new InvalidOperationException("Production Restore candidate id contains unsafe characters.");
        }

        var existingResponse = await _candidateHistory.GetCandidateAsync(
            candidateId,
            ct);

        var existing = existingResponse?.Candidate;

        if (existing is null)
        {
            throw new FileNotFoundException($"Production Restore candidate '{candidateId}' was not found.");
        }

        if (existing.Destroy is not null ||
            string.Equals(existing.Status, "destroyed", StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        var warnings = new List<string>();
        var elementContainerRemoved = await RemoveElementContainerAsync(
            existing,
            warnings,
            ct);

        PrivateStagingRunResult destroyedPrivateRuntime;

        try
        {
            destroyedPrivateRuntime = await _stagingService.DestroyAsync(
                existing.PrivateRuntimeId,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Could not destroy private runtime '{existing.PrivateRuntimeId}': {ex.Message}");

            var failedDestroyResult = existing with
            {
                Warnings = existing.Warnings
                    .Concat(warnings)
                    .Distinct(StringComparer.Ordinal)
                    .ToList(),
                Detail = "Production Restore candidate destroy was requested, but private runtime destroy failed."
            };

            await _candidateHistory.SaveCandidateAsync(
                failedDestroyResult,
                ct);

            return failedDestroyResult;
        }

        var destroyWarnings = (destroyedPrivateRuntime.Destroy?.Warnings ?? [])
            .Concat(warnings)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var destroy = new RuntimeStackBackupProductionCandidateDestroySummary(
            DestroyedAtUtc: DateTimeOffset.UtcNow,
            PrivateRuntimeDestroyed: destroyedPrivateRuntime.Destroy is not null ||
                                     string.Equals(destroyedPrivateRuntime.Status, "destroyed", StringComparison.OrdinalIgnoreCase),
            ElementContainerRemoved: elementContainerRemoved,
            Warnings: destroyWarnings);

        var result = existing with
        {
            Status = "destroyed",
            FinishedAtUtc = DateTimeOffset.UtcNow,
            PrivateRuntimeStatus = destroyedPrivateRuntime.Status,
            Runtime = existing.Runtime with
            {
                ElementContainerStarted = false,
                ElementHealthPassed = false,
                ElementHealthResponse = elementContainerRemoved
                    ? "Element private candidate container was removed."
                    : existing.Runtime.ElementHealthResponse
            },
            Destroy = destroy,
            Detail = "Private Production Restore candidate resources were destroyed. DNS, NPM, certificates, and public routes were not touched."
        };

        await _candidateHistory.SaveCandidateAsync(
            result,
            ct);

        _logger.LogInformation(
            "Destroyed private Production Restore candidate. CandidateId={CandidateId} PrivateRuntimeId={PrivateRuntimeId} ElementRemoved={ElementRemoved}",
            result.CandidateId,
            result.PrivateRuntimeId,
            elementContainerRemoved);

        return result;
    }

    private async Task<RuntimeStackBackupProductionCandidateResult> TryStartPrivateElementRuntimeAsync(
        RuntimeStackBackupProductionCandidateResult candidate,
        CancellationToken ct)
    {
        if (!string.Equals(candidate.Status, "private_candidate_ready", StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        if (!candidate.Runtime.ElementConfigExtracted ||
            string.IsNullOrWhiteSpace(candidate.ElementDataPath))
        {
            return candidate;
        }

        var warnings = candidate.Warnings.ToList();
        var checks = candidate.Checks.ToList();

        var elementConfigPath = Path.Combine(
            candidate.ElementDataPath,
            "config.json");

        var elementContainerName = $"mem-restore-staging-element-{candidate.CandidateId}";
        string? elementContainerId = null;
        var elementConfigPatched = false;
        var elementContainerStarted = false;
        var elementHealthPassed = false;
        string? elementHealthResponse = null;
        string? elementLogsTail = null;

        try
        {
            PatchElementConfigForPrivateCandidate(
                elementConfigPath,
                candidate.MatrixServerName);

            elementConfigPatched = true;

            checks.Add(new RuntimeStackBackupProductionCandidateCheck(
                Code: "production-candidate.element-config.patched",
                Severity: "info",
                Passed: true,
                Message: "Element config.json was patched for the restored Matrix homeserver before private candidate startup.",
                Detail: elementConfigPath));

            elementContainerId = await _dockerHost.CreateContainerAsync(
                new DockerContainerSpec(
                    Name: elementContainerName,
                    Image: candidate.ElementImage
                        ?? throw new InvalidOperationException(
                            "The release-approved Element runtime identity is missing from the private staging evidence."),
                    Labels: BuildElementContainerLabels(candidate),
                    PortBindings: null,
                    BindMounts:
                    [
                        new BindMount(
                            HostPath: elementConfigPath,
                            ContainerPath: "/app/config.json",
                            ReadOnly: true)
                    ],
                    RestartPolicy: new DockerRestartPolicy(DockerRestartPolicyName.UnlessStopped),
                    NetworkName: candidate.NetworkName,
                    NetworkAliases:
                    [
                        "restore-staging-element",
                        "element"
                    ]),
                ct);

            await _dockerHost.StartContainerAsync(elementContainerId, ct);

            elementContainerStarted = true;

            checks.Add(new RuntimeStackBackupProductionCandidateCheck(
                Code: "production-candidate.element.started",
                Severity: "info",
                Passed: true,
                Message: "Private candidate Element container was started on the internal restore network with no host port binding.",
                Detail: elementContainerName));

            var health = await WaitForElementHealthAsync(
                elementContainerName,
                ct);

            elementHealthPassed = health.Passed;
            elementHealthResponse = health.Response;

            checks.Add(new RuntimeStackBackupProductionCandidateCheck(
                Code: "production-candidate.element.health",
                Severity: elementHealthPassed ? "info" : "warning",
                Passed: elementHealthPassed,
                Message: elementHealthPassed
                    ? "Private candidate Element container served config.json and static web content."
                    : "Private candidate Element container did not pass the static config/web health check.",
                Detail: elementHealthResponse));

            if (!elementHealthPassed)
            {
                warnings.Add("Private candidate Element container started, but did not pass the private static config/web health check. Matrix candidate remains available; Element route preview will stay deferred.");
            }

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Private candidate Element runtime could not be started or verified: {ex.Message}");

            checks.Add(new RuntimeStackBackupProductionCandidateCheck(
                Code: "production-candidate.element.runtime",
                Severity: "warning",
                Passed: false,
                Message: "Private candidate Element runtime could not be started or verified. Matrix candidate remains available.",
                Detail: ex.Message));

            if (!string.IsNullOrWhiteSpace(elementContainerId))
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
        }

        return candidate with
        {
            ElementContainerName = elementContainerName,
            ElementContainerId = elementContainerId,
            Runtime = candidate.Runtime with
            {
                ElementConfigPatched = elementConfigPatched,
                ElementContainerStarted = elementContainerStarted,
                ElementHealthPassed = elementHealthPassed,
                ElementHealthResponse = Trim(elementHealthResponse ?? string.Empty, 2000),
                ElementLogsTail = Trim(elementLogsTail ?? string.Empty, 8000)
            },
            Checks = checks,
            Warnings = warnings
                .Concat(
                [
                    elementHealthPassed
                        ? "Private candidate Element runtime is available for read-only public cutover preview. It has no host port and no public route."
                        : "Private candidate Element runtime is not available for executable public cutover preview yet."
                ])
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Detail = elementHealthPassed
                ? "Private Production Restore candidate is ready. Matrix and Element private candidate runtimes are available, private-only, and not publicly routed."
                : candidate.Detail
        };
    }

    private async Task<bool> RemoveElementContainerAsync(
        RuntimeStackBackupProductionCandidateResult existing,
        List<string> warnings,
        CancellationToken ct)
    {
        var elementContainerRef = FirstNonEmpty(
            existing.ElementContainerId,
            existing.ElementContainerName);

        if (string.IsNullOrWhiteSpace(elementContainerRef))
        {
            return false;
        }

        try
        {
            await _dockerHost.RemoveContainerAsync(
                elementContainerRef,
                force: true,
                ct);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Could not remove private candidate Element container '{existing.ElementContainerName ?? elementContainerRef}': {ex.Message}");
            return false;
        }
    }

    private async Task<PrivateElementHealthResult> WaitForElementHealthAsync(
        string elementContainerName,
        CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        string? lastOutput = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var result = await ExecInContainerAsync(
                    elementContainerName,
                    [
                        "sh",
                        "-c",
                        "(wget -qO- http://127.0.0.1/config.json || curl -fsS http://127.0.0.1/config.json) | grep -q 'default_server_config' && (wget -qO- http://127.0.0.1/ >/tmp/mem-element-index.out || curl -fsS http://127.0.0.1/ >/tmp/mem-element-index.out) && test -s /tmp/mem-element-index.out"
                    ],
                    ct,
                    throwOnNonZeroExit: false);

                lastOutput = Trim(result.Stderr + Environment.NewLine + result.Stdout, 2000);

                if (result.ExitCode == 0)
                {
                    return new PrivateElementHealthResult(
                        Passed: true,
                        Response: "Element config.json and index page responded inside the private container.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastOutput = ex.Message;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        return new PrivateElementHealthResult(
            Passed: false,
            Response: lastOutput);
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

        var inspect = await _docker.Exec.InspectContainerExecAsync(exec.ID, ct);

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

    private static RuntimeStackBackupProductionCandidateResult ToCandidateResult(
        PrivateStagingRunResult staging,
        string restoreMode,
        string targetStackSlug,
        string? elementImage)
    {
        var status = staging.Status switch
        {
            "ready" => "private_candidate_ready",
            "destroyed" => "destroyed",
            "failed-cleaned" => "failed-cleaned",
            "failed" => "failed",
            _ => staging.Status
        };

        var warnings = staging.Warnings
            .Concat(
            [
                "This private Production Restore candidate was created using the proven private restore-staging engine.",
                "It is not public production yet.",
                "No DNS, NPM route, certificate, or public cutover mutation has been performed."
            ])
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var safety = new RuntimeStackBackupProductionCandidateSafetySummary(
            PrivateOnly: staging.Safety.PrivateOnly,
            DockerNetworkInternal: staging.Safety.DockerNetworkInternal,
            PublicRoutesCreated: false,
            DnsChanged: false,
            CertificatesChanged: false,
            NpmRoutesChanged: false,
            ProductionContainersTouched: false,
            ProductionDatabasesTouched: false,
            PublicCutoverPerformed: false,
            ProductionExecutionLocked: true,
            RequiresExplicitDestroy: true,
            Notes:
            [
                "Private Production Restore candidate only.",
                "No NPM routes are created or changed.",
                "No DNS records are changed.",
                "No certificates are requested, imported, renewed, attached, or removed.",
                "No existing MEM runtime stack is stopped, deleted, replaced, or modified.",
                "No public cutover has been performed.",
                "Destroy this candidate explicitly if it is not promoted in a later gated sprint."
            ]);

        return new RuntimeStackBackupProductionCandidateResult(
            Source: "control-plane",
            Status: status,
            Mode: "recreate-production-private-candidate",
            CandidateId: staging.StagingId,
            // Candidate history remains validation-ID-shaped until the later
            // catalog-native candidate-history slice. An empty value means the
            // private staging source had no legacy validation receipt.
            ValidationId: staging.ValidationId ?? string.Empty,
            RestoreMode: restoreMode,
            StartedAtUtc: staging.StartedAtUtc,
            FinishedAtUtc: staging.FinishedAtUtc,
            TargetStackSlug: targetStackSlug,
            MatrixServerName: staging.MatrixServerName,
            PrivateRuntimeId: staging.StagingId,
            PrivateRuntimeStatus: staging.Status,
            WorkspacePath: staging.WorkspacePath,
            RuntimePath: staging.RuntimePath,
            MatrixDataPath: staging.MatrixDataPath,
            ElementDataPath: staging.ElementDataPath,
            NetworkName: staging.NetworkName,
            NetworkId: staging.NetworkId,
            PostgresContainerName: staging.PostgresContainerName,
            PostgresContainerId: staging.PostgresContainerId,
            SynapseContainerName: staging.SynapseContainerName,
            SynapseContainerId: staging.SynapseContainerId,
            ElementContainerName: null,
            ElementContainerId: null,
            ElementImage: elementImage,
            DatabaseName: staging.DatabaseName,
            DatabaseUser: staging.DatabaseUser,
            Safety: safety,
            Database: new RuntimeStackBackupProductionCandidateDatabaseSummary(
                ImportSucceeded: staging.Database.ImportSucceeded,
                PublicTableCount: staging.Database.PublicTableCount,
                SynapseKnownTableCount: staging.Database.SynapseKnownTableCount,
                UsersCount: staging.Database.UsersCount,
                EventsCount: staging.Database.EventsCount,
                RoomsCount: staging.Database.RoomsCount,
                StateEventsCount: staging.Database.StateEventsCount),
            Runtime: new RuntimeStackBackupProductionCandidateRuntimeSummary(
                HomeserverConfigExtracted: staging.Runtime.HomeserverConfigExtracted,
                HomeserverConfigPatched: staging.Runtime.HomeserverConfigPatched,
                SigningKeyExtracted: staging.Runtime.SigningKeyExtracted,
                MediaStoreExtracted: staging.Runtime.MediaStoreExtracted,
                MediaFiles: staging.Runtime.MediaFiles,
                MediaBytes: staging.Runtime.MediaBytes,
                ElementConfigExtracted: staging.Runtime.ElementConfigExtracted,
                ElementConfigPatched: false,
                ElementContainerStarted: false,
                ElementHealthPassed: false,
                ElementHealthResponse: null,
                ElementLogsTail: null,
                PostgresContainerStarted: staging.Runtime.PostgresContainerStarted,
                SynapseContainerStarted: staging.Runtime.SynapseContainerStarted,
                SynapseHealthPassed: staging.Runtime.SynapseHealthPassed,
                HealthResponse: staging.Runtime.HealthResponse,
                SynapseLogsTail: staging.Runtime.SynapseLogsTail),
            Checks: staging.Checks
                .Select(check => new RuntimeStackBackupProductionCandidateCheck(
                    Code: check.Code.Replace("restore-staging", "production-candidate", StringComparison.Ordinal),
                    Severity: check.Severity,
                    Passed: check.Passed,
                    Message: check.Message,
                    Detail: check.Detail))
                .ToList(),
            Warnings: warnings,
            Errors: staging.Errors,
            Destroy: staging.Destroy is null
                ? null
                : new RuntimeStackBackupProductionCandidateDestroySummary(
                    DestroyedAtUtc: staging.Destroy.DestroyedAtUtc,
                    PrivateRuntimeDestroyed: true,
                    ElementContainerRemoved: false,
                    Warnings: staging.Destroy.Warnings),
            Detail: status == "private_candidate_ready"
                ? "Private Production Restore candidate is ready. It is restored, private-only, health-checked, and not publicly routed."
                : "Private Production Restore candidate did not become ready. Inspect checks, errors, and Synapse logs.");
    }

    private static void PatchElementConfigForPrivateCandidate(
        string elementConfigPath,
        string? matrixServerName)
    {
        if (string.IsNullOrWhiteSpace(matrixServerName))
        {
            throw new InvalidOperationException("Matrix server_name is required before Element config can be patched.");
        }

        if (!File.Exists(elementConfigPath))
        {
            throw new FileNotFoundException(
                $"Element config.json was expected but not found: {elementConfigPath}");
        }

        var json = File.ReadAllText(elementConfigPath);
        var node = JsonNode.Parse(json) as JsonObject;

        if (node is null)
        {
            throw new InvalidDataException("Element config.json could not be parsed as a JSON object.");
        }

        var defaultServerConfig = node["default_server_config"] as JsonObject ?? new JsonObject();
        var homeserver = defaultServerConfig["m.homeserver"] as JsonObject ?? new JsonObject();

        homeserver["base_url"] = $"https://{matrixServerName.Trim().TrimEnd('/')}";
        homeserver["server_name"] = matrixServerName.Trim();
        defaultServerConfig["m.homeserver"] = homeserver;
        node["default_server_config"] = defaultServerConfig;
        node["default_server_name"] = matrixServerName.Trim();
        node["disable_custom_urls"] = false;

        File.WriteAllText(
            elementConfigPath,
            node.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            }));
    }

    private static Dictionary<string, string> BuildElementContainerLabels(
        RuntimeStackBackupProductionCandidateResult candidate)
    {
        var labels = new Dictionary<string, string>
        {
            ["mem.component"] = "production-restore-candidate",
            ["mem.restore-staging.id"] = candidate.PrivateRuntimeId,
            ["mem.production-candidate.id"] = candidate.CandidateId,
            ["mem.managed-by"] = "mem-host-agent",
            ["mem.service"] = "element"
        };

        if (!string.IsNullOrWhiteSpace(candidate.ValidationId))
        {
            labels["mem.production-candidate.validation-id"] = candidate.ValidationId;
        }

        if (!string.IsNullOrWhiteSpace(candidate.CatalogEntryId))
        {
            labels["mem.production-candidate.catalog-entry-id"] = candidate.CatalogEntryId;
        }

        if (!string.IsNullOrWhiteSpace(candidate.SourceKind))
        {
            labels["mem.production-candidate.source-kind"] = candidate.SourceKind;
        }

        return labels;
    }

    private static string ResolveSupportedRestoreMode(
        string? requestedRestoreMode)
    {
        var restoreMode = string.IsNullOrWhiteSpace(requestedRestoreMode)
            ? "recreate-production"
            : requestedRestoreMode.Trim();

        var allowedRestoreMode =
            string.Equals(restoreMode, "recreate-production", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(restoreMode, "new-production-from-backup", StringComparison.OrdinalIgnoreCase);

        if (!allowedRestoreMode)
        {
            throw new InvalidOperationException(
                $"Production candidate creation only supports recreate-production mode in this sprint. Requested mode: {restoreMode}");
        }

        return restoreMode;
    }

    private static string? FirstNonEmpty(
        params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
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
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment != "." && segment != "..");
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

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug.Trim('-'))
            ? "production-candidate"
            : slug.Trim('-');
    }

    private static string Trim(string value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= max
            ? value
            : value[..max] + "...";
    }

    private sealed record ExecResult(long ExitCode, string Stdout, string Stderr);
    private sealed record PrivateElementHealthResult(bool Passed, string? Response);
}
