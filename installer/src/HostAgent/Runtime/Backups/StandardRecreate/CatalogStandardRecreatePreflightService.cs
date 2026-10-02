using HostAgent.Platform;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Coturn;
using HostAgent.Runtime.Stacks.Turn;

namespace HostAgent.Runtime.Backups.StandardRecreate;

/// <summary>
/// Builds a read-only Standard Recreate assessment directly from a canonical
/// Backup Catalog payload. It deliberately has no dependency on the validated
/// import archive store, and it never reserves targets or starts execution.
/// </summary>
public sealed class CatalogStandardRecreatePreflightService
{
    private readonly BackupCatalogPayloadResolver _payloadResolver;
    private readonly CatalogRestoreSessionService _catalogRestoreSessions;
    private readonly RestoreAttemptCoordinator _restoreAttemptCoordinator;
    private readonly RuntimeStackManifestStore _manifestStore;
    private readonly PlatformDomainResolver _domainResolver;
    private readonly IRuntimeStackTurnPlatformConfigurationProvider _platformTurn;

    public CatalogStandardRecreatePreflightService(
        BackupCatalogPayloadResolver payloadResolver,
        CatalogRestoreSessionService catalogRestoreSessions,
        RestoreAttemptCoordinator restoreAttemptCoordinator,
        RuntimeStackManifestStore manifestStore,
        PlatformDomainResolver domainResolver,
        IRuntimeStackTurnPlatformConfigurationProvider platformTurn)
    {
        _payloadResolver = payloadResolver;
        _catalogRestoreSessions = catalogRestoreSessions;
        _restoreAttemptCoordinator = restoreAttemptCoordinator;
        _manifestStore = manifestStore;
        _domainResolver = domainResolver;
        _platformTurn = platformTurn;
    }

    public async Task<CatalogStandardRecreatePreflightResponse> AssessAsync(
        string catalogEntryId,
        StandardRecreatePreflightRequest request,
        CancellationToken ct)
    {
        var source = await _payloadResolver.ResolveStandardRecreateSourceAsync(
            catalogEntryId,
            ct);

        // Creating or resuming the durable restore workspace is intentional
        // audit/workflow state. It does not reserve targets or mutate runtime,
        // Docker, DNS, certificates, routes, or production databases.
        var session = await _catalogRestoreSessions.PrepareAsync(
            source.CatalogEntryId,
            ct);

        var attempt = await _restoreAttemptCoordinator.GetByRestoreSessionIdAsync(
            session.RestoreSessionId,
            ct) ?? throw new InvalidOperationException(
                $"Restore attempt '{session.RestoreSessionId}' was not found after catalog session preparation.");

        var sourceMatrixIdentity = StandardRecreateMatrixIdentity.NormalizeHost(
            source.MatrixServerName);
        if (string.IsNullOrWhiteSpace(sourceMatrixIdentity))
        {
            return BuildMissingMatrixIdentityResponse(source, session, request);
        }

        RestoreStandardRecreateTargetAssessment assessment;
        try
        {
            assessment = await _restoreAttemptCoordinator.AssessStandardRecreateTargetsAsync(
                    attempt.RestoreSessionId,
                    request.TargetStackSlug ?? string.Empty,
                    sourceMatrixIdentity,
                    request.ElementHost ?? string.Empty,
                    ct)
                ?? throw new InvalidOperationException(
                    "Restore attempt was not found while assessing Standard Recreate targets.");
        }
        catch (InvalidOperationException)
        {
            return BuildInvalidTargetResponse(
                source,
                session,
                request,
                sourceMatrixIdentity);
        }

        var checks = assessment.Checks
            .Select(ToPreflightCheck)
            .ToList();
        var blockers = assessment.Blockers.ToList();
        var warnings = new List<string>
        {
            "This catalog-native preflight read only the managed Backup Catalog payload. It did not read the original uploaded ZIP.",
            "Targets are not reserved by this check. MEM repeats every ownership check and reserves targets atomically when creation begins."
        };

        AddCheck(
            checks,
            blockers,
            "backup-catalog-payload-available",
            "Backup Catalog payload is available",
            true,
            source.WarningCount == 0
                ? "The managed Backup Catalog payload is available for restoration."
                : $"The managed Backup Catalog payload is available with {source.WarningCount} retained validation warning(s).");

        if (source.WarningCount > 0)
        {
            warnings.Add(
                $"Review the {source.WarningCount} retained validation warning(s) before approving a production recreate.");
        }

        var requestedMatrixHost = StandardRecreateMatrixIdentity.NormalizeHost(request.MatrixHost);
        var matrixIdentityPreserved = StandardRecreateMatrixIdentity.Matches(
            requestedMatrixHost,
            sourceMatrixIdentity);
        AddCheck(
            checks,
            blockers,
            "matrix-server-identity-preserved",
            "Matrix server identity is preserved",
            matrixIdentityPreserved,
            matrixIdentityPreserved
                ? $"This restore will preserve the Matrix server identity '{sourceMatrixIdentity}'."
                : StandardRecreateMatrixIdentity.CreateMismatchMessage(sourceMatrixIdentity));

        var targetStackSlug = assessment.Resources
            .Single(resource => resource.ResourceType == RestoreTargetResourceTypes.StackSlug)
            .ResourceValue;
        var matrixHost = assessment.Resources
            .Single(resource => resource.ResourceType == RestoreTargetResourceTypes.MatrixHost)
            .ResourceValue;
        var elementHost = assessment.Resources
            .Single(resource => resource.ResourceType == RestoreTargetResourceTypes.ElementHost)
            .ResourceValue;

        var existingManifest = await _manifestStore.FindAsync(targetStackSlug, ct);
        AddCheck(
            checks,
            blockers,
            "target-stack-manifest-available",
            "Stack name is available",
            existingManifest is null,
            existingManifest is null
                ? $"No saved MEM stack manifest uses '{targetStackSlug}'."
                : $"Target stack '{targetStackSlug}' already has a saved MEM stack manifest.");

        try
        {
            await _domainResolver.ResolveMainPlatformDomainAsync(
                request.RequestedDomainId,
                ct);

            AddCheck(
                checks,
                blockers,
                "platform-domain-resolved",
                "Platform domain is available",
                true,
                "MEM can resolve the platform domain required by the standard create path.");
        }
        catch (InvalidOperationException)
        {
            AddCheck(
                checks,
                blockers,
                "platform-domain-resolved",
                "Platform domain is available",
                false,
                "MEM could not resolve the platform domain required by the standard create path.");
        }

        AddRecoveryWarnings(source, elementHost, warnings);

        var backedUpTurnSettingsPresent =
            source.BackupCoturn?.Configured == true &&
            source.BackupCoturn.TurnUris.Count > 0 &&
            source.BackupCoturn.SharedSecretPresent;
        var platformCoturn = await InspectPlatformTurnAsync(ct);
        var turnPlan = StandardRecreateTurnPolicy.Resolve(
            source.BackupCoturn,
            backedUpSettingsPresent: backedUpTurnSettingsPresent,
            platformCoturnAvailable: platformCoturn is not null);
        AddCheck(
            checks,
            blockers,
            "backup-turn-restore-policy",
            "TURN restore policy is usable",
            turnPlan.CanProceed,
            turnPlan.Detail);

        var canCreate = checks.All(check =>
            string.Equals(check.State, "passed", StringComparison.OrdinalIgnoreCase));

        return new CatalogStandardRecreatePreflightResponse(
            Source: "control-plane",
            Status: canCreate ? "ready" : "blocked",
            CheckedAtUtc: DateTimeOffset.UtcNow,
            CatalogEntryId: source.CatalogEntryId,
            RestoreSessionId: assessment.Attempt.RestoreSessionId,
            RestoreAttemptCreated: session.RestoreAttemptCreated,
            RestoreAttemptResumed: session.RestoreAttemptResumed,
            PayloadState: BackupCatalogPayloadStates.Available,
            IntegrityStatus: source.IntegrityStatus,
            WarningCount: source.WarningCount,
            CanCreate: canCreate,
            Targets: new StandardRecreatePreflightTargets(
                targetStackSlug,
                matrixHost,
                elementHost),
            Checks: checks,
            Blockers: blockers.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings: warnings.Distinct(StringComparer.Ordinal).ToArray(),
            Detail: canCreate
                ? "All catalog-native read-only checks passed. Creation is not enabled by this endpoint and would repeat every ownership check before reserving targets atomically."
                : "One or more creation checks need attention. No runtime resources or target reservations were created.")
        {
            Turn = new StandardRecreateTurnSummary(
                Mode: turnPlan.Mode,
                State: turnPlan.TargetState,
                Management: turnPlan.TargetManagement,
                PlatformTurnRequired: turnPlan.RequiresPlatformCoturn,
                PlatformTurnReady: turnPlan.RequiresPlatformCoturn
                    ? platformCoturn is not null
                    : null,
                PublicHost: turnPlan.Mode == StandardRecreateTurnModes.RebindPlatform
                    ? platformCoturn?.PublicHost
                    : source.BackupCoturn?.PublicHost,
                TurnUris: turnPlan.Mode == StandardRecreateTurnModes.RebindPlatform
                    ? platformCoturn?.TurnUris ?? []
                    : source.BackupCoturn?.TurnUris ?? [],
                Detail: turnPlan.Detail)
        };
    }

    private async Task<CoturnSynapseConfig?> InspectPlatformTurnAsync(
        CancellationToken ct)
    {
        try
        {
            return await _platformTurn.GetSynapseConfigAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Platform TURN is a read-only dependency of preflight. Treat an
            // inspection failure as unavailable so MEM-managed restores block
            // safely while disconnected/external restore intent remains usable.
            return null;
        }
    }

    private static CatalogStandardRecreatePreflightResponse BuildMissingMatrixIdentityResponse(
        BackupCatalogStandardRecreateSource source,
        CatalogRestoreSessionResponse session,
        StandardRecreatePreflightRequest request)
    {
        const string safeMessage = "This Backup Catalog payload does not declare the Matrix server identity required for a standard restore.";

        return new CatalogStandardRecreatePreflightResponse(
            Source: "control-plane",
            Status: "blocked",
            CheckedAtUtc: DateTimeOffset.UtcNow,
            CatalogEntryId: source.CatalogEntryId,
            RestoreSessionId: session.RestoreSessionId,
            RestoreAttemptCreated: session.RestoreAttemptCreated,
            RestoreAttemptResumed: session.RestoreAttemptResumed,
            PayloadState: BackupCatalogPayloadStates.Available,
            IntegrityStatus: source.IntegrityStatus,
            WarningCount: source.WarningCount,
            CanCreate: false,
            Targets: new StandardRecreatePreflightTargets(
                NormalizeDisplayValue(request.TargetStackSlug),
                null,
                NormalizeDisplayValue(request.ElementHost)),
            Checks:
            [
                new StandardRecreatePreflightCheck(
                    "matrix-server-identity-available",
                    "Matrix server identity is available",
                    "blocked",
                    safeMessage)
            ],
            Blockers: [safeMessage],
            Warnings:
            [
                "This catalog-native preflight read only the managed Backup Catalog payload. It did not read the original uploaded ZIP.",
                "No runtime resources or target reservations were created."
            ],
            Detail: "The Backup Catalog payload must identify its original Matrix server before MEM can assess a standard restore.");
    }

    private static CatalogStandardRecreatePreflightResponse BuildInvalidTargetResponse(
        BackupCatalogStandardRecreateSource source,
        CatalogRestoreSessionResponse session,
        StandardRecreatePreflightRequest request,
        string sourceMatrixIdentity)
    {
        const string safeMessage = "Enter a valid replacement stack name and Element public host. The Matrix server identity is preserved from the Backup Catalog payload.";

        return new CatalogStandardRecreatePreflightResponse(
            Source: "control-plane",
            Status: "blocked",
            CheckedAtUtc: DateTimeOffset.UtcNow,
            CatalogEntryId: source.CatalogEntryId,
            RestoreSessionId: session.RestoreSessionId,
            RestoreAttemptCreated: session.RestoreAttemptCreated,
            RestoreAttemptResumed: session.RestoreAttemptResumed,
            PayloadState: BackupCatalogPayloadStates.Available,
            IntegrityStatus: source.IntegrityStatus,
            WarningCount: source.WarningCount,
            CanCreate: false,
            Targets: new StandardRecreatePreflightTargets(
                NormalizeDisplayValue(request.TargetStackSlug),
                sourceMatrixIdentity,
                NormalizeDisplayValue(request.ElementHost)),
            Checks:
            [
                new StandardRecreatePreflightCheck(
                    "target-values-valid",
                    "Replacement details are valid",
                    "blocked",
                    safeMessage)
            ],
            Blockers: [safeMessage],
            Warnings:
            [
                "This catalog-native preflight read only the managed Backup Catalog payload. It did not read the original uploaded ZIP.",
                "No runtime resources or target reservations were created."
            ],
            Detail: "Replacement details need attention before MEM can assess availability.");
    }

    private static StandardRecreatePreflightCheck ToPreflightCheck(
        RestoreStandardRecreateTargetAssessmentCheck check) =>
        new(
            check.Code,
            ToTitle(check.Code),
            check.Passed ? "passed" : "blocked",
            check.Message);

    private static void AddCheck(
        ICollection<StandardRecreatePreflightCheck> checks,
        ICollection<string> blockers,
        string code,
        string title,
        bool passed,
        string message)
    {
        checks.Add(new StandardRecreatePreflightCheck(
            code,
            title,
            passed ? "passed" : "blocked",
            message));

        if (!passed)
        {
            blockers.Add(message);
        }
    }

    private static void AddRecoveryWarnings(
        BackupCatalogStandardRecreateSource source,
        string elementHost,
        ICollection<string> warnings)
    {
        var sourceElementHost = StandardRecreateMatrixIdentity.NormalizeHost(
            source.SourceElementHost);

        if (string.Equals(elementHost, sourceElementHost, StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "The Element public host matches the backup's original host. Use it only after the original Element client is no longer serving traffic.");
        }

        if (source.RequiresOldServerStoppedForSameServerName)
        {
            warnings.Add(
                "The original Matrix public route must no longer be active before this server identity can be recovered.");
        }
    }

    private static string? NormalizeDisplayValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string ToTitle(string code) => code switch
    {
        "restore-attempt-ready" => "Restore is ready",
        "restore-operation-idle" => "No create operation is running",
        "restore-target-selection-compatible" => "Selected targets are compatible",
        "target-stack-runtime-available" => "Stack name is available",
        "target-stack-claim-available" => "Stack name is not reserved",
        "matrix-server-identity-preserved" => "Matrix server identity is preserved",
        "matrix-server-identity-available" => "Matrix server identity is available",
        "matrix-host-runtime-available" => "Original Matrix address is ready for recovery",
        "matrix-host-claim-available" => "Original Matrix address is not reserved",
        "element-host-runtime-available" => "Element host is available",
        "element-host-claim-available" => "Element host is not reserved",
        _ => "Creation check"
    };
}

/// <summary>
/// Catalog-native response model. Unlike the legacy preflight response, this
/// deliberately exposes CatalogEntryId and never labels that identity as a
/// validation receipt.
/// </summary>
public sealed record CatalogStandardRecreatePreflightResponse(
    string Source,
    string Status,
    DateTimeOffset CheckedAtUtc,
    string CatalogEntryId,
    string RestoreSessionId,
    bool RestoreAttemptCreated,
    bool RestoreAttemptResumed,
    string PayloadState,
    string IntegrityStatus,
    int WarningCount,
    bool CanCreate,
    StandardRecreatePreflightTargets Targets,
    IReadOnlyList<StandardRecreatePreflightCheck> Checks,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings,
    string Detail)
{
    public StandardRecreateTurnSummary? Turn { get; init; }
}
