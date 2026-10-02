using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview;
using Microsoft.Extensions.Logging;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using HostAgent.Runtime.Migrations.Cutover;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Confirmation;

public sealed class RuntimeStackBackupPublicCutoverConfirmationService
{
    private readonly RuntimeStackBackupPublicCutoverPreviewHistoryService _previewHistory;
    private readonly RuntimeStackBackupProductionCandidateHistoryService _candidateHistory;
    private readonly ValidatedImportArtifactService _validatedImportArtifacts;
    private readonly RuntimeStackBackupProductionRestorePlanService _planService;
    private readonly RuntimeStackBackupPublicCutoverConfirmationHistoryService _confirmationHistory;
    private readonly MigrationCutoverContextResolver _migrationContextResolver;
    private readonly ILogger<RuntimeStackBackupPublicCutoverConfirmationService> _logger;

    public RuntimeStackBackupPublicCutoverConfirmationService(
        RuntimeStackBackupPublicCutoverPreviewHistoryService previewHistory,
        RuntimeStackBackupProductionCandidateHistoryService candidateHistory,
        ValidatedImportArtifactService validatedImportArtifacts,
        RuntimeStackBackupProductionRestorePlanService planService,
        RuntimeStackBackupPublicCutoverConfirmationHistoryService confirmationHistory,
        MigrationCutoverContextResolver migrationContextResolver,
        ILogger<RuntimeStackBackupPublicCutoverConfirmationService> logger)
    {
        _previewHistory = previewHistory;
        _candidateHistory = candidateHistory;
        _validatedImportArtifacts = validatedImportArtifacts;
        _planService = planService;
        _confirmationHistory = confirmationHistory;
        _migrationContextResolver = migrationContextResolver;
        _logger = logger;
    }

    public async Task<RuntimeStackBackupPublicCutoverConfirmationResult> EvaluateAsync(
        string previewId,
        RuntimeStackBackupPublicCutoverConfirmationRequest request,
        CancellationToken ct)
    {
        ValidatePathSegment(previewId, "Public Cutover preview id");

        var confirmationId = CreateConfirmationId();
        var checks = new List<RuntimeStackBackupPublicCutoverConfirmationCheck>();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        var previewResponse = await _previewHistory.GetPreviewAsync(
            previewId,
            ct);

        var preview = previewResponse?.Preview;

        if (preview is null)
        {
            throw new FileNotFoundException($"Public Cutover preview '{previewId}' was not found.");
        }

        var candidateResponse = await _candidateHistory.GetCandidateAsync(
            preview.CandidateId,
            ct);

        var candidate = candidateResponse?.Candidate;

        if (candidate is null)
        {
            throw new FileNotFoundException($"Production Restore candidate '{preview.CandidateId}' was not found.");
        }

        if (!string.Equals(candidate.ValidationId, preview.ValidationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Production Restore candidate '{candidate.CandidateId}' belongs to source '{candidate.ValidationId}', not preview source '{preview.ValidationId}'.");
        }

        RuntimeStackBackupProductionRestorePlanResult freshPlan;

        if (string.Equals(preview.SourceKind, "migration-session", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(candidate.SourceKind, "migration-session", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Migration-keyed preview is not linked to a Migration-owned production candidate.");
            }

            var migration = await _migrationContextResolver.ResolveAsync(preview.ValidationId, ct);
            freshPlan = await _planService.CreatePlanFromMigrationSourceAsync(
                preview.ValidationId,
                migration.SourceSummary,
                preview.CandidateId,
                preview.Candidate.TargetStackSlug,
                preview.RestoreMode,
                preview.Routes.Matrix.Host,
                preview.Routes.Element.Host,
                ct);
        }
        else
        {
            await _validatedImportArtifacts.EnsureRestoreCanContinueAsync(
                preview.ValidationId,
                ct);

            freshPlan = await _planService.CreatePlanAsync(
                preview.ValidationId,
                stagingId: null,
                candidateId: preview.CandidateId,
                targetStackSlug: preview.Candidate.TargetStackSlug,
                restoreMode: preview.RestoreMode,
                intendedMatrixHost: preview.Routes.Matrix.Host,
                intendedElementHost: preview.Routes.Element.Host,
                ct: ct);
        }

        warnings.AddRange(freshPlan.Warnings);
        errors.AddRange(freshPlan.Errors);

        var previewSummary = BuildPreviewSummary(
            preview,
            checks,
            blockers);

        var candidateSummary = BuildCandidateSummary(
            candidate,
            checks,
            blockers,
            warnings);

        var matrixRoute = BuildRouteGate(
            component: "matrix",
            previewAction: preview.Routes.Matrix,
            freshRoute: freshPlan.Npm.MatrixRoute,
            freshCertificate: freshPlan.Certificates.MatrixCertificate,
            stableCutoverAlias: $"mem-matrix-{Slugify(preview.Candidate.TargetStackSlug)}",
            required: true,
            checks,
            blockers,
            warnings);

        var elementRequired = !string.IsNullOrWhiteSpace(preview.Routes.Element.Host);
        var elementRoute = BuildRouteGate(
            component: "element",
            previewAction: preview.Routes.Element,
            freshRoute: freshPlan.Npm.ElementRoute,
            freshCertificate: freshPlan.Certificates.ElementCertificate,
            stableCutoverAlias: $"mem-element-{Slugify(preview.Candidate.TargetStackSlug)}",
            required: elementRequired,
            checks,
            blockers,
            warnings);

        AddFreshPlanChecks(
            freshPlan,
            checks,
            blockers);

        var acknowledgements = BuildAcknowledgements(
            request);

        foreach (var acknowledgement in acknowledgements.Where(ack => ack.Required && !ack.Acknowledged))
        {
            blockers.Add($"Required acknowledgement missing: {acknowledgement.Label}");
        }

        AddCheck(
            checks,
            "public-cutover-confirmation.acknowledgements.required",
            "acknowledgement",
            acknowledgements.Where(ack => ack.Required).All(ack => ack.Acknowledged) ? "info" : "blocker",
            acknowledgements.Where(ack => ack.Required).All(ack => ack.Acknowledged),
            acknowledgements.Where(ack => ack.Required).All(ack => ack.Acknowledged) ? "satisfied" : "blocked",
            acknowledgements.Where(ack => ack.Required).All(ack => ack.Acknowledged)
                ? "All required Public Cutover danger acknowledgements were supplied."
                : "One or more required Public Cutover danger acknowledgements are missing.",
            null);

        AddCheck(
            checks,
            "public-cutover-confirmation.execution.locked",
            "execution-lock",
            "info",
            true,
            "satisfied",
            "Public cutover execution remains locked in Confirmation Gates v1.",
            "This endpoint does not call NPM create/update/delete, mutate DNS, touch certificates, promote runtime, or expose federation.");

        var allRequiredRoutesEligible = matrixRoute.Passed && (!elementRequired || elementRoute.Passed);
        var liveNpmStateMatchesPreview = matrixRoute.LiveStateMatchesPreview && (!elementRequired || elementRoute.LiveStateMatchesPreview);

        var routes = new RuntimeStackBackupPublicCutoverConfirmationRouteSummary(
            Matrix: matrixRoute,
            Element: elementRoute,
            AllRequiredRoutesEligible: allRequiredRoutesEligible,
            LiveNpmStateMatchesPreview: liveNpmStateMatchesPreview,
            Notes:
            [
                "Confirmation gates compare the saved preview with fresh read-only plan evidence.",
                "Route actions remain descriptive only; no public route is created or changed here.",
                "A later executor must repeat these checks immediately before mutation."
            ]);

        if (!allRequiredRoutesEligible)
        {
            blockers.Add("One or more required public routes are not eligible for future gated execution.");
        }

        if (!liveNpmStateMatchesPreview)
        {
            blockers.Add("Live NPM route state has drifted from the saved Public Cutover preview.");
        }

        var mutations = new RuntimeStackBackupProductionRestoreMutationSummary(
            RuntimeChanged: false,
            ProductionContainersTouched: false,
            ProductionDatabasesTouched: false,
            DnsChanged: false,
            NpmRoutesChanged: false,
            CertificatesChanged: false,
            PublicRoutesChanged: false,
            FederationExposureChanged: false,
            Notes:
            [
                "Public Cutover Confirmation Gates v1 is read-only.",
                "No production containers were stopped, deleted, replaced, or modified.",
                "No production databases were created, dropped, overwritten, or modified.",
                "No DNS records were created, updated, or deleted.",
                "No NPM routes were created, updated, enabled, disabled, or deleted.",
                "No certificates were requested, imported, renewed, attached, or removed.",
                "No public federation exposure was created.",
                "Execution remains unavailable until a later explicit executor sprint exists."
            ]);

        var distinctBlockers = blockers.Distinct(StringComparer.Ordinal).ToList();
        var distinctWarnings = warnings.Distinct(StringComparer.Ordinal).ToList();
        var distinctErrors = errors.Distinct(StringComparer.Ordinal).ToList();

        var executionAvailable = distinctErrors.Count == 0 && distinctBlockers.Count == 0;
        var status = executionAvailable
            ? "confirmed_ready_for_future_executor"
            : "blocked";

        var result = new RuntimeStackBackupPublicCutoverConfirmationResult(
            Source: "control-plane",
            Status: status,
            ConfirmationId: confirmationId,
            PreviewId: preview.PreviewId,
            ValidationId: preview.ValidationId,
            CandidateId: preview.CandidateId,
            PreviewPlanId: preview.PlanId,
            FreshPlanId: freshPlan.PlanId,
            RestoreMode: preview.RestoreMode,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Operator: NormalizeOptional(request.Operator),
            Note: NormalizeOptional(request.Note),
            ProductionExecutionLocked: true,
            ExecutionAvailable: executionAvailable,
            Mutations: mutations,
            Preview: previewSummary,
            Candidate: candidateSummary,
            Routes: routes,
            Acknowledgements: acknowledgements,
            Checks: checks,
            Blockers: distinctBlockers,
            Warnings: distinctWarnings,
            Errors: distinctErrors,
            Detail: status == "blocked"
                ? "Public Cutover confirmation gates did not pass. No public mutation was performed."
                : "Public Cutover confirmation gates passed. The separately stepped-up executor may consume this durable confirmation.");

        await _confirmationHistory.SaveConfirmationAsync(
            result,
            ct);

        _logger.LogInformation(
            "Evaluated Public Cutover confirmation gates. ConfirmationId={ConfirmationId} PreviewId={PreviewId} CandidateId={CandidateId} FreshPlanId={FreshPlanId} Status={Status}",
            result.ConfirmationId,
            result.PreviewId,
            result.CandidateId,
            result.FreshPlanId,
            result.Status);

        return result;
    }

    private static RuntimeStackBackupPublicCutoverConfirmationPreviewSummary BuildPreviewSummary(
        RuntimeStackBackupPublicCutoverPreviewResult preview,
        List<RuntimeStackBackupPublicCutoverConfirmationCheck> checks,
        List<string> blockers)
    {
        var mutationFlagsClear =
            !preview.Mutations.RuntimeChanged &&
            !preview.Mutations.ProductionContainersTouched &&
            !preview.Mutations.ProductionDatabasesTouched &&
            !preview.Mutations.DnsChanged &&
            !preview.Mutations.NpmRoutesChanged &&
            !preview.Mutations.CertificatesChanged &&
            !preview.Mutations.PublicRoutesChanged &&
            !preview.Mutations.FederationExposureChanged;

        var readyForReview = string.Equals(preview.Status, "ready_for_review", StringComparison.OrdinalIgnoreCase) &&
            preview.ProductionExecutionLocked &&
            mutationFlagsClear &&
            preview.Blockers.Count == 0 &&
            preview.Errors.Count == 0;

        AddCheck(
            checks,
            "public-cutover-confirmation.preview.ready-for-review",
            "preview",
            readyForReview ? "info" : "blocker",
            readyForReview,
            readyForReview ? "satisfied" : "blocked",
            readyForReview
                ? "Saved Public Cutover preview is ready for confirmation review."
                : "Saved Public Cutover preview is not ready for confirmation review.",
            preview.Status);

        if (!readyForReview)
        {
            blockers.Add("Saved Public Cutover preview must be ready_for_review, locked, mutation-free, and without blockers/errors.");
        }

        return new RuntimeStackBackupPublicCutoverConfirmationPreviewSummary(
            PreviewId: preview.PreviewId,
            Status: preview.Status,
            CreatedAtUtc: preview.CreatedAtUtc,
            ProductionExecutionLocked: preview.ProductionExecutionLocked,
            MutationFlagsClear: mutationFlagsClear,
            HasNoBlockers: preview.Blockers.Count == 0,
            HasNoErrors: preview.Errors.Count == 0,
            ReadyForReview: readyForReview,
            Detail: preview.Detail);
    }

    private static RuntimeStackBackupPublicCutoverConfirmationCandidateSummary BuildCandidateSummary(
        RuntimeStackBackupProductionCandidateResult candidate,
        List<RuntimeStackBackupPublicCutoverConfirmationCheck> checks,
        List<string> blockers,
        List<string> warnings)
    {
        var destroyed = candidate.Destroy is not null ||
            string.Equals(candidate.Status, "destroyed", StringComparison.OrdinalIgnoreCase);

        var matrixReady = string.Equals(candidate.Status, "private_candidate_ready", StringComparison.OrdinalIgnoreCase) &&
            !destroyed &&
            candidate.Safety.PrivateOnly &&
            candidate.Safety.DockerNetworkInternal &&
            !candidate.Safety.PublicRoutesCreated &&
            !candidate.Safety.DnsChanged &&
            !candidate.Safety.CertificatesChanged &&
            !candidate.Safety.NpmRoutesChanged &&
            !candidate.Safety.ProductionContainersTouched &&
            !candidate.Safety.ProductionDatabasesTouched &&
            !candidate.Safety.PublicCutoverPerformed &&
            candidate.Database.ImportSucceeded &&
            candidate.Runtime.SynapseHealthPassed;

        var elementReady = matrixReady &&
            candidate.Runtime.ElementConfigExtracted &&
            candidate.Runtime.ElementConfigPatched &&
            candidate.Runtime.ElementContainerStarted &&
            candidate.Runtime.ElementHealthPassed &&
            !string.IsNullOrWhiteSpace(candidate.ElementContainerName);

        AddCheck(
            checks,
            "public-cutover-confirmation.candidate.matrix-private-ready",
            "production-candidate",
            matrixReady ? "info" : "blocker",
            matrixReady,
            matrixReady ? "satisfied" : "blocked",
            matrixReady
                ? "Private candidate Matrix runtime is ready and still private-only."
                : "Private candidate Matrix runtime is not ready or no longer private-only.",
            candidate.CandidateId);

        if (!matrixReady)
        {
            blockers.Add("Candidate must be ready, private-only, internal-network-only, not destroyed, mutation-free, imported, and Synapse-health-checked.");
        }

        AddCheck(
            checks,
            "public-cutover-confirmation.candidate.element-private-ready",
            "production-candidate",
            elementReady ? "info" : "blocker",
            elementReady,
            elementReady ? "satisfied" : "blocked",
            elementReady
                ? "Private candidate Element runtime is ready and still private-only."
                : "Private candidate Element runtime is not ready for public cutover confirmation.",
            candidate.ElementContainerName);

        if (!elementReady)
        {
            blockers.Add("Element candidate runtime must be extracted, patched, started, health-checked, and private-only before full public cutover confirmation can pass.");
        }

        if (candidate.Warnings.Count > 0)
        {
            warnings.AddRange(candidate.Warnings.Select(warning => $"Candidate warning: {warning}"));
        }

        return new RuntimeStackBackupPublicCutoverConfirmationCandidateSummary(
            CandidateFound: true,
            CandidateId: candidate.CandidateId,
            Status: candidate.Status,
            PrivateRuntimeStatus: candidate.PrivateRuntimeStatus,
            PrivateOnly: candidate.Safety.PrivateOnly,
            Destroyed: destroyed,
            DatabaseImportSucceeded: candidate.Database.ImportSucceeded,
            SynapseHealthPassed: candidate.Runtime.SynapseHealthPassed,
            ElementHealthPassed: candidate.Runtime.ElementHealthPassed,
            MatrixServerName: candidate.MatrixServerName,
            SynapseContainerName: candidate.SynapseContainerName,
            ElementContainerName: candidate.ElementContainerName,
            Detail: candidate.Detail);
    }

    private static RuntimeStackBackupPublicCutoverConfirmationRouteGate BuildRouteGate(
        string component,
        RuntimeStackBackupPublicCutoverPreviewRouteAction previewAction,
        RuntimeStackBackupProductionRestoreNpmRouteSummary? freshRoute,
        RuntimeStackBackupProductionRestoreCertificateMatch? freshCertificate,
        string stableCutoverAlias,
        bool required,
        List<RuntimeStackBackupPublicCutoverConfirmationCheck> checks,
        List<string> blockers,
        List<string> warnings)
    {
        var certificateReady = freshCertificate is not null &&
            freshCertificate.NpmCertificateId is > 0 &&
            freshCertificate.ImportedToNpm &&
            !freshCertificate.IsStaging;

        var previewActionEligible = IsEligiblePreviewAction(
            previewAction.Action,
            required);

        var liveStateMatchesPreview = FreshRouteStateMatchesPreview(
            previewAction,
            freshRoute,
            stableCutoverAlias);

        var existingRouteTargetsCandidate = freshRoute is not null &&
            RouteTargetsPreviewCandidate(
                freshRoute,
                previewAction.UpstreamContainerName,
                stableCutoverAlias,
                previewAction.UpstreamPort,
                previewAction.NpmCertificateId);

        var passed = (!required || previewActionEligible) &&
            (!required || previewAction.AvailableForFutureExecution) &&
            (!required || certificateReady) &&
            liveStateMatchesPreview;

        var status = passed
            ? "satisfied"
            : "blocked";

        var detail = BuildRouteDetail(
            component,
            required,
            previewActionEligible,
            previewAction.AvailableForFutureExecution,
            certificateReady,
            liveStateMatchesPreview,
            freshRoute,
            previewAction);

        AddCheck(
            checks,
            $"public-cutover-confirmation.route.{component}",
            "npm-route",
            passed ? "info" : "blocker",
            passed,
            status,
            passed
                ? $"{component} route confirmation gate passed."
                : $"{component} route confirmation gate did not pass.",
            detail);

        if (!passed && required)
        {
            blockers.Add($"{component} route is not eligible for future gated execution: {detail}");
        }
        else if (!required && !previewActionEligible)
        {
            warnings.Add($"{component} route is not required by this preview: action={previewAction.Action}.");
        }

        return new RuntimeStackBackupPublicCutoverConfirmationRouteGate(
            Component: component,
            Host: previewAction.Host,
            PreviewAction: previewAction.Action,
            PreviewAvailableForFutureExecution: previewAction.AvailableForFutureExecution,
            PreviewRouteCurrentlyExists: previewAction.RouteCurrentlyExists,
            FreshRouteCurrentlyExists: freshRoute is not null,
            LiveStateMatchesPreview: liveStateMatchesPreview,
            CertificateReady: certificateReady,
            ExistingRouteAlreadyTargetsCandidate: existingRouteTargetsCandidate,
            UpstreamContainerName: previewAction.UpstreamContainerName,
            UpstreamPort: previewAction.UpstreamPort,
            Status: status,
            Passed: passed,
            Detail: detail);
    }

    private static void AddFreshPlanChecks(
        RuntimeStackBackupProductionRestorePlanResult freshPlan,
        List<RuntimeStackBackupPublicCutoverConfirmationCheck> checks,
        List<string> blockers)
    {
        var planClean = freshPlan.Errors.Count == 0 &&
            freshPlan.Blockers.Count == 0 &&
            !freshPlan.Mutations.RuntimeChanged &&
            !freshPlan.Mutations.ProductionContainersTouched &&
            !freshPlan.Mutations.ProductionDatabasesTouched &&
            !freshPlan.Mutations.DnsChanged &&
            !freshPlan.Mutations.NpmRoutesChanged &&
            !freshPlan.Mutations.CertificatesChanged &&
            !freshPlan.Mutations.PublicRoutesChanged &&
            !freshPlan.Mutations.FederationExposureChanged;

        AddCheck(
            checks,
            "public-cutover-confirmation.fresh-plan.clean",
            "production-plan",
            planClean ? "info" : "blocker",
            planClean,
            planClean ? "satisfied" : "blocked",
            planClean
                ? "Fresh read-only Production Restore plan has no blockers/errors and no mutation flags."
                : "Fresh read-only Production Restore plan is not clean.",
            freshPlan.PlanId);

        foreach (var blocker in freshPlan.Blockers)
        {
            blockers.Add($"Fresh Production Restore plan blocker: {blocker}");
        }
    }

    private static IReadOnlyList<RuntimeStackBackupPublicCutoverConfirmationAcknowledgement> BuildAcknowledgements(
        RuntimeStackBackupPublicCutoverConfirmationRequest request)
    {
        return
        [
            BuildAcknowledgement(
                "public-cutover.ack.preview-reviewed",
                "Preview reviewed",
                "Operator reviewed the saved Public Cutover preview and understands the calculated future Matrix and Element route actions.",
                "warning",
                request.AcknowledgePreviewReviewed),
            BuildAcknowledgement(
                "public-cutover.ack.candidate-private-healthy",
                "Candidate private and healthy",
                "Operator confirms the selected candidate is the intended private runtime and Matrix/Element health evidence is acceptable.",
                "warning",
                request.AcknowledgeCandidateIsPrivateAndHealthy),
            BuildAcknowledgement(
                "public-cutover.ack.public-exposure-risk",
                "Public exposure risk understood",
                "Operator understands a later executor will expose Matrix federation/client traffic and Element web traffic publicly.",
                "danger",
                request.AcknowledgePublicRouteExposureRisk),
            BuildAcknowledgement(
                "public-cutover.ack.no-automatic-rollback",
                "No automatic rollback assumed",
                "Operator understands confirmation gates do not provide rollback and that a later executor must have explicit rollback rules.",
                "danger",
                request.AcknowledgeNoAutomaticRollback),
            BuildAcknowledgement(
                "public-cutover.ack.final-backup-required",
                "Final backup required",
                "Operator acknowledges a final pre-cutover backup/snapshot is required before any future public route mutation.",
                "danger",
                request.AcknowledgeFinalBackupRequired),
            BuildAcknowledgement(
                "public-cutover.ack.execution-still-locked",
                "Execution still locked",
                "Operator understands this endpoint does not execute cutover and public execution remains unavailable in this sprint.",
                "info",
                request.AcknowledgeExecutionStillLocked)
        ];
    }

    private static RuntimeStackBackupPublicCutoverConfirmationAcknowledgement BuildAcknowledgement(
        string code,
        string label,
        string description,
        string severity,
        bool acknowledged)
    {
        return new RuntimeStackBackupPublicCutoverConfirmationAcknowledgement(
            Code: code,
            Label: label,
            Description: description,
            Severity: severity,
            Required: true,
            Acknowledged: acknowledged,
            Status: acknowledged ? "acknowledged" : "missing");
    }

    private static bool IsEligiblePreviewAction(
        string action,
        bool required)
    {
        if (!required)
        {
            return string.Equals(action, "not_requested", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "create", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "no-op_existing_route_targets_candidate", StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(action, "create", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(action, "no-op_existing_route_targets_candidate", StringComparison.OrdinalIgnoreCase);
    }

    private static bool FreshRouteStateMatchesPreview(
        RuntimeStackBackupPublicCutoverPreviewRouteAction previewAction,
        RuntimeStackBackupProductionRestoreNpmRouteSummary? freshRoute,
        string stableCutoverAlias)
    {
        if (previewAction.RouteCurrentlyExists != (freshRoute is not null))
        {
            return false;
        }

        if (freshRoute is null)
        {
            return true;
        }

        return RouteTargetsPreviewCandidate(
            freshRoute,
            previewAction.UpstreamContainerName,
            stableCutoverAlias,
            previewAction.UpstreamPort,
            previewAction.NpmCertificateId);
    }

    private static bool RouteTargetsPreviewCandidate(
        RuntimeStackBackupProductionRestoreNpmRouteSummary route,
        string? upstreamContainer,
        string stableCutoverAlias,
        int? upstreamPort,
        int? npmCertificateId)
    {
        if (string.IsNullOrWhiteSpace(upstreamContainer) || upstreamPort is not > 0)
        {
            return false;
        }

        var normalizedForwardHost = NormalizeHost(route.ForwardHost);

        var hostMatches = string.Equals(
                normalizedForwardHost,
                NormalizeHost(upstreamContainer),
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                normalizedForwardHost,
                NormalizeHost(stableCutoverAlias),
                StringComparison.OrdinalIgnoreCase);

        var portMatches = route.ForwardPort == upstreamPort;

        var certificateMatches = npmCertificateId is not > 0 ||
            route.NpmCertificateId == npmCertificateId;

        return hostMatches && portMatches && certificateMatches;
    }

    private static string BuildRouteDetail(
        string component,
        bool required,
        bool previewActionEligible,
        bool previewAvailable,
        bool certificateReady,
        bool liveStateMatchesPreview,
        RuntimeStackBackupProductionRestoreNpmRouteSummary? freshRoute,
        RuntimeStackBackupPublicCutoverPreviewRouteAction previewAction)
    {
        if (!required)
        {
            return $"{component} route is not required by this confirmation request. Preview action: {previewAction.Action}.";
        }

        if (!previewActionEligible)
        {
            return $"Preview action '{previewAction.Action}' is not executable by a future gated executor.";
        }

        if (!previewAvailable)
        {
            return "Preview did not mark this route as available for future execution.";
        }

        if (!certificateReady)
        {
            return "A non-staging certificate imported to NPM is required.";
        }

        if (!liveStateMatchesPreview)
        {
            return freshRoute is null
                ? "Saved preview expected an existing route, but the fresh read-only plan no longer sees one."
                : $"Saved preview expected routeExists={previewAction.RouteCurrentlyExists}, but fresh route state is routeExists=true host={freshRoute.Host} forward={freshRoute.ForwardHost}:{freshRoute.ForwardPort}.";
        }

        return $"{component} route is eligible for a later gated executor. Preview action: {previewAction.Action}.";
    }

    private static void AddCheck(
        List<RuntimeStackBackupPublicCutoverConfirmationCheck> checks,
        string code,
        string category,
        string severity,
        bool passed,
        string status,
        string message,
        string? detail)
    {
        checks.Add(new RuntimeStackBackupPublicCutoverConfirmationCheck(
            Code: code,
            Category: category,
            Severity: severity,
            Status: status,
            Passed: passed,
            Message: message,
            Detail: detail));
    }

    private static string? NormalizeHost(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().TrimEnd('.').ToLowerInvariant();
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

        return slug.Trim('-');
    }

    private static string? NormalizeOptional(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static void ValidatePathSegment(
        string value,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{label} is required.");
        }

        if (!IsSafePathSegment(value))
        {
            throw new InvalidOperationException($"{label} contains unsafe characters.");
        }
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

    private static string CreateConfirmationId()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss'Z'");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        return $"{timestamp}-{suffix}";
    }
}
