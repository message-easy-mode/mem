namespace HostAgent.Runtime.Migrations.ProductionAdoption;

/// <summary>
/// Coordinates the normal operator journey for publishing the privately created
/// migration target and immediately verifying the resulting live service.
/// Existing preview, cutover, compensation, and verification services remain authoritative.
/// </summary>
public sealed class MigrationGoLiveService(
    MigrationProductionAdoptionService adoptionService,
    MigrationProductionCutoverService cutoverService,
    MigrationProductionVerificationService verificationService)
{
    private static readonly TimeSpan GoLiveTimeout = TimeSpan.FromMinutes(45);

    public async Task<MigrationProductionAdoptionStateResponse> MakeLiveAsync(
        string migrationId,
        MakeMigrationServerLiveRequest request,
        CancellationToken requestCancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateConfirmations(request);

        var current = await adoptionService.GetStateAsync(migrationId, requestCancellationToken);
        var plan = current.Plan ?? throw new InvalidOperationException(
            "Create and privately verify the normal MEM server before making it live.");

        var action = DetermineAction(current.Status, plan);
        if (action == GoLiveAction.Completed)
        {
            return current;
        }

        if (action == GoLiveAction.AlreadyRunning)
        {
            throw new InvalidOperationException(
                "Making the server live is already running. Refresh the Migration workspace for durable state.");
        }

        if (action == GoLiveAction.RecoveryRequired)
        {
            throw new InvalidOperationException(
                plan.Cutover.Execution.FailureSummary ??
                "The public route state could not be confirmed as safely restored. Review the retained cutover and rollback evidence before continuing.");
        }

        if (action == GoLiveAction.Verify)
        {
            return await RunVerificationAsync(migrationId);
        }

        var previewState = HasReadyPreview(current)
            ? current
            : await cutoverService.PreparePreviewAsync(
                migrationId,
                new PrepareMigrationProductionCutoverRequest(),
                requestCancellationToken);

        var previewPlan = previewState.Plan ?? throw new InvalidOperationException(
            "The public route review could not be prepared.");
        var preview = previewPlan.Cutover.Preview;
        if (!string.Equals(preview.Status, "ready", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(preview.PreviewId) ||
            preview.Blockers.Count > 0)
        {
            throw new InvalidOperationException(
                previewPlan.BlockerSummary ??
                (preview.Blockers.Count > 0
                    ? $"The server cannot be made live: {string.Join("; ", preview.Blockers)}"
                    : "The public route review is not ready."));
        }

        using var operationTimeout = new CancellationTokenSource(GoLiveTimeout);
        var cutoverState = await cutoverService.ExecuteAsync(
            migrationId,
            new ExecuteMigrationProductionCutoverRequest(
                Operator: null,
                Note: null,
                PreviewId: preview.PreviewId,
                ExecuteNpmRouteMutation: true,
                AcknowledgeSourceFrozen: true,
                AcknowledgePrivateRuntimeHealthy: true,
                AcknowledgeRouteSnapshotReviewed: true,
                AcknowledgeCreatesPublicRoutes: true,
                AcknowledgeNoDnsMutation: true,
                AcknowledgeNoCertificateMutation: true,
                AcknowledgeRollbackIsNextSlice: true,
                AcknowledgePostCutoverVerificationRequired: true,
                AcknowledgeProductionAuthority: true),
            operationTimeout.Token);

        if (cutoverState.Plan is null ||
            !cutoverState.Plan.Cutover.Execution.PublicRoutesCreated ||
            !cutoverState.Plan.Cutover.Execution.RuntimePromotionCompleted)
        {
            return cutoverState;
        }

        return await RunVerificationAsync(migrationId, operationTimeout);
    }

    private async Task<MigrationProductionAdoptionStateResponse> RunVerificationAsync(
        string migrationId,
        CancellationTokenSource? existingTimeout = null)
    {
        var ownsTimeout = existingTimeout is null;
        var timeout = existingTimeout ?? new CancellationTokenSource(GoLiveTimeout);
        try
        {
            return await verificationService.VerifyAsync(
                migrationId,
                new RunMigrationProductionVerificationRequest(),
                timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return await adoptionService.GetStateAsync(migrationId, CancellationToken.None);
        }
        finally
        {
            if (ownsTimeout)
            {
                timeout.Dispose();
            }
        }
    }

    internal static IReadOnlyList<string> GetMissingConfirmations(MakeMigrationServerLiveRequest request)
    {
        var missing = new List<string>();
        if (!request.ConfirmMovePublicTraffic) missing.Add("confirmMovePublicTraffic");
        if (!request.ConfirmStopUsingOldServer) missing.Add("confirmStopUsingOldServer");
        if (!request.ConfirmRunLiveVerification) missing.Add("confirmRunLiveVerification");
        return missing;
    }

    internal static GoLiveAction DetermineAction(
        string status,
        MigrationProductionAdoptionPlanDto plan)
    {
        var privateRuntimeReady =
            plan.Materialization.ProductionDatabaseImported &&
            plan.Materialization.MatrixContainerStarted &&
            plan.Materialization.MatrixHealthPassed &&
            plan.Materialization.ElementContainerStarted &&
            plan.Materialization.ElementHealthPassed &&
            plan.Materialization.RuntimeManifestSaved &&
            plan.Materialization.DatabaseOwnershipSaved &&
            plan.Materialization.RuntimeRecordsCreated &&
            !plan.Materialization.PublicRoutesCreated;

        return DetermineAction(
            status,
            plan.Cutover.Execution.Status,
            plan.Cutover.Execution.PublicRoutesCreated,
            plan.Cutover.Execution.RuntimePromotionCompleted,
            plan.Cutover.Execution.RouteCompensationAttempted,
            plan.Cutover.Execution.RouteCompensationCompleted,
            plan.ProductionVerification.Status,
            plan.ProductionVerification.Passed,
            privateRuntimeReady);
    }

    internal static GoLiveAction DetermineAction(
        string status,
        string cutoverStatus,
        bool publicRoutesCreated,
        bool runtimePromotionCompleted,
        bool routeCompensationAttempted,
        bool routeCompensationCompleted,
        string verificationStatus,
        bool verificationPassed,
        bool privateRuntimeReady)
    {
        if (verificationPassed &&
            string.Equals(verificationStatus, "passed", StringComparison.OrdinalIgnoreCase))
        {
            return GoLiveAction.Completed;
        }

        if (string.Equals(status, "cutover-executing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "production-verification-running", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(cutoverStatus, "executing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(verificationStatus, "running", StringComparison.OrdinalIgnoreCase))
        {
            return GoLiveAction.AlreadyRunning;
        }

        if (publicRoutesCreated &&
            runtimePromotionCompleted &&
            string.Equals(cutoverStatus, "public-awaiting-verification", StringComparison.OrdinalIgnoreCase))
        {
            return GoLiveAction.Verify;
        }

        if (string.Equals(cutoverStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            var safelyCompensated =
                routeCompensationAttempted &&
                routeCompensationCompleted &&
                !publicRoutesCreated &&
                !runtimePromotionCompleted;
            return safelyCompensated
                ? GoLiveAction.Cutover
                : GoLiveAction.RecoveryRequired;
        }

        return privateRuntimeReady
            ? GoLiveAction.Cutover
            : GoLiveAction.RecoveryRequired;
    }

    private static bool HasReadyPreview(MigrationProductionAdoptionStateResponse state) =>
        state.Plan is not null &&
        string.Equals(state.Status, "cutover-preview-ready", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(state.Plan.Cutover.Preview.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(state.Plan.Cutover.Preview.PreviewId) &&
        state.Plan.Cutover.Preview.Blockers.Count == 0;

    private static void ValidateConfirmations(MakeMigrationServerLiveRequest request)
    {
        var missing = GetMissingConfirmations(request);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Missing required confirmations: {string.Join(", ", missing)}");
        }
    }

    internal enum GoLiveAction
    {
        Cutover,
        Verify,
        Completed,
        AlreadyRunning,
        RecoveryRequired,
    }
}
