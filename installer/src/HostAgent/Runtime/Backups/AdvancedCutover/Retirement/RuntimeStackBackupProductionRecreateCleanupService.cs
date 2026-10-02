using Docker.DotNet;
using Docker.DotNet.Models;
using HostAgent.Runtime.Backups.AdvancedCutover.Candidate;
using HostAgent.Runtime.Backups.StandardRecreate;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Readiness;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.AdvancedCutover.Retirement;

/// <summary>
/// Retires disposable private restore/cutover candidate resources only after a recreated
/// production stack has re-proved its normal MEM ownership, route configuration and reachability.
/// This service never mutates the recreated production stack, production Postgres, DNS or certificates.
/// </summary>
public sealed class RuntimeStackBackupProductionRecreateCleanupService
{
    private const string NpmContainerName = "mem-npm";
    private const string RuntimeGatewayNetworkName = "mem-gateway";

    private readonly StandardRecreateHistoryService _recreateHistory;
    private readonly RuntimeStackBackupProductionCandidateHistoryService _candidateHistory;
    private readonly RuntimeStackBackupProductionCandidateService _candidateService;
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly RuntimeReadinessVerifier _readinessVerifier;
    private readonly DockerClient _docker;
    private readonly ILogger<RuntimeStackBackupProductionRecreateCleanupService> _logger;

    public RuntimeStackBackupProductionRecreateCleanupService(
        StandardRecreateHistoryService recreateHistory,
        RuntimeStackBackupProductionCandidateHistoryService candidateHistory,
        RuntimeStackBackupProductionCandidateService candidateService,
        RuntimeStackManifestStore manifestStore,
        RuntimeReadinessVerifier readinessVerifier,
        DockerClient docker,
        ILogger<RuntimeStackBackupProductionRecreateCleanupService> logger)
    {
        _recreateHistory = recreateHistory;
        _candidateHistory = candidateHistory;
        _candidateService = candidateService;
        _manifestStore = manifestStore;
        _readinessVerifier = readinessVerifier;
        _docker = docker;
        _logger = logger;
    }

    public async Task<RuntimeStackBackupProductionRecreateCleanupAssessment> AssessAsync(
        string recreateId,
        string? requestedCandidateId,
        CancellationToken ct)
    {
        var recreateResponse = await _recreateHistory.GetAsync(recreateId, ct)
            ?? throw new FileNotFoundException($"Production recreate run '{recreateId}' was not found.");

        var recreate = recreateResponse.Recreate;
        var checks = new List<RuntimeStackBackupProductionRecreateCleanupCheck>();
        var warnings = new List<string>();
        var errors = new List<string>();

        AddCheck(
            checks,
            "production-recreate.cleanup.recreate.status",
            "error",
            string.Equals(recreate.Status, "production_recreate_verified", StringComparison.OrdinalIgnoreCase),
            "Production recreate completed with verified status.",
            recreate.Status);

        AddCheck(
            checks,
            "production-recreate.cleanup.stack.registered",
            "error",
            recreate.Runtime.StackRegistered && recreate.Runtime.ManifestSaved && recreate.Runtime.DatabaseOwnershipSaved,
            "Production recreate saved the runtime stack manifest and database ownership.",
            $"stackRegistered={recreate.Runtime.StackRegistered}; manifestSaved={recreate.Runtime.ManifestSaved}; databaseOwnershipSaved={recreate.Runtime.DatabaseOwnershipSaved}");

        AddCheck(
            checks,
            "production-recreate.cleanup.recreate.public-readiness",
            "error",
            recreate.Routes.PublicReadinessPassed,
            "Production recreate recorded passing public readiness.",
            $"matrixRoute={recreate.Routes.MatrixRouteReady}; elementRoute={recreate.Routes.ElementRouteReady}");

        AddCheck(
            checks,
            "production-recreate.cleanup.recreate.errors",
            "error",
            recreate.Errors.Count == 0,
            "Production recreate completed without recorded errors.",
            recreate.Errors.Count == 0 ? null : string.Join(" | ", recreate.Errors));

        var manifest = await _manifestStore.FindAsync(recreate.TargetStackSlug, ct);

        AddCheck(
            checks,
            "production-recreate.cleanup.manifest.present",
            "error",
            manifest is not null,
            "The recreated stack is present in the MEM runtime manifest store.",
            manifest?.Slug);

        AddCheck(
            checks,
            "production-recreate.cleanup.manifest.stack-id",
            "error",
            manifest is not null && manifest.StackId == recreate.RuntimeStackId,
            "The runtime manifest belongs to the same recreated stack identity.",
            manifest is null ? null : $"manifestStackId={manifest.StackId}; recreateStackId={recreate.RuntimeStackId}");

        AddCheck(
            checks,
            "production-recreate.cleanup.manifest.containers",
            "error",
            manifest is not null &&
            string.Equals(manifest.Matrix.ContainerName, recreate.Runtime.MatrixContainerName, StringComparison.OrdinalIgnoreCase) &&
            manifest.Element is not null &&
            string.Equals(manifest.Element.ContainerName, recreate.Runtime.ElementContainerName, StringComparison.OrdinalIgnoreCase),
            "The runtime manifest points at the normalized production Matrix and Element containers.",
            manifest is null
                ? null
                : $"matrix={manifest.Matrix.ContainerName}; element={manifest.Element?.ContainerName}");

        if (manifest is not null)
        {
            await AddFreshProductionReadinessChecksAsync(manifest, checks, warnings, ct);
            await AddProductionGatewayChecksAsync(manifest, checks, warnings, ct);
        }

        var candidateResolution = await ResolveCandidateAsync(recreate, requestedCandidateId, ct);
        warnings.AddRange(candidateResolution.Warnings);

        if (candidateResolution.Candidate is null)
        {
            AddCheck(
                checks,
                "production-recreate.cleanup.candidate.resolved",
                "error",
                false,
                "A disposable production restore candidate was resolved for retirement.",
                candidateResolution.Detail);
        }
        else
        {
            var candidate = candidateResolution.Candidate;
            var candidateAlreadyRetired = IsCandidateDestroyed(candidate);

            AddCheck(
                checks,
                "production-recreate.cleanup.candidate.catalog-source",
                "error",
                string.Equals(candidate.CatalogEntryId, recreate.CatalogEntryId, StringComparison.OrdinalIgnoreCase),
                "The candidate belongs to the same Backup Catalog entry as the recreated stack.",
                $"candidateCatalogEntryId={candidate.CatalogEntryId}; recreateCatalogEntryId={recreate.CatalogEntryId}");

            AddCheck(
                checks,
                "production-recreate.cleanup.candidate.target-stack",
                "error",
                string.Equals(candidate.TargetStackSlug, recreate.TargetStackSlug, StringComparison.OrdinalIgnoreCase),
                "The candidate belongs to the same target stack slug as the recreated production stack.",
                $"candidateTargetStackSlug={candidate.TargetStackSlug}; recreateTargetStackSlug={recreate.TargetStackSlug}");

            AddCheck(
                checks,
                "production-recreate.cleanup.candidate.retire-state",
                "info",
                !candidateAlreadyRetired,
                candidateAlreadyRetired
                    ? "Candidate has already been retired. No candidate containers need to be destroyed."
                    : "Candidate is still present and can be retired after all production checks pass.",
                candidate.Status);

            if (!candidateAlreadyRetired)
            {
                await AddCandidateIsolationChecksAsync(candidate, checks, warnings, ct);
            }
        }

        var failedErrors = checks.Where(x =>
                string.Equals(x.Severity, "error", StringComparison.OrdinalIgnoreCase) &&
                !x.Passed)
            .ToArray();

        errors.AddRange(failedErrors.Select(x => x.Message));

        var resolvedCandidate = candidateResolution.Candidate;
        var candidateAlreadyRetiredResult = resolvedCandidate is not null && IsCandidateDestroyed(resolvedCandidate);
        var autoCleanupAvailable = resolvedCandidate is not null &&
                                   !candidateAlreadyRetiredResult &&
                                   failedErrors.Length == 0;

        var status = candidateAlreadyRetiredResult
            ? "candidate_already_retired"
            : autoCleanupAvailable
                ? "ready_to_retire_candidate"
                : "cleanup_blocked";

        var recommendedAction = candidateAlreadyRetiredResult
            ? "none"
            : autoCleanupAvailable
                ? "retire_candidate"
                : "keep_candidate_until_blockers_are_resolved";

        var detail = candidateAlreadyRetiredResult
            ? "The recreated stack is retained and the associated disposable candidate is already retired."
            : autoCleanupAvailable
                ? "The recreated stack is live and independently verified. The disposable restore/cutover candidate can now be retired."
                : "Candidate cleanup is blocked because the recreated production stack does not yet meet all retirement checks.";

        return new RuntimeStackBackupProductionRecreateCleanupAssessment(
            Source: "control-plane",
            Status: status,
            RecreateId: recreate.RecreateId,
            CatalogEntryId: recreate.CatalogEntryId,
            RuntimeStackId: recreate.RuntimeStackId,
            TargetStackSlug: recreate.TargetStackSlug,
            CandidateId: resolvedCandidate?.CandidateId,
            CandidateStatus: resolvedCandidate?.Status,
            CandidateNetworkName: resolvedCandidate?.NetworkName,
            CutoverIngressNetworkName: resolvedCandidate is null
                ? null
                : CutoverIngressNetworkName(resolvedCandidate.CandidateId),
            CandidateResolved: resolvedCandidate is not null,
            CandidateAlreadyRetired: candidateAlreadyRetiredResult,
            RequiresOperatorConfirmation: autoCleanupAvailable,
            AutoCleanupAvailable: autoCleanupAvailable,
            RecommendedAction: recommendedAction,
            Checks: checks,
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray(),
            Errors: errors.Distinct(StringComparer.Ordinal).ToArray(),
            Detail: detail);
    }

    public async Task<RuntimeStackBackupProductionRecreateCleanupResult> ExecuteAsync(
        string recreateId,
        RuntimeStackBackupProductionRecreateCleanupRequest request,
        CancellationToken ct)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var cleanupId = BuildCleanupId();
        var mode = NormalizeMode(request.RetirementMode);
        var assessment = await AssessAsync(recreateId, request.CandidateId, ct);
        var warnings = assessment.Warnings.ToList();
        var errors = assessment.Errors.ToList();

        if (assessment.CandidateAlreadyRetired)
        {
            return new RuntimeStackBackupProductionRecreateCleanupResult(
                Source: "control-plane",
                Status: "candidate_already_retired",
                CleanupId: cleanupId,
                RecreateId: assessment.RecreateId,
                CatalogEntryId: assessment.CatalogEntryId,
                RuntimeStackId: assessment.RuntimeStackId,
                TargetStackSlug: assessment.TargetStackSlug,
                CandidateId: assessment.CandidateId,
                RetirementMode: mode,
                StartedAtUtc: startedAtUtc,
                FinishedAtUtc: DateTimeOffset.UtcNow,
                CandidateDestroyRequested: false,
                CandidateDestroyed: true,
                CandidateElementContainerRemoved: false,
                CandidatePrivateRuntimeDestroyed: true,
                CutoverIngressNetworkName: assessment.CutoverIngressNetworkName,
                CutoverIngressNetworkRemoved: false,
                Assessment: assessment,
                Warnings: warnings,
                Errors: errors,
                Detail: "The disposable candidate was already retired; no cleanup mutation was needed.");
        }

        if (!assessment.AutoCleanupAvailable)
        {
            return new RuntimeStackBackupProductionRecreateCleanupResult(
                Source: "control-plane",
                Status: "cleanup_blocked",
                CleanupId: cleanupId,
                RecreateId: assessment.RecreateId,
                CatalogEntryId: assessment.CatalogEntryId,
                RuntimeStackId: assessment.RuntimeStackId,
                TargetStackSlug: assessment.TargetStackSlug,
                CandidateId: assessment.CandidateId,
                RetirementMode: mode,
                StartedAtUtc: startedAtUtc,
                FinishedAtUtc: DateTimeOffset.UtcNow,
                CandidateDestroyRequested: false,
                CandidateDestroyed: false,
                CandidateElementContainerRemoved: false,
                CandidatePrivateRuntimeDestroyed: false,
                CutoverIngressNetworkName: assessment.CutoverIngressNetworkName,
                CutoverIngressNetworkRemoved: false,
                Assessment: assessment,
                Warnings: warnings,
                Errors: errors,
                Detail: "Candidate cleanup did not run because retirement checks are not all passing.");
        }

        if (string.Equals(mode, "operator-confirmed", StringComparison.OrdinalIgnoreCase) &&
            !request.AcknowledgeRetireCandidate)
        {
            return new RuntimeStackBackupProductionRecreateCleanupResult(
                Source: "control-plane",
                Status: "operator_confirmation_required",
                CleanupId: cleanupId,
                RecreateId: assessment.RecreateId,
                CatalogEntryId: assessment.CatalogEntryId,
                RuntimeStackId: assessment.RuntimeStackId,
                TargetStackSlug: assessment.TargetStackSlug,
                CandidateId: assessment.CandidateId,
                RetirementMode: mode,
                StartedAtUtc: startedAtUtc,
                FinishedAtUtc: DateTimeOffset.UtcNow,
                CandidateDestroyRequested: false,
                CandidateDestroyed: false,
                CandidateElementContainerRemoved: false,
                CandidatePrivateRuntimeDestroyed: false,
                CutoverIngressNetworkName: assessment.CutoverIngressNetworkName,
                CutoverIngressNetworkRemoved: false,
                Assessment: assessment,
                Warnings: warnings,
                Errors: errors,
                Detail: "The cleanup gate is ready, but explicit operator confirmation is required before the disposable candidate is retired.");
        }

        var candidateId = assessment.CandidateId
            ?? throw new InvalidOperationException("Candidate cleanup passed assessment without a candidate id.");

        var destroyedCandidate = await _candidateService.DestroyAsync(candidateId, ct);
        var candidateDestroyed = IsCandidateDestroyed(destroyedCandidate);
        var candidateDestroySummary = destroyedCandidate.Destroy;
        warnings.AddRange(destroyedCandidate.Warnings);
        warnings.AddRange(candidateDestroySummary?.Warnings ?? Array.Empty<string>());

        if (!candidateDestroyed)
        {
            errors.Add("Candidate destroy did not reach a destroyed state.");
        }

        var networkRemoved = false;
        var cutoverNetworkName = assessment.CutoverIngressNetworkName;

        if (candidateDestroyed && !string.IsNullOrWhiteSpace(cutoverNetworkName))
        {
            var networkCleanup = await RemoveEmptyCutoverIngressNetworkAsync(cutoverNetworkName, ct);
            networkRemoved = networkCleanup.Removed;
            warnings.AddRange(networkCleanup.Warnings);
        }

        var status = candidateDestroyed && networkRemoved && errors.Count == 0
            ? "candidate_retired"
            : "cleanup_partial";

        var detail = status == "candidate_retired"
            ? "The disposable restore/cutover candidate was retired after the normalized production stack passed all retirement checks."
            : "Candidate cleanup was attempted but did not complete cleanly. Review warnings and errors before removing any remaining resources manually.";

        var result = new RuntimeStackBackupProductionRecreateCleanupResult(
            Source: "control-plane",
            Status: status,
            CleanupId: cleanupId,
            RecreateId: assessment.RecreateId,
            CatalogEntryId: assessment.CatalogEntryId,
            RuntimeStackId: assessment.RuntimeStackId,
            TargetStackSlug: assessment.TargetStackSlug,
            CandidateId: candidateId,
            RetirementMode: mode,
            StartedAtUtc: startedAtUtc,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            CandidateDestroyRequested: true,
            CandidateDestroyed: candidateDestroyed,
            CandidateElementContainerRemoved: candidateDestroySummary?.ElementContainerRemoved ?? false,
            CandidatePrivateRuntimeDestroyed: candidateDestroySummary?.PrivateRuntimeDestroyed ?? false,
            CutoverIngressNetworkName: cutoverNetworkName,
            CutoverIngressNetworkRemoved: networkRemoved,
            Assessment: assessment,
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray(),
            Errors: errors.Distinct(StringComparer.Ordinal).ToArray(),
            Detail: detail);

        _logger.LogInformation(
            "Production recreate candidate cleanup finished. CleanupId={CleanupId} RecreateId={RecreateId} CandidateId={CandidateId} Status={Status} NetworkRemoved={NetworkRemoved}",
            result.CleanupId,
            result.RecreateId,
            result.CandidateId,
            result.Status,
            result.CutoverIngressNetworkRemoved);

        return result;
    }

    private async Task AddFreshProductionReadinessChecksAsync(
        RuntimeStackManifest manifest,
        List<RuntimeStackBackupProductionRecreateCleanupCheck> checks,
        List<string> warnings,
        CancellationToken ct)
    {
        if (manifest.Element is null ||
            string.IsNullOrWhiteSpace(manifest.Matrix.InternalBaseUrl) ||
            string.IsNullOrWhiteSpace(manifest.Matrix.PublicBaseUrl) ||
            string.IsNullOrWhiteSpace(manifest.Matrix.PublicHost) ||
            string.IsNullOrWhiteSpace(manifest.Matrix.InternalHost) ||
            string.IsNullOrWhiteSpace(manifest.Element.InternalBaseUrl) ||
            string.IsNullOrWhiteSpace(manifest.Element.PublicBaseUrl) ||
            string.IsNullOrWhiteSpace(manifest.Element.PublicHost) ||
            string.IsNullOrWhiteSpace(manifest.Element.InternalHost))
        {
            AddCheck(
                checks,
                "production-recreate.cleanup.fresh-readiness",
                "error",
                false,
                "The recreated stack manifest contains the URLs and internal hosts required for a fresh readiness verification.",
                "Manifest was incomplete.");
            return;
        }

        try
        {
            var readiness = await _readinessVerifier.VerifyAsync(
                new RuntimeReadinessVerificationRequest(
                    MatrixInternalBaseUrl: manifest.Matrix.InternalBaseUrl,
                    ElementInternalBaseUrl: manifest.Element.InternalBaseUrl,
                    MatrixPublicBaseUrl: manifest.Matrix.PublicBaseUrl,
                    ElementPublicBaseUrl: manifest.Element.PublicBaseUrl,
                    MatrixPublicHost: manifest.Matrix.PublicHost,
                    ElementPublicHost: manifest.Element.PublicHost,
                    MatrixForwardHost: manifest.Matrix.InternalHost,
                    MatrixForwardPort: 8008,
                    ElementForwardHost: manifest.Element.InternalHost,
                    ElementForwardPort: 80,
                    ExpectedNpmCertificateId: manifest.Matrix.NpmCertificateId ?? manifest.Element.NpmCertificateId),
                ct);

            foreach (var readinessCheck in readiness.Checks)
            {
                AddCheck(
                    checks,
                    $"production-recreate.cleanup.fresh.{readinessCheck.Code}",
                    "error",
                    readinessCheck.Success,
                    readinessCheck.Name,
                    readinessCheck.Success
                        ? readinessCheck.Detail
                        : $"{readinessCheck.Detail} {readinessCheck.BodyPreview}".Trim());
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            warnings.Add($"Fresh production readiness verification could not complete: {ex.Message}");
            AddCheck(
                checks,
                "production-recreate.cleanup.fresh-readiness",
                "error",
                false,
                "Fresh production readiness verification completed.",
                ex.Message);
        }
    }

    private async Task AddProductionGatewayChecksAsync(
        RuntimeStackManifest manifest,
        List<RuntimeStackBackupProductionRecreateCleanupCheck> checks,
        List<string> warnings,
        CancellationToken ct)
    {
        var gateway = await FindNetworkByNameAsync(RuntimeGatewayNetworkName, ct);

        if (gateway is null)
        {
            AddCheck(
                checks,
                "production-recreate.cleanup.production-gateway",
                "error",
                false,
                "The normal MEM gateway network exists and contains the live public path.",
                $"Network '{RuntimeGatewayNetworkName}' was not found.");
            return;
        }

        var containers = gateway.Containers ?? new Dictionary<string, EndpointResource>();
        var expected = new[]
        {
            NpmContainerName,
            manifest.Matrix.ContainerName,
            manifest.Element?.ContainerName
        }
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Cast<string>()
        .ToArray();

        var missing = expected
            .Where(name => !ContainsContainer(containers, name))
            .ToArray();

        AddCheck(
            checks,
            "production-recreate.cleanup.production-gateway",
            "error",
            missing.Length == 0,
            "The normal MEM gateway network contains NPM and both normalized production containers.",
            missing.Length == 0 ? gateway.Name : $"Missing: {string.Join(", ", missing)}");
    }

    private async Task AddCandidateIsolationChecksAsync(
        RuntimeStackBackupProductionCandidateResult candidate,
        List<RuntimeStackBackupProductionRecreateCleanupCheck> checks,
        List<string> warnings,
        CancellationToken ct)
    {
        var privateNetwork = await FindNetworkByNameAsync(candidate.NetworkName, ct);
        var cutoverNetworkName = CutoverIngressNetworkName(candidate.CandidateId);
        var cutoverNetwork = await FindNetworkByNameAsync(cutoverNetworkName, ct);

        var npmOnPrivateNetwork = privateNetwork is not null &&
                                  ContainsContainer(privateNetwork.Containers, NpmContainerName);

        var npmOnCutoverNetwork = cutoverNetwork is not null &&
                                  ContainsContainer(cutoverNetwork.Containers, NpmContainerName);

        AddCheck(
            checks,
            "production-recreate.cleanup.candidate.private-network-isolated",
            "error",
            !npmOnPrivateNetwork,
            "NPM is not attached to the candidate private restore network.",
            privateNetwork?.Name ?? "Candidate private network no longer exists.");

        AddCheck(
            checks,
            "production-recreate.cleanup.candidate.cutover-network-isolated",
            "error",
            !npmOnCutoverNetwork,
            "NPM is not attached to the candidate cutover ingress network.",
            cutoverNetwork?.Name ?? "Candidate cutover ingress network no longer exists.");

        if (privateNetwork is null)
        {
            warnings.Add($"Candidate private network '{candidate.NetworkName}' was not found. The candidate may already be partially cleaned up.");
        }
    }

    private async Task<CandidateResolution> ResolveCandidateAsync(
        StandardRecreateResult recreate,
        string? requestedCandidateId,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requestedCandidateId))
        {
            var response = await _candidateHistory.GetCandidateAsync(requestedCandidateId.Trim(), ct);
            return new CandidateResolution(
                response?.Candidate,
                [],
                response?.Candidate is null
                    ? $"Candidate '{requestedCandidateId}' was not found."
                    : null);
        }

        var history = await _candidateHistory.ListCandidatesAsync(
            validationId: null,
            status: null,
            max: 100,
            ct);

        var matching = history.Candidates
            .Where(candidate =>
                string.Equals(candidate.CatalogEntryId, recreate.CatalogEntryId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.TargetStackSlug, recreate.TargetStackSlug, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.StartedAtUtc)
            .ToArray();

        if (matching.Length == 0)
        {
            return new CandidateResolution(
                Candidate: null,
                Warnings: history.Warnings,
                Detail: "No Production Restore candidate matched the recreate Backup Catalog entry and target stack slug.");
        }

        var active = matching
            .Where(candidate => !candidate.Destroyed)
            .ToArray();

        if (active.Length > 1)
        {
            return new CandidateResolution(
                Candidate: null,
                Warnings: history.Warnings,
                Detail: $"Multiple active Production Restore candidates match this recreate. Supply candidateId explicitly. Matches: {string.Join(", ", active.Select(x => x.CandidateId))}");
        }

        var selected = active.Length == 1
            ? active[0]
            : matching[0];

        var result = await _candidateHistory.GetCandidateAsync(selected.CandidateId, ct);
        return new CandidateResolution(
            result?.Candidate,
            history.Warnings,
            result?.Candidate is null
                ? $"Candidate '{selected.CandidateId}' could not be loaded."
                : null);
    }

    private async Task<NetworkCleanupResult> RemoveEmptyCutoverIngressNetworkAsync(
        string networkName,
        CancellationToken ct)
    {
        var network = await FindNetworkByNameAsync(networkName, ct);
        if (network is null)
        {
            return new NetworkCleanupResult(
                Removed: true,
                Warnings: [$"Cutover ingress network '{networkName}' was already absent."]);
        }

        var containers = network.Containers ?? new Dictionary<string, EndpointResource>();
        if (containers.Count > 0)
        {
            return new NetworkCleanupResult(
                Removed: false,
                Warnings:
                [
                    $"Cutover ingress network '{networkName}' still has attached containers and was left in place: {string.Join(", ", containers.Values.Select(x => x.Name))}."
                ]);
        }

        try
        {
            await _docker.Networks.DeleteNetworkAsync(network.ID, ct);
            return new NetworkCleanupResult(Removed: true, Warnings: []);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new NetworkCleanupResult(
                Removed: false,
                Warnings: [$"Could not remove empty cutover ingress network '{networkName}': {ex.Message}"]);
        }
    }

    private async Task<NetworkResponse?> FindNetworkByNameAsync(string networkName, CancellationToken ct)
    {
        var networks = await _docker.Networks.ListNetworksAsync(new NetworksListParameters(), ct);
        var summary = networks.FirstOrDefault(x =>
            string.Equals(x.Name, networkName, StringComparison.OrdinalIgnoreCase));

        if (summary is null)
        {
            return null;
        }

        try
        {
            return await _docker.Networks.InspectNetworkAsync(summary.ID, ct);
        }
        catch
        {
            return null;
        }
    }

    private static bool ContainsContainer(
        IDictionary<string, EndpointResource>? containers,
        string containerName)
    {
        return containers is not null && containers.Values.Any(endpoint =>
            string.Equals(endpoint.Name, containerName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCandidateDestroyed(RuntimeStackBackupProductionCandidateResult candidate)
    {
        return candidate.Destroy is not null ||
               string.Equals(candidate.Status, "destroyed", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCheck(
        List<RuntimeStackBackupProductionRecreateCleanupCheck> checks,
        string code,
        string severity,
        bool passed,
        string message,
        string? detail)
    {
        checks.Add(new RuntimeStackBackupProductionRecreateCleanupCheck(
            Code: code,
            Severity: severity,
            Passed: passed,
            Message: message,
            Detail: detail));
    }

    private static string NormalizeMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "operator-confirmed";
        }

        var mode = value.Trim().ToLowerInvariant();
        return mode switch
        {
            "operator-confirmed" or "auto" => mode,
            _ => throw new InvalidOperationException("retirementMode must be 'operator-confirmed' or 'auto'.")
        };
    }

    private static string CutoverIngressNetworkName(string candidateId) =>
        $"mem-cutover-ingress-{candidateId}";

    private static string BuildCleanupId() =>
        $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssZ}-{Guid.NewGuid():N}"[..26];

    private sealed record CandidateResolution(
        RuntimeStackBackupProductionCandidateResult? Candidate,
        IReadOnlyList<string> Warnings,
        string? Detail);

    private sealed record NetworkCleanupResult(
        bool Removed,
        IReadOnlyList<string> Warnings);
}
