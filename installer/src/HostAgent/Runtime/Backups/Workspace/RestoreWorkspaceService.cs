using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Backups.StandardRecreate;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.Readiness;
using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Runtime.Backups.Workspace;

/// <summary>
/// Builds the UI-oriented Restore Workspace projection from the durable restore
/// attempt, target-claim, runtime-operation, structured-log, and safe source
/// metadata stores. This service is intentionally read-only.
/// </summary>
public sealed class RestoreWorkspaceService
{
    private const int MaxWorkspaceEvents = 100;
    private const int MaxWorkspaceOperations = 50;
    private const int MaxEvidenceItemsPerCategory = 50;

    private static readonly JsonSerializerOptions PrivateTestEvidenceJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly MemDbContext _db;
    private readonly RestoreStructuredLogService _logs;
    private readonly RestoreAttemptWorkspaceStore _workspaceStore;
    private readonly BackupCatalogStore _catalogStore;
    private readonly BackupCatalogPayloadResolver _catalogPayloadResolver;
    private readonly PrivateStagingHistoryService _privateStagingHistory;

    public RestoreWorkspaceService(
        MemDbContext db,
        RestoreStructuredLogService logs,
        RestoreAttemptWorkspaceStore workspaceStore,
        BackupCatalogStore catalogStore,
        BackupCatalogPayloadResolver catalogPayloadResolver,
        PrivateStagingHistoryService privateStagingHistory)
    {
        _db = db;
        _logs = logs;
        _workspaceStore = workspaceStore;
        _catalogStore = catalogStore;
        _catalogPayloadResolver = catalogPayloadResolver;
        _privateStagingHistory = privateStagingHistory;
    }

    public async Task<RestoreWorkspaceResponse?> GetAsync(
        string restoreSessionId,
        CancellationToken ct)
    {
        var normalizedRestoreSessionId = RestoreSourceKeyFactory.NormalizePathSegment(
            restoreSessionId,
            "Restore session id is required.");

        var attempt = await _db.RestoreAttempts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RestoreSessionId == normalizedRestoreSessionId, ct);

        if (attempt is null)
        {
            return null;
        }

        var claims = await _db.RestoreTargetClaims
            .AsNoTracking()
            .Where(x => x.RestoreAttemptId == attempt.Id)
            .OrderByDescending(x => x.ClaimedAtUtc)
            .ToListAsync(ct);

        var operations = await _db.RuntimeOperations
            .AsNoTracking()
            .Where(x => x.RestoreAttemptId == attempt.Id)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Take(MaxWorkspaceOperations)
            .ToListAsync(ct);

        var logPage = await _logs.ListAsync(
            attempt.RestoreSessionId,
            new RestoreLogQuery(
                Page: 1,
                PageSize: MaxWorkspaceEvents,
                Severity: null,
                Stage: null,
                Search: null),
            ct);

        var events = logPage?.Events ?? Array.Empty<RestoreLogEvent>();
        var warnings = new List<string>();

        if (logPage is not null)
        {
            warnings.AddRange(logPage.Warnings.Select(ToSafeDisplayMessage));
        }

        var source = await BuildSourceAsync(attempt, events, warnings, ct);
        var target = BuildTarget(claims);

        var operationSummaries = operations
            .Select(operation => ToOperationSummary(operation, events))
            .ToArray();

        var standardRecreateOperation = operations
            .FirstOrDefault(x => string.Equals(
                x.Operation,
                "restore.standard-recreate",
                StringComparison.OrdinalIgnoreCase));

        var standardRecreate = standardRecreateOperation is null
            ? null
            : ToOperationSummary(standardRecreateOperation, events);

        var privateTestOperation = operations
            .FirstOrDefault(x => string.Equals(
                x.Operation,
                "restore.private-test",
                StringComparison.OrdinalIgnoreCase));

        var privateTest = privateTestOperation is null
            ? null
            : ToOperationSummary(privateTestOperation, events);

        var privateTestEvidence = await BuildPrivateTestEvidenceAsync(
            privateTestOperation,
            ct);

        var verification = await BuildVerificationAsync(
            standardRecreateOperation?.RuntimeStackId,
            ct);

        var evidence = BuildEvidence(events, operationSummaries);
        var logs = BuildLogs(attempt, logPage, events);
        var cancellation = BuildCancellation(attempt, operations);
        var overallStatus = BuildOverallStatus(
            attempt,
            source,
            privateTest,
            standardRecreate,
            verification,
            logs);

        var stages = BuildStandardStages(
            attempt,
            target,
            privateTest,
            privateTestEvidence,
            standardRecreate,
            verification,
            source,
            evidence,
            logs);

        var advancedTools = BuildAdvancedTools(
            attempt,
            target,
            standardRecreate,
            source);

        if (attempt.ErrorCount > 0 && evidence.LatestFailure is null)
        {
            warnings.Add("The attempt has recorded errors, but no matching structured evidence item was available.");
        }

        return new RestoreWorkspaceResponse(
            SchemaVersion: 4,
            RestoreSessionId: attempt.RestoreSessionId,
            Attempt: new RestoreWorkspaceAttempt(
                attempt.Id,
                attempt.Status,
                attempt.CurrentStage,
                ToOffset(attempt.CreatedAtUtc),
                ToOffset(attempt.UpdatedAtUtc),
                ToOffset(attempt.TerminalAtUtc),
                ToOffset(attempt.LastEventAtUtc),
                EmptyToNull(attempt.LastErrorCode),
                SafeAttemptErrorSummary(attempt),
                attempt.WarningCount,
                attempt.ErrorCount,
                attempt.RuntimeOperationId),
            Source: source,
            Target: target,
            OverallStatus: overallStatus,
            StandardStages: stages,
            AdvancedTools: advancedTools,
            Verification: verification,
            Evidence: evidence,
            Logs: logs,
            Warnings: warnings
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToArray())
        {
            Cancellation = cancellation
        };
    }

    private static RestoreWorkspaceCancellation BuildCancellation(
        RestoreAttemptEntity attempt,
        IReadOnlyList<RuntimeOperationEntity> operations)
    {
        if (string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.Cancelled,
                StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreWorkspaceCancellation(
                CanCancel: false,
                ReasonUnavailable: "This restore has already been cancelled.",
                Summary: "The restore is terminal. Its logs and evidence remain available for audit.");
        }

        if (string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.Completed,
                StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreWorkspaceCancellation(
                CanCancel: false,
                ReasonUnavailable: "This restore has completed and cannot be cancelled. Manage the restored server through normal stack operations.",
                Summary: "Completed restores remain available as audit records and are not rolled back by workspace cancellation.");
        }

        if (RestoreAttemptStatuses.IsTerminal(attempt.Status))
        {
            return new RestoreWorkspaceCancellation(
                CanCancel: false,
                ReasonUnavailable: $"This restore is terminal with status '{attempt.Status}'.",
                Summary: "Terminal restore records remain available for audit and support.");
        }

        var runningOperation = operations.Any(operation =>
            string.Equals(operation.Status, "queued", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(operation.Status, "running", StringComparison.OrdinalIgnoreCase));

        if (runningOperation)
        {
            return new RestoreWorkspaceCancellation(
                CanCancel: false,
                ReasonUnavailable: "A restore operation is queued or running. Wait for it to finish or fail before cancelling safely.",
                Summary: "MEM will not cancel a live operation halfway through a filesystem, database, Docker, or route mutation.");
        }

        return new RestoreWorkspaceCancellation(
            CanCancel: true,
            ReasonUnavailable: null,
            Summary: "Cancelling releases temporary restore target claims and closes this workspace. It does not delete the backup, audit history, or any completed restored server.");
    }

    private async Task<RestoreWorkspaceSource> BuildSourceAsync(
        RestoreAttemptEntity attempt,
        IReadOnlyList<RestoreLogEvent> events,
        ICollection<string> warnings,
        CancellationToken ct)
    {
        DateTimeOffset? createdAtUtc = ToOffset(attempt.CreatedAtUtc);
        long? sizeBytes = null;
        string? matrixHost = null;
        string? elementHost = null;
        var sourceDeleted = false;
        var sourceAvailable = false;
        var sourceSummary = "The managed Backup Catalog payload is available for restoration.";

        if (!string.Equals(attempt.SourceKind, "backup-catalog", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("This restore record predates the catalog-bound restore model and is retained for audit only.");
            sourceDeleted = true;
            sourceSummary = "This historical restore source is unavailable because it is not backed by the Backup Catalog.";
        }
        else if (attempt.BackupCatalogEntryId is not { } catalogEntryId || catalogEntryId == Guid.Empty)
        {
            sourceDeleted = true;
            sourceSummary = "The source Backup Catalog item was permanently deleted. This restore record remains available for audit.";
            warnings.Add("The source Backup Catalog item was permanently deleted. Restore actions are unavailable.");
        }
        else
        {
            try
            {
                var catalogSource = await _catalogStore.FindRestoreSourceByEntryIdAsync(catalogEntryId, ct);
                if (catalogSource is null)
                {
                    sourceDeleted = true;
                    sourceSummary = "The source Backup Catalog item was permanently deleted. This restore record remains available for audit.";
                    warnings.Add("The source Backup Catalog item could not be found. Restore actions are unavailable.");
                }
                else if (!string.Equals(catalogSource.PayloadState, BackupCatalogPayloadStates.Available, StringComparison.Ordinal) ||
                         string.Equals(catalogSource.IntegrityStatus, BackupCatalogIntegrityStatuses.Invalid, StringComparison.Ordinal))
                {
                    sourceSummary = "The managed Backup Catalog payload is not currently available for restoration.";
                    warnings.Add("The managed Backup Catalog payload is not currently available for restoration.");
                }
                else
                {
                    sourceAvailable = true;
                    createdAtUtc = ToOffset(catalogSource.CapturedAtUtc) ?? createdAtUtc;
                    sizeBytes = catalogSource.PayloadBytes;
                    elementHost = ExtractHost(catalogSource.ElementHost);

                    if (catalogSource.WarningCount > 0)
                    {
                        sourceSummary = $"The managed Backup Catalog payload is available with {catalogSource.WarningCount} retained advisory warning(s).";
                    }

                    try
                    {
                        var standardRecreateSource = await _catalogPayloadResolver
                            .ResolveStandardRecreateSourceAsync(catalogSource.CatalogEntryId, ct);
                        matrixHost = StandardRecreateMatrixIdentity.NormalizeHost(standardRecreateSource.MatrixServerName);
                        elementHost = StandardRecreateMatrixIdentity.NormalizeHost(standardRecreateSource.SourceElementHost) ?? elementHost;

                        if (string.IsNullOrWhiteSpace(matrixHost))
                        {
                            warnings.Add("The Backup Catalog payload is available, but Matrix server identity could not be recovered for Standard Recreate.");
                        }
                    }
                    catch (BackupCatalogPayloadResolutionException)
                    {
                        warnings.Add("The Backup Catalog payload could not be fully inspected for Standard Recreate identity.");
                    }
                }
            }
            catch (Exception ex) when (ex is DirectoryNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                sourceSummary = "The managed Backup Catalog payload could not be inspected. Restore actions remain unavailable until it is checked.";
                warnings.Add("The managed Backup Catalog payload could not be inspected. Restore actions remain unavailable until it is checked.");
            }
        }

        var validationEvent = events
            .OrderByDescending(x => x.TimestampUtc)
            .FirstOrDefault(x => x.EventCode.StartsWith("restore.source.validation.", StringComparison.OrdinalIgnoreCase));

        var validationStatus = sourceDeleted
            ? "source-deleted"
            : sourceAvailable
                ? "available"
                : ResolveValidationStatus(attempt, validationEvent);

        var validationSummary = sourceAvailable || sourceDeleted
            ? sourceSummary
            : ResolveValidationSummary(validationStatus, validationEvent);

        return new RestoreWorkspaceSource(
            Kind: attempt.SourceKind,
            StackSlug: EmptyToNull(attempt.SourceStackSlugSnapshot),
            BackupId: EmptyToNull(attempt.SourceBackupIdSnapshot),
            CreatedAtUtc: createdAtUtc,
            SizeBytes: sizeBytes,
            MatrixHost: matrixHost,
            ElementHost: elementHost,
            ValidationStatus: validationStatus,
            ValidationSummary: validationSummary,
            CatalogEntryId: EmptyToNull(attempt.SourceCatalogEntryIdSnapshot),
            SourceDisplayName: attempt.SourceDisplayNameSnapshot,
            SourceOriginKind: attempt.SourceOriginKindSnapshot,
            SourceDeleted: sourceDeleted);
    }

    private static RestoreWorkspaceTarget BuildTarget(
        IReadOnlyList<RestoreTargetClaimEntity> claims)
    {
        var projections = claims
            .Select(x => new RestoreWorkspaceTargetClaim(
                x.ResourceType,
                x.ResourceValue,
                x.ActiveClaimKey is null ? "released" : "active",
                ToOffset(x.ClaimedAtUtc),
                ToOffset(x.ReleasedAtUtc),
                EmptyToNull(x.ReleaseReason)))
            .ToArray();

        var targets = claims
            .GroupBy(x => x.ResourceType, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(y => y.ClaimedAtUtc).First().ResourceValue,
                StringComparer.OrdinalIgnoreCase);

        var hasActiveClaims = claims.Any(x => x.ActiveClaimKey is not null);

        var availability = hasActiveClaims
            ? "reserved"
            : claims.Count > 0
                ? "released"
                : "not-selected";

        var detail = availability switch
        {
            "reserved" => "This restore currently owns its selected target resources.",
            "released" => "This restore previously used target resources, but its temporary claims are no longer active.",
            _ => "No target has been reserved yet. A normal restore will not replace an existing stack automatically."
        };

        return new RestoreWorkspaceTarget(
            GetTarget(targets, RestoreTargetResourceTypes.StackSlug),
            GetTarget(targets, RestoreTargetResourceTypes.MatrixHost),
            GetTarget(targets, RestoreTargetResourceTypes.ElementHost),
            availability,
            detail,
            projections);
    }

    private async Task<RestoreWorkspaceVerification> BuildVerificationAsync(
        Guid? runtimeStackId,
        CancellationToken ct)
    {
        if (runtimeStackId is null)
        {
            return new RestoreWorkspaceVerification(
                Status: "not-available",
                HasRun: false,
                AllPassed: null,
                CheckedAtUtc: null,
                Checks: Array.Empty<RestoreWorkspaceVerificationCheck>(),
                Summary: "Public server checks are available after the restored stack has been registered.");
        }

        // ReportKind is written by the controlled doctor command using the canonical
        // literal "doctor". Use direct equality so EF Core can translate this query
        // and retain use of the RuntimeStackId/ReportKind index.
        var report = await _db.RuntimeReadinessReports
            .AsNoTracking()
            .Where(x => x.RuntimeStackId == runtimeStackId.Value &&
                        x.ReportKind == "doctor")
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (report is null)
        {
            return new RestoreWorkspaceVerification(
                Status: "not-run",
                HasRun: false,
                AllPassed: null,
                CheckedAtUtc: null,
                Checks: Array.Empty<RestoreWorkspaceVerificationCheck>(),
                Summary: "No fresh public server checks have been run yet.");
        }

        IReadOnlyList<RestoreWorkspaceVerificationCheck> checks =
            Array.Empty<RestoreWorkspaceVerificationCheck>();

        try
        {
            var payload = JsonSerializer.Deserialize<RuntimeReadinessReportPayload>(
                report.ReportJson ?? string.Empty,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (payload?.Checks is { } reportChecks)
            {
                checks = reportChecks
                    .Select(check => new RestoreWorkspaceVerificationCheck(
                        check.Code,
                        ToSafeDisplayMessage(check.Name),
                        check.Success ? "passed" : "failed"))
                    .ToArray();
            }
        }
        catch (JsonException)
        {
            // Preserve the durable report status while withholding malformed
            // details. The normal workspace must never fall back to raw JSON.
        }

        return new RestoreWorkspaceVerification(
            Status: report.AllPassed ? "passed" : "failed",
            HasRun: true,
            AllPassed: report.AllPassed,
            CheckedAtUtc: ToOffset(report.CreatedAtUtc),
            Checks: checks,
            Summary: report.AllPassed
                ? "The latest public server checks passed."
                : "One or more public server checks did not pass.");
    }

    private static RestoreWorkspaceOverallStatus BuildOverallStatus(
        RestoreAttemptEntity attempt,
        RestoreWorkspaceSource source,
        RestoreWorkspaceOperationSummary? privateTest,
        RestoreWorkspaceOperationSummary? standardRecreate,
        RestoreWorkspaceVerification verification,
        RestoreWorkspaceLogsSummary logs)
    {
        var standardRecreateIsRunning =
            string.Equals(
                standardRecreate?.Status,
                "running",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.Recreating,
                StringComparison.OrdinalIgnoreCase);

        var standardRecreateHasStarted = standardRecreate is not null;
        var privateTestFailed = string.Equals(
            privateTest?.Status,
            "failed",
            StringComparison.OrdinalIgnoreCase);
        var privateTestOnlyAttention =
            privateTestFailed &&
            !standardRecreateHasStarted &&
            string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.NeedsAttention,
                StringComparison.OrdinalIgnoreCase) &&
            (attempt.LastErrorCode?.StartsWith(
                "restore.private-test",
                StringComparison.OrdinalIgnoreCase) ?? false);

        if (source.SourceDeleted && !RestoreAttemptStatuses.IsTerminal(attempt.Status))
        {
            return new RestoreWorkspaceOverallStatus(
                "source-deleted",
                "Backup Catalog source is unavailable",
                "The source Backup Catalog item is unavailable. This workspace remains available for audit, but no further restore action can run from it.",
                "warning",
                new RestoreWorkspaceAction(
                    "view-source-details",
                    "Review source details",
                    "Review the catalog-source history and retained restore evidence.",
                    true,
                    "backup-ready"));
        }

        if (string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.Cancelled,
                StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreWorkspaceOverallStatus(
                "cancelled",
                "Restore cancelled",
                "No further restore actions will run for this attempt. Historical evidence remains available.",
                "information",
                new RestoreWorkspaceAction(
                    "view-logs",
                    "View logs",
                    "Review the restore history and cancellation record.",
                    true,
                    "logs"));
        }

        if (string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.Testing,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                privateTest?.Status,
                "running",
                StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreWorkspaceOverallStatus(
                "running-private-test",
                "Private backup test is running",
                "MEM is restoring the backup into an isolated private staging environment. No public server, DNS, or route changes are being made.",
                "information",
                new RestoreWorkspaceAction(
                    "view-live-logs",
                    "View live logs",
                    "Review private-test progress and evidence.",
                    true,
                    "logs"));
        }

        // A running Standard Recreate is the current authoritative lifecycle.
        // A failed optional private test remains visible in its own stage and
        // evidence, but must not make an actively progressing public restore
        // look terminally unhealthy.
        if (standardRecreateIsRunning)
        {
            return new RestoreWorkspaceOverallStatus(
                "creating-restored-server",
                "Creating restored chat server",
                privateTestFailed
                    ? "The restored server is being created. A previous optional private test needs review, but it is historical and is not blocking the current server-owned restore."
                    : "The restore is running. Do not start a second restore for this backup or target.",
                "information",
                new RestoreWorkspaceAction(
                    "view-live-logs",
                    "View live logs",
                    "Review the structured progress events for this running restore.",
                    true,
                    "logs"));
        }

        if (privateTestOnlyAttention)
        {
            return new RestoreWorkspaceOverallStatus(
                "private-test-warning",
                "Optional private test needs review",
                SafeAttemptErrorSummary(attempt) ??
                "The optional private restore test did not complete successfully. You can retry it or continue deliberately to restored server details.",
                "warning",
                new RestoreWorkspaceAction(
                    "choose-restored-server-details",
                    "Choose restored server details",
                    "Continue deliberately with the normal restore path or return to the optional private test.",
                    true,
                    "choose-restored-server-details"));
        }

        if (string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.NeedsAttention,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                standardRecreate?.Status,
                "failed",
                StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreWorkspaceOverallStatus(
                "needs-attention",
                "Restore needs attention",
                SafeAttemptErrorSummary(attempt) ??
                "A restore action needs review before it can continue safely.",
                "error",
                new RestoreWorkspaceAction(
                    "review-failure",
                    "Review the issue",
                    "Open safe evidence and structured logs before retrying or cleaning up.",
                    true,
                    "create-restored-chat-server"));
        }

        if (string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.Completed,
                StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreWorkspaceOverallStatus(
                "completed",
                "Restore complete",
                "The restore has been handed over. Keep this workspace as an audit and support record.",
                "success",
                new RestoreWorkspaceAction(
                    "view-support-report",
                    "View support report",
                    "Open the redacted diagnostic summary for this restore.",
                    logs.SupportReportAvailable,
                    "logs"));
        }

        if (verification.HasRun && verification.AllPassed is false)
        {
            return new RestoreWorkspaceOverallStatus(
                "verification-needs-attention",
                "Restored server checks need attention",
                "One or more public readiness checks did not pass. Review the safe results and run the checks again after resolving the issue.",
                "error",
                new RestoreWorkspaceAction(
                    "check-restored-server",
                    "Review check results",
                    "Review the latest public server check results.",
                    true,
                    "check-restored-server"));
        }

        if (verification.HasRun && verification.AllPassed is true)
        {
            return new RestoreWorkspaceOverallStatus(
                "verification-passed",
                "Restored server checks passed",
                "The latest public readiness checks passed. Complete the final handover when you are satisfied with the recovered service.",
                "success",
                new RestoreWorkspaceAction(
                    "complete-and-hand-over",
                    "Complete and hand over",
                    "Review final handover details and preserve this restore record.",
                    true,
                    "complete-and-hand-over"));
        }

        if (string.Equals(
                standardRecreate?.Status,
                "succeeded",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.Verifying,
                StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreWorkspaceOverallStatus(
                "verification-ready",
                "Restored server is ready to check",
                "The restored server was created. Review public checks and complete the handover when you are satisfied.",
                "success",
                new RestoreWorkspaceAction(
                    "check-restored-server",
                    "Check the restored server",
                    "Run safe public server checks and review the result.",
                    true,
                    "check-restored-server"));
        }

        if (string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.Creating,
                StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreWorkspaceOverallStatus(
                "preparing-backup",
                "Preparing backup",
                "MEM is creating the restore workspace and checking the backup. No public server has been created yet.",
                "information",
                new RestoreWorkspaceAction(
                    "view-logs",
                    "View logs",
                    "Review preparation progress.",
                    true,
                    "logs"));
        }

        return new RestoreWorkspaceOverallStatus(
            "ready",
            "Ready to continue",
            "The backup is ready. A normal restore creates a new stack and does not automatically replace an existing server.",
            "information",
            new RestoreWorkspaceAction(
                "choose-restored-server-details",
                "Choose restored server details",
                "Select the restored stack name and public hosts before creating the server.",
                true,
                "choose-restored-server-details"));
    }

    private static IReadOnlyList<RestoreWorkspaceStandardStage> BuildStandardStages(
        RestoreAttemptEntity attempt,
        RestoreWorkspaceTarget target,
        RestoreWorkspaceOperationSummary? privateTest,
        RestoreWorkspacePrivateTestEvidence? privateTestEvidence,
        RestoreWorkspaceOperationSummary? standardRecreate,
        RestoreWorkspaceVerification verification,
        RestoreWorkspaceSource source,
        RestoreWorkspaceEvidenceSummary evidence,
        RestoreWorkspaceLogsSummary logs)
    {
        var isCancelled = string.Equals(
            attempt.Status,
            RestoreAttemptStatuses.Cancelled,
            StringComparison.OrdinalIgnoreCase);

        var sourceUnavailable =
            source.SourceDeleted &&
            !RestoreAttemptStatuses.IsTerminal(attempt.Status);

        if (sourceUnavailable)
        {
            return BuildSourceDeletedStandardStages(
                evidence,
                logs);
        }

        var backupReady =
            !sourceUnavailable &&
            (string.Equals(
                source.ValidationStatus,
                "available",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.Ready,
                StringComparison.OrdinalIgnoreCase) ||
            standardRecreate is not null);

        var privateTestRetryAvailable =
            string.Equals(
                source.Kind,
                "backup-catalog",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.NeedsAttention,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                privateTest?.Status,
                "failed",
                StringComparison.OrdinalIgnoreCase);

        var hasTarget =
            !string.IsNullOrWhiteSpace(target.StackSlug) ||
            !string.IsNullOrWhiteSpace(target.MatrixHost) ||
            !string.IsNullOrWhiteSpace(target.ElementHost);

        var recreateState = ToStageState(standardRecreate?.Status);

        var recreateCompleted = string.Equals(
            recreateState,
            RestoreWorkspaceStageStates.Completed,
            StringComparison.OrdinalIgnoreCase);

        var recreateFailed = string.Equals(
            recreateState,
            RestoreWorkspaceStageStates.Failed,
            StringComparison.OrdinalIgnoreCase);

        // Standard Recreate performs creation-time route and health checks, but
        // that is not the operator's Step 4 completion. Step 4 completes only
        // after a fresh explicit public server check has been recorded.
        var verificationCompleted = verification.HasRun &&
            verification.AllPassed is true;

        var verificationFailed = verification.HasRun &&
            verification.AllPassed is false;

        var stages = new List<RestoreWorkspaceStandardStage>
        {
            new(
                "backup-ready",
                "Backup ready",
                "Check that the backup can be used before any public server is created.",
                isCancelled
                    ? RestoreWorkspaceStageStates.Cancelled
                    : sourceUnavailable
                        ? RestoreWorkspaceStageStates.Blocked
                        : backupReady
                            ? RestoreWorkspaceStageStates.Completed
                            : RestoreWorkspaceStageStates.Running,
                true,
                true,
                backupReady
                    ? attempt.LastEventAtUtc is null
                        ? null
                        : ToOffset(attempt.LastEventAtUtc.Value)
                    : null,
                backupReady
                    ? null
                    : new RestoreWorkspaceAction(
                        "view-logs",
                        "View logs",
                        "Review backup preparation progress.",
                        true,
                        "logs"),
                new[]
                {
                    new RestoreWorkspaceAction(
                        "view-evidence",
                        "View evidence",
                        "Review catalog-source evidence for this backup.",
                        true,
                        "evidence")
                },
                backupReady
                    ? source.ValidationSummary
                    : sourceUnavailable
                        ? source.ValidationSummary
                        : "Backup preparation is still in progress.",
                backupReady
                    ? Array.Empty<string>()
                    : sourceUnavailable
                        ? new[] { "The source Backup Catalog item is unavailable." }
                        : new[] { "The managed Backup Catalog payload is not available yet." },
                EvidenceForCategory(evidence, "backup-validation"),
                null),

            new(
                "private-test",
                "Optional: test the backup privately",
                "Run an isolated test before creating a public restored server. This is optional reassurance, not a requirement.",
                isCancelled
                    ? RestoreWorkspaceStageStates.Cancelled
                    : string.Equals(privateTest?.Status, "running", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(attempt.Status, RestoreAttemptStatuses.Testing, StringComparison.OrdinalIgnoreCase)
                        ? RestoreWorkspaceStageStates.Running
                        : string.Equals(privateTest?.Status, "succeeded", StringComparison.OrdinalIgnoreCase)
                            ? RestoreWorkspaceStageStates.Completed
                            : string.Equals(privateTest?.Status, "failed", StringComparison.OrdinalIgnoreCase)
                                ? RestoreWorkspaceStageStates.Failed
                                : RestoreWorkspaceStageStates.Optional,
                false,
                (backupReady || privateTestRetryAvailable) && !isCancelled && !string.Equals(privateTest?.Status, "running", StringComparison.OrdinalIgnoreCase),
                string.Equals(privateTest?.Status, "succeeded", StringComparison.OrdinalIgnoreCase)
                    ? privateTest.CompletedAtUtc
                    : null,
                new RestoreWorkspaceAction(
                    "run-private-test",
                    "Run private test",
                    "Start an isolated restore test without replacing or publishing an existing server.",
                    (backupReady || privateTestRetryAvailable) && !isCancelled && !string.Equals(privateTest?.Status, "running", StringComparison.OrdinalIgnoreCase),
                    "private-test"),
                new[]
                {
                    new RestoreWorkspaceAction(
                        "open-database-lab",
                        "Open database lab",
                        "Inspect restored database evidence before a public restore.",
                        backupReady && !isCancelled,
                        "advanced-tools")
                },
                string.Equals(privateTest?.Status, "running", StringComparison.OrdinalIgnoreCase)
                    ? "Private test is running in an isolated staging environment."
                    : string.Equals(privateTest?.Status, "succeeded", StringComparison.OrdinalIgnoreCase)
                        ? string.Equals(
                            privateTestEvidence?.StagingRuntimeStatus,
                            "destroyed",
                            StringComparison.OrdinalIgnoreCase)
                            ? "Private test completed. The isolated staging environment was explicitly destroyed; historical evidence remains available."
                            : "Private test completed. The isolated staging environment is retained until explicitly destroyed."
                        : string.Equals(privateTest?.Status, "failed", StringComparison.OrdinalIgnoreCase)
                            ? "Private test needs attention. Review safe evidence and logs before retrying."
                            : backupReady
                                ? "Optional. You can continue directly to the restored server details when policy permits."
                                : "Available after the Backup Catalog payload becomes available.",
                backupReady || privateTestRetryAvailable || isCancelled
                    ? Array.Empty<string>()
                    : new[] { "Complete backup preparation first." },
                EvidenceForCategory(evidence, "private-test"),
                privateTest)
            {
                PrivateTestEvidence = privateTestEvidence
            },

            new(
                "choose-restored-server-details",
                "Choose restored server details",
                "Select a new stack name and the Matrix and Element public hosts. MEM will not overwrite an existing stack automatically.",
                isCancelled
                    ? RestoreWorkspaceStageStates.Cancelled
                    : hasTarget
                        ? RestoreWorkspaceStageStates.Completed
                        : backupReady
                            ? RestoreWorkspaceStageStates.Ready
                            : RestoreWorkspaceStageStates.Blocked,
                true,
                backupReady && !isCancelled,
                hasTarget
                    ? LatestClaimedAtUtc(target.Claims)
                    : null,
                new RestoreWorkspaceAction(
                    hasTarget
                        ? "review-restored-server-details"
                        : "choose-restored-server-details",
                    hasTarget
                        ? "Review restored server details"
                        : "Choose restored server details",
                    hasTarget
                        ? "Review the target currently associated with this restore."
                        : "Choose a new stack name and public hosts.",
                    backupReady && !isCancelled,
                    "choose-restored-server-details"),
                Array.Empty<RestoreWorkspaceAction>(),
                target.Detail,
                backupReady || isCancelled
                    ? Array.Empty<string>()
                    : new[] { "Complete backup preparation first." },
                EvidenceForCategory(evidence, "target-reservation"),
                null),

            new(
                "create-restored-chat-server",
                "Create restored chat server",
                "Create a new Matrix and Element server from the backup. Existing stacks are not deleted or replaced automatically.",
                isCancelled
                    ? RestoreWorkspaceStageStates.Cancelled
                    : recreateState,
                true,
                backupReady &&
                !isCancelled &&
                !recreateCompleted &&
                !recreateFailed,
                recreateCompleted
                    ? standardRecreate?.CompletedAtUtc
                    : null,
                BuildRecreateAction(
                    recreateState,
                    backupReady,
                    isCancelled),
                new[]
                {
                    new RestoreWorkspaceAction(
                        "view-live-logs",
                        "View live logs",
                        "Review structured restore progress and safe failure summaries.",
                        true,
                        "logs")
                },
                BuildRecreateSummary(
                    standardRecreate,
                    target),
                BuildRecreateBlockers(
                    backupReady,
                    isCancelled,
                    recreateState),
                EvidenceForCategory(evidence, "standard-recreate"),
                standardRecreate),

            new(
                "check-restored-server",
                "Check the restored server",
                "Run safe public Matrix, Element, route, and connectivity checks for the restored server.",
                isCancelled
                    ? RestoreWorkspaceStageStates.Cancelled
                    : verificationCompleted
                        ? RestoreWorkspaceStageStates.Completed
                        : verificationFailed
                            ? RestoreWorkspaceStageStates.Failed
                            : recreateCompleted
                                ? RestoreWorkspaceStageStates.Ready
                                : recreateFailed
                                    ? RestoreWorkspaceStageStates.Blocked
                                    : RestoreWorkspaceStageStates.Blocked,
                true,
                recreateCompleted && !isCancelled,
                verificationCompleted
                    ? verification.CheckedAtUtc
                    : null,
                new RestoreWorkspaceAction(
                    "check-restored-server",
                    "Check the restored server",
                    "Run a fresh public server check and review the safe result.",
                    recreateCompleted && !isCancelled,
                    "check-restored-server"),
                new[]
                {
                    new RestoreWorkspaceAction(
                        "view-evidence",
                        "View verification evidence",
                        "Inspect successful and failed verification evidence.",
                        recreateCompleted,
                        "evidence")
                },
                verificationCompleted
                    ? "The latest public server checks passed. Continue to final handover when you are satisfied."
                    : verificationFailed
                        ? "One or more public server checks need attention. Review the safe results and run the checks again after resolving the issue."
                        : recreateCompleted
                            ? "The restored server was created. Run a fresh public check before handover."
                            : "Available after the restored chat server is created.",
                recreateCompleted || isCancelled
                    ? Array.Empty<string>()
                    : new[] { "Create the restored chat server first." },
                EvidenceForCategory(evidence, "public-verification"),
                standardRecreate),

            new(
                "complete-and-hand-over",
                "Complete and hand over",
                "Confirm the restored service is ready, then keep this workspace as a durable audit and support record.",
                isCancelled
                    ? RestoreWorkspaceStageStates.Cancelled
                    : string.Equals(
                        attempt.Status,
                        RestoreAttemptStatuses.Completed,
                        StringComparison.OrdinalIgnoreCase)
                        ? RestoreWorkspaceStageStates.Completed
                        : verificationCompleted
                            ? RestoreWorkspaceStageStates.Ready
                            : RestoreWorkspaceStageStates.Blocked,
                true,
                verificationCompleted && !isCancelled,
                string.Equals(
                    attempt.Status,
                    RestoreAttemptStatuses.Completed,
                    StringComparison.OrdinalIgnoreCase)
                    ? ToOffset(attempt.TerminalAtUtc)
                    : null,
                new RestoreWorkspaceAction(
                    "complete-and-hand-over",
                    "Complete and hand over",
                    "Review final verification and preserve the restore record.",
                    verificationCompleted && !isCancelled,
                    "complete-and-hand-over"),
                new[]
                {
                    new RestoreWorkspaceAction(
                        "view-support-report",
                        "View support report",
                        "Open the current redacted support report.",
                        logs.SupportReportAvailable,
                        "logs")
                },
                verificationCompleted
                    ? "The restored server is ready for final operator handover."
                    : "Available after the restored server checks pass.",
                verificationCompleted || isCancelled
                    ? Array.Empty<string>()
                    : new[] { "Complete restored-server checks first." },
                EvidenceForCategory(evidence, "handover"),
                null)
        };

        return stages;
    }

    /// <summary>
    /// A permanently deleted Backup Catalog source leaves the workspace readable
    /// for audit, logs, evidence, configuration, and support, while preventing
    /// every guided continuation action from implying it can still run.
    /// </summary>
    private static IReadOnlyList<RestoreWorkspaceStandardStage> BuildSourceDeletedStandardStages(
        RestoreWorkspaceEvidenceSummary evidence,
        RestoreWorkspaceLogsSummary logs)
    {
        const string summary =
            "The source Backup Catalog item is unavailable. This workspace remains available for audit only.";

        const string blocker =
            "Start a new restore from an available Backup Catalog item.";

        return new[]
        {
            new RestoreWorkspaceStandardStage(
                "backup-ready",
                "Backup ready",
                "Check that the backup can be used before any public server is created.",
                RestoreWorkspaceStageStates.Blocked,
                true,
                false,
                null,
                new RestoreWorkspaceAction(
                    "view-logs",
                    "View logs",
                    "Review the restore history and source-deletion event.",
                    true,
                    "logs"),
                new[]
                {
                    new RestoreWorkspaceAction(
                        "view-evidence",
                        "View evidence",
                        "Review retained catalog-source evidence for this backup.",
                        true,
                        "evidence")
                },
                summary,
                new[] { blocker },
                EvidenceForCategory(evidence, "backup-validation"),
                null),

            new RestoreWorkspaceStandardStage(
                "private-test",
                "Optional: test the backup privately",
                "Run an isolated test before creating a public restored server. This is optional reassurance, not a requirement.",
                RestoreWorkspaceStageStates.Blocked,
                false,
                false,
                null,
                new RestoreWorkspaceAction(
                    "run-private-test",
                    "Run private test",
                    "Start an isolated restore test without replacing or publishing an existing server.",
                    false,
                    "private-test"),
                new[]
                {
                    new RestoreWorkspaceAction(
                        "open-database-lab",
                        "Open database lab",
                        "Inspect restored database evidence before a public restore.",
                        false,
                        "advanced-tools")
                },
                summary,
                new[] { blocker },
                EvidenceForCategory(evidence, "private-test"),
                null),

            new RestoreWorkspaceStandardStage(
                "choose-restored-server-details",
                "Choose restored server details",
                "Select a new stack name and the Matrix and Element public hosts. MEM will not overwrite an existing stack automatically.",
                RestoreWorkspaceStageStates.Blocked,
                true,
                false,
                null,
                new RestoreWorkspaceAction(
                    "choose-restored-server-details",
                    "Choose restored server details",
                    "Choose a new stack name and public hosts.",
                    false,
                    "choose-restored-server-details"),
                Array.Empty<RestoreWorkspaceAction>(),
                summary,
                new[] { blocker },
                EvidenceForCategory(evidence, "target-reservation"),
                null),

            new RestoreWorkspaceStandardStage(
                "create-restored-chat-server",
                "Create restored chat server",
                "Create a new Matrix and Element server from the backup. Existing stacks are not deleted or replaced automatically.",
                RestoreWorkspaceStageStates.Blocked,
                true,
                false,
                null,
                new RestoreWorkspaceAction(
                    "create-restored-chat-server",
                    "Create restored chat server",
                    "Create a new restored Matrix and Element server from this backup.",
                    false,
                    "create-restored-chat-server"),
                new[]
                {
                    new RestoreWorkspaceAction(
                        "view-live-logs",
                        "View live logs",
                        "Review retained structured restore progress and safe failure summaries.",
                        true,
                        "logs")
                },
                summary,
                new[] { blocker },
                EvidenceForCategory(evidence, "standard-recreate"),
                null),

            new RestoreWorkspaceStandardStage(
                "check-restored-server",
                "Check the restored server",
                "Run safe public Matrix, Element, route, and connectivity checks for the restored server.",
                RestoreWorkspaceStageStates.Blocked,
                true,
                false,
                null,
                new RestoreWorkspaceAction(
                    "check-restored-server",
                    "Check the restored server",
                    "Run a fresh public server check and review the safe result.",
                    false,
                    "check-restored-server"),
                new[]
                {
                    new RestoreWorkspaceAction(
                        "view-evidence",
                        "View verification evidence",
                        "Inspect retained verification evidence.",
                        true,
                        "evidence")
                },
                summary,
                new[] { blocker },
                EvidenceForCategory(evidence, "public-verification"),
                null),

            new RestoreWorkspaceStandardStage(
                "complete-and-hand-over",
                "Complete and hand over",
                "Confirm the restored service is ready, then keep this workspace as a durable audit and support record.",
                RestoreWorkspaceStageStates.Blocked,
                true,
                false,
                null,
                new RestoreWorkspaceAction(
                    "complete-and-hand-over",
                    "Complete and hand over",
                    "Review final verification and preserve the restore record.",
                    false,
                    "complete-and-hand-over"),
                new[]
                {
                    new RestoreWorkspaceAction(
                        "view-support-report",
                        "View support report",
                        "Open the current redacted support report.",
                        logs.SupportReportAvailable,
                        "logs")
                },
                summary,
                new[] { blocker },
                EvidenceForCategory(evidence, "handover"),
                null)
        };
    }

    private static IReadOnlyList<RestoreWorkspaceAdvancedTool> BuildAdvancedTools(
        RestoreAttemptEntity attempt,
        RestoreWorkspaceTarget target,
        RestoreWorkspaceOperationSummary? standardRecreate,
        RestoreWorkspaceSource source)
    {
        var sourceUnavailable = source.SourceDeleted;

        if (sourceUnavailable)
        {
            const string reason =
                "The source Backup Catalog item is unavailable. Start a new restore from an available catalog item before using this tool.";

            return new[]
            {
                new RestoreWorkspaceAdvancedTool(
                    "database-lab",
                    "Database lab",
                    "unavailable",
                    reason,
                    "backup-ready"),

                new RestoreWorkspaceAdvancedTool(
                    "private-synapse-rehearsal",
                    "Private Synapse rehearsal",
                    "unavailable",
                    reason,
                    "private-test"),

                new RestoreWorkspaceAdvancedTool(
                    "private-staging",
                    "Private staging",
                    "unavailable",
                    reason,
                    "private-test"),

                new RestoreWorkspaceAdvancedTool(
                    "advanced-cutover",
                    "Advanced cutover",
                    "unavailable",
                    reason,
                    "create-restored-chat-server"),

                new RestoreWorkspaceAdvancedTool(
                    "restore-management",
                    "Restore management",
                    "unavailable",
                    reason,
                    "choose-restored-server-details")
            };
        }

        var hasFailure =
            string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.NeedsAttention,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                standardRecreate?.Status,
                "failed",
                StringComparison.OrdinalIgnoreCase);

        var failureReason = hasFailure
            ? "This restore needs attention. Advanced diagnostics are available."
            : null;

        return new[]
        {
            new RestoreWorkspaceAdvancedTool(
                "database-lab",
                "Database lab",
                "available",
                failureReason,
                "backup-ready"),

            new RestoreWorkspaceAdvancedTool(
                "private-synapse-rehearsal",
                "Private Synapse rehearsal",
                "available",
                failureReason,
                "private-test"),

            new RestoreWorkspaceAdvancedTool(
                "private-staging",
                "Private staging",
                "available",
                failureReason,
                "private-test"),

            new RestoreWorkspaceAdvancedTool(
                "advanced-cutover",
                "Advanced cutover",
                "available",
                "Advanced cutover is separate from the normal create-restored-chat-server journey.",
                "create-restored-chat-server"),

            new RestoreWorkspaceAdvancedTool(
                "restore-management",
                "Restore management",
                target.Availability == "reserved"
                    ? "attention"
                    : "available",
                target.Availability == "reserved"
                    ? "This restore currently owns target claims. Cancel or complete it before reusing the same target."
                    : null,
                "choose-restored-server-details")
        };
    }

    private static RestoreWorkspaceEvidenceSummary BuildEvidence(
        IReadOnlyList<RestoreLogEvent> events,
        IReadOnlyList<RestoreWorkspaceOperationSummary> operations)
    {
        var items = new List<RestoreWorkspaceEvidenceItem>();

        foreach (var @event in events)
        {
            var category = GetEventCategory(@event);

            items.Add(new RestoreWorkspaceEvidenceItem(
                Code: @event.EventCode,
                Category: category.Code,
                Title: category.Title,
                Status: GetEvidenceStatus(
                    @event.Severity,
                    @event.EventCode),
                OccurredAtUtc: @event.TimestampUtc,
                EventCode: @event.EventCode,
                OperationId: @event.OperationId,
                Stage: @event.Stage,
                Description: ToSafeDisplayMessage(@event.Message)));
        }

        foreach (var operation in operations)
        {
            var category = GetOperationCategory(operation.Operation);

            var occurredAt =
                operation.CompletedAtUtc ??
                operation.StartedAtUtc ??
                operation.RequestedAtUtc;

            items.Add(new RestoreWorkspaceEvidenceItem(
                Code: $"operation:{operation.Operation}:{operation.OperationId:N}",
                Category: category.Code,
                Title: category.Title,
                Status: GetOperationEvidenceStatus(operation.Status),
                OccurredAtUtc: occurredAt,
                EventCode: null,
                OperationId: operation.OperationId,
                Stage: operation.CurrentStep,
                Description: BuildOperationEvidenceDescription(operation)));
        }

        var ordered = items
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ToArray();

        var categories = ordered
            .GroupBy(x => new
            {
                x.Category,
                x.Title
            })
            .OrderBy(x => EvidenceCategoryOrder(x.Key.Category))
            .Select(group => new RestoreWorkspaceEvidenceCategory(
                group.Key.Category,
                group.Key.Title,
                GetCategoryStatus(group),
                group.Count(),
                group.Max(x => x.OccurredAtUtc),
                group.Take(MaxEvidenceItemsPerCategory).ToArray()))
            .ToArray();

        return new RestoreWorkspaceEvidenceSummary(
            categories,
            ordered.FirstOrDefault(x => string.Equals(
                x.Status,
                "failed",
                StringComparison.OrdinalIgnoreCase)),
            ordered.FirstOrDefault(x => string.Equals(
                x.Status,
                "succeeded",
                StringComparison.OrdinalIgnoreCase)));
    }

    private RestoreWorkspaceLogsSummary BuildLogs(
        RestoreAttemptEntity attempt,
        RestoreLogPage? logPage,
        IReadOnlyList<RestoreLogEvent> events)
    {
        var summary = logPage?.Summary ??
            RestoreStructuredLogService.BuildSummary(
                events,
                attempt.WarningCount,
                attempt.ErrorCount);

        var reportPath = Path.Combine(
            _workspaceStore.GetSessionDirectoryPath(attempt.RestoreSessionId),
            "support",
            "support-report.json");

        return new RestoreWorkspaceLogsSummary(
            summary.TotalEvents,
            summary.WarningCount,
            summary.ErrorCount,
            ToLogEventSummary(summary.LatestEvent),
            ToLogEventSummary(summary.LatestWarningOrError),
            File.Exists(reportPath),
            false,
            logPage?.Warnings
                .Select(ToSafeDisplayMessage)
                .ToArray() ??
            Array.Empty<string>());
    }

    private static RestoreWorkspaceStageEvidenceSummary EvidenceForCategory(
        RestoreWorkspaceEvidenceSummary evidence,
        string categoryCode)
    {
        var category = evidence.Categories.FirstOrDefault(x => string.Equals(
            x.Code,
            categoryCode,
            StringComparison.OrdinalIgnoreCase));

        return category is null
            ? new RestoreWorkspaceStageEvidenceSummary(
                0,
                null,
                null)
            : new RestoreWorkspaceStageEvidenceSummary(
                category.ItemCount,
                category.LatestOccurredAtUtc,
                category.Status);
    }

    private static RestoreWorkspaceAction? BuildRecreateAction(
        string recreateState,
        bool backupReady,
        bool isCancelled)
    {
        if (isCancelled)
        {
            return null;
        }

        return recreateState switch
        {
            RestoreWorkspaceStageStates.Running => new RestoreWorkspaceAction(
                "view-live-logs",
                "View live logs",
                "Review restore progress while creation is running.",
                true,
                "logs"),

            RestoreWorkspaceStageStates.Failed => new RestoreWorkspaceAction(
                "review-failure",
                "Review failure",
                "Review safe evidence and logs before retrying or cleaning up.",
                true,
                "create-restored-chat-server"),

            RestoreWorkspaceStageStates.Completed => new RestoreWorkspaceAction(
                "check-restored-server",
                "Check the restored server",
                "Review public readiness and post-restore verification.",
                true,
                "check-restored-server"),

            _ => new RestoreWorkspaceAction(
                "create-restored-chat-server",
                "Create restored chat server",
                "Create a new restored Matrix and Element server from this backup.",
                backupReady,
                "create-restored-chat-server")
        };
    }

    private static string BuildRecreateSummary(
        RestoreWorkspaceOperationSummary? operation,
        RestoreWorkspaceTarget target)
    {
        if (operation is null)
        {
            return target.Availability == "not-selected"
                ? "Choose the restored server details, then create a new restored chat server."
                : "The selected target is recorded. You can continue with creating the restored chat server.";
        }

        return operation.Status.ToLowerInvariant() switch
        {
            "running" => "The restored chat server is being created. Review logs for progress.",
            "failed" => "Creation failed. Review the safe failure summary, evidence, and logs before retrying or cleaning up.",
            "succeeded" => "The restored chat server was created. Continue to checks and handover.",
            _ => "The restore operation has a recorded state. Review evidence and logs for details."
        };
    }

    private static IReadOnlyList<string> BuildRecreateBlockers(
        bool backupReady,
        bool isCancelled,
        string recreateState)
    {
        if (isCancelled)
        {
            return new[] { "This restore was cancelled." };
        }

        if (!backupReady)
        {
            return new[] { "Complete backup preparation first." };
        }

        if (recreateState == RestoreWorkspaceStageStates.Running)
        {
            return new[] { "A create-restored-chat-server operation is already running." };
        }

        return Array.Empty<string>();
    }

    private static string ToStageState(string? operationStatus) =>
        operationStatus?.ToLowerInvariant() switch
        {
            "running" or "queued" or "active" =>
                RestoreWorkspaceStageStates.Running,

            "succeeded" or "completed" or "passed" or "public_routes_verified" =>
                RestoreWorkspaceStageStates.Completed,

            "failed" or "error" =>
                RestoreWorkspaceStageStates.Failed,

            "cancelled" =>
                RestoreWorkspaceStageStates.Cancelled,

            _ =>
                RestoreWorkspaceStageStates.NotStarted
        };

    private async Task<RestoreWorkspacePrivateTestEvidence?> BuildPrivateTestEvidenceAsync(
        RuntimeOperationEntity? operation,
        CancellationToken ct)
    {
        if (operation is null ||
            !string.Equals(
                operation.Operation,
                "restore.private-test",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(operation.EvidenceJson))
        {
            return null;
        }

        RestorePrivateTestEvidence? persistedEvidence;
        try
        {
            persistedEvidence = JsonSerializer.Deserialize<RestorePrivateTestEvidence>(
                operation.EvidenceJson,
                PrivateTestEvidenceJsonOptions);
        }
        catch (JsonException)
        {
            // A malformed historical operation record must not make the
            // read-only workspace unavailable. Its structured logs and normal
            // operation summary remain visible.
            return null;
        }

        if (persistedEvidence is null)
        {
            return null;
        }

        var history = await FindMatchingPrivateStagingHistoryAsync(
            persistedEvidence,
            ct);

        return new RestoreWorkspacePrivateTestEvidence(
            SourceKind: EmptyToNull(persistedEvidence.SourceKind) ?? "unknown",
            CatalogEntryId: EmptyToNull(persistedEvidence.CatalogEntryId),
            StagingId: EmptyToNull(persistedEvidence.StagingId),
            // Prefer the immutable operation evidence, but safely recover the
            // server name from the matching private-staging history for older
            // operations that were recorded before that field was persisted.
            MatrixServerName: EmptyToNull(persistedEvidence.MatrixServerName) ??
                              EmptyToNull(history?.MatrixServerName),
            Status: EmptyToNull(persistedEvidence.Status) ?? "unknown",
            PrivateOnly: persistedEvidence.PrivateOnly,
            DockerNetworkInternal: persistedEvidence.DockerNetworkInternal,
            DatabaseImportSucceeded: persistedEvidence.DatabaseImportSucceeded,
            SynapseHealthPassed: persistedEvidence.SynapseHealthPassed,
            RequiresExplicitDestroy: persistedEvidence.RequiresExplicitDestroy,
            CompletedAtUtc: ToOffset(operation.CompletedAtUtc),
            StagingRuntimeStatus: ResolveStagingRuntimeStatus(history),
            StagingRuntimeDestroyed: history?.Destroyed,
            DestroyAvailable: history?.DestroyAvailable,
            DestroyedAtUtc: history?.DestroyedAtUtc);
    }

    private async Task<PrivateStagingSafeRunSummary?> FindMatchingPrivateStagingHistoryAsync(
        RestorePrivateTestEvidence evidence,
        CancellationToken ct)
    {
        var stagingId = EmptyToNull(evidence.StagingId);
        if (stagingId is null)
        {
            return null;
        }

        try
        {
            var response = await _privateStagingHistory.GetSafeRunAsync(
                stagingId,
                ct);
            var run = response?.Run;

            return run is not null && MatchesPrivateTestEvidence(run, evidence)
                ? run
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // History is supplementary lifecycle information. Preserve durable
            // operation evidence even when an old history file is unavailable
            // or cannot be read safely.
            return null;
        }
    }

    private static bool MatchesPrivateTestEvidence(
        PrivateStagingSafeRunSummary run,
        RestorePrivateTestEvidence evidence) =>
        string.Equals(run.StagingId, evidence.StagingId, StringComparison.Ordinal) &&
        string.Equals(run.SourceKind, evidence.SourceKind, StringComparison.Ordinal) &&
        string.Equals(run.CatalogEntryId, evidence.CatalogEntryId, StringComparison.Ordinal);

    private static string ResolveStagingRuntimeStatus(
        PrivateStagingSafeRunSummary? history) =>
        history is null
            ? "unknown"
            : history.Destroyed
                ? "destroyed"
                : string.Equals(
                    history.Status,
                    "destroy-needs-attention",
                    StringComparison.OrdinalIgnoreCase)
                    ? "retirement-needs-attention"
                    : history.DestroyAvailable
                        ? "retained"
                        : "not-retained";

    private static RestoreWorkspaceOperationSummary ToOperationSummary(
        RuntimeOperationEntity operation,
        IReadOnlyList<RestoreLogEvent> events)
    {
        var lastActivityAtUtc = events
            .Where(@event => @event.OperationId == operation.Id)
            .OrderByDescending(@event => @event.TimestampUtc)
            .Select(@event => (DateTimeOffset?)@event.TimestampUtc)
            .FirstOrDefault()
            ?? ToOffset(operation.CompletedAtUtc)
            ?? ToOffset(operation.StartedAtUtc)
            ?? ToOffset(operation.RequestedAtUtc);

        return new RestoreWorkspaceOperationSummary(
            operation.Id,
            operation.Operation,
            operation.Status,
            EmptyToNull(operation.CurrentStep),
            ToOffset(operation.RequestedAtUtc),
            ToOffset(operation.StartedAtUtc),
            ToOffset(operation.CompletedAtUtc))
        {
            LastActivityAtUtc = lastActivityAtUtc,
            AttemptNumber = Math.Max(1, operation.AttemptCount)
        };
    }

    private static RestoreWorkspaceLogEventSummary? ToLogEventSummary(
        RestoreLogEvent? @event) =>
        @event is null
            ? null
            : new RestoreWorkspaceLogEventSummary(
                @event.TimestampUtc,
                @event.Stage,
                @event.Severity,
                @event.EventCode,
                ToSafeDisplayMessage(@event.Message),
                @event.OperationId);

    private static string ResolveValidationStatus(
        RestoreAttemptEntity attempt,
        RestoreLogEvent? validationEvent)
    {
        if (validationEvent is not null)
        {
            return validationEvent.EventCode.EndsWith(
                ".passed",
                StringComparison.OrdinalIgnoreCase)
                ? "passed"
                : validationEvent.Severity is
                    RestoreLogSeverities.Error or
                    RestoreLogSeverities.Critical
                    ? "failed"
                    : "in-progress";
        }

        if (string.Equals(
                attempt.Status,
                RestoreAttemptStatuses.NeedsAttention,
                StringComparison.OrdinalIgnoreCase) &&
            attempt.CurrentStage.Contains(
                "source",
                StringComparison.OrdinalIgnoreCase))
        {
            return "failed";
        }

        return "not-started";
    }

    private static string ResolveValidationSummary(
        string validationStatus,
        RestoreLogEvent? validationEvent) =>
        validationStatus switch
        {
            "passed" => validationEvent is null
                ? "The managed Backup Catalog payload is available."
                : ToSafeDisplayMessage(validationEvent.Message),

            "failed" =>
                "The managed Backup Catalog payload needs attention. Review safe evidence and logs.",

            "in-progress" =>
                "The managed Backup Catalog payload is still being prepared.",

            "available" =>
                "The managed Backup Catalog payload is available for this restore.",

            _ =>
                "The managed Backup Catalog payload is not available yet."
        };

    private static (string Code, string Title) GetEventCategory(
        RestoreLogEvent @event)
    {
        if (@event.EventCode.Contains(
                "validation",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                @event.Stage,
                "validation",
                StringComparison.OrdinalIgnoreCase))
        {
            return ("backup-validation", "Backup validation");
        }

        if (@event.EventCode.Contains(
                "verification",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                @event.Stage,
                RestoreAttemptStages.PublicVerification,
                StringComparison.OrdinalIgnoreCase))
        {
            return ("public-verification", "Public verification");
        }

        if (@event.EventCode.Contains(
                "target",
                StringComparison.OrdinalIgnoreCase) ||
            @event.EventCode.Contains(
                "claim",
                StringComparison.OrdinalIgnoreCase))
        {
            return ("target-reservation", "Target reservation");
        }

        if (@event.EventCode.Contains(
                "standard-recreate",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                @event.Stage,
                RestoreAttemptStages.CreateRestoredChatServer,
                StringComparison.OrdinalIgnoreCase))
        {
            return ("standard-recreate", "Standard recreate");
        }

        if (@event.EventCode.Contains(
                "cleanup",
                StringComparison.OrdinalIgnoreCase))
        {
            return ("cleanup", "Cleanup and retirement");
        }

        if (@event.EventCode.Contains(
                "cancel",
                StringComparison.OrdinalIgnoreCase))
        {
            return ("restore-management", "Restore management");
        }

        if (@event.EventCode.Contains(
                "private",
                StringComparison.OrdinalIgnoreCase) ||
            @event.EventCode.Contains(
                "rehearsal",
                StringComparison.OrdinalIgnoreCase) ||
            @event.EventCode.Contains(
                "staging",
                StringComparison.OrdinalIgnoreCase))
        {
            return ("private-test", "Private test");
        }

        return ("restore-workspace", "Restore workspace");
    }

    private static (string Code, string Title) GetOperationCategory(
        string operation)
    {
        if (operation.Contains(
                "standard-recreate",
                StringComparison.OrdinalIgnoreCase))
        {
            return ("standard-recreate", "Standard recreate");
        }

        if (operation.Contains(
                "cleanup",
                StringComparison.OrdinalIgnoreCase) ||
            operation.Contains(
                "retire",
                StringComparison.OrdinalIgnoreCase))
        {
            return ("cleanup", "Cleanup and retirement");
        }

        if (operation.Contains(
                "private",
                StringComparison.OrdinalIgnoreCase) ||
            operation.Contains(
                "rehearsal",
                StringComparison.OrdinalIgnoreCase) ||
            operation.Contains(
                "staging",
                StringComparison.OrdinalIgnoreCase))
        {
            return ("private-test", "Private test");
        }

        if (operation.Contains(
                "verification",
                StringComparison.OrdinalIgnoreCase))
        {
            return ("public-verification", "Public verification");
        }

        return ("restore-operation", "Restore operation");
    }

    private static string GetEvidenceStatus(
        string severity,
        string eventCode)
    {
        if (severity is RestoreLogSeverities.Error or RestoreLogSeverities.Critical)
        {
            return "failed";
        }

        if (severity == RestoreLogSeverities.Warning)
        {
            return "warning";
        }

        return eventCode.EndsWith(
                   ".passed",
                   StringComparison.OrdinalIgnoreCase) ||
               eventCode.EndsWith(
                   ".completed",
                   StringComparison.OrdinalIgnoreCase) ||
               eventCode.EndsWith(
                   ".succeeded",
                   StringComparison.OrdinalIgnoreCase) ||
               eventCode.EndsWith(
                   ".destroyed",
                   StringComparison.OrdinalIgnoreCase)
            ? "succeeded"
            : "information";
    }

    private static string GetOperationEvidenceStatus(
        string status) =>
        status.ToLowerInvariant() switch
        {
            "failed" or "error" => "failed",
            "succeeded" or "completed" or "passed" or "public_routes_verified" =>
                "succeeded",
            "cancelled" => "cancelled",
            "running" or "active" => "running",
            _ => "information"
        };

    private static string BuildOperationEvidenceDescription(
        RestoreWorkspaceOperationSummary operation) =>
        operation.Status.ToLowerInvariant() switch
        {
            "failed" =>
                "The operation failed. Review structured logs for the safe failure summary.",

            "succeeded" or "completed" or "passed" =>
                "The operation completed successfully.",

            "running" =>
                "The operation is currently running.",

            "cancelled" =>
                "The operation was cancelled.",

            _ =>
                "The operation has a recorded state."
        };

    private static string GetCategoryStatus(
        IEnumerable<RestoreWorkspaceEvidenceItem> category)
    {
        if (category.Any(x => x.Status == "failed"))
        {
            return "failed";
        }

        if (category.Any(x => x.Status == "warning"))
        {
            return "warning";
        }

        if (category.Any(x => x.Status == "running"))
        {
            return "running";
        }

        if (category.Any(x => x.Status == "succeeded"))
        {
            return "succeeded";
        }

        return "information";
    }

    private static int EvidenceCategoryOrder(string category) =>
        category switch
        {
            "backup-validation" => 10,
            "private-test" => 20,
            "target-reservation" => 30,
            "standard-recreate" => 40,
            "public-verification" => 50,
            "cleanup" => 60,
            "restore-management" => 70,
            _ => 99
        };

    private static DateTimeOffset? LatestClaimedAtUtc(
        IReadOnlyList<RestoreWorkspaceTargetClaim> claims) =>
        claims.Count == 0
            ? null
            : claims.Max(x => x.ClaimedAtUtc);

    private static string? GetTarget(
        IReadOnlyDictionary<string, string> targets,
        string resourceType) =>
        targets.TryGetValue(resourceType, out var value)
            ? value
            : null;

    private static string? ExtractHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var candidate = value.Trim();

        if (Uri.TryCreate(
                candidate,
                UriKind.Absolute,
                out var absoluteUri) &&
            !string.IsNullOrWhiteSpace(absoluteUri.Host))
        {
            return absoluteUri.Host;
        }

        if (Uri.TryCreate(
                $"https://{candidate}",
                UriKind.Absolute,
                out var hostUri) &&
            !string.IsNullOrWhiteSpace(hostUri.Host))
        {
            return hostUri.Host;
        }

        return null;
    }

    private static string? SafeAttemptErrorSummary(
        RestoreAttemptEntity attempt)
    {
        var candidate = EmptyToNull(attempt.LastErrorSummary);

        if (candidate is null)
        {
            return null;
        }

        return LooksLikeRawInfrastructureDiagnostic(candidate)
            ? "A restore action failed. Review structured logs for the safe failure summary."
            : ToSafeDisplayMessage(candidate);
    }

    private static string ToSafeDisplayMessage(string? value)
    {
        var redacted = RestoreDiagnosticRedactor.RedactText(value, 600);

        return LooksLikeRawInfrastructureDiagnostic(redacted)
            ? "A protected infrastructure diagnostic is available. Review the safe event code and support report."
            : string.IsNullOrWhiteSpace(redacted)
                ? "No additional safe summary is available."
                : redacted;
    }

    private static bool LooksLikeRawInfrastructureDiagnostic(string value) =>
        value.Contains(
            "failed to resolve reference",
            StringComparison.OrdinalIgnoreCase) ||
        value.Contains(
            "docker api responded",
            StringComparison.OrdinalIgnoreCase) ||
        value.Contains(
            "response={",
            StringComparison.OrdinalIgnoreCase) ||
        value.Contains(
            "stack trace",
            StringComparison.OrdinalIgnoreCase) ||
        value.Contains(
            "pg_restore",
            StringComparison.OrdinalIgnoreCase) ||
        value.Contains(
            "exception:",
            StringComparison.OrdinalIgnoreCase) ||
        value.Contains(
            " at ",
            StringComparison.Ordinal) ||
        value.Length > 500;

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static DateTimeOffset ToOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static DateTimeOffset? ToOffset(DateTime? value) =>
        value.HasValue
            ? ToOffset(value.Value)
            : null;
}