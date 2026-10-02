using System.Net.Http.Json;
using Mem.Cli.Models;
using Mem.Cli.Output;

namespace Mem.Cli.Clients;

public sealed partial class HostAgentClient
{
    private const string PrivateStagingBasePath =
        BackupsBasePath + "/verification/private-runtime/private-staging";
    /// <summary>
    /// Creates or resumes the one active canonical Restore Workspace for a
    /// Backup Catalog entry. This prepares durable state only; it does not run
    /// a private test or create a production runtime stack.
    /// </summary>
    public async Task<CatalogRestoreSessionCliResult> CreateOrResumeRestoreSessionAsync(
        string catalogEntryId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.PostAsync(
                $"{BackupCatalogBasePath}/{Uri.EscapeDataString(catalogEntryId)}/restore-session",
                content: null);

            if (!response.IsSuccessStatusCode)
            {
                return CatalogRestoreSessionError(
                    catalogEntryId,
                    ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<CatalogRestoreSessionCliResult>(
                CliOutput.JsonOptions());

            return payload ?? CatalogRestoreSessionError(
                catalogEntryId,
                ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return CatalogRestoreSessionError(
                catalogEntryId,
                ControlPlaneFailureDetails.Unreachable,
                status: "unreachable");
        }
    }

    /// <summary>
    /// Runs an isolated, catalog-backed private test for one canonical Restore
    /// Workspace. The HostAgent owns runtime isolation and evidence capture.
    /// </summary>
    public async Task<RestorePrivateTestCliResult> RunRestorePrivateTestAsync(
        string restoreSessionId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.PostAsync(
                $"{RestoreAttemptsBasePath}/{Uri.EscapeDataString(restoreSessionId)}/private-test",
                content: null);

            if (!response.IsSuccessStatusCode)
            {
                return RestorePrivateTestError(
                    restoreSessionId,
                    ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<RestorePrivateTestCliResult>(
                CliOutput.JsonOptions());

            return payload ?? RestorePrivateTestError(
                restoreSessionId,
                ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return RestorePrivateTestError(
                restoreSessionId,
                ControlPlaneFailureDetails.Unreachable,
                status: "unreachable");
        }
    }

    /// <summary>
    /// Destroys a retained private staging runtime only after its staging ID
    /// has been resolved from the canonical Restore Workspace evidence. The
    /// low-level HostAgent endpoint returns a broad run record; this client
    /// maps only the safe explicit-destruction summary.
    /// </summary>
    public async Task<RestorePrivateTestDestroyCliResult> DestroyPrivateTestStagingAsync(
        string restoreSessionId,
        string stagingId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.PostAsync(
                $"{PrivateStagingBasePath}/{Uri.EscapeDataString(stagingId)}/destroy",
                content: null);

            if (!response.IsSuccessStatusCode)
            {
                return RestorePrivateTestDestroyError(
                    restoreSessionId,
                    stagingId,
                    ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<PrivateStagingDestroyApiResponse>(
                CliOutput.JsonOptions());

            if (payload?.Destroy is null)
            {
                return RestorePrivateTestDestroyError(
                    restoreSessionId,
                    stagingId,
                    "HostAgent returned no safe private-test destruction summary.");
            }

            var warnings = (payload.Warnings ?? [])
                .Concat(payload.Errors ?? [])
                .Concat(payload.Destroy.Warnings ?? [])
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            return new RestorePrivateTestDestroyCliResult(
                Source: payload.Source,
                Status: payload.Status,
                RestoreSessionId: restoreSessionId,
                StagingId: payload.StagingId,
                DestroyedAtUtc: payload.Destroy.DestroyedAtUtc,
                SynapseContainerRemoved: payload.Destroy.SynapseContainerRemoved,
                PostgresContainerRemoved: payload.Destroy.PostgresContainerRemoved,
                NetworkRemoved: payload.Destroy.NetworkRemoved,
                WorkspaceRemoved: payload.Destroy.WorkspaceRemoved,
                Warnings: warnings,
                Detail: payload.Detail);
        }
        catch (Exception)
        {
            return RestorePrivateTestDestroyError(
                restoreSessionId,
                stagingId,
                ControlPlaneFailureDetails.Unreachable,
                status: "unreachable");
        }
    }

    /// <summary>
    /// Performs a read-only availability assessment for Standard Recreate. The
    /// endpoint does not reserve targets or mutate runtime state.
    /// </summary>
    public async Task<RestoreStandardRecreatePreflightCliResult> PreflightRestoreStandardRecreateAsync(
        string restoreSessionId,
        RestoreStandardRecreatePreflightCliRequest request)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.PostAsync(
                $"{RestoreAttemptsBasePath}/{Uri.EscapeDataString(restoreSessionId)}/standard-recreate/preflight" +
                BuildRestoreActionQuery(
                    ("targetStackSlug", request.TargetStackSlug),
                    ("requestedDomainId", request.RequestedDomainId),
                    ("matrixHost", request.MatrixHost),
                    ("elementHost", request.ElementHost)),
                content: null);

            if (!response.IsSuccessStatusCode)
            {
                return RestoreStandardRecreatePreflightError(
                    restoreSessionId,
                    ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<RestoreStandardRecreatePreflightCliResult>(
                CliOutput.JsonOptions());

            return payload ?? RestoreStandardRecreatePreflightError(
                restoreSessionId,
                ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return RestoreStandardRecreatePreflightError(
                restoreSessionId,
                ControlPlaneFailureDetails.Unreachable,
                status: "unreachable");
        }
    }

    /// <summary>
    /// Executes Standard Recreate through the canonical Restore Workspace. The
    /// request contains the server's required explicit acknowledgement values.
    /// Internal runtime, database, and filesystem details in the raw response
    /// are projected away before any CLI output is produced.
    /// </summary>
    public async Task<RestoreStandardRecreateCliResult> ExecuteRestoreStandardRecreateAsync(
        string restoreSessionId,
        RestoreStandardRecreateCliRequest request)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.PostAsync(
                $"{RestoreAttemptsBasePath}/{Uri.EscapeDataString(restoreSessionId)}/standard-recreate" +
                BuildRestoreActionQuery(
                    ("targetStackSlug", request.TargetStackSlug),
                    ("requestedDomainId", request.RequestedDomainId),
                    ("matrixHost", request.MatrixHost),
                    ("elementHost", request.ElementHost),
                    ("matrixImage", request.MatrixImage),
                    ("elementImage", request.ElementImage),
                    ("operatorName", request.Operator),
                    ("note", request.Note),
                    ("executeProductionRecreate", ToBooleanQueryValue(request.ExecuteProductionRecreate)),
                    ("acknowledgeCreatesRealStack", ToBooleanQueryValue(request.AcknowledgeCreatesRealStack)),
                    ("acknowledgeMutatesProductionPostgres", ToBooleanQueryValue(request.AcknowledgeMutatesProductionPostgres)),
                    ("acknowledgeMutatesNpmRoutes", ToBooleanQueryValue(request.AcknowledgeMutatesNpmRoutes)),
                    ("acknowledgeNoAutomaticRollback", ToBooleanQueryValue(request.AcknowledgeNoAutomaticRollback))
                ),
                content: null);

            if (!response.IsSuccessStatusCode)
            {
                return RestoreStandardRecreateError(
                    restoreSessionId,
                    ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<RestoreStandardRecreateApiResponse>(
                CliOutput.JsonOptions());

            return payload is null
                ? RestoreStandardRecreateError(
                    restoreSessionId,
                    ControlPlaneFailureDetails.EmptyResponse)
                : ToRestoreStandardRecreateCliResult(
                    restoreSessionId,
                    payload);
        }
        catch (Exception)
        {
            return RestoreStandardRecreateError(
                restoreSessionId,
                ControlPlaneFailureDetails.Unreachable,
                status: "unreachable");
        }
    }

    /// <summary>
    /// Cancels a non-terminal Restore Workspace after the CLI has required an
    /// explicit acknowledgement. The catalog source and durable audit history
    /// remain under their independent lifecycles.
    /// </summary>
    public Task<RestoreSessionActionCliResult> CancelRestoreAsync(
        string restoreSessionId) =>
        PostRestoreAttemptActionAsync(
            restoreSessionId,
            action: "cancel",
            relativePath: "cancel?acknowledgeCancel=true",
            successDetail: "Restore cancellation was recorded. Temporary claims were released when safe; source and audit history were retained.");

    /// <summary>
    /// Completes the terminal handover transition after the CLI has required an
    /// explicit acknowledgement. It never generates a support report as a
    /// hidden side effect.
    /// </summary>
    public Task<RestoreSessionActionCliResult> CompleteRestoreHandoverAsync(
        string restoreSessionId) =>
        PostRestoreAttemptActionAsync(
            restoreSessionId,
            action: "complete-handover",
            relativePath: "complete-handover?acknowledgeCompletion=true",
            successDetail: "Restore handover was completed. The workspace remains available as an audit record.");

    private async Task<RestoreSessionActionCliResult> PostRestoreAttemptActionAsync(
        string restoreSessionId,
        string action,
        string relativePath,
        string successDetail)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.PostAsync(
                $"{RestoreAttemptsBasePath}/{Uri.EscapeDataString(restoreSessionId)}/{relativePath}",
                content: null);

            if (!response.IsSuccessStatusCode)
            {
                return RestoreSessionActionError(
                    action,
                    restoreSessionId,
                    ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<RestoreAttemptActionApiResponse>(
                CliOutput.JsonOptions());

            return payload is null
                ? RestoreSessionActionError(
                    action,
                    restoreSessionId,
                    ControlPlaneFailureDetails.EmptyResponse)
                : new RestoreSessionActionCliResult(
                    Source: "control-plane",
                    Status: "ok",
                    Action: action,
                    RestoreSessionId: payload.RestoreSessionId,
                    AttemptStatus: payload.Status,
                    CurrentStage: payload.CurrentStage,
                    UpdatedAtUtc: payload.UpdatedAtUtc,
                    TerminalAtUtc: payload.TerminalAtUtc,
                    WarningCount: payload.WarningCount,
                    ErrorCount: payload.ErrorCount,
                    LastErrorCode: payload.LastErrorCode,
                    LastErrorSummary: payload.LastErrorSummary,
                    Detail: successDetail);
        }
        catch (Exception)
        {
            return RestoreSessionActionError(
                action,
                restoreSessionId,
                ControlPlaneFailureDetails.Unreachable,
                status: "unreachable");
        }
    }

    private static CatalogRestoreSessionCliResult CatalogRestoreSessionError(
        string catalogEntryId,
        string detail,
        string status = "error") =>
        new(
            Source: "control-plane",
            Status: status,
            CatalogEntryId: catalogEntryId,
            RestoreSessionId: null,
            RestoreAttemptCreated: false,
            RestoreAttemptResumed: false,
            SourceKind: null,
            PayloadState: null,
            IntegrityStatus: null,
            WarningCount: 0,
            Detail: detail);

    private static RestorePrivateTestCliResult RestorePrivateTestError(
        string restoreSessionId,
        string detail,
        string status = "error") =>
        new(
            Source: "control-plane",
            Status: status,
            RestoreSessionId: restoreSessionId,
            OperationId: null,
            SourceKind: null,
            CatalogEntryId: null,
            StagingId: null,
            PrivateOnly: null,
            DatabaseImportSucceeded: null,
            SynapseHealthPassed: null,
            RequiresExplicitDestroy: null,
            Detail: detail);

    private static RestorePrivateTestDestroyCliResult RestorePrivateTestDestroyError(
        string restoreSessionId,
        string? stagingId,
        string detail,
        string status = "error") =>
        new(
            Source: "control-plane",
            Status: status,
            RestoreSessionId: restoreSessionId,
            StagingId: stagingId,
            DestroyedAtUtc: null,
            SynapseContainerRemoved: null,
            PostgresContainerRemoved: null,
            NetworkRemoved: null,
            WorkspaceRemoved: null,
            Warnings: [],
            Detail: detail);

    private static RestoreStandardRecreatePreflightCliResult RestoreStandardRecreatePreflightError(
        string restoreSessionId,
        string detail,
        string status = "error") =>
        new(
            Source: "control-plane",
            Status: status,
            CheckedAtUtc: null,
            RestoreSessionId: restoreSessionId,
            CanCreate: false,
            Targets: null,
            Checks: [],
            Blockers: [],
            Warnings: [],
            Detail: detail);

    private static RestoreStandardRecreateCliResult RestoreStandardRecreateError(
        string restoreSessionId,
        string detail,
        string status = "error") =>
        new(
            Source: "control-plane",
            Status: status,
            RestoreSessionId: restoreSessionId,
            RecreateId: null,
            CatalogEntryId: null,
            SourceKind: "backup-catalog",
            TargetStackSlug: null,
            MatrixHost: null,
            ElementHost: null,
            StartedAtUtc: null,
            FinishedAtUtc: null,
            Operator: null,
            Note: null,
            Database: null,
            Runtime: null,
            Routes: null,
            Mutations: null,
            Checks: [],
            Warnings: [],
            Errors: [],
            Detail: detail);

    private static RestoreSessionActionCliResult RestoreSessionActionError(
        string action,
        string restoreSessionId,
        string detail,
        string status = "error") =>
        new(
            Source: "control-plane",
            Status: status,
            Action: action,
            RestoreSessionId: restoreSessionId,
            AttemptStatus: null,
            CurrentStage: null,
            UpdatedAtUtc: null,
            TerminalAtUtc: null,
            WarningCount: 0,
            ErrorCount: 0,
            LastErrorCode: null,
            LastErrorSummary: null,
            Detail: detail);

    private static RestoreStandardRecreateCliResult ToRestoreStandardRecreateCliResult(
        string restoreSessionId,
        RestoreStandardRecreateApiResponse response) =>
        new(
            Source: response.Source,
            Status: response.Status,
            RestoreSessionId: restoreSessionId,
            RecreateId: response.RecreateId,
            CatalogEntryId: response.CatalogEntryId,
            SourceKind: "backup-catalog",
            TargetStackSlug: response.TargetStackSlug,
            MatrixHost: response.MatrixHost,
            ElementHost: response.ElementHost,
            StartedAtUtc: response.StartedAtUtc,
            FinishedAtUtc: response.FinishedAtUtc,
            Operator: response.Operator,
            Note: response.Note,
            Database: new RestoreStandardRecreateDatabaseCliSummary(
                response.Database.Provisioned,
                response.Database.ImportSucceeded,
                response.Database.PublicTableCount,
                response.Database.SynapseKnownTableCount,
                response.Database.UsersCount,
                response.Database.EventsCount,
                response.Database.RoomsCount,
                response.Database.StateEventsCount),
            Runtime: new RestoreStandardRecreateRuntimeCliSummary(
                response.Runtime.MatrixStarted,
                response.Runtime.MatrixHealthPassed,
                response.Runtime.ElementStarted,
                response.Runtime.ElementHealthPassed,
                response.Runtime.StackRegistered),
            Routes: new RestoreStandardRecreateRouteCliSummary(
                response.Routes.MatrixPublicHost,
                response.Routes.MatrixRouteReady,
                response.Routes.ElementPublicHost,
                response.Routes.ElementRouteReady,
                response.Routes.PublicReadinessPassed),
            Mutations: new RestoreStandardRecreateMutationCliSummary(
                response.Mutations.RuntimeStackCreated,
                response.Mutations.ProductionPostgresMutated,
                response.Mutations.ProductionContainersTouched,
                response.Mutations.NpmRoutesChanged,
                response.Mutations.DnsChanged,
                response.Mutations.CertificatesChanged,
                response.Mutations.OldStacksDeleted),
            Checks: (response.Checks ?? [])
                .Select(check => new RestoreStandardRecreateCheckCliProjection(
                    check.Code,
                    check.Severity,
                    check.Passed,
                    check.Message))
                .ToArray(),
            Warnings: response.Warnings ?? [],
            Errors: response.Errors ?? [],
            Detail: response.Detail);

    private static string BuildRestoreActionQuery(
        params (string Name, string? Value)[] parameters)
    {
        var parts = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
            .Select(parameter =>
                $"{Uri.EscapeDataString(parameter.Name)}={Uri.EscapeDataString(parameter.Value!)}")
            .ToArray();

        return parts.Length == 0
            ? string.Empty
            : "?" + string.Join("&", parts);
    }

    private static string ToBooleanQueryValue(bool value) =>
        value ? "true" : "false";
}
