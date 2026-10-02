using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Contracts;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;

namespace HostAgent.Runtime.Backups.Workspace.PrivateTest;

/// <summary>
/// Executes the Restore Workspace "Run private test" action. It resolves the
/// canonical Backup Catalog source from the durable restore attempt, starts a
/// linked runtime operation, and records safe structured evidence for the
/// workspace. It deliberately creates no public routes, DNS changes, or
/// replacement runtime.
/// </summary>
public sealed class RestoreWorkspacePrivateTestService
{
    private const string PrivateTestStage = "private-test";

    private readonly RestoreAttemptCoordinator _restoreAttemptCoordinator;
    private readonly BackupCatalogStore _catalogStore;
    private readonly IPrivateStagingRunner _privateStagingRunner;
    private readonly RestoreStructuredLogService _restoreLogs;
    private readonly RestoreWorkspacePrivateTestOperationLifetime _operationLifetime;

    public RestoreWorkspacePrivateTestService(
        RestoreAttemptCoordinator restoreAttemptCoordinator,
        BackupCatalogStore catalogStore,
        IPrivateStagingRunner privateStagingRunner,
        RestoreStructuredLogService restoreLogs,
        RestoreWorkspacePrivateTestOperationLifetime operationLifetime)
    {
        _restoreAttemptCoordinator = restoreAttemptCoordinator;
        _catalogStore = catalogStore;
        _privateStagingRunner = privateStagingRunner;
        _restoreLogs = restoreLogs;
        _operationLifetime = operationLifetime;
    }

    public async Task<RestoreWorkspacePrivateTestActionResponse> RunAsync(
        string restoreSessionId,
        CancellationToken ct)
    {
        var requestAborted = ct;
        var reservation = await _restoreAttemptCoordinator.StartPrivateTestAsync(
            restoreSessionId,
            requestedBy: "host-agent",
            ct);

        var attempt = reservation.Attempt;
        var catalogEntryId = (string?)null;

        using var operationLifetime = _operationLifetime.BeginAfterAcceptance(
            attempt.RestoreSessionId,
            reservation.RuntimeOperationId,
            requestAborted);
        ct = operationLifetime.CancellationToken;

        try
        {
            await _restoreLogs.RecordAsync(
                attempt.Id,
                reservation.RuntimeOperationId,
                stage: PrivateTestStage,
                severity: RestoreLogSeverities.Information,
                eventCode: "restore.private-test.operation-lifetime.server-owned",
                message: "Private restore test is running under a bounded server-owned lifetime independent of the initiating HTTP request.",
                details: new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["requestAbortCancelsMutation"] = "false",
                    ["operationTimeoutMinutes"] = RestoreWorkspacePrivateTestOperationLifetime.DefaultOperationTimeout.TotalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                },
                ct: ct,
                updateAttemptSummary: false);
            if (attempt.BackupCatalogEntryId is null)
            {
                throw new RestoreWorkspacePrivateTestSourceException(
                    "restore.private-test.catalog-source-missing",
                    "This restore workspace does not reference a Backup Catalog entry.");
            }

            var source = await _catalogStore.FindRestoreSourceByEntryIdAsync(
                attempt.BackupCatalogEntryId.Value,
                ct);

            if (source is null)
            {
                throw new RestoreWorkspacePrivateTestSourceException(
                    "restore.private-test.catalog-source-missing",
                    "The Backup Catalog entry for this restore workspace was not found.");
            }

            catalogEntryId = source.CatalogEntryId;

            await _restoreLogs.RecordAsync(
                attempt.Id,
                reservation.RuntimeOperationId,
                stage: PrivateTestStage,
                severity: RestoreLogSeverities.Information,
                eventMessage: HostAgentStructuredMessages.RestorePrivateTestStarted(
                    attempt.RestoreSessionId,
                    attempt.SourceKind,
                    source.CatalogEntryId),
                message: "Private restore test started from the Backup Catalog source.",
                ct: ct,
                updateAttemptSummary: false);

            var run = await _privateStagingRunner.CreatePrivateSynapseFromCatalogAsync(
                source.CatalogEntryId,
                new PrivateStagingRunRequest(
                    KeepOnFailure: true,
                    PostgresImage: null,
                    SynapseImage: null,
                    TargetStackSlug: null),
                ct);

            var evidence = ToEvidence(attempt.SourceKind, source.CatalogEntryId, run);

            if (!IsSuccessfulCatalogPrivateTest(run, source.CatalogEntryId))
            {
                await _restoreAttemptCoordinator.RecordPrivateTestFailureAsync(
                    attempt.Id,
                    reservation.RuntimeOperationId,
                    errorCode: "restore.private-test.failed",
                    operatorSummary: "Private restore test did not produce a healthy isolated staging runtime. Review safe evidence and logs before retrying.",
                    evidence: evidence,
                    ct: ct);

                return ToFailureResponse(
                    attempt.RestoreSessionId,
                    reservation.RuntimeOperationId,
                    attempt.SourceKind,
                    source.CatalogEntryId,
                    run,
                    "Private restore test needs attention. Review workspace evidence and logs before retrying.");
            }

            await _restoreAttemptCoordinator.CompletePrivateTestAsync(
                attempt.Id,
                reservation.RuntimeOperationId,
                evidence,
                ct);

            return new RestoreWorkspacePrivateTestActionResponse(
                Source: "control-plane",
                Status: "ready",
                RestoreSessionId: attempt.RestoreSessionId,
                OperationId: reservation.RuntimeOperationId,
                SourceKind: attempt.SourceKind,
                CatalogEntryId: source.CatalogEntryId,
                StagingId: run.StagingId,
                PrivateOnly: run.Safety.PrivateOnly,
                DatabaseImportSucceeded: run.Database.ImportSucceeded,
                SynapseHealthPassed: run.Runtime.SynapseHealthPassed,
                RequiresExplicitDestroy: run.Safety.RequiresExplicitDestroy,
                Detail: "Private restore test is ready. It is retained, private-only, and requires explicit destruction when inspection is complete.");
        }
        catch (OperationCanceledException)
        {
            var errorCode = operationLifetime.OperationTimeoutRequested
                ? "restore.private-test.operation-timeout"
                : operationLifetime.ApplicationStoppingRequested
                    ? "restore.private-test.control-plane-stopping"
                    : "restore.private-test.cancelled";

            var operatorSummary = operationLifetime.OperationTimeoutRequested
                ? "Private restore test exceeded its bounded server-owned operation timeout. Review the workspace before retrying."
                : operationLifetime.ApplicationStoppingRequested
                    ? "Private restore test stopped because the Control Plane is shutting down. Review the workspace after restart before retrying."
                    : "Private restore test was cancelled before completion. Review the workspace before retrying.";

            using var terminalEvidenceCancellation = new CancellationTokenSource(
                TimeSpan.FromSeconds(20));

            await _restoreAttemptCoordinator.RecordPrivateTestFailureAsync(
                attempt.Id,
                reservation.RuntimeOperationId,
                errorCode: errorCode,
                operatorSummary: operatorSummary,
                evidence: null,
                ct: terminalEvidenceCancellation.Token);

            if (operationLifetime.OperationTimeoutRequested)
            {
                return new RestoreWorkspacePrivateTestActionResponse(
                    Source: "control-plane",
                    Status: "needs-attention",
                    RestoreSessionId: attempt.RestoreSessionId,
                    OperationId: reservation.RuntimeOperationId,
                    SourceKind: attempt.SourceKind,
                    CatalogEntryId: catalogEntryId,
                    StagingId: null,
                    PrivateOnly: null,
                    DatabaseImportSucceeded: null,
                    SynapseHealthPassed: null,
                    RequiresExplicitDestroy: null,
                    Detail: operatorSummary);
            }

            throw;
        }
        catch (BackupCatalogPayloadResolutionException ex)
        {
            await _restoreAttemptCoordinator.RecordPrivateTestFailureAsync(
                attempt.Id,
                reservation.RuntimeOperationId,
                errorCode: ex.ErrorCode,
                operatorSummary: "Private restore test could not prepare the Backup Catalog payload. Review safe evidence and source material before retrying.",
                evidence: null,
                ct: ct);

            return new RestoreWorkspacePrivateTestActionResponse(
                Source: "control-plane",
                Status: "needs-attention",
                RestoreSessionId: attempt.RestoreSessionId,
                OperationId: reservation.RuntimeOperationId,
                SourceKind: attempt.SourceKind,
                CatalogEntryId: catalogEntryId,
                StagingId: null,
                PrivateOnly: null,
                DatabaseImportSucceeded: null,
                SynapseHealthPassed: null,
                RequiresExplicitDestroy: null,
                Detail: "Private restore test could not prepare this Backup Catalog payload. Review workspace evidence and logs before retrying.");
        }
        catch (RestoreWorkspacePrivateTestSourceException ex)
        {
            await _restoreAttemptCoordinator.RecordPrivateTestFailureAsync(
                attempt.Id,
                reservation.RuntimeOperationId,
                errorCode: ex.ErrorCode,
                operatorSummary: ex.SafeSummary,
                evidence: null,
                ct: ct);

            return new RestoreWorkspacePrivateTestActionResponse(
                Source: "control-plane",
                Status: "needs-attention",
                RestoreSessionId: attempt.RestoreSessionId,
                OperationId: reservation.RuntimeOperationId,
                SourceKind: attempt.SourceKind,
                CatalogEntryId: catalogEntryId,
                StagingId: null,
                PrivateOnly: null,
                DatabaseImportSucceeded: null,
                SynapseHealthPassed: null,
                RequiresExplicitDestroy: null,
                Detail: ex.SafeSummary);
        }
        catch (Exception)
        {
            await _restoreAttemptCoordinator.RecordPrivateTestFailureAsync(
                attempt.Id,
                reservation.RuntimeOperationId,
                errorCode: "restore.private-test.failed",
                operatorSummary: "Private restore test could not be completed. Review safe evidence and logs before retrying.",
                evidence: null,
                ct: ct);

            return new RestoreWorkspacePrivateTestActionResponse(
                Source: "control-plane",
                Status: "needs-attention",
                RestoreSessionId: attempt.RestoreSessionId,
                OperationId: reservation.RuntimeOperationId,
                SourceKind: attempt.SourceKind,
                CatalogEntryId: catalogEntryId,
                StagingId: null,
                PrivateOnly: null,
                DatabaseImportSucceeded: null,
                SynapseHealthPassed: null,
                RequiresExplicitDestroy: null,
                Detail: "Private restore test could not be completed. Review workspace evidence and logs before retrying.");
        }
    }

    private static bool IsSuccessfulCatalogPrivateTest(
        PrivateStagingRunResult run,
        string catalogEntryId) =>
        string.Equals(run.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(run.SourceKind, PrivateStagingSourceKinds.BackupCatalog, StringComparison.Ordinal) &&
        string.Equals(run.CatalogEntryId, catalogEntryId, StringComparison.Ordinal) &&
        run.Safety.PrivateOnly &&
        run.Safety.DockerNetworkInternal &&
        !run.Safety.PublicRoutesCreated &&
        !run.Safety.DnsChanged &&
        !run.Safety.CertificatesChanged &&
        !run.Safety.ProductionContainersTouched &&
        !run.Safety.ProductionDatabasesTouched &&
        run.Database.ImportSucceeded &&
        run.Runtime.SynapseHealthPassed;

    private static RestorePrivateTestEvidence ToEvidence(
        string sourceKind,
        string catalogEntryId,
        PrivateStagingRunResult run) =>
        new(
            SourceKind: sourceKind,
            CatalogEntryId: catalogEntryId,
            StagingId: run.StagingId,
            MatrixServerName: string.IsNullOrWhiteSpace(run.MatrixServerName)
                ? null
                : run.MatrixServerName.Trim(),
            Status: run.Status,
            PrivateOnly: run.Safety.PrivateOnly,
            DockerNetworkInternal: run.Safety.DockerNetworkInternal,
            DatabaseImportSucceeded: run.Database.ImportSucceeded,
            SynapseHealthPassed: run.Runtime.SynapseHealthPassed,
            RequiresExplicitDestroy: run.Safety.RequiresExplicitDestroy);

    private static RestoreWorkspacePrivateTestActionResponse ToFailureResponse(
        string restoreSessionId,
        Guid operationId,
        string sourceKind,
        string catalogEntryId,
        PrivateStagingRunResult run,
        string detail) =>
        new(
            Source: "control-plane",
            Status: "needs-attention",
            RestoreSessionId: restoreSessionId,
            OperationId: operationId,
            SourceKind: sourceKind,
            CatalogEntryId: catalogEntryId,
            StagingId: string.IsNullOrWhiteSpace(run.StagingId) ? null : run.StagingId,
            PrivateOnly: run.Safety.PrivateOnly,
            DatabaseImportSucceeded: run.Database.ImportSucceeded,
            SynapseHealthPassed: run.Runtime.SynapseHealthPassed,
            RequiresExplicitDestroy: run.Safety.RequiresExplicitDestroy,
            Detail: detail);
}

public sealed class RestoreWorkspacePrivateTestSourceException : InvalidOperationException
{
    public RestoreWorkspacePrivateTestSourceException(
        string errorCode,
        string safeSummary)
        : base(safeSummary)
    {
        ErrorCode = errorCode;
        SafeSummary = safeSummary;
    }

    public string ErrorCode { get; }
    public string SafeSummary { get; }
}
