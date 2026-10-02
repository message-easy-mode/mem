using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.AdvancedCutover.Confirmation.Catalog;
using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Ingress;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Readiness;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Execution.Catalog;

/// <summary>
/// Executes a catalog-backed public route transition only after a fresh
/// catalogue plan, candidate health, certificate, route-ownership, final-backup,
/// and explicit operator gates have all passed. It stops the old runtime but
/// intentionally retains its containers, database, files, and manifest as the
/// rollback material. It never changes DNS or certificate inventory.
/// </summary>
public sealed class CatalogPublicCutoverExecutionService
{
    private readonly CatalogPublicCutoverConfirmationHistoryService _confirmationHistory;
    private readonly RuntimeStackBackupPublicCutoverPreviewHistoryService _previewHistory;
    private readonly RuntimeStackBackupProductionCandidateHistoryService _candidateHistory;
    private readonly CatalogProductionRestorePlanService _planService;
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly LocalBackupCaptureService _backupCaptureService;
    private readonly RuntimeReadinessVerifier _readinessVerifier;
    private readonly IRoutePublisher _routePublisher;
    private readonly DockerClient _docker;
    private readonly CatalogPublicCutoverExecutionHistoryService _executionHistory;
    private readonly ILogger<CatalogPublicCutoverExecutionService> _logger;

    public CatalogPublicCutoverExecutionService(
        CatalogPublicCutoverConfirmationHistoryService confirmationHistory,
        RuntimeStackBackupPublicCutoverPreviewHistoryService previewHistory,
        RuntimeStackBackupProductionCandidateHistoryService candidateHistory,
        CatalogProductionRestorePlanService planService,
        RuntimeStackManifestStore manifestStore,
        LocalBackupCaptureService backupCaptureService,
        RuntimeReadinessVerifier readinessVerifier,
        IRoutePublisher routePublisher,
        DockerClient docker,
        CatalogPublicCutoverExecutionHistoryService executionHistory,
        ILogger<CatalogPublicCutoverExecutionService> logger)
    {
        _confirmationHistory = confirmationHistory;
        _previewHistory = previewHistory;
        _candidateHistory = candidateHistory;
        _planService = planService;
        _manifestStore = manifestStore;
        _backupCaptureService = backupCaptureService;
        _readinessVerifier = readinessVerifier;
        _routePublisher = routePublisher;
        _docker = docker;
        _executionHistory = executionHistory;
        _logger = logger;
    }

    public async Task<CatalogPublicCutoverExecutionResponse> ExecuteAsync(
        string catalogEntryId,
        CatalogPublicCutoverExecutionRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidatePathSegment(catalogEntryId, "Backup Catalog entry id");
        ValidatePathSegment(request.ConfirmationId, "Catalog cutover confirmation id");
        ValidatePathSegment(request.CandidateId, "Production candidate id");
        ValidatePathSegment(request.OldRuntimeStackSlug, "Old runtime stack slug");

        var startedAtUtc = DateTimeOffset.UtcNow;
        var executionId = CreateExecutionId();
        var checks = new List<CatalogPublicCutoverExecutionCheck>();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();
        var acknowledgements = BuildAcknowledgements(request);

        var confirmationResponse = await _confirmationHistory.GetConfirmationAsync(
            catalogEntryId,
            request.ConfirmationId,
            ct);
        var confirmation = confirmationResponse?.Confirmation
            ?? throw new FileNotFoundException($"Catalog cutover confirmation '{request.ConfirmationId}' was not found for Backup Catalog entry '{catalogEntryId}'.");

        var previewResponse = await _previewHistory.GetPreviewAsync(confirmation.PreviewId, ct);
        var preview = previewResponse?.Preview
            ?? throw new FileNotFoundException($"Catalog cutover preview '{confirmation.PreviewId}' was not found.");

        var candidateResponse = await _candidateHistory.GetCandidateAsync(request.CandidateId, ct);
        var candidate = candidateResponse?.Candidate
            ?? throw new FileNotFoundException($"Catalog production candidate '{request.CandidateId}' was not found.");

        var sessionPlan = await _planService.CreatePlanAsync(
            catalogEntryId,
            new CatalogProductionRestorePlanRequest(
                StagingId: null,
                CandidateId: request.CandidateId,
                TargetStackSlug: preview.Candidate.TargetStackSlug,
                RestoreMode: confirmation.RestoreMode,
                IntendedMatrixHost: preview.Routes.Matrix.Host,
                IntendedElementHost: preview.Routes.Element.Host),
            ct);

        var plan = sessionPlan.Plan;
        var oldRuntime = await _manifestStore.FindAsync(request.OldRuntimeStackSlug, ct);

        ValidateLinkage(catalogEntryId, confirmation, preview, candidate, sessionPlan.RestoreSessionId, checks, blockers);
        ValidateAcknowledgements(request, acknowledgements, checks, blockers);
        ValidateFreshPlan(plan, catalogEntryId, checks, blockers);

        var candidateSummary = await BuildCandidateSummaryAsync(candidate, catalogEntryId, sessionPlan.RestoreSessionId, checks, blockers, ct);
        var oldRuntimeSummary = ValidateOldRuntimeOwnership(oldRuntime, candidate, preview, plan, checks, blockers);
        var ingressIntent = BuildIngressIntent(candidate, preview.Candidate.TargetStackSlug);

        var backupSummary = new CatalogPublicCutoverExecutionBackupSummary(
            Required: true,
            CaptureAttempted: false,
            Captured: false,
            BackupId: null,
            BackupCatalogEntryId: null,
            BackupRootPath: null,
            Detail: "Final pre-cutover backup has not been captured.");

        var ingressSummary = BuildSkippedIngress(ingressIntent, "Cutover ingress was not prepared because execution gates have not passed.");
        var matrixRoute = BuildSkippedRoute("matrix", preview.Routes.Matrix, plan.Npm.MatrixRoute, "Matrix route was not changed.");
        var elementRoute = BuildSkippedRoute("element", preview.Routes.Element, plan.Npm.ElementRoute, "Element route was not changed.");
        var verification = new CatalogPublicCutoverExecutionPublicVerificationSummary(false, false, [], "Public verification was not attempted.");
        var rollback = new CatalogPublicCutoverExecutionRollbackSummary(false, false, false, false, false, false, false, [], "Rollback was not required.");
        var oldMatrixStopped = false;
        var oldElementStopped = false;

        var priorCompleted = await _executionHistory.FindLatestSuccessfulAsync(
            catalogEntryId,
            confirmation.ConfirmationId,
            candidate.CandidateId,
            ct);

        if (priorCompleted is not null)
        {
            warnings.Add($"Catalog public cutover was already completed by execution '{priorCompleted.ExecutionId}'. No repeat mutation was performed.");
            var already = priorCompleted with
            {
                Status = "already_completed",
                Detail = "An earlier catalog-backed public cutover execution already completed for this confirmation and candidate. No repeat mutation was performed."
            };

            return new CatalogPublicCutoverExecutionResponse(
                "control-plane", already.Status, catalogEntryId, sessionPlan.RestoreSessionId,
                sessionPlan.RestoreAttemptCreated, sessionPlan.RestoreAttemptResumed,
                confirmation.ConfirmationId, candidate.CandidateId,
                sessionPlan.PayloadState, sessionPlan.IntegrityStatus, sessionPlan.WarningCount,
                already, already.Detail!);
        }

        if (!request.Execute)
        {
            blockers.Add("execute must be true before this endpoint can capture a final backup, stop an old runtime, or mutate public routes.");
        }
        else if (request.GateEvaluationOnly)
        {
            blockers.Add("gateEvaluationOnly was requested. Preconditions were evaluated, but final backup capture, runtime retirement, NPM route mutation, and public verification are intentionally disabled.");
            AddCheck(
                checks,
                "catalog-public-cutover-execution.gate-evaluation-only",
                "execution-gate",
                "info",
                true,
                "evaluated",
                "Execution gates were evaluated with execute=true, but the explicit gate-only safety switch prevented all mutation.",
                null);
        }

        if (blockers.Count == 0 && errors.Count == 0)
        {
            try
            {
                var finalBackup = await _backupCaptureService.BackupAsync(oldRuntime!.Slug, ct);
                backupSummary = new CatalogPublicCutoverExecutionBackupSummary(
                    Required: true,
                    CaptureAttempted: true,
                    Captured: true,
                    BackupId: finalBackup.BackupId,
                    BackupCatalogEntryId: finalBackup.CatalogEntryId,
                    BackupRootPath: finalBackup.BackupRootPath,
                    Detail: finalBackup.CatalogEntryId is null
                        ? "Final local backup was captured, but Backup Catalog registration is pending. Cutover was not started."
                        : "Final local backup was captured and registered in the Backup Catalog.");

                warnings.AddRange(finalBackup.Warnings);
                AddCheck(checks, "catalog-public-cutover-execution.final-backup", "backup", "info", true, "satisfied", "Final pre-cutover backup was captured before public mutation.", finalBackup.BackupId);

                if (string.IsNullOrWhiteSpace(finalBackup.CatalogEntryId))
                {
                    blockers.Add("Final pre-cutover backup was captured but Backup Catalog registration did not complete. Resolve the registration gap before public cutover.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"Final pre-cutover backup failed: {ex.Message}");
                backupSummary = backupSummary with { CaptureAttempted = true, Detail = ex.Message };
                AddCheck(checks, "catalog-public-cutover-execution.final-backup", "backup", "error", false, "failed", "Final pre-cutover backup could not be captured.", ex.Message);
            }
        }

        if (blockers.Count == 0 && errors.Count == 0)
        {
            // Last moment rechecks occur after the final backup, immediately before old runtime stop and route mutation.
            candidateSummary = await BuildCandidateSummaryAsync(candidate, catalogEntryId, sessionPlan.RestoreSessionId, checks, blockers, ct);
            var immediatePlanResponse = await _planService.CreatePlanAsync(
                catalogEntryId,
                new CatalogProductionRestorePlanRequest(
                    StagingId: null,
                    CandidateId: request.CandidateId,
                    TargetStackSlug: preview.Candidate.TargetStackSlug,
                    RestoreMode: confirmation.RestoreMode,
                    IntendedMatrixHost: preview.Routes.Matrix.Host,
                    IntendedElementHost: preview.Routes.Element.Host),
                ct);
            plan = immediatePlanResponse.Plan;
            ValidateFreshPlan(plan, catalogEntryId, checks, blockers);
            oldRuntimeSummary = ValidateOldRuntimeOwnership(oldRuntime, candidate, preview, plan, checks, blockers);
        }

        if (blockers.Count == 0 && errors.Count == 0)
        {
            try
            {
                oldMatrixStopped = await StopIfRunningAsync(oldRuntime!.Matrix.ContainerName, ct);
                oldElementStopped = oldRuntime.Element is null || await StopIfRunningAsync(oldRuntime.Element.ContainerName, ct);
                if (!oldMatrixStopped || !oldElementStopped)
                {
                    throw new InvalidOperationException("One or more old runtime containers could not be confirmed stopped.");
                }

                oldRuntimeSummary = oldRuntimeSummary with
                {
                    MatrixContainerStopped = oldMatrixStopped,
                    ElementContainerStopped = oldElementStopped,
                    RetainedForRollback = true,
                    Detail = "Old runtime containers were stopped but not removed. Database, files, manifest, and containers remain available for manual rollback."
                };

                AddCheck(checks, "catalog-public-cutover-execution.old-runtime.stopped", "retirement", "info", true, "satisfied", "Old public runtime was stopped and retained for rollback.", oldRuntime!.Slug);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"Could not stop old runtime: {ex.Message}");
                AddCheck(checks, "catalog-public-cutover-execution.old-runtime.stopped", "retirement", "error", false, "failed", "Old runtime could not be stopped; no route mutation was attempted.", ex.Message);
            }
        }

        if (blockers.Count == 0 && errors.Count == 0)
        {
            try
            {
                ingressSummary = await EnsureIngressAsync(ingressIntent, ct);
                AddCheck(checks, "catalog-public-cutover-execution.cutover-ingress", "docker-network", ingressSummary.Ready ? "info" : "blocker", ingressSummary.Ready, ingressSummary.Ready ? "satisfied" : "blocked", "Candidate cutover ingress network was prepared without attaching Postgres.", ingressSummary.NetworkName);
                if (!ingressSummary.Ready)
                {
                    blockers.Add("Candidate cutover ingress network is not ready for NPM routing.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"Could not prepare candidate cutover ingress network: {ex.Message}");
            }
        }

        if (blockers.Count == 0 && errors.Count == 0)
        {
            try
            {
                matrixRoute = await PublishRouteAsync("matrix", preview.Routes.Matrix, plan.Npm.MatrixRoute, ingressSummary.MatrixAlias!, RouteKind.Matrix, true, ct);
                if (!matrixRoute.Succeeded)
                {
                    errors.Add(matrixRoute.Detail ?? "Matrix route mutation failed.");
                }

                if (errors.Count == 0)
                {
                    elementRoute = await PublishRouteAsync("element", preview.Routes.Element, plan.Npm.ElementRoute, ingressSummary.ElementAlias!, RouteKind.ElementWeb, !string.IsNullOrWhiteSpace(preview.Routes.Element.Host), ct);
                    if (!elementRoute.Succeeded)
                    {
                        errors.Add(elementRoute.Detail ?? "Element route mutation failed.");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"Public route mutation failed: {ex.Message}");
            }
        }

        if (blockers.Count == 0 && errors.Count == 0 && matrixRoute.Succeeded && elementRoute.Succeeded)
        {
            try
            {
                verification = await VerifyPublicAsync(preview, ingressSummary, plan, ct);
                foreach (var check in verification.Checks)
                {
                    AddCheck(checks, $"catalog-public-cutover-execution.public.{check.Code}", "public-verification", check.Passed ? "info" : "error", check.Passed, check.Passed ? "satisfied" : "failed", check.Code, check.Detail);
                }

                if (!verification.Passed)
                {
                    errors.Add("Post-cutover public verification did not pass.");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"Post-cutover public verification failed: {ex.Message}");
            }
        }

        var anyRouteMutated = matrixRoute.Mutated || elementRoute.Mutated;
        if (errors.Count > 0 && (anyRouteMutated || oldMatrixStopped || oldElementStopped))
        {
            rollback = await TryRollbackAsync(
                oldRuntime!,
                matrixRoute,
                elementRoute,
                oldMatrixStopped,
                oldElementStopped,
                ct);

            warnings.AddRange(rollback.Warnings);
        }

        var status = errors.Count > 0
            ? rollback.Attempted && rollback.Completed ? "failed_rolled_back" : "failed_requires_operator"
            : blockers.Count > 0 ? "blocked" : "completed";

        var mutations = new RuntimeStackBackupProductionRestoreMutationSummary(
            RuntimeChanged: ingressSummary.NetworkCreated || ingressSummary.NpmAttached || ingressSummary.MatrixAttached || ingressSummary.ElementAttached || oldMatrixStopped || oldElementStopped,
            ProductionContainersTouched: oldMatrixStopped || oldElementStopped,
            ProductionDatabasesTouched: false,
            DnsChanged: false,
            NpmRoutesChanged: anyRouteMutated,
            CertificatesChanged: false,
            PublicRoutesChanged: anyRouteMutated,
            FederationExposureChanged: matrixRoute.Mutated,
            Notes:
            [
                "Final pre-cutover local backup was captured before public mutation when execution gates passed.",
                "Old runtime containers were stopped but intentionally not deleted; data and rollback material were retained.",
                "DNS and certificate inventory were not changed.",
                "A failed route transition triggers a best-effort rollback to saved previous NPM upstreams and a restart of old runtime containers."
            ]);

        var routes = new CatalogPublicCutoverExecutionRoutesSummary(
            Matrix: matrixRoute,
            Element: elementRoute,
            AllRequiredRoutesSucceeded: matrixRoute.Succeeded && elementRoute.Succeeded,
            AnyRouteMutated: anyRouteMutated,
            Detail: status == "completed" ? "Matrix and Element routes now target the private candidate through the per-candidate cutover ingress network." : "Route transition did not complete successfully.");

        var result = new CatalogPublicCutoverExecutionResult(
            Source: "control-plane",
            Status: status,
            ExecutionId: executionId,
            CatalogEntryId: catalogEntryId,
            SourceKind: "backup-catalog",
            RestoreSessionId: sessionPlan.RestoreSessionId,
            ConfirmationId: confirmation.ConfirmationId,
            PreviewId: preview.PreviewId,
            CandidateId: candidate.CandidateId,
            OldRuntimeStackSlug: request.OldRuntimeStackSlug,
            FreshPlanId: plan.PlanId,
            StartedAtUtc: startedAtUtc,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            Operator: NormalizeOptional(request.Operator),
            Note: NormalizeOptional(request.Note),
            ExecutionRequested: request.Execute,
            ProductionExecutionLocked: false,
            RuntimePromotionLocked: true,
            DnsMutationLocked: true,
            CertificateMutationLocked: true,
            Mutations: mutations,
            FinalBackup: backupSummary,
            Candidate: candidateSummary,
            OldRuntime: oldRuntimeSummary,
            CutoverIngress: ingressSummary,
            Routes: routes,
            PublicVerification: verification,
            Rollback: rollback,
            Acknowledgements: acknowledgements,
            Checks: checks,
            Blockers: blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings: warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Errors: errors.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Detail: BuildDetail(status, rollback));

        await _executionHistory.SaveAsync(result, ct);
        _logger.LogWarning("Catalog cutover execution finished. ExecutionId={ExecutionId} CatalogEntryId={CatalogEntryId} CandidateId={CandidateId} Status={Status}", result.ExecutionId, result.CatalogEntryId, result.CandidateId, result.Status);

        return new CatalogPublicCutoverExecutionResponse(
            Source: "control-plane",
            Status: result.Status,
            CatalogEntryId: catalogEntryId,
            RestoreSessionId: sessionPlan.RestoreSessionId,
            RestoreAttemptCreated: sessionPlan.RestoreAttemptCreated,
            RestoreAttemptResumed: sessionPlan.RestoreAttemptResumed,
            ConfirmationId: confirmation.ConfirmationId,
            CandidateId: candidate.CandidateId,
            PayloadState: sessionPlan.PayloadState,
            IntegrityStatus: sessionPlan.IntegrityStatus,
            WarningCount: sessionPlan.WarningCount,
            Execution: result,
            Detail: result.Detail ?? "Catalog public cutover execution completed.");
    }

    private static void ValidateLinkage(
        string catalogEntryId,
        CatalogPublicCutoverConfirmationResult confirmation,
        RuntimeStackBackupPublicCutoverPreviewResult preview,
        RuntimeStackBackupProductionCandidateResult candidate,
        string restoreSessionId,
        List<CatalogPublicCutoverExecutionCheck> checks,
        List<string> blockers)
    {
        var valid = string.Equals(confirmation.SourceKind, "backup-catalog", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(confirmation.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(preview.SourceKind, "backup-catalog", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(preview.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.SourceKind, "backup-catalog", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(confirmation.PreviewId, preview.PreviewId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(confirmation.CandidateId, candidate.CandidateId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.RestoreSessionId, restoreSessionId, StringComparison.OrdinalIgnoreCase);

        AddCheck(checks, "catalog-public-cutover-execution.linkage", "catalog-linkage", valid ? "info" : "blocker", valid, valid ? "satisfied" : "blocked", valid ? "Confirmation, preview, candidate, and restore session are linked to the requested Backup Catalog entry." : "Confirmation, preview, candidate, and restore session are not consistently linked.", confirmation.ConfirmationId);
        if (!valid)
        {
            blockers.Add("Confirmation, preview, candidate, and active restore session must all be linked to the requested Backup Catalog entry.");
        }

        if (confirmation.Errors.Count > 0)
        {
            blockers.Add("Saved catalog cutover confirmation contains errors and cannot authorize execution.");
        }
    }

    private static void ValidateAcknowledgements(
        CatalogPublicCutoverExecutionRequest request,
        IReadOnlyList<CatalogPublicCutoverExecutionAcknowledgement> acknowledgements,
        List<CatalogPublicCutoverExecutionCheck> checks,
        List<string> blockers)
    {
        var missing = acknowledgements.Where(x => x.Required && !x.Acknowledged).ToArray();
        var passed = missing.Length == 0;
        AddCheck(checks, "catalog-public-cutover-execution.acknowledgements", "acknowledgement", passed ? "info" : "blocker", passed, passed ? "satisfied" : "blocked", passed ? "All explicit execution acknowledgements were supplied." : "One or more explicit execution acknowledgements are missing.", passed ? null : string.Join(", ", missing.Select(x => x.Code)));
        foreach (var acknowledgement in missing)
        {
            blockers.Add($"Required cutover execution acknowledgement missing: {acknowledgement.Label}");
        }
    }

    private static void ValidateFreshPlan(
        RuntimeStackBackupProductionRestorePlanResult plan,
        string catalogEntryId,
        List<CatalogPublicCutoverExecutionCheck> checks,
        List<string> blockers)
    {
        var sourceMatches = string.Equals(plan.SourceKind, "backup-catalog", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(plan.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase);
        var certificatesReady = CertificateReady(plan.Certificates.MatrixCertificate) && CertificateReady(plan.Certificates.ElementCertificate);
        var planWithoutErrors = plan.Errors.Count == 0 && plan.Blockers.Count == 0 && sourceMatches && certificatesReady;

        AddCheck(checks, "catalog-public-cutover-execution.fresh-plan", "production-plan", planWithoutErrors ? "info" : "blocker", planWithoutErrors, planWithoutErrors ? "satisfied" : "blocked", planWithoutErrors ? "Fresh catalog plan confirms source identity and certificate readiness." : "Fresh catalog plan does not confirm source identity, certificate readiness, or a clean blocker/error state.", plan.PlanId);
        if (!planWithoutErrors)
        {
            blockers.Add("Fresh catalog production plan must remain source-linked, blocker/error-free, and certificate-ready immediately before execution.");
        }
    }

    private async Task<CatalogPublicCutoverExecutionCandidateSummary> BuildCandidateSummaryAsync(
        RuntimeStackBackupProductionCandidateResult candidate,
        string catalogEntryId,
        string restoreSessionId,
        List<CatalogPublicCutoverExecutionCheck> checks,
        List<string> blockers,
        CancellationToken ct)
    {
        var catalogMatches = string.Equals(candidate.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase);
        var sessionMatches = string.Equals(candidate.RestoreSessionId, restoreSessionId, StringComparison.OrdinalIgnoreCase);
        var synapseRunning = await IsRunningAsync(candidate.SynapseContainerName, ct);
        var elementRunning = !string.IsNullOrWhiteSpace(candidate.ElementContainerName) && await IsRunningAsync(candidate.ElementContainerName, ct);
        var synapseProbePassed = synapseRunning && await ProbeContainerAsync(
            candidate.SynapseContainerName,
            ["python3", "-c", "import urllib.request; print(urllib.request.urlopen('http://127.0.0.1:8008/health', timeout=3).read().decode('utf-8', 'replace'))"],
            ct);
        var elementProbePassed = elementRunning && await ProbeContainerAsync(
            candidate.ElementContainerName!,
            ["wget", "-qO-", "http://127.0.0.1/config.json"],
            ct);
        var privateHealthy = candidate.Safety.PrivateOnly && candidate.Safety.DockerNetworkInternal &&
            !candidate.Safety.PublicRoutesCreated && !candidate.Safety.PublicCutoverPerformed &&
            candidate.Database.ImportSucceeded && candidate.Runtime.SynapseHealthPassed && candidate.Runtime.ElementHealthPassed &&
            synapseRunning && elementRunning && synapseProbePassed && elementProbePassed;

        AddCheck(checks, "catalog-public-cutover-execution.candidate-health", "candidate", privateHealthy ? "info" : "blocker", privateHealthy, privateHealthy ? "satisfied" : "blocked", privateHealthy ? "Candidate remains private, running, and healthy immediately before cutover." : "Candidate is not currently private, running, and healthy.", candidate.CandidateId);
        if (!privateHealthy || !catalogMatches || !sessionMatches)
        {
            blockers.Add("Catalog candidate must remain source-linked, private-only, Docker-running, database-imported, and Matrix/Element healthy immediately before execution.");
        }

        return new CatalogPublicCutoverExecutionCandidateSummary(
            Found: true,
            CatalogEntryMatches: catalogMatches,
            RestoreSessionMatches: sessionMatches,
            SynapseRunning: synapseRunning,
            ElementRunning: elementRunning,
            SynapseHealthPassed: candidate.Runtime.SynapseHealthPassed,
            ElementHealthPassed: candidate.Runtime.ElementHealthPassed,
            PrivateOnly: candidate.Safety.PrivateOnly,
            SynapseContainerName: candidate.SynapseContainerName,
            ElementContainerName: candidate.ElementContainerName,
            Detail: privateHealthy
                ? candidate.Detail
                : "One or more immediate candidate container health probes did not pass.");
    }

    private async Task<bool> ProbeContainerAsync(
        string containerName,
        IReadOnlyList<string> command,
        CancellationToken ct)
    {
        try
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
            _ = await stream.ReadOutputToEndAsync(ct);
            var inspected = await _docker.Exec.InspectContainerExecAsync(exec.ID, ct);
            return inspected.ExitCode == 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Catalog cutover candidate health probe failed for container {ContainerName}", containerName);
            return false;
        }
    }

    private static CatalogPublicCutoverExecutionOldRuntimeSummary ValidateOldRuntimeOwnership(
        RuntimeStackManifest? oldRuntime,
        RuntimeStackBackupProductionCandidateResult candidate,
        RuntimeStackBackupPublicCutoverPreviewResult preview,
        RuntimeStackBackupProductionRestorePlanResult plan,
        List<CatalogPublicCutoverExecutionCheck> checks,
        List<string> blockers)
    {
        if (oldRuntime is null)
        {
            AddCheck(checks, "catalog-public-cutover-execution.old-runtime", "retirement", "blocker", false, "blocked", "Specified old runtime stack was not found.", null);
            blockers.Add("The explicitly selected old runtime stack was not found.");
            return new CatalogPublicCutoverExecutionOldRuntimeSummary(false, null, null, null, null, null, false, false, false, false, false, "Old runtime stack was not found.");
        }

        var serverMatches = string.Equals(oldRuntime.Matrix.ServerName, candidate.MatrixServerName, StringComparison.OrdinalIgnoreCase);
        var hostMatches = string.Equals(NormalizeHost(oldRuntime.Matrix.PublicHost), NormalizeHost(preview.Routes.Matrix.Host), StringComparison.OrdinalIgnoreCase);
        var matrixRouteOwned = RouteOwnedByService(plan.Npm.MatrixRoute, oldRuntime.Matrix, 8008);
        var elementRouteOwned = preview.Routes.Element.RouteCurrentlyExists
            ? oldRuntime.Element is not null && RouteOwnedByService(plan.Npm.ElementRoute, oldRuntime.Element, 80)
            : true;
        var valid = serverMatches && hostMatches && matrixRouteOwned && elementRouteOwned && !string.IsNullOrWhiteSpace(oldRuntime.Matrix.ContainerName);

        AddCheck(checks, "catalog-public-cutover-execution.old-runtime-ownership", "retirement", valid ? "info" : "blocker", valid, valid ? "satisfied" : "blocked", valid ? "Existing public Matrix route is owned by the explicitly selected old runtime and can be stopped while retained for rollback." : "Selected old runtime does not prove ownership of the currently public route.", oldRuntime.Slug);
        if (!valid)
        {
            blockers.Add("Selected old runtime must own the current Matrix public route, match the candidate Matrix server_name, and remain identifiable by manifest before a controlled route transition.");
        }

        return new CatalogPublicCutoverExecutionOldRuntimeSummary(
            true, oldRuntime.Slug, oldRuntime.Matrix.ServerName, oldRuntime.Matrix.PublicHost,
            oldRuntime.Matrix.ContainerName, oldRuntime.Element?.ContainerName,
            matrixRouteOwned, elementRouteOwned, false, false, false,
            valid ? "Old runtime route ownership was confirmed. It will be stopped but not deleted after final backup capture." : "Old runtime ownership could not be confirmed.");
    }

    private async Task<CatalogPublicCutoverExecutionIngressSummary> EnsureIngressAsync(IngressIntent intent, CancellationToken ct)
    {
        var networkCreated = false;
        if (!await NetworkExistsAsync(intent.NetworkName, ct))
        {
            try
            {
                await _docker.Networks.CreateNetworkAsync(new NetworksCreateParameters
                {
                    Name = intent.NetworkName,
                    Driver = "bridge",
                    Internal = false,
                    CheckDuplicate = true,
                    Labels = new Dictionary<string, string>
                    {
                        ["mem.component"] = "catalog-public-cutover-ingress",
                        ["mem.production-candidate.id"] = intent.CandidateId,
                        ["mem.managed-by"] = "mem-host-agent"
                    }
                }, ct);
                networkCreated = true;
            }
            catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                // Another safe retry created the same deterministic network.
            }
        }

        var npmAttached = await EnsureNetworkAttachmentAsync(intent.NetworkName, intent.NpmContainerName, [intent.NpmContainerName], ct);
        var matrixAttached = await EnsureNetworkAttachmentAsync(intent.NetworkName, intent.MatrixContainerName, [intent.MatrixAlias, "cutover-matrix"], ct);
        var elementAttached = await EnsureNetworkAttachmentAsync(intent.NetworkName, intent.ElementContainerName, [intent.ElementAlias, "cutover-element"], ct);
        var postgresAttached = !string.IsNullOrWhiteSpace(intent.PostgresContainerName) && await IsAttachedAsync(intent.NetworkName, intent.PostgresContainerName!, ct);
        var ready = npmAttached && matrixAttached && elementAttached && !postgresAttached;

        return new CatalogPublicCutoverExecutionIngressSummary(
            intent.NetworkName, networkCreated, npmAttached, matrixAttached, elementAttached, postgresAttached, ready,
            intent.MatrixAlias, intent.ElementAlias,
            ready ? "NPM and the candidate Matrix/Element containers share the cutover ingress network; candidate Postgres remains private." : "Cutover ingress network is not safe or ready.");
    }

    private async Task<CatalogPublicCutoverExecutionRouteResult> PublishRouteAsync(
        string component,
        RuntimeStackBackupPublicCutoverPreviewRouteAction previewRoute,
        RuntimeStackBackupProductionRestoreNpmRouteSummary? previousRoute,
        string desiredForwardHost,
        RouteKind kind,
        bool required,
        CancellationToken ct)
    {
        if (!required)
        {
            return new CatalogPublicCutoverExecutionRouteResult(component, null, null, null, null, null, null, null, false, false, true, "not_required", null, "No public host was requested for this component.");
        }

        if (string.IsNullOrWhiteSpace(previewRoute.Host) || previewRoute.UpstreamPort is not > 0 || previewRoute.NpmCertificateId is not > 0)
        {
            return new CatalogPublicCutoverExecutionRouteResult(component, previewRoute.Host, previousRoute?.ForwardHost, previousRoute?.ForwardPort, previousRoute?.NpmCertificateId, desiredForwardHost, previewRoute.UpstreamPort, previewRoute.NpmCertificateId, true, false, false, "blocked", null, "Route host, candidate port, or NPM certificate id is missing.");
        }

        var published = await _routePublisher.EnsureAsync(new RoutePublishRequest(
            Domain: previewRoute.Host,
            ForwardHost: desiredForwardHost,
            ForwardPort: previewRoute.UpstreamPort.Value,
            Kind: kind,
            ForwardScheme: string.IsNullOrWhiteSpace(previewRoute.ForwardScheme) ? "http" : previewRoute.ForwardScheme,
            IsPublic: true,
            RequireSsl: true,
            CertificateId: previewRoute.NpmCertificateId,
            ForceSsl: true,
            Http2: true), ct);

        var succeeded = published.Ready && published.SslConfigured &&
            string.Equals(NormalizeHost(published.ForwardHost), NormalizeHost(desiredForwardHost), StringComparison.OrdinalIgnoreCase) &&
            published.ForwardPort == previewRoute.UpstreamPort;

        return new CatalogPublicCutoverExecutionRouteResult(
            component, previewRoute.Host, previousRoute?.ForwardHost, previousRoute?.ForwardPort, previousRoute?.NpmCertificateId,
            desiredForwardHost, previewRoute.UpstreamPort, previewRoute.NpmCertificateId,
            true, true, succeeded, succeeded ? "completed" : "failed", published.RouteId,
            succeeded ? "NPM route now targets the candidate cutover ingress alias." : published.Warning ?? "NPM route did not report the expected ready state.");
    }

    private async Task<CatalogPublicCutoverExecutionPublicVerificationSummary> VerifyPublicAsync(
        RuntimeStackBackupPublicCutoverPreviewResult preview,
        CatalogPublicCutoverExecutionIngressSummary ingress,
        RuntimeStackBackupProductionRestorePlanResult plan,
        CancellationToken ct)
    {
        var matrixHost = preview.Routes.Matrix.Host ?? throw new InvalidOperationException("Matrix host is required for public verification.");
        var elementHost = preview.Routes.Element.Host ?? throw new InvalidOperationException("Element host is required for public verification.");
        var certificateId = plan.Certificates.MatrixCertificate?.NpmCertificateId ?? preview.Routes.Matrix.NpmCertificateId;
        var verification = await _readinessVerifier.VerifyAsync(new RuntimeReadinessVerificationRequest(
            MatrixInternalBaseUrl: $"http://{ingress.MatrixAlias}:8008",
            ElementInternalBaseUrl: $"http://{ingress.ElementAlias}",
            MatrixPublicBaseUrl: $"https://{matrixHost}",
            ElementPublicBaseUrl: $"https://{elementHost}",
            MatrixPublicHost: matrixHost,
            ElementPublicHost: elementHost,
            MatrixForwardHost: ingress.MatrixAlias!,
            MatrixForwardPort: 8008,
            ElementForwardHost: ingress.ElementAlias!,
            ElementForwardPort: 80,
            ExpectedNpmCertificateId: certificateId), ct);

        var checks = verification.Checks.Select(x => new CatalogPublicCutoverExecutionPublicCheck(x.Code, x.Success, x.Success ? x.Detail : $"{x.Detail} {x.BodyPreview}".Trim())).ToArray();
        return new CatalogPublicCutoverExecutionPublicVerificationSummary(true, verification.AllPassed, checks, verification.AllPassed ? "Public Matrix and Element checks passed through NPM." : "One or more post-cutover public verification checks failed.");
    }

    private async Task<CatalogPublicCutoverExecutionRollbackSummary> TryRollbackAsync(
        RuntimeStackManifest oldRuntime,
        CatalogPublicCutoverExecutionRouteResult matrix,
        CatalogPublicCutoverExecutionRouteResult element,
        bool oldMatrixStopped,
        bool oldElementStopped,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var matrixRestored = false;
        var elementRestored = false;
        var oldMatrixStarted = false;
        var oldElementStarted = false;

        try
        {
            if (matrix.Mutated)
            {
                matrixRestored = await RestoreRouteAsync(matrix, RouteKind.Matrix, ct);
            }
            else
            {
                matrixRestored = true;
            }

            if (element.Mutated)
            {
                elementRestored = await RestoreRouteAsync(element, RouteKind.ElementWeb, ct);
            }
            else
            {
                elementRestored = true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Route rollback failed: {ex.Message}");
        }

        try
        {
            oldMatrixStarted = !oldMatrixStopped || await StartAsync(oldRuntime.Matrix.ContainerName, ct);
            oldElementStarted = oldRuntime.Element is null || !oldElementStopped || await StartAsync(oldRuntime.Element.ContainerName, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Old runtime restart rollback failed: {ex.Message}");
        }

        var completed = matrixRestored && elementRestored && oldMatrixStarted && oldElementStarted;
        return new CatalogPublicCutoverExecutionRollbackSummary(
            true, true, completed, matrixRestored, elementRestored, oldMatrixStarted, oldElementStarted, warnings,
            completed ? "Best-effort rollback restored previous routes and restarted retained old runtime containers." : "Rollback was attempted but did not complete cleanly; inspect retained old runtime and route state immediately.");
    }

    private async Task<bool> RestoreRouteAsync(CatalogPublicCutoverExecutionRouteResult route, RouteKind kind, CancellationToken ct)
    {
        if (!route.Mutated)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(route.PreviousForwardHost) || route.PreviousForwardPort is not > 0)
        {
            await _routePublisher.DeleteByIdIfExistsAsync(route.RouteId, ct);
            return true;
        }

        await _routePublisher.EnsureAsync(new RoutePublishRequest(
            Domain: route.Host ?? throw new InvalidOperationException("Route host is required for rollback."),
            ForwardHost: route.PreviousForwardHost,
            ForwardPort: route.PreviousForwardPort.Value,
            Kind: kind,
            RequireSsl: route.PreviousNpmCertificateId is > 0,
            CertificateId: route.PreviousNpmCertificateId,
            ForceSsl: route.PreviousNpmCertificateId is > 0,
            Http2: true,
            IsPublic: true), ct);
        return true;
    }

    private async Task<bool> IsRunningAsync(string? containerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return false;
        }

        try
        {
            var inspected = await _docker.Containers.InspectContainerAsync(containerName, ct);
            return inspected.State?.Running ?? false;
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<bool> StopIfRunningAsync(string? containerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return false;
        }

        var inspected = await _docker.Containers.InspectContainerAsync(containerName, ct);
        if (inspected.State?.Running == true)
        {
            await _docker.Containers.StopContainerAsync(containerName, new ContainerStopParameters { WaitBeforeKillSeconds = 30 }, ct);
        }

        var after = await _docker.Containers.InspectContainerAsync(containerName, ct);
        return after.State?.Running == false;
    }

    private async Task<bool> StartAsync(string? containerName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(containerName))
        {
            return false;
        }

        var inspected = await _docker.Containers.InspectContainerAsync(containerName, ct);
        if (inspected.State?.Running == true)
        {
            return true;
        }

        return await _docker.Containers.StartContainerAsync(containerName, new ContainerStartParameters(), ct);
    }

    private async Task<bool> NetworkExistsAsync(string name, CancellationToken ct)
    {
        try
        {
            _ = await _docker.Networks.InspectNetworkAsync(name, ct);
            return true;
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private async Task<bool> EnsureNetworkAttachmentAsync(string networkName, string containerName, IReadOnlyList<string> aliases, CancellationToken ct)
    {
        if (!await IsAttachedAsync(networkName, containerName, ct))
        {
            await _docker.Networks.ConnectNetworkAsync(networkName, new NetworkConnectParameters
            {
                Container = containerName,
                EndpointConfig = new EndpointSettings { Aliases = aliases.ToList() }
            }, ct);
        }
        return await IsAttachedAsync(networkName, containerName, ct);
    }

    private async Task<bool> IsAttachedAsync(string networkName, string containerName, CancellationToken ct)
    {
        try
        {
            var network = await _docker.Networks.InspectNetworkAsync(networkName, ct);
            return network.Containers?.Values.Any(x => string.Equals(x.Name, containerName, StringComparison.OrdinalIgnoreCase)) ?? false;
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    private static bool CertificateReady(RuntimeStackBackupProductionRestoreCertificateMatch? certificate) =>
        certificate is not null && certificate.NpmCertificateId is > 0 && certificate.ImportedToNpm && !certificate.IsStaging;

    private static bool RouteOwnedByService(RuntimeStackBackupProductionRestoreNpmRouteSummary? route, RuntimeStackServiceManifest service, int port) =>
        route is not null && route.ForwardPort == port &&
        (string.Equals(NormalizeHost(route.ForwardHost), NormalizeHost(service.InternalHost), StringComparison.OrdinalIgnoreCase) ||
         string.Equals(NormalizeHost(route.ForwardHost), NormalizeHost(service.ContainerName), StringComparison.OrdinalIgnoreCase));

    private static IngressIntent BuildIngressIntent(RuntimeStackBackupProductionCandidateResult candidate, string targetStackSlug) =>
        new(
            candidate.CandidateId,
            $"mem-cutover-ingress-{candidate.CandidateId}",
            ResolveNpmContainerName(),
            candidate.SynapseContainerName,
            candidate.ElementContainerName ?? throw new InvalidOperationException("Candidate Element container is required for public cutover."),
            candidate.PostgresContainerName,
            $"mem-matrix-{Slugify(targetStackSlug)}",
            $"mem-element-{Slugify(targetStackSlug)}");

    private static CatalogPublicCutoverExecutionIngressSummary BuildSkippedIngress(IngressIntent intent, string detail) =>
        new(intent.NetworkName, false, false, false, false, false, false, intent.MatrixAlias, intent.ElementAlias, detail);

    private static CatalogPublicCutoverExecutionRouteResult BuildSkippedRoute(string component, RuntimeStackBackupPublicCutoverPreviewRouteAction preview, RuntimeStackBackupProductionRestoreNpmRouteSummary? previous, string detail) =>
        new(component, preview.Host, previous?.ForwardHost, previous?.ForwardPort, previous?.NpmCertificateId, null, preview.UpstreamPort, preview.NpmCertificateId, !string.IsNullOrWhiteSpace(preview.Host), false, false, "skipped", null, detail);

    private static IReadOnlyList<CatalogPublicCutoverExecutionAcknowledgement> BuildAcknowledgements(CatalogPublicCutoverExecutionRequest request) =>
        [
            Acknowledgement("catalog-public-cutover-execution.final-approval", "Final public cutover approval", request.AcknowledgeFinalApproval),
            Acknowledgement("catalog-public-cutover-execution.final-backup", "Final backup will be captured", request.AcknowledgeFinalBackupWillBeCaptured),
            Acknowledgement("catalog-public-cutover-execution.old-runtime-stop", "Old runtime will be stopped", request.AcknowledgeOldRuntimeWillBeStopped),
            Acknowledgement("catalog-public-cutover-execution.public-route-mutation", "Public route mutation acknowledged", request.AcknowledgePublicRouteMutation),
            Acknowledgement("catalog-public-cutover-execution.manual-rollback", "Manual rollback responsibility acknowledged", request.AcknowledgeManualRollback)
        ];

    private static CatalogPublicCutoverExecutionAcknowledgement Acknowledgement(string code, string label, bool acknowledged) =>
        new(code, label, true, acknowledged, acknowledged ? "acknowledged" : "missing");

    private static void AddCheck(List<CatalogPublicCutoverExecutionCheck> checks, string code, string category, string severity, bool passed, string status, string message, string? detail) =>
        checks.Add(new CatalogPublicCutoverExecutionCheck(code, category, severity, passed, status, message, detail));

    private static string BuildDetail(string status, CatalogPublicCutoverExecutionRollbackSummary rollback) => status switch
    {
        "completed" => "Catalog-backed public cutover completed. The old runtime is stopped but retained for rollback; runtime promotion remains a separate gated step.",
        "failed_rolled_back" => "Catalog-backed public cutover failed after mutation, and best-effort rollback restored the previous route/runtime state.",
        "failed_requires_operator" => "Catalog-backed public cutover failed and automatic rollback did not complete cleanly. Inspect retained old runtime and route state immediately.",
        _ => "Catalog-backed public cutover was blocked before any mutation."
    };

    private static string ResolveNpmContainerName() => Environment.GetEnvironmentVariable("MEM_NPM_CONTAINER_NAME")?.Trim() is { Length: > 0 } value ? value : "mem-npm";

    private static string NormalizeHost(string? value) => (value ?? string.Empty).Trim().TrimEnd('.').ToLowerInvariant();
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Slugify(string value)
    {
        var normalized = new string(value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
        return string.Join('-', normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));
    }
    private static string CreateExecutionId() => $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssZ}-{Guid.NewGuid():N}"[..25];
    private static void ValidatePathSegment(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains('/') || value.Contains('\\') || value.Contains(':') || value.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label} contains unsafe characters.");
        }
    }

    private sealed record IngressIntent(
        string CandidateId,
        string NetworkName,
        string NpmContainerName,
        string MatrixContainerName,
        string ElementContainerName,
        string? PostgresContainerName,
        string MatrixAlias,
        string ElementAlias);
}
