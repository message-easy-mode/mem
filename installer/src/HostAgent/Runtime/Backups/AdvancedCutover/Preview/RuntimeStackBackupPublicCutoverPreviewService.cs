using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.AdvancedCutover.Preflight;
using Microsoft.Extensions.Logging;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Preview;

public sealed class RuntimeStackBackupPublicCutoverPreviewService
{
    private readonly RuntimeStackBackupProductionRestorePlanService _planService;
    private readonly CatalogProductionRestorePlanService _catalogPlanService;
    private readonly RuntimeStackBackupProductionCandidateHistoryService _candidateHistory;
    private readonly ValidatedImportArtifactService _validatedImportArtifacts;
    private readonly RuntimeStackBackupPublicCutoverPreviewHistoryService _previewHistory;
    private readonly ILogger<RuntimeStackBackupPublicCutoverPreviewService> _logger;

    public RuntimeStackBackupPublicCutoverPreviewService(
        RuntimeStackBackupProductionRestorePlanService planService,
        CatalogProductionRestorePlanService catalogPlanService,
        RuntimeStackBackupProductionCandidateHistoryService candidateHistory,
        ValidatedImportArtifactService validatedImportArtifacts,
        RuntimeStackBackupPublicCutoverPreviewHistoryService previewHistory,
        ILogger<RuntimeStackBackupPublicCutoverPreviewService> logger)
    {
        _planService = planService;
        _catalogPlanService = catalogPlanService;
        _candidateHistory = candidateHistory;
        _validatedImportArtifacts = validatedImportArtifacts;
        _previewHistory = previewHistory;
        _logger = logger;
    }

    public async Task<RuntimeStackBackupPublicCutoverPreviewResult> CreatePreviewAsync(
        string validationId,
        string candidateId,
        string? targetStackSlug,
        string? restoreMode,
        string? intendedMatrixHost,
        string? intendedElementHost,
        CancellationToken ct)
    {
        ValidatePathSegment(validationId, "Validation id");
        ValidatePathSegment(candidateId, "Production Restore candidate id");

        await _validatedImportArtifacts.EnsureRestoreCanContinueAsync(
            validationId,
            ct);

        var previewId = CreatePreviewId();
        var checks = new List<RuntimeStackBackupPublicCutoverPreviewCheck>();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        var candidateResponse = await _candidateHistory.GetCandidateAsync(
            candidateId,
            ct);

        var candidate = candidateResponse?.Candidate;

        if (candidate is null)
        {
            throw new FileNotFoundException($"Production Restore candidate '{candidateId}' was not found.");
        }

        if (!string.Equals(candidate.ValidationId, validationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Production Restore candidate '{candidateId}' belongs to validation '{candidate.ValidationId}', not '{validationId}'.");
        }

        var effectiveRestoreMode = string.IsNullOrWhiteSpace(restoreMode)
            ? candidate.RestoreMode
            : restoreMode.Trim();

        var effectiveTargetStackSlug = string.IsNullOrWhiteSpace(targetStackSlug)
            ? candidate.TargetStackSlug
            : Slugify(targetStackSlug);

        var plan = await _planService.CreatePlanAsync(
            validationId,
            stagingId: null,
            candidateId: candidateId,
            targetStackSlug: effectiveTargetStackSlug,
            restoreMode: effectiveRestoreMode,
            intendedMatrixHost: intendedMatrixHost,
            intendedElementHost: intendedElementHost,
            ct: ct);

        warnings.AddRange(plan.Warnings);
        errors.AddRange(plan.Errors);

        AddCheck(
            checks,
            "public-cutover-preview.plan.generated",
            "production-plan",
            plan.Status == "blocked" ? "warning" : "info",
            plan.Errors.Count == 0,
            plan.Status == "blocked" ? "requires_review" : "satisfied",
            "A fresh read-only Production Restore plan was generated and linked to this preview.",
            plan.PlanId);

        var candidateSummary = BuildCandidateSummary(
            candidate,
            checks,
            blockers,
            warnings);

        var matrixAction = BuildMatrixAction(
            candidate,
            plan,
            FirstNonEmpty(
                intendedMatrixHost,
                plan.Dns.IntendedMatrixHost,
                plan.MatrixIdentity.TargetServerName,
                plan.MatrixIdentity.SourceServerName),
            checks,
            blockers,
            warnings);

        var elementAction = BuildElementAction(
            candidate,
            plan,
            FirstNonEmpty(
                intendedElementHost,
                plan.Dns.IntendedElementHost),
            checks,
            blockers,
            warnings);

        AddCheck(
            checks,
            "public-cutover-preview.execution.locked",
            "execution-lock",
            "info",
            true,
            "satisfied",
            "Public cutover execution is locked in this sprint.",
            "This preview calculates future NPM route actions only. It does not call NPM create/update/delete, mutate DNS, touch certificates, register production runtime, or expose federation.");

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
                "Public Cutover Preview v1 is read-only.",
                "No production containers were stopped, deleted, replaced, or modified.",
                "No production databases were created, dropped, overwritten, or modified.",
                "No DNS records were created, updated, or deleted.",
                "No NPM routes were created, updated, enabled, disabled, or deleted.",
                "No certificates were requested, imported, renewed, attached, or removed.",
                "No public federation exposure was created."
            ]);

        var routeSummary = new RuntimeStackBackupPublicCutoverPreviewRouteSummary(
            Matrix: matrixAction,
            Element: elementAction,
            Notes:
            [
                "Route actions describe what a future gated executor may do.",
                "This preview is intentionally safe to run repeatedly.",
                "Existing public routes are treated conservatively: a mismatched existing route blocks automatic future execution until the operator resolves ownership."
            ]);

        AddPlanBlockers(
            plan,
            blockers);

        var status = errors.Count > 0 || blockers.Count > 0
            ? "blocked"
            : "ready_for_review";

        var result = new RuntimeStackBackupPublicCutoverPreviewResult(
            Source: "control-plane",
            Status: status,
            PreviewId: previewId,
            ValidationId: validationId,
            CandidateId: candidateId,
            PlanId: plan.PlanId,
            RestoreMode: effectiveRestoreMode,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ProductionExecutionLocked: true,
            Mutations: mutations,
            Candidate: candidateSummary,
            Routes: routeSummary,
            Checks: checks,
            Blockers: blockers.Distinct(StringComparer.Ordinal).ToList(),
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToList(),
            Errors: errors.Distinct(StringComparer.Ordinal).ToList(),
            Detail: status == "blocked"
                ? "Public Cutover preview was generated, but blockers must be resolved before any future execution sprint. No public mutation was performed."
                : "Public Cutover preview is ready for operator review. It is read-only; public execution remains locked.");

        await _previewHistory.SavePreviewAsync(
            result,
            ct);

        _logger.LogInformation(
            "Created read-only Public Cutover preview. PreviewId={PreviewId} ValidationId={ValidationId} CandidateId={CandidateId} PlanId={PlanId} Status={Status}",
            result.PreviewId,
            result.ValidationId,
            result.CandidateId,
            result.PlanId,
            result.Status);

        return result;
    }


    /// <summary>
    /// Creates a read-only public cutover preview for a private candidate that
    /// was created from canonical Backup Catalog material. This path never
    /// validates or reads a legacy uploaded-ZIP receipt.
    /// </summary>
    public async Task<CatalogPublicCutoverPreviewResponse> CreatePreviewFromCatalogAsync(
        string catalogEntryId,
        CatalogPublicCutoverPreviewRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidatePathSegment(catalogEntryId, "Backup Catalog entry id");
        ValidatePathSegment(request.CandidateId, "Production Restore candidate id");

        var candidateResponse = await _candidateHistory.GetCandidateAsync(
            request.CandidateId,
            ct);

        var candidate = candidateResponse?.Candidate;

        if (candidate is null)
        {
            throw new FileNotFoundException(
                $"Production Restore candidate '{request.CandidateId}' was not found.");
        }

        if (!string.Equals(
                candidate.SourceKind,
                "backup-catalog",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                candidate.CatalogEntryId,
                catalogEntryId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Production Restore candidate '{candidate.CandidateId}' does not belong to Backup Catalog entry '{catalogEntryId}'.");
        }

        var effectiveRestoreMode = string.IsNullOrWhiteSpace(request.RestoreMode)
            ? candidate.RestoreMode
            : request.RestoreMode.Trim();

        var effectiveTargetStackSlug = string.IsNullOrWhiteSpace(request.TargetStackSlug)
            ? candidate.TargetStackSlug
            : Slugify(request.TargetStackSlug);

        var planResponse = await _catalogPlanService.CreatePlanAsync(
            catalogEntryId,
            new CatalogProductionRestorePlanRequest(
                StagingId: null,
                CandidateId: candidate.CandidateId,
                TargetStackSlug: effectiveTargetStackSlug,
                RestoreMode: effectiveRestoreMode,
                IntendedMatrixHost: request.IntendedMatrixHost,
                IntendedElementHost: request.IntendedElementHost),
            ct);

        var plan = planResponse.Plan;
        var previewId = CreatePreviewId();
        var checks = new List<RuntimeStackBackupPublicCutoverPreviewCheck>();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        warnings.AddRange(plan.Warnings);
        warnings.Add("This catalog-native Public Cutover preview read the managed Backup Catalog payload and private candidate history only. It did not read the original uploaded ZIP.");
        errors.AddRange(plan.Errors);

        AddCheck(
            checks,
            "public-cutover-preview.catalog-source-linked",
            "source",
            "info",
            true,
            "satisfied",
            "The private candidate and Production Restore plan are linked to the requested Backup Catalog entry.",
            planResponse.CatalogEntryId);

        AddCheck(
            checks,
            "public-cutover-preview.plan.generated",
            "production-plan",
            plan.Status == "blocked" ? "warning" : "info",
            plan.Errors.Count == 0,
            plan.Status == "blocked" ? "requires_review" : "satisfied",
            "A fresh catalog-native, read-only Production Restore plan was generated and linked to this preview.",
            plan.PlanId);

        var candidateSummary = BuildCandidateSummary(
            candidate,
            checks,
            blockers,
            warnings);

        var matrixAction = BuildMatrixAction(
            candidate,
            plan,
            FirstNonEmpty(
                request.IntendedMatrixHost,
                plan.Dns.IntendedMatrixHost,
                plan.MatrixIdentity.TargetServerName,
                plan.MatrixIdentity.SourceServerName),
            checks,
            blockers,
            warnings);

        var elementAction = BuildElementAction(
            candidate,
            plan,
            FirstNonEmpty(
                request.IntendedElementHost,
                plan.Dns.IntendedElementHost),
            checks,
            blockers,
            warnings);

        AddCheck(
            checks,
            "public-cutover-preview.execution.locked",
            "execution-lock",
            "info",
            true,
            "satisfied",
            "Public cutover execution is locked in this sprint.",
            "This preview calculates future NPM route actions only. It does not call NPM create/update/delete, mutate DNS, touch certificates, register production runtime, or expose federation.");

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
                "Catalog-native Public Cutover Preview is read-only.",
                "No production containers were stopped, deleted, replaced, or modified.",
                "No production databases were created, dropped, overwritten, or modified.",
                "No DNS records were created, updated, or deleted.",
                "No NPM routes were created, updated, enabled, disabled, or deleted.",
                "No certificates were requested, imported, renewed, attached, or removed.",
                "No public federation exposure was created."
            ]);

        var routeSummary = new RuntimeStackBackupPublicCutoverPreviewRouteSummary(
            Matrix: matrixAction,
            Element: elementAction,
            Notes:
            [
                "Route actions describe what a future gated executor may do.",
                "This catalog-native preview is intentionally safe to run repeatedly.",
                "Existing public routes are treated conservatively: a mismatched existing route blocks automatic future execution until the operator resolves ownership."
            ]);

        AddPlanBlockers(
            plan,
            blockers);

        var status = errors.Count > 0 || blockers.Count > 0
            ? "blocked"
            : "ready_for_review";

        // Candidate.ValidationId is retained for backwards-compatible candidate
        // history. CatalogEntryId and SourceKind below are the canonical source
        // identity for this preview.
        var result = new RuntimeStackBackupPublicCutoverPreviewResult(
            Source: "control-plane",
            Status: status,
            PreviewId: previewId,
            ValidationId: candidate.ValidationId,
            CandidateId: candidate.CandidateId,
            PlanId: plan.PlanId,
            RestoreMode: effectiveRestoreMode,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ProductionExecutionLocked: true,
            Mutations: mutations,
            Candidate: candidateSummary,
            Routes: routeSummary,
            Checks: checks,
            Blockers: blockers.Distinct(StringComparer.Ordinal).ToList(),
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToList(),
            Errors: errors.Distinct(StringComparer.Ordinal).ToList(),
            Detail: status == "blocked"
                ? "Catalog-native Public Cutover preview was generated, but blockers must be resolved before any future execution sprint. No public mutation was performed."
                : "Catalog-native Public Cutover preview is ready for operator review. It is read-only; public execution remains locked.",
            CatalogEntryId: planResponse.CatalogEntryId,
            SourceKind: "backup-catalog",
            RestoreSessionId: planResponse.RestoreSessionId);

        await _previewHistory.SavePreviewAsync(
            result,
            ct);

        _logger.LogInformation(
            "Created catalog-native read-only Public Cutover preview. PreviewId={PreviewId} CatalogEntryId={CatalogEntryId} CandidateId={CandidateId} PlanId={PlanId} Status={Status}",
            result.PreviewId,
            result.CatalogEntryId,
            result.CandidateId,
            result.PlanId,
            result.Status);

        return new CatalogPublicCutoverPreviewResponse(
            Source: "control-plane",
            Status: result.Status,
            CatalogEntryId: planResponse.CatalogEntryId,
            RestoreSessionId: planResponse.RestoreSessionId,
            RestoreAttemptCreated: planResponse.RestoreAttemptCreated,
            RestoreAttemptResumed: planResponse.RestoreAttemptResumed,
            CandidateId: candidate.CandidateId,
            PayloadState: planResponse.PayloadState,
            IntegrityStatus: planResponse.IntegrityStatus,
            WarningCount: planResponse.WarningCount,
            Preview: result,
            Detail: result.Status == "blocked"
                ? "Catalog-native Public Cutover preview was created, but blockers require attention before any future execution sprint. Public cutover remains locked."
                : "Catalog-native Public Cutover preview is ready for operator review. Public cutover remains locked.");
    }

    /// <summary>
    /// Creates a read-only public cutover preview for a Migration-owned private
    /// candidate. Migration identity is the durable source reference; no
    /// Backup Catalog or Restore Session identifier is required.
    /// </summary>
    public async Task<RuntimeStackBackupPublicCutoverPreviewResult>
        CreatePreviewFromMigrationAsync(
            string migrationId,
            RuntimeStackBackupProductionRestoreSourceSummary source,
            string candidateId,
            string? targetStackSlug,
            string? restoreMode,
            string? intendedMatrixHost,
            string? intendedElementHost,
            CancellationToken ct)
    {
        ValidatePathSegment(migrationId, "Migration id");
        ValidatePathSegment(candidateId, "Production Restore candidate id");
        ArgumentNullException.ThrowIfNull(source);

        var candidateResponse = await _candidateHistory.GetCandidateAsync(candidateId, ct);
        var candidate = candidateResponse?.Candidate
            ?? throw new FileNotFoundException(
                $"Production Restore candidate '{candidateId}' was not found.");

        if (!string.Equals(candidate.SourceKind, "migration-session", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.ValidationId, migrationId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Production Restore candidate '{candidate.CandidateId}' does not belong to Migration Session '{migrationId}'.");
        }

        var effectiveRestoreMode = string.IsNullOrWhiteSpace(restoreMode)
            ? candidate.RestoreMode
            : restoreMode.Trim();
        var effectiveTargetStackSlug = string.IsNullOrWhiteSpace(targetStackSlug)
            ? candidate.TargetStackSlug
            : Slugify(targetStackSlug);

        var plan = await _planService.CreatePlanFromMigrationSourceAsync(
            migrationId,
            source,
            candidate.CandidateId,
            effectiveTargetStackSlug,
            effectiveRestoreMode,
            intendedMatrixHost,
            intendedElementHost,
            ct);

        var previewId = CreatePreviewId();
        var checks = new List<RuntimeStackBackupPublicCutoverPreviewCheck>();
        var blockers = new List<string>();
        var warnings = new List<string>();
        var errors = new List<string>();

        warnings.AddRange(plan.Warnings);
        warnings.Add("This Migration-keyed preview used the verified candidate and private staging evidence only. No Backup Catalog item or Restore Session was created.");
        errors.AddRange(plan.Errors);

        AddCheck(
            checks,
            "public-cutover-preview.migration-source-linked",
            "source",
            "info",
            true,
            "satisfied",
            "The private candidate and fresh Production Restore plan are linked to the requested Migration Session.",
            migrationId);

        AddCheck(
            checks,
            "public-cutover-preview.plan.generated",
            "production-plan",
            plan.Status == "blocked" ? "warning" : "info",
            plan.Errors.Count == 0,
            plan.Status == "blocked" ? "requires_review" : "satisfied",
            "A fresh Migration-keyed, read-only Production Restore plan was generated.",
            plan.PlanId);

        var candidateSummary = BuildCandidateSummary(candidate, checks, blockers, warnings);
        var matrixAction = BuildMatrixAction(
            candidate,
            plan,
            FirstNonEmpty(
                intendedMatrixHost,
                plan.Dns.IntendedMatrixHost,
                plan.MatrixIdentity.TargetServerName,
                plan.MatrixIdentity.SourceServerName),
            checks,
            blockers,
            warnings);
        var elementAction = BuildElementAction(
            candidate,
            plan,
            FirstNonEmpty(intendedElementHost, plan.Dns.IntendedElementHost),
            checks,
            blockers,
            warnings);

        AddCheck(
            checks,
            "public-cutover-preview.execution.locked",
            "execution-lock",
            "info",
            true,
            "satisfied",
            "This preview is read-only and performs no public mutation.",
            null);

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
                "Migration-keyed Public Cutover Preview is read-only.",
                "No production runtime, DNS, NPM route, certificate, or public federation state was changed."
            ]);

        var routes = new RuntimeStackBackupPublicCutoverPreviewRouteSummary(
            matrixAction,
            elementAction,
            [
                "Route actions describe what the separately stepped-up executor may do.",
                "Existing route ownership is compared conservatively and drift blocks execution."
            ]);

        AddPlanBlockers(plan, blockers);
        var status = errors.Count > 0 || blockers.Count > 0
            ? "blocked"
            : "ready_for_review";

        var result = new RuntimeStackBackupPublicCutoverPreviewResult(
            Source: "control-plane",
            Status: status,
            PreviewId: previewId,
            ValidationId: migrationId,
            CandidateId: candidate.CandidateId,
            PlanId: plan.PlanId,
            RestoreMode: effectiveRestoreMode,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ProductionExecutionLocked: true,
            Mutations: mutations,
            Candidate: candidateSummary,
            Routes: routes,
            Checks: checks,
            Blockers: blockers.Distinct(StringComparer.Ordinal).ToList(),
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToList(),
            Errors: errors.Distinct(StringComparer.Ordinal).ToList(),
            Detail: status == "blocked"
                ? "Migration-keyed Public Cutover preview was generated, but blockers require attention. No public mutation was performed."
                : "Migration-keyed Public Cutover preview is ready for operator review.",
            CatalogEntryId: null,
            SourceKind: "migration-session",
            RestoreSessionId: null);

        await _previewHistory.SavePreviewAsync(result, ct);

        _logger.LogInformation(
            "Created Migration-keyed read-only Public Cutover preview. MigrationId={MigrationId} PreviewId={PreviewId} CandidateId={CandidateId} PlanId={PlanId} Status={Status}",
            migrationId,
            result.PreviewId,
            result.CandidateId,
            result.PlanId,
            result.Status);

        return result;
    }

    private static RuntimeStackBackupPublicCutoverPreviewCandidateSummary BuildCandidateSummary(
        RuntimeStackBackupProductionCandidateResult candidate,
        List<RuntimeStackBackupPublicCutoverPreviewCheck> checks,
        List<string> blockers,
        List<string> warnings)
    {
        var destroyed = candidate.Destroy is not null ||
            string.Equals(candidate.Status, "destroyed", StringComparison.OrdinalIgnoreCase);

        var ready =
            string.Equals(candidate.Status, "private_candidate_ready", StringComparison.OrdinalIgnoreCase) &&
            !destroyed &&
            candidate.Safety.PrivateOnly &&
            candidate.Database.ImportSucceeded &&
            candidate.Runtime.SynapseHealthPassed;

        AddCheck(
            checks,
            "public-cutover-preview.candidate.found",
            "production-candidate",
            "info",
            true,
            "satisfied",
            "Private Production Restore candidate was found.",
            candidate.CandidateId);

        AddCheck(
            checks,
            "public-cutover-preview.candidate.private-ready",
            "production-candidate",
            ready ? "info" : "blocker",
            ready,
            ready ? "satisfied" : "blocked",
            ready
                ? "Private Production Restore candidate is ready for Matrix route preview."
                : "Private Production Restore candidate is not ready for Matrix route preview.",
            candidate.Status);

        if (!ready)
        {
            blockers.Add("Private Production Restore candidate must be ready, private-only, not destroyed, imported successfully, and Synapse-health-checked before public cutover preview can be executable in a future sprint.");
        }

        var elementReady = ready &&
            candidate.Runtime.ElementConfigExtracted &&
            candidate.Runtime.ElementConfigPatched &&
            candidate.Runtime.ElementContainerStarted &&
            candidate.Runtime.ElementHealthPassed &&
            !string.IsNullOrWhiteSpace(candidate.ElementContainerName);

        if (!candidate.Runtime.ElementConfigExtracted)
        {
            warnings.Add("Element config was not extracted in the private candidate. Element route preview will be deferred.");
        }
        else if (!elementReady)
        {
            warnings.Add("Element config was extracted, but the private candidate Element runtime is not ready. Element route preview will be deferred.");
        }

        return new RuntimeStackBackupPublicCutoverPreviewCandidateSummary(
            CandidateFound: true,
            CandidateId: candidate.CandidateId,
            ValidationId: candidate.ValidationId,
            Status: candidate.Status,
            RestoreMode: candidate.RestoreMode,
            TargetStackSlug: candidate.TargetStackSlug,
            MatrixServerName: candidate.MatrixServerName,
            PrivateRuntimeId: candidate.PrivateRuntimeId,
            PrivateRuntimeStatus: candidate.PrivateRuntimeStatus,
            PrivateOnly: candidate.Safety.PrivateOnly,
            Destroyed: destroyed,
            DatabaseImportSucceeded: candidate.Database.ImportSucceeded,
            SynapseHealthPassed: candidate.Runtime.SynapseHealthPassed,
            SynapseHealthResponse: candidate.Runtime.HealthResponse,
            NetworkName: candidate.NetworkName,
            PostgresContainerName: candidate.PostgresContainerName,
            SynapseContainerName: candidate.SynapseContainerName,
            ElementDataPath: candidate.ElementDataPath,
            ElementConfigExtracted: candidate.Runtime.ElementConfigExtracted,
            ElementConfigPatched: candidate.Runtime.ElementConfigPatched,
            ElementContainerName: candidate.ElementContainerName,
            ElementContainerId: candidate.ElementContainerId,
            ElementContainerStarted: candidate.Runtime.ElementContainerStarted,
            ElementHealthPassed: candidate.Runtime.ElementHealthPassed,
            ElementHealthResponse: candidate.Runtime.ElementHealthResponse,
            ReadyForMatrixCutoverPreview: ready,
            ReadyForElementCutoverPreview: elementReady,
            Notes:
            [
                "Candidate remains private-only.",
                "The Synapse upstream for a future public Matrix route is the private candidate Synapse container.",
                "This preview does not promote the candidate or change its lifecycle."
            ]);
    }

    private static RuntimeStackBackupPublicCutoverPreviewRouteAction BuildMatrixAction(
        RuntimeStackBackupProductionCandidateResult candidate,
        RuntimeStackBackupProductionRestorePlanResult plan,
        string? matrixHost,
        List<RuntimeStackBackupPublicCutoverPreviewCheck> checks,
        List<string> blockers,
        List<string> warnings)
    {
        var certificate = plan.Certificates.MatrixCertificate;
        var existingRoute = plan.Npm.MatrixRoute;
        var upstreamContainer = candidate.SynapseContainerName;
        const int upstreamPort = 8008;

        var certificateReady = certificate is not null &&
            certificate.NpmCertificateId is > 0 &&
            certificate.ImportedToNpm &&
            !certificate.IsStaging;

        AddCertificateCheck(
            checks,
            blockers,
            "matrix",
            certificate,
            certificateReady);

        var routeTargetsCandidate = RouteTargetsCandidate(
            existingRoute,
            upstreamContainer,
            BuildStableMatrixAlias(candidate.TargetStackSlug),
            upstreamPort,
            certificate?.NpmCertificateId);

        var requiredBeforeExecution = new List<string>
        {
            "Operator must review and approve this preview.",
            "Public cutover confirmation gates must be acknowledged in a later sprint.",
            "A final pre-cutover backup/snapshot must exist before any executor mutates public routing.",
            "Candidate Synapse health must be verified immediately before public route creation or update."
        };

        string action;
        bool available;
        string? detail;

        if (string.IsNullOrWhiteSpace(matrixHost))
        {
            action = "blocked_missing_host";
            available = false;
            detail = "No intended Matrix public host was supplied or discovered.";
            blockers.Add("Intended Matrix public host is required before a future public route can be created.");
        }
        else if (string.IsNullOrWhiteSpace(upstreamContainer))
        {
            action = "blocked_missing_candidate_synapse_upstream";
            available = false;
            detail = "The candidate does not have a Synapse container name.";
            blockers.Add("Candidate Synapse container name is required before a future public Matrix route can be created.");
        }
        else if (!certificateReady)
        {
            action = "blocked_certificate_not_ready";
            available = false;
            detail = "A non-staging certificate imported into NPM is required before a future public Matrix route can be created.";
        }
        else if (existingRoute is null)
        {
            action = "create";
            available = true;
            detail = "Future executor would create the Matrix NPM proxy host for the private candidate Synapse upstream.";
        }
        else if (routeTargetsCandidate)
        {
            action = "no_op_existing_route_already_targets_candidate";
            available = true;
            detail = "Existing Matrix route already targets the private candidate upstream and expected certificate.";
        }
        else
        {
            action = "blocked_existing_route_conflict";
            available = false;
            detail = "A Matrix route already exists, but it does not target the private candidate upstream. Operator must resolve ownership before any future executor updates it.";
            blockers.Add($"Existing Matrix route for host '{matrixHost}' does not target the selected private candidate upstream.");
        }

        AddCheck(
            checks,
            "public-cutover-preview.npm.matrix-action",
            "npm-route",
            available ? "info" : "blocker",
            available,
            available ? "satisfied" : "blocked",
            available
                ? $"Future Matrix route action calculated: {action}."
                : $"Future Matrix route action is blocked: {action}.",
            detail);

        return new RuntimeStackBackupPublicCutoverPreviewRouteAction(
            Component: "matrix",
            Host: matrixHost,
            UpstreamContainerName: upstreamContainer,
            UpstreamPort: upstreamPort,
            ForwardScheme: "http",
            CertificateEntityId: certificate?.CertificateEntityId,
            NpmCertificateId: certificate?.NpmCertificateId,
            CertificateAvailable: certificate is not null,
            CertificateImportedToNpm: certificate?.ImportedToNpm == true,
            CertificateIsStaging: certificate?.IsStaging == true,
            RouteCurrentlyExists: existingRoute is not null,
            ExistingRoute: existingRoute,
            ExistingRouteAlreadyTargetsCandidate: routeTargetsCandidate,
            Action: action,
            AvailableForFutureExecution: available,
            WillMutateNow: false,
            RequiredBeforeExecution: requiredBeforeExecution,
            Notes:
            [
                "Matrix route preview is read-only.",
                "A future executor should call NPM only after separate confirmation gates pass.",
                "The upstream is intentionally the private candidate Synapse container until promotion/runtime registration exists."
            ],
            Detail: detail);
    }

    private static RuntimeStackBackupPublicCutoverPreviewRouteAction BuildElementAction(
        RuntimeStackBackupProductionCandidateResult candidate,
        RuntimeStackBackupProductionRestorePlanResult plan,
        string? elementHost,
        List<RuntimeStackBackupPublicCutoverPreviewCheck> checks,
        List<string> blockers,
        List<string> warnings)
    {
        var certificate = plan.Certificates.ElementCertificate;
        var existingRoute = plan.Npm.ElementRoute;
        var upstreamContainer = candidate.ElementContainerName;
        const int upstreamPort = 80;

        var hasElementConfig = candidate.Runtime.ElementConfigExtracted &&
            candidate.Runtime.ElementConfigPatched &&
            !string.IsNullOrWhiteSpace(candidate.ElementDataPath);

        var hasElementRuntime = hasElementConfig &&
            candidate.Runtime.ElementContainerStarted &&
            candidate.Runtime.ElementHealthPassed &&
            !string.IsNullOrWhiteSpace(upstreamContainer);

        var certificateReady = certificate is not null &&
            certificate.NpmCertificateId is > 0 &&
            certificate.ImportedToNpm &&
            !certificate.IsStaging;

        if (!string.IsNullOrWhiteSpace(elementHost) && hasElementRuntime)
        {
            AddCertificateCheck(
                checks,
                blockers,
                "element",
                certificate,
                certificateReady);
        }

        var routeTargetsCandidate = hasElementRuntime && RouteTargetsCandidate(
            existingRoute,
            upstreamContainer!,
            BuildStableElementAlias(candidate.TargetStackSlug),
            upstreamPort,
            certificate?.NpmCertificateId);

        var requiredBeforeExecution = new List<string>
        {
            "Operator must review and approve this preview.",
            "Public cutover confirmation gates must be acknowledged in a later sprint.",
            "A final pre-cutover backup/snapshot must exist before any executor mutates public routing.",
            "Candidate Element static config/web health must be verified immediately before public route creation or update.",
            "Matrix public route must be created or verified before Element is exposed to users."
        };

        string action;
        bool available;
        string severity;
        bool passed;
        string status;
        string? detail;

        if (string.IsNullOrWhiteSpace(elementHost))
        {
            action = "not_requested";
            available = false;
            severity = "info";
            passed = true;
            status = "skipped";
            detail = "No intended Element public host was supplied or discovered.";
        }
        else if (!candidate.Runtime.ElementConfigExtracted)
        {
            action = "deferred_element_config_missing";
            available = false;
            severity = "warning";
            passed = false;
            status = "deferred";
            detail = "Candidate does not include extracted Element config, so Element public route preview is deferred.";
            warnings.Add("Element public route preview deferred because candidate does not include Element config.");
        }
        else if (!hasElementRuntime)
        {
            action = "deferred_candidate_element_runtime_not_ready";
            available = false;
            severity = "warning";
            passed = false;
            status = "deferred";
            detail = "Candidate contains Element config, but the private candidate Element container is not ready and health-checked yet.";
            warnings.Add("Element public route preview deferred because the private candidate Element container is not ready and health-checked.");
        }
        else if (!certificateReady)
        {
            action = "blocked_certificate_not_ready";
            available = false;
            severity = "blocker";
            passed = false;
            status = "blocked";
            detail = "Future Element route cannot be executable until a non-staging certificate is imported to NPM.";
        }
        else if (existingRoute is null)
        {
            action = "create";
            available = true;
            severity = "info";
            passed = true;
            status = "satisfied";
            detail = "Future executor would create the Element NPM proxy host for the private candidate Element upstream.";
        }
        else if (routeTargetsCandidate)
        {
            action = "no-op_existing_route_targets_candidate";
            available = true;
            severity = "info";
            passed = true;
            status = "satisfied";
            detail = "Existing Element route already targets the private candidate Element upstream and matching certificate.";
        }
        else
        {
            action = "blocked_existing_route_conflict";
            available = false;
            severity = "blocker";
            passed = false;
            status = "blocked";
            detail = "An existing Element public route was found, but it does not target this private candidate Element upstream.";
            blockers.Add($"Existing Element NPM route for '{elementHost}' does not target this private candidate. Operator must resolve route ownership before any future public cutover executor runs.");
        }

        AddCheck(
            checks,
            "public-cutover-preview.npm.element-action",
            "npm-route",
            severity,
            passed,
            status,
            $"Future Element route action calculated: {action}.",
            detail);

        return new RuntimeStackBackupPublicCutoverPreviewRouteAction(
            Component: "element",
            Host: elementHost,
            UpstreamContainerName: hasElementRuntime ? upstreamContainer : null,
            UpstreamPort: hasElementRuntime ? upstreamPort : null,
            ForwardScheme: "http",
            CertificateEntityId: certificate?.CertificateEntityId,
            NpmCertificateId: certificate?.NpmCertificateId,
            CertificateAvailable: certificate is not null,
            CertificateImportedToNpm: certificate?.ImportedToNpm == true,
            CertificateIsStaging: certificate?.IsStaging == true,
            RouteCurrentlyExists: existingRoute is not null,
            ExistingRoute: existingRoute,
            ExistingRouteAlreadyTargetsCandidate: routeTargetsCandidate,
            Action: action,
            AvailableForFutureExecution: available,
            WillMutateNow: false,
            RequiredBeforeExecution: requiredBeforeExecution,
            Notes:
            [
                "Element route preview is read-only.",
                "A future executor should call NPM only after separate confirmation gates pass.",
                "The upstream is the private candidate Element container, with no host port binding until a future public route is explicitly created."
            ],
            Detail: detail);
    }

    private static void AddCertificateCheck(
        List<RuntimeStackBackupPublicCutoverPreviewCheck> checks,
        List<string> blockers,
        string component,
        RuntimeStackBackupProductionRestoreCertificateMatch? certificate,
        bool certificateReady)
    {
        AddCheck(
            checks,
            $"public-cutover-preview.certificate.{component}-npm-ready",
            "certificate",
            certificateReady ? "info" : "blocker",
            certificateReady,
            certificateReady ? "satisfied" : "blocked",
            certificateReady
                ? $"{component} certificate is non-staging and imported to NPM."
                : $"{component} certificate is not ready for future public route attachment.",
            certificate is null
                ? null
                : $"certificateId={certificate.CertificateId}; npmCertificateId={certificate.NpmCertificateId}; importedToNpm={certificate.ImportedToNpm}; isStaging={certificate.IsStaging}");

        if (certificateReady)
        {
            return;
        }

        if (certificate is null)
        {
            blockers.Add($"No matching {component} certificate was found for future public route attachment.");
            return;
        }

        if (certificate.IsStaging)
        {
            blockers.Add($"Matching {component} certificate is a staging certificate; future public cutover requires a production certificate.");
        }

        if (certificate.NpmCertificateId is null or <= 0 || !certificate.ImportedToNpm)
        {
            blockers.Add($"Matching {component} certificate must be imported to NPM before future public route attachment.");
        }
    }

    private static void AddPlanBlockers(
        RuntimeStackBackupProductionRestorePlanResult plan,
        List<string> blockers)
    {
        foreach (var blocker in plan.Blockers)
        {
            blockers.Add($"Production Restore plan blocker: {blocker}");
        }
    }

    private static bool RouteTargetsCandidate(
        RuntimeStackBackupProductionRestoreNpmRouteSummary? route,
        string upstreamContainer,
        string stableCutoverAlias,
        int upstreamPort,
        int? npmCertificateId)
    {
        if (route is null)
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

    private static string BuildStableMatrixAlias(
        string targetStackSlug)
    {
        return $"mem-matrix-{Slugify(targetStackSlug)}";
    }

    private static string BuildStableElementAlias(
        string targetStackSlug)
    {
        return $"mem-element-{Slugify(targetStackSlug)}";
    }

    private static void AddCheck(
        List<RuntimeStackBackupPublicCutoverPreviewCheck> checks,
        string code,
        string category,
        string severity,
        bool passed,
        string status,
        string message,
        string? detail)
    {
        checks.Add(new RuntimeStackBackupPublicCutoverPreviewCheck(
            Code: code,
            Category: category,
            Severity: severity,
            Status: status,
            Passed: passed,
            Message: message,
            Detail: detail));
    }

    private static string? FirstNonEmpty(
        params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static string? NormalizeHost(
        string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().TrimEnd('.').ToLowerInvariant();
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

    private static string CreatePreviewId()
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss'Z'");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        return $"{timestamp}-{suffix}";
    }
}
