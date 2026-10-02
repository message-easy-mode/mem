using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.AdvancedCutover.Confirmation;
using HostAgent.Runtime.Backups.AdvancedCutover.Execution;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview;

namespace HostAgent.Runtime.Migrations.Cutover;

public sealed class MigrationCutoverFacadeService(
    MigrationCutoverContextResolver contextResolver,
    RuntimeStackBackupProductionCandidateService candidateService,
    RuntimeStackBackupProductionCandidateHistoryService candidateHistory,
    RuntimeStackBackupPublicCutoverPreviewService previewService,
    RuntimeStackBackupPublicCutoverPreviewHistoryService previewHistory,
    RuntimeStackBackupPublicCutoverConfirmationService confirmationService,
    RuntimeStackBackupPublicCutoverConfirmationHistoryService confirmationHistory,
    RuntimeStackBackupPublicCutoverExecutionService executionService,
    RuntimeStackBackupPublicCutoverExecutionHistoryService executionHistory)
{
    public async Task<MigrationCutoverStateResponse> GetStateAsync(
        string migrationId,
        CancellationToken ct)
    {
        var context = await contextResolver.ResolveAsync(migrationId, ct);
        var candidate = await GetLatestCandidateAsync(migrationId, ct);
        var preview = candidate is null
            ? null
            : await GetLatestPreviewAsync(migrationId, candidate.CandidateId, ct);
        var confirmation = preview is null
            ? null
            : await GetLatestConfirmationAsync(
                migrationId,
                candidate!.CandidateId,
                preview.PreviewId,
                ct);
        var execution = confirmation is null
            ? null
            : await GetLatestExecutionAsync(
                migrationId,
                candidate!.CandidateId,
                preview!.PreviewId,
                confirmation.ConfirmationId,
                ct);

        var readiness = BuildReadiness(
            context,
            candidate,
            preview,
            confirmation);

        return new MigrationCutoverStateResponse(
            Source: "control-plane",
            Status: DetermineStateStatus(candidate, preview, confirmation, execution, readiness),
            MigrationId: migrationId,
            Capture: context.Capture,
            CandidateArtifactId: context.CandidateArtifact.CandidateArtifactId,
            Staging: new MigrationCutoverStagingSummary(
                context.StagingRun.StagingRunId,
                context.StagingRun.PrivateRuntimeStagingId!,
                context.StagingRun.Status,
                context.StagingRun.PrivateOnly,
                context.StagingRun.PublicRoutesCreated,
                context.StagingRun.DatabaseImportSucceeded,
                context.StagingRun.SynapseHealthPassed,
                context.StagingRun.ElementConfigPresent,
                context.StagingRun.UsersCount,
                context.StagingRun.RoomsCount,
                context.StagingRun.EventsCount),
            ProductionCandidate: candidate,
            Preview: preview,
            Confirmation: confirmation,
            Readiness: readiness,
            RouteSnapshot: BuildRouteSnapshot(preview),
            LatestExecution: execution,
            Rollback: BuildRollbackEvidence(execution),
            BackupCatalogItemCreated: false,
            RestoreSessionCreated: false,
            Detail: BuildStateDetail(candidate, preview, confirmation, execution, readiness));
    }

    public async Task<MigrationCutoverCandidateResponse> PrepareCandidateAsync(
        string migrationId,
        PrepareMigrationCutoverCandidateRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = await contextResolver.ResolveAsync(migrationId, ct);
        var before = await GetLatestCandidateAsync(migrationId, ct);

        var candidate = await candidateService.CreateOrResumeFromMigrationStagingAsync(
            migrationId,
            context.StagingRun.PrivateRuntimeStagingId!,
            request.TargetStackSlug,
            ct);

        return new MigrationCutoverCandidateResponse(
            Source: "control-plane",
            Status: candidate.Status,
            MigrationId: migrationId,
            CandidateCreated: before is null || !string.Equals(before.CandidateId, candidate.CandidateId, StringComparison.Ordinal),
            CandidateResumed: before is not null && string.Equals(before.CandidateId, candidate.CandidateId, StringComparison.Ordinal),
            Candidate: candidate,
            Detail: "The verified Migration Staging Run was resolved server-side and adopted as a private production candidate. No Backup Catalog item or Restore Session was created.");
    }

    public async Task<MigrationCutoverPreviewResponse> CreatePreviewAsync(
        string migrationId,
        CreateMigrationCutoverPreviewRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var context = await contextResolver.ResolveAsync(migrationId, ct);
        var candidate = await GetLatestCandidateAsync(migrationId, ct)
            ?? throw new InvalidOperationException(
                "Prepare a Migration-keyed private production candidate before creating a cutover preview.");

        var preview = await previewService.CreatePreviewFromMigrationAsync(
            migrationId,
            context.SourceSummary,
            candidate.CandidateId,
            request.TargetStackSlug,
            request.RestoreMode,
            request.IntendedMatrixHost,
            request.IntendedElementHost,
            ct);

        return new MigrationCutoverPreviewResponse(
            "control-plane",
            preview.Status,
            migrationId,
            preview,
            "A fresh read-only Migration-keyed cutover preview was created from the active candidate and verified staging evidence.");
    }

    public async Task<MigrationCutoverConfirmationResponse> ConfirmAsync(
        string migrationId,
        ConfirmMigrationCutoverRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = await contextResolver.ResolveAsync(migrationId, ct);
        var candidate = await GetLatestCandidateAsync(migrationId, ct)
            ?? throw new InvalidOperationException("Prepare a private production candidate before confirmation.");
        var preview = await GetLatestPreviewAsync(migrationId, candidate.CandidateId, ct)
            ?? throw new InvalidOperationException("Create a Migration-keyed cutover preview before confirmation.");

        var confirmation = await confirmationService.EvaluateAsync(
            preview.PreviewId,
            new RuntimeStackBackupPublicCutoverConfirmationRequest(
                request.Operator,
                request.Note,
                request.AcknowledgePreviewReviewed,
                request.AcknowledgeCandidateIsPrivateAndHealthy,
                request.AcknowledgePublicRouteExposureRisk,
                request.AcknowledgeNoAutomaticRollback,
                request.AcknowledgeFinalBackupRequired,
                request.AcknowledgeExecutionStillLocked),
            ct);

        return new MigrationCutoverConfirmationResponse(
            "control-plane",
            confirmation.Status,
            migrationId,
            confirmation,
            "Durable Migration-keyed confirmation evidence was recorded. The browser supplied no Catalog, Restore, candidate, preview, or host-runtime identifier.");
    }

    public Task<MigrationCutoverStateResponse> GetReadinessAsync(
        string migrationId,
        CancellationToken ct) => GetStateAsync(migrationId, ct);

    public async Task<MigrationCutoverExecutionResponse> ExecuteAsync(
        string migrationId,
        ExecuteMigrationCutoverRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var state = await GetStateAsync(migrationId, ct);

        if (!state.Readiness.ExecutionReady)
        {
            throw new InvalidOperationException(
                $"Migration cutover execution is blocked: {string.Join("; ", state.Readiness.Blockers)}");
        }

        var confirmation = state.Confirmation
            ?? throw new InvalidOperationException("A ready confirmation is required before execution.");

        var execution = await executionService.ExecuteAsync(
            confirmation.ConfirmationId,
            new RuntimeStackBackupPublicCutoverExecutionRequest(
                request.Operator,
                request.Note,
                request.ExecuteNpmRouteMutation,
                request.ExecuteCutoverIngressNetworkMutation,
                request.AcknowledgeConfirmationReviewed,
                request.AcknowledgeDockerNetworkMutation,
                request.AcknowledgeNpmMustNotJoinPrivateRestoreNetwork,
                request.AcknowledgeCreatesPublicRoutes,
                request.AcknowledgeNpmRoutesWillChange,
                request.AcknowledgeMatrixFederationExposureMayChange,
                request.AcknowledgeNoDnsMutation,
                request.AcknowledgeNoCertificateMutation,
                request.AcknowledgeNoRuntimePromotion,
                request.AcknowledgeNoAutomaticRollback,
                request.AcknowledgePostCutoverVerificationRequired),
            ct);

        return new MigrationCutoverExecutionResponse(
            "control-plane",
            execution.Status,
            migrationId,
            execution,
            "Migration-keyed cutover execution completed through trusted server-resolved candidate, preview, confirmation, and route evidence.");
    }

    public Task<MigrationCutoverStateResponse> GetPostCutoverAsync(
        string migrationId,
        CancellationToken ct) => GetStateAsync(migrationId, ct);

    private async Task<RuntimeStackBackupProductionCandidateResult?> GetLatestCandidateAsync(
        string migrationId,
        CancellationToken ct)
    {
        var history = await candidateHistory.ListCandidatesAsync(migrationId, null, 50, ct);
        var summary = history.Candidates
            .Where(x => string.Equals(x.SourceKind, "migration-session", StringComparison.OrdinalIgnoreCase) && !x.Destroyed)
            .OrderByDescending(x => x.StartedAtUtc)
            .FirstOrDefault();

        return summary is null
            ? null
            : (await candidateHistory.GetCandidateAsync(summary.CandidateId, ct))?.Candidate;
    }

    private async Task<RuntimeStackBackupPublicCutoverPreviewResult?> GetLatestPreviewAsync(
        string migrationId,
        string candidateId,
        CancellationToken ct)
    {
        var history = await previewHistory.ListPreviewsAsync(migrationId, candidateId, null, 50, ct);
        var summary = history.Previews
            .Where(x => string.Equals(x.SourceKind, "migration-session", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

        return summary is null
            ? null
            : (await previewHistory.GetPreviewAsync(summary.PreviewId, ct))?.Preview;
    }

    private async Task<RuntimeStackBackupPublicCutoverConfirmationResult?> GetLatestConfirmationAsync(
        string migrationId,
        string candidateId,
        string previewId,
        CancellationToken ct)
    {
        var history = await confirmationHistory.ListConfirmationsAsync(
            previewId,
            migrationId,
            candidateId,
            null,
            50,
            ct);
        var summary = history.Confirmations
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

        return summary is null
            ? null
            : (await confirmationHistory.GetConfirmationAsync(summary.ConfirmationId, ct))?.Confirmation;
    }

    private async Task<RuntimeStackBackupPublicCutoverExecutionResult?> GetLatestExecutionAsync(
        string migrationId,
        string candidateId,
        string previewId,
        string confirmationId,
        CancellationToken ct)
    {
        var history = await executionHistory.ListExecutionsAsync(
            confirmationId,
            previewId,
            migrationId,
            candidateId,
            null,
            50,
            ct);
        var summary = history.Executions
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();

        return summary is null
            ? null
            : (await executionHistory.GetExecutionAsync(summary.ExecutionId, ct))?.Execution;
    }

    private static MigrationCutoverReadiness BuildReadiness(
        MigrationCutoverContext context,
        RuntimeStackBackupProductionCandidateResult? candidate,
        RuntimeStackBackupPublicCutoverPreviewResult? preview,
        RuntimeStackBackupPublicCutoverConfirmationResult? confirmation)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();

        var candidateReady = candidate is not null &&
            candidate.Destroy is null &&
            string.Equals(candidate.Status, "private_candidate_ready", StringComparison.OrdinalIgnoreCase) &&
            candidate.Safety.PrivateOnly &&
            candidate.Database.ImportSucceeded &&
            candidate.Runtime.SynapseHealthPassed &&
            candidate.Runtime.ElementHealthPassed;
        if (!candidateReady)
        {
            blockers.Add("A healthy active private production candidate is required.");
        }

        var previewReady = preview is not null &&
            string.Equals(preview.Status, "ready_for_review", StringComparison.OrdinalIgnoreCase) &&
            preview.Blockers.Count == 0 && preview.Errors.Count == 0;
        if (!previewReady)
        {
            blockers.Add("A blocker-free Migration-keyed cutover preview is required.");
        }

        var confirmationReady = confirmation is not null &&
            confirmation.ExecutionAvailable &&
            confirmation.Blockers.Count == 0 && confirmation.Errors.Count == 0;
        if (!confirmationReady)
        {
            blockers.Add("A ready durable cutover confirmation is required.");
        }

        var routesReady = confirmation is not null &&
            confirmation.Routes.AllRequiredRoutesEligible &&
            confirmation.Routes.LiveNpmStateMatchesPreview;
        if (!routesReady)
        {
            blockers.Add("Required Matrix and Element route evidence is not ready or has drifted.");
        }

        if (!context.Capture.FinalCutoverEligible)
        {
            blockers.Add("Current production authority is required before public execution.");
            warnings.Add("The current package remains valid for rehearsal and private verification, but it does not yet authorize public execution.");
        }

        var executionReady = blockers.Count == 0;
        return new MigrationCutoverReadiness(
            Status: executionReady ? "ready" : "blocked",
            ExecutionReady: executionReady,
            FinalCaptureRequired: !context.Capture.FinalCutoverEligible,
            CandidateReady: candidateReady,
            PreviewReady: previewReady,
            ConfirmationReady: confirmationReady,
            RoutesReady: routesReady,
            Blockers: blockers.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings: warnings,
            Detail: executionReady
                ? "All Migration-keyed cutover gates passed for the current production authority."
                : "Cutover remains read-only until every blocker is resolved.");
    }

    private static MigrationCutoverRouteSnapshot BuildRouteSnapshot(
        RuntimeStackBackupPublicCutoverPreviewResult? preview) =>
        new(
            preview?.Routes.Matrix,
            preview?.Routes.Element,
            preview?.CreatedAtUtc,
            preview is null ? "not-created" : preview.Status);

    private static MigrationCutoverRollbackEvidence BuildRollbackEvidence(
        RuntimeStackBackupPublicCutoverExecutionResult? execution)
    {
        if (execution is null)
        {
            return new MigrationCutoverRollbackEvidence(
                false, false, false, false, "not-required",
                "No public cutover execution has been recorded for this Migration Session.");
        }

        var mutated = execution.Routes.AnyRouteMutated || execution.CutoverIngress.DockerNetworksChanged;
        return new MigrationCutoverRollbackEvidence(
            RouteMutationOccurred: mutated,
            AutomaticRollbackAvailable: false,
            AutomaticRollbackAttempted: false,
            AutomaticRollbackCompleted: false,
            Status: mutated ? "manual-rollback-evidence-required" : "not-required",
            Detail: mutated
                ? "This facade records route and ingress mutation evidence. Automated Migration rollback remains a later bounded slice and must not be implied."
                : "The latest execution recorded no public route or cutover-ingress mutation.");
    }

    private static string DetermineStateStatus(
        RuntimeStackBackupProductionCandidateResult? candidate,
        RuntimeStackBackupPublicCutoverPreviewResult? preview,
        RuntimeStackBackupPublicCutoverConfirmationResult? confirmation,
        RuntimeStackBackupPublicCutoverExecutionResult? execution,
        MigrationCutoverReadiness readiness)
    {
        if (execution is not null)
        {
            return execution.Status;
        }

        if (readiness.ExecutionReady)
        {
            return "ready_for_execution";
        }

        if (confirmation is not null)
        {
            return "confirmation-recorded";
        }

        if (preview is not null)
        {
            return "preview-created";
        }

        return candidate is not null ? "candidate-ready" : "staging-ready";
    }

    private static string BuildStateDetail(
        RuntimeStackBackupProductionCandidateResult? candidate,
        RuntimeStackBackupPublicCutoverPreviewResult? preview,
        RuntimeStackBackupPublicCutoverConfirmationResult? confirmation,
        RuntimeStackBackupPublicCutoverExecutionResult? execution,
        MigrationCutoverReadiness readiness)
    {
        if (execution is not null)
        {
            return "Latest Migration-keyed cutover execution and mutation evidence were reconstructed from durable server-owned history.";
        }

        if (readiness.ExecutionReady)
        {
            return "The latest server-resolved candidate, preview, confirmation, route evidence, and final capture are ready for stepped-up execution.";
        }

        if (confirmation is not null)
        {
            return "Confirmation evidence exists, but readiness remains blocked. Review the returned blockers.";
        }

        if (preview is not null)
        {
            return "A read-only Migration-keyed preview exists. Confirmation and readiness are the next gates.";
        }

        return candidate is not null
            ? "A private production candidate exists. Create a read-only Migration-keyed cutover preview next."
            : "Verified Migration staging is available. Prepare the private production candidate next.";
    }
}
