using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;
using HostAgent.Runtime.Backups.AdvancedCutover.Preview;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Confirmation.Catalog;

/// <summary>
/// Evaluates durable confirmation gates for a catalog-native private candidate.
///
/// This service intentionally uses catalogEntryId as the recovery-source
/// identity. It never calls the legacy validated-upload store and never treats
/// a validation receipt as a payload locator. It only reads candidate/preview
/// history and produces a fresh catalog-native read-only plan for drift checks.
/// </summary>
public sealed class CatalogPublicCutoverConfirmationService
{
    private readonly RuntimeStackBackupPublicCutoverPreviewHistoryService _previewHistory;
    private readonly RuntimeStackBackupProductionCandidateHistoryService _candidateHistory;
    private readonly CatalogProductionRestorePlanService _catalogPlanService;
    private readonly CatalogPublicCutoverConfirmationHistoryService _confirmationHistory;
    private readonly ILogger<CatalogPublicCutoverConfirmationService> _logger;

    public CatalogPublicCutoverConfirmationService(
        RuntimeStackBackupPublicCutoverPreviewHistoryService previewHistory,
        RuntimeStackBackupProductionCandidateHistoryService candidateHistory,
        CatalogProductionRestorePlanService catalogPlanService,
        CatalogPublicCutoverConfirmationHistoryService confirmationHistory,
        ILogger<CatalogPublicCutoverConfirmationService> logger)
    {
        _previewHistory = previewHistory;
        _candidateHistory = candidateHistory;
        _catalogPlanService = catalogPlanService;
        _confirmationHistory = confirmationHistory;
        _logger = logger;
    }

    public async Task<CatalogPublicCutoverConfirmationResponse> EvaluateAsync(
        string catalogEntryId,
        CatalogPublicCutoverConfirmationRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidatePathSegment(catalogEntryId, "Backup Catalog entry id");
        ValidatePathSegment(request.PreviewId, "Public Cutover preview id");
        ValidatePathSegment(request.CandidateId, "Production Restore candidate id");

        var previewResponse = await _previewHistory.GetPreviewAsync(
            request.PreviewId,
            ct);

        var preview = previewResponse?.Preview;

        if (preview is null)
        {
            throw new FileNotFoundException(
                $"Public Cutover preview '{request.PreviewId}' was not found.");
        }

        EnsureCatalogPreviewMatches(
            preview,
            catalogEntryId,
            request.CandidateId);

        var candidateResponse = await _candidateHistory.GetCandidateAsync(
            request.CandidateId,
            ct);

        var candidate = candidateResponse?.Candidate;

        if (candidate is null)
        {
            throw new FileNotFoundException(
                $"Production Restore candidate '{request.CandidateId}' was not found.");
        }

        EnsureCatalogCandidateMatches(
            candidate,
            catalogEntryId,
            preview);

        // CatalogProductionRestorePlanService resolves only the managed catalog
        // payload and performs read-only domain/certificate/NPM discovery.
        var planResponse = await _catalogPlanService.CreatePlanAsync(
            catalogEntryId,
            new CatalogProductionRestorePlanRequest(
                StagingId: null,
                CandidateId: candidate.CandidateId,
                TargetStackSlug: candidate.TargetStackSlug,
                RestoreMode: candidate.RestoreMode,
                IntendedMatrixHost: preview.Routes.Matrix.Host,
                IntendedElementHost: preview.Routes.Element.Host),
            ct);

        var plan = planResponse.Plan;
        var confirmationId = CreateConfirmationId();
        var checks = new List<CatalogPublicCutoverConfirmationCheck>();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        warnings.AddRange(plan.Warnings);
        warnings.AddRange(preview.Warnings.Select(warning => $"Preview warning: {warning}"));
        warnings.AddRange(candidate.Warnings.Select(warning => $"Candidate warning: {warning}"));
        warnings.Add("This catalog-native cutover confirmation read the managed Backup Catalog payload, private candidate history, and saved preview only. It did not read the original uploaded ZIP.");
        errors.AddRange(plan.Errors);

        AddCheck(
            checks,
            "catalog-public-cutover-confirmation.source.catalog-linked",
            "source",
            "info",
            true,
            "satisfied",
            "Saved preview, selected candidate, and fresh Production Restore plan are linked to the requested Backup Catalog entry.",
            catalogEntryId);

        var previewSummary = BuildPreviewSummary(
            preview,
            catalogEntryId,
            candidate.CandidateId,
            checks,
            blockers);

        var candidateSummary = BuildCandidateSummary(
            candidate,
            catalogEntryId,
            planResponse.RestoreSessionId,
            checks,
            blockers);

        AddFreshPlanChecks(
            plan,
            catalogEntryId,
            checks,
            blockers);

        var elementRequired = !string.IsNullOrWhiteSpace(preview.Routes.Element.Host);

        var matrixCertificate = BuildCertificateGate(
            component: "matrix",
            host: preview.Routes.Matrix.Host,
            required: true,
            certificate: plan.Certificates.MatrixCertificate,
            checks,
            blockers);

        var elementCertificate = BuildCertificateGate(
            component: "element",
            host: preview.Routes.Element.Host,
            required: elementRequired,
            certificate: plan.Certificates.ElementCertificate,
            checks,
            blockers);

        var certificates = new CatalogPublicCutoverConfirmationCertificateSummary(
            Matrix: matrixCertificate,
            Element: elementCertificate,
            AllRequiredCertificatesReady: matrixCertificate.Passed &&
                (!elementRequired || elementCertificate.Passed),
            Notes:
            [
                "Certificate readiness is derived from a fresh catalog-native Production Restore plan.",
                "This confirmation does not request, renew, import, attach, or remove certificates."
            ]);

        var matrixRoute = BuildRouteGate(
            component: "matrix",
            previewAction: preview.Routes.Matrix,
            freshRoute: plan.Npm.MatrixRoute,
            certificateReady: matrixCertificate.Passed,
            stableCutoverAlias: BuildStableCutoverAlias("matrix", candidate.TargetStackSlug),
            required: true,
            checks,
            blockers);

        var elementRoute = BuildRouteGate(
            component: "element",
            previewAction: preview.Routes.Element,
            freshRoute: plan.Npm.ElementRoute,
            certificateReady: elementCertificate.Passed,
            stableCutoverAlias: BuildStableCutoverAlias("element", candidate.TargetStackSlug),
            required: elementRequired,
            checks,
            blockers);

        var routes = new CatalogPublicCutoverConfirmationRouteSummary(
            Matrix: matrixRoute,
            Element: elementRoute,
            AllRequiredRoutesEligible: matrixRoute.Passed &&
                (!elementRequired || elementRoute.Passed),
            LiveNpmStateMatchesPreview: matrixRoute.LiveStateMatchesPreview &&
                (!elementRequired || elementRoute.LiveStateMatchesPreview),
            Notes:
            [
                "Confirmation gates compare saved catalog preview evidence with fresh read-only plan evidence.",
                "Route actions remain descriptive only; no public route is created or changed here.",
                "A later executor must repeat health, route ownership, and certificate checks immediately before any mutation."
            ]);

        if (!routes.AllRequiredRoutesEligible)
        {
            blockers.Add("One or more required public routes are not eligible for a future gated executor.");
        }

        if (!routes.LiveNpmStateMatchesPreview)
        {
            blockers.Add("Live NPM route state does not match the saved catalog-native Public Cutover preview.");
        }

        var acknowledgements = BuildAcknowledgements(request);
        var acknowledgementsPassed = acknowledgements
            .Where(acknowledgement => acknowledgement.Required)
            .All(acknowledgement => acknowledgement.Acknowledged);

        foreach (var acknowledgement in acknowledgements.Where(
                     acknowledgement => acknowledgement.Required && !acknowledgement.Acknowledged))
        {
            blockers.Add($"Required acknowledgement missing: {acknowledgement.Label}");
        }

        AddCheck(
            checks,
            "catalog-public-cutover-confirmation.acknowledgements.required",
            "acknowledgement",
            acknowledgementsPassed ? "info" : "blocker",
            acknowledgementsPassed,
            acknowledgementsPassed ? "satisfied" : "blocked",
            acknowledgementsPassed
                ? "All required catalog-native Public Cutover acknowledgements were supplied."
                : "One or more required catalog-native Public Cutover acknowledgements are missing.",
            null);

        AddCheck(
            checks,
            "catalog-public-cutover-confirmation.execution.locked",
            "execution-lock",
            "info",
            true,
            "satisfied",
            "This confirmation endpoint remains read-only; a separate recently stepped-up execution request is required.",
            "No mutation occurs here. A passing confirmation may be consumed only by the existing browser-step-up public-cutover executor.");

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
                "Catalog-native Public Cutover Confirmation is read-only.",
                "No production containers were stopped, deleted, replaced, or modified.",
                "No production databases were created, dropped, overwritten, or modified.",
                "No DNS records were created, updated, or deleted.",
                "No NPM routes were created, updated, enabled, disabled, or deleted.",
                "No certificates were requested, imported, renewed, attached, or removed.",
                "No public federation exposure was created.",
                "Execution is available only through the separate browser-session public-cutover executor after recent step-up."
            ]);

        var distinctBlockers = blockers.Distinct(StringComparer.Ordinal).ToList();
        var distinctWarnings = warnings.Distinct(StringComparer.Ordinal).ToList();
        var distinctErrors = errors.Distinct(StringComparer.Ordinal).ToList();

        var executionAvailable = distinctErrors.Count == 0 && distinctBlockers.Count == 0;

        var status = distinctErrors.Count > 0
            ? "error"
            : distinctBlockers.Count > 0
                ? "blocked"
                : "confirmed_ready_for_execution";

        var result = new CatalogPublicCutoverConfirmationResult(
            Source: "control-plane",
            Status: status,
            ConfirmationId: confirmationId,
            CatalogEntryId: planResponse.CatalogEntryId,
            SourceKind: "backup-catalog",
            RestoreSessionId: planResponse.RestoreSessionId,
            PreviewId: preview.PreviewId,
            CandidateId: candidate.CandidateId,
            FreshPlanId: plan.PlanId,
            RestoreMode: candidate.RestoreMode,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Operator: NormalizeOptional(request.Operator),
            Note: NormalizeOptional(request.Note),
            ProductionExecutionLocked: true,
            ExecutionAvailable: executionAvailable,
            Mutations: mutations,
            Preview: previewSummary,
            Candidate: candidateSummary,
            Certificates: certificates,
            Routes: routes,
            Acknowledgements: acknowledgements,
            Checks: checks,
            Blockers: distinctBlockers,
            Warnings: distinctWarnings,
            Errors: distinctErrors,
            Detail: status switch
            {
                "error" => "Catalog-native Public Cutover confirmation could not complete cleanly. No public mutation was performed.",
                "blocked" => "Catalog-native Public Cutover confirmation gates did not pass. No public mutation was performed.",
                _ => "Catalog-native Public Cutover confirmation gates passed. The saved evidence may be consumed by the separate recently stepped-up executor; no mutation occurred here."
            });

        await _confirmationHistory.SaveConfirmationAsync(
            result,
            ct);

        _logger.LogInformation(
            "Evaluated catalog-native Public Cutover confirmation. ConfirmationId={ConfirmationId} CatalogEntryId={CatalogEntryId} PreviewId={PreviewId} CandidateId={CandidateId} FreshPlanId={FreshPlanId} Status={Status}",
            result.ConfirmationId,
            result.CatalogEntryId,
            result.PreviewId,
            result.CandidateId,
            result.FreshPlanId,
            result.Status);

        return new CatalogPublicCutoverConfirmationResponse(
            Source: "control-plane",
            Status: result.Status,
            CatalogEntryId: planResponse.CatalogEntryId,
            RestoreSessionId: planResponse.RestoreSessionId,
            RestoreAttemptCreated: planResponse.RestoreAttemptCreated,
            RestoreAttemptResumed: planResponse.RestoreAttemptResumed,
            PreviewId: preview.PreviewId,
            CandidateId: candidate.CandidateId,
            PayloadState: planResponse.PayloadState,
            IntegrityStatus: planResponse.IntegrityStatus,
            WarningCount: planResponse.WarningCount,
            Confirmation: result,
            Detail: result.Status == "blocked"
                ? "Catalog-native Public Cutover confirmation was recorded, but blockers require attention before any future execution sprint. Public cutover remains locked."
                : result.Status == "error"
                    ? "Catalog-native Public Cutover confirmation was recorded with errors. Public cutover remains locked."
                    : "Catalog-native Public Cutover confirmation is ready for future executor review. Public cutover remains locked.");
    }

    private static void EnsureCatalogPreviewMatches(
        RuntimeStackBackupPublicCutoverPreviewResult preview,
        string catalogEntryId,
        string candidateId)
    {
        if (!string.Equals(preview.SourceKind, "backup-catalog", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(preview.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Public Cutover preview '{preview.PreviewId}' does not belong to Backup Catalog entry '{catalogEntryId}'.");
        }

        if (!string.Equals(preview.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Public Cutover preview '{preview.PreviewId}' belongs to candidate '{preview.CandidateId}', not '{candidateId}'.");
        }
    }

    private static void EnsureCatalogCandidateMatches(
        RuntimeStackBackupProductionCandidateResult candidate,
        string catalogEntryId,
        RuntimeStackBackupPublicCutoverPreviewResult preview)
    {
        if (!string.Equals(candidate.SourceKind, "backup-catalog", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Production Restore candidate '{candidate.CandidateId}' does not belong to Backup Catalog entry '{catalogEntryId}'.");
        }

        if (!string.Equals(candidate.CandidateId, preview.CandidateId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Production Restore candidate '{candidate.CandidateId}' does not match Public Cutover preview candidate '{preview.CandidateId}'.");
        }
    }

    private static CatalogPublicCutoverConfirmationPreviewSummary BuildPreviewSummary(
        RuntimeStackBackupPublicCutoverPreviewResult preview,
        string catalogEntryId,
        string candidateId,
        List<CatalogPublicCutoverConfirmationCheck> checks,
        List<string> blockers)
    {
        var mutationFlagsClear = AreMutationFlagsClear(preview.Mutations);
        var catalogEntryMatches = string.Equals(
            preview.CatalogEntryId,
            catalogEntryId,
            StringComparison.OrdinalIgnoreCase);
        var candidateMatches = string.Equals(
            preview.CandidateId,
            candidateId,
            StringComparison.OrdinalIgnoreCase);
        var readyForReview = string.Equals(preview.Status, "ready_for_review", StringComparison.OrdinalIgnoreCase) &&
            preview.ProductionExecutionLocked &&
            mutationFlagsClear &&
            preview.Blockers.Count == 0 &&
            preview.Errors.Count == 0 &&
            catalogEntryMatches &&
            candidateMatches;

        AddCheck(
            checks,
            "catalog-public-cutover-confirmation.preview.catalog-linked",
            "preview",
            catalogEntryMatches && candidateMatches ? "info" : "blocker",
            catalogEntryMatches && candidateMatches,
            catalogEntryMatches && candidateMatches ? "satisfied" : "blocked",
            catalogEntryMatches && candidateMatches
                ? "Saved Public Cutover preview is linked to the requested catalog entry and candidate."
                : "Saved Public Cutover preview does not match the requested catalog entry or candidate.",
            preview.PreviewId);

        AddCheck(
            checks,
            "catalog-public-cutover-confirmation.preview.ready-for-review",
            "preview",
            readyForReview ? "info" : "blocker",
            readyForReview,
            readyForReview ? "satisfied" : "blocked",
            readyForReview
                ? "Saved catalog-native Public Cutover preview is ready for confirmation review."
                : "Saved catalog-native Public Cutover preview is not ready for confirmation review.",
            preview.Status);

        if (!readyForReview)
        {
            blockers.Add("Saved catalog-native Public Cutover preview must be ready_for_review, locked, mutation-free, and without blockers/errors.");
        }

        return new CatalogPublicCutoverConfirmationPreviewSummary(
            PreviewFound: true,
            PreviewId: preview.PreviewId,
            Status: preview.Status,
            SourceKind: preview.SourceKind,
            CatalogEntryId: preview.CatalogEntryId,
            CandidateId: preview.CandidateId,
            RestoreSessionId: preview.RestoreSessionId,
            CreatedAtUtc: preview.CreatedAtUtc,
            ProductionExecutionLocked: preview.ProductionExecutionLocked,
            MutationFlagsClear: mutationFlagsClear,
            HasNoBlockers: preview.Blockers.Count == 0,
            HasNoErrors: preview.Errors.Count == 0,
            ReadyForReview: readyForReview,
            CatalogEntryMatches: catalogEntryMatches,
            CandidateMatches: candidateMatches,
            Detail: preview.Detail);
    }

    private static CatalogPublicCutoverConfirmationCandidateSummary BuildCandidateSummary(
        RuntimeStackBackupProductionCandidateResult candidate,
        string catalogEntryId,
        string restoreSessionId,
        List<CatalogPublicCutoverConfirmationCheck> checks,
        List<string> blockers)
    {
        var destroyed = candidate.Destroy is not null ||
            string.Equals(candidate.Status, "destroyed", StringComparison.OrdinalIgnoreCase);
        var catalogEntryMatches = string.Equals(
            candidate.CatalogEntryId,
            catalogEntryId,
            StringComparison.OrdinalIgnoreCase);
        var restoreSessionMatches = !string.IsNullOrWhiteSpace(candidate.RestoreSessionId) &&
            string.Equals(candidate.RestoreSessionId, restoreSessionId, StringComparison.OrdinalIgnoreCase);
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
            "catalog-public-cutover-confirmation.candidate.catalog-linked",
            "production-candidate",
            catalogEntryMatches && restoreSessionMatches ? "info" : "blocker",
            catalogEntryMatches && restoreSessionMatches,
            catalogEntryMatches && restoreSessionMatches ? "satisfied" : "blocked",
            catalogEntryMatches && restoreSessionMatches
                ? "Private candidate is linked to the requested Backup Catalog entry and restore session."
                : "Private candidate does not match the requested Backup Catalog entry or active restore session.",
            candidate.CandidateId);

        if (!catalogEntryMatches || !restoreSessionMatches)
        {
            blockers.Add("Private candidate must be linked to the requested Backup Catalog entry and active restore session.");
        }

        AddCheck(
            checks,
            "catalog-public-cutover-confirmation.candidate.matrix-private-ready",
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
            "catalog-public-cutover-confirmation.candidate.element-private-ready",
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

        return new CatalogPublicCutoverConfirmationCandidateSummary(
            CandidateFound: true,
            CandidateId: candidate.CandidateId,
            Status: candidate.Status,
            SourceKind: candidate.SourceKind,
            CatalogEntryId: candidate.CatalogEntryId,
            RestoreSessionId: candidate.RestoreSessionId,
            PrivateRuntimeStatus: candidate.PrivateRuntimeStatus,
            PrivateOnly: candidate.Safety.PrivateOnly,
            Destroyed: destroyed,
            DatabaseImportSucceeded: candidate.Database.ImportSucceeded,
            SynapseHealthPassed: candidate.Runtime.SynapseHealthPassed,
            ElementHealthPassed: candidate.Runtime.ElementHealthPassed,
            MatrixServerName: candidate.MatrixServerName,
            SynapseContainerName: candidate.SynapseContainerName,
            ElementContainerName: candidate.ElementContainerName,
            CatalogEntryMatches: catalogEntryMatches,
            RestoreSessionMatches: restoreSessionMatches,
            Detail: candidate.Detail);
    }

    private static void AddFreshPlanChecks(
        RuntimeStackBackupProductionRestorePlanResult plan,
        string catalogEntryId,
        List<CatalogPublicCutoverConfirmationCheck> checks,
        List<string> blockers)
    {
        var sourceMatches = string.Equals(plan.SourceKind, "backup-catalog", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(plan.CatalogEntryId, catalogEntryId, StringComparison.OrdinalIgnoreCase);
        var mutationFlagsClear = AreMutationFlagsClear(plan.Mutations);
        var planClean = sourceMatches &&
            plan.Errors.Count == 0 &&
            plan.Blockers.Count == 0 &&
            mutationFlagsClear &&
            plan.ProductionExecutionLocked;

        AddCheck(
            checks,
            "catalog-public-cutover-confirmation.fresh-plan.catalog-linked",
            "production-plan",
            sourceMatches ? "info" : "blocker",
            sourceMatches,
            sourceMatches ? "satisfied" : "blocked",
            sourceMatches
                ? "Fresh Production Restore plan was sourced from the requested Backup Catalog entry."
                : "Fresh Production Restore plan was not sourced from the requested Backup Catalog entry.",
            plan.PlanId);

        AddCheck(
            checks,
            "catalog-public-cutover-confirmation.fresh-plan.clean",
            "production-plan",
            planClean ? "info" : "blocker",
            planClean,
            planClean ? "satisfied" : "blocked",
            planClean
                ? "Fresh catalog-native Production Restore plan has no blockers/errors and no mutation flags."
                : "Fresh catalog-native Production Restore plan is not clean.",
            plan.PlanId);

        if (!sourceMatches)
        {
            blockers.Add("Fresh Production Restore plan must remain linked to the requested Backup Catalog entry.");
        }

        foreach (var blocker in plan.Blockers)
        {
            blockers.Add($"Fresh catalog-native Production Restore plan blocker: {blocker}");
        }

        if (plan.Errors.Count > 0)
        {
            blockers.Add("Fresh catalog-native Production Restore plan contains errors.");
        }
    }

    private static CatalogPublicCutoverConfirmationCertificateGate BuildCertificateGate(
        string component,
        string? host,
        bool required,
        RuntimeStackBackupProductionRestoreCertificateMatch? certificate,
        List<CatalogPublicCutoverConfirmationCheck> checks,
        List<string> blockers)
    {
        var certificateFound = certificate is not null;
        var passed = !required || (certificate is not null &&
            certificate.NpmCertificateId is > 0 &&
            certificate.ImportedToNpm &&
            !certificate.IsStaging);
        var status = !required
            ? "not_required"
            : passed
                ? "satisfied"
                : "blocked";
        var detail = !required
            ? "This route is not required because no public host was requested."
            : !certificateFound
                ? "No matching certificate was found."
                : certificate!.NpmCertificateId is not > 0
                    ? "The matching certificate is not associated with an NPM certificate id."
                    : !certificate.ImportedToNpm
                        ? "The matching certificate has not been imported into NPM."
                        : certificate.IsStaging
                            ? "A staging certificate cannot be used for public cutover."
                            : "A non-staging matching certificate is imported into NPM.";

        AddCheck(
            checks,
            $"catalog-public-cutover-confirmation.certificate.{component}",
            "certificate",
            passed ? "info" : "blocker",
            passed,
            status,
            passed
                ? $"{component} certificate confirmation gate passed."
                : $"{component} certificate confirmation gate did not pass.",
            detail);

        if (!passed && required)
        {
            blockers.Add($"{component} certificate is not ready for a future gated executor: {detail}");
        }

        return new CatalogPublicCutoverConfirmationCertificateGate(
            Component: component,
            Host: host,
            Required: required,
            CertificateFound: certificateFound,
            CertificateEntityId: certificate?.CertificateEntityId,
            CommonName: certificate?.CommonName,
            NpmCertificateId: certificate?.NpmCertificateId,
            ImportedToNpm: certificate?.ImportedToNpm ?? false,
            IsStaging: certificate?.IsStaging ?? false,
            Passed: passed,
            Status: status,
            Detail: detail);
    }

    private static CatalogPublicCutoverConfirmationRouteGate BuildRouteGate(
        string component,
        RuntimeStackBackupPublicCutoverPreviewRouteAction previewAction,
        RuntimeStackBackupProductionRestoreNpmRouteSummary? freshRoute,
        bool certificateReady,
        string stableCutoverAlias,
        bool required,
        List<CatalogPublicCutoverConfirmationCheck> checks,
        List<string> blockers)
    {
        var previewActionEligible = IsEligiblePreviewAction(
            previewAction.Action,
            required);
        var liveStateMatchesPreview = FreshRouteStateMatchesPreview(
            previewAction,
            freshRoute,
            stableCutoverAlias);
        var existingRouteAlreadyTargetsCandidate = freshRoute is not null &&
            RouteTargetsPreviewCandidate(
                freshRoute,
                previewAction.UpstreamContainerName,
                stableCutoverAlias,
                previewAction.UpstreamPort,
                previewAction.NpmCertificateId);
        var passed = !required || (previewActionEligible &&
            previewAction.AvailableForFutureExecution &&
            certificateReady &&
            liveStateMatchesPreview);
        var status = !required
            ? "not_required"
            : passed
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
            $"catalog-public-cutover-confirmation.route.{component}",
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
            blockers.Add($"{component} route is not eligible for a future gated executor: {detail}");
        }

        return new CatalogPublicCutoverConfirmationRouteGate(
            Component: component,
            Host: previewAction.Host,
            Required: required,
            PreviewAction: previewAction.Action,
            PreviewAvailableForFutureExecution: previewAction.AvailableForFutureExecution,
            PreviewRouteCurrentlyExists: previewAction.RouteCurrentlyExists,
            FreshRouteCurrentlyExists: freshRoute is not null,
            LiveStateMatchesPreview: liveStateMatchesPreview,
            CertificateReady: certificateReady,
            ExistingRouteAlreadyTargetsCandidate: existingRouteAlreadyTargetsCandidate,
            UpstreamContainerName: previewAction.UpstreamContainerName,
            UpstreamPort: previewAction.UpstreamPort,
            Passed: passed,
            Status: status,
            Detail: detail);
    }

    private static IReadOnlyList<CatalogPublicCutoverConfirmationAcknowledgement> BuildAcknowledgements(
        CatalogPublicCutoverConfirmationRequest request)
    {
        return
        [
            BuildAcknowledgement(
                "catalog-public-cutover.ack.preview-reviewed",
                "Preview reviewed",
                "Operator reviewed the saved catalog-native Public Cutover preview and understands the calculated future Matrix and Element route actions.",
                "warning",
                request.AcknowledgePreviewReviewed),
            BuildAcknowledgement(
                "catalog-public-cutover.ack.candidate-private-healthy",
                "Candidate private and healthy",
                "Operator confirms the selected catalog-backed candidate is the intended private runtime and Matrix/Element health evidence is acceptable.",
                "warning",
                request.AcknowledgeCandidateIsPrivateAndHealthy),
            BuildAcknowledgement(
                "catalog-public-cutover.ack.public-exposure-risk",
                "Public exposure risk understood",
                "Operator understands a later executor will expose Matrix federation/client traffic and Element web traffic publicly.",
                "danger",
                request.AcknowledgePublicRouteExposureRisk),
            BuildAcknowledgement(
                "catalog-public-cutover.ack.no-automatic-rollback",
                "No automatic rollback assumed",
                "Operator understands confirmation gates do not provide rollback and that a later executor must have explicit rollback rules.",
                "danger",
                request.AcknowledgeNoAutomaticRollback),
            BuildAcknowledgement(
                "catalog-public-cutover.ack.final-backup-required",
                "Final backup required",
                "Operator acknowledges a final pre-cutover backup/snapshot is required before any future public route mutation.",
                "danger",
                request.AcknowledgeFinalBackupRequired),
            BuildAcknowledgement(
                "catalog-public-cutover.ack.execution-still-locked",
                "Execution still locked",
                "Operator understands this endpoint does not execute cutover and public execution remains unavailable in this slice.",
                "info",
                request.AcknowledgeExecutionStillLocked)
        ];
    }

    private static CatalogPublicCutoverConfirmationAcknowledgement BuildAcknowledgement(
        string code,
        string label,
        string description,
        string severity,
        bool acknowledged)
    {
        return new CatalogPublicCutoverConfirmationAcknowledgement(
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
                : "Fresh live NPM route does not target the candidate upstream expected by the saved preview.";
        }

        return freshRoute is null
            ? "No existing route remains; a future executor would create the route."
            : "Existing route still targets the expected candidate upstream.";
    }

    private static bool AreMutationFlagsClear(
        RuntimeStackBackupProductionRestoreMutationSummary mutations)
    {
        return !mutations.RuntimeChanged &&
            !mutations.ProductionContainersTouched &&
            !mutations.ProductionDatabasesTouched &&
            !mutations.DnsChanged &&
            !mutations.NpmRoutesChanged &&
            !mutations.CertificatesChanged &&
            !mutations.PublicRoutesChanged &&
            !mutations.FederationExposureChanged;
    }

    private static void AddCheck(
        List<CatalogPublicCutoverConfirmationCheck> checks,
        string code,
        string category,
        string severity,
        bool passed,
        string status,
        string message,
        string? detail)
    {
        checks.Add(new CatalogPublicCutoverConfirmationCheck(
            Code: code,
            Category: category,
            Severity: severity,
            Status: status,
            Passed: passed,
            Message: message,
            Detail: detail));
    }

    private static string BuildStableCutoverAlias(
        string component,
        string targetStackSlug)
    {
        return $"mem-{component}-{Slugify(targetStackSlug)}";
    }

    private static string Slugify(string value)
    {
        var normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());

        var collapsed = string.Join(
            '-',
            normalized.Split('-', StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(collapsed)
            ? "restored"
            : collapsed;
    }

    private static string NormalizeHost(string? host)
    {
        return (host ?? string.Empty)
            .Trim()
            .TrimEnd('.')
            .ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
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

        if (value.Contains('/', StringComparison.Ordinal) ||
            value.Contains('\\', StringComparison.Ordinal) ||
            value.Contains(':', StringComparison.Ordinal) ||
            value.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label} contains unsafe characters.");
        }
    }

    private static string CreateConfirmationId()
    {
        return $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssZ}-{Guid.NewGuid():N}"[..25];
    }
}
