using System.Net.Http.Json;
using Mem.Cli.Models;
using Mem.Cli.Output;

namespace Mem.Cli.Clients;

public sealed partial class HostAgentClient
{
    private const string RestoreAttemptsBasePath = BackupsBasePath + "/restores";

    /// <summary>
    /// Reads the durable catalog-backed Restore Attempt inventory. The route
    /// uses restoreSessionId rows and deliberately has no validation-id restore
    /// path or local-artifact recovery identity.
    /// </summary>
    public async Task<RestoreAttemptListCliResult> GetRestoreAttemptsAsync(
        RestoreAttemptListRequest? request = null)
    {
        await EnsureAuthenticatedAsync();

        var normalized = Normalize(request ?? new RestoreAttemptListRequest());
        var fallbackQuery = new RestoreAttemptListQuery(
            Page: normalized.Page ?? 1,
            PageSize: normalized.PageSize ?? 10,
            Search: normalized.Search,
            Status: normalized.Status,
            TargetStack: normalized.TargetStack,
            SortBy: normalized.SortBy,
            SortDirection: normalized.SortDirection);

        try
        {
            var response = await _client.GetAsync(
                RestoreAttemptsBasePath + BuildRestoreAttemptQuery(normalized));

            if (!response.IsSuccessStatusCode)
            {
                return EmptyRestoreAttemptList(
                    status: "error",
                    query: fallbackQuery,
                    detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<RestoreAttemptListApiResponse>(
                CliOutput.JsonOptions());

            return payload is null
                ? EmptyRestoreAttemptList(
                    status: "error",
                    query: fallbackQuery,
                    detail: ControlPlaneFailureDetails.EmptyResponse)
                : new RestoreAttemptListCliResult(
                    Source: payload.Source,
                    Status: payload.Status,
                    Query: payload.Query,
                    Summary: payload.Summary,
                    TotalSessions: payload.TotalSessions,
                    Page: payload.Page,
                    PageSize: payload.PageSize,
                    TotalPages: payload.TotalPages,
                    HasPreviousPage: payload.HasPreviousPage,
                    HasNextPage: payload.HasNextPage,
                    TargetStacks: payload.TargetStacks ?? [],
                    Sessions: payload.Sessions ?? [],
                    Warnings: payload.Warnings ?? [],
                    Detail: payload.Detail);
        }
        catch (Exception)
        {
            return EmptyRestoreAttemptList(
                status: "unreachable",
                query: fallbackQuery,
                detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    /// <summary>
    /// Reads the canonical Restore Workspace by restore session identity. This
    /// is a safe, read-only projection of durable attempt, source, target,
    /// evidence, verification, and log-summary state.
    /// </summary>
    public async Task<RestoreWorkspaceCliResult> GetRestoreWorkspaceAsync(
        string restoreSessionId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(
                $"{RestoreAttemptsBasePath}/{Uri.EscapeDataString(restoreSessionId)}/workspace");

            if (!response.IsSuccessStatusCode)
            {
                return new RestoreWorkspaceCliResult(
                    Source: "control-plane",
                    Status: "error",
                    Workspace: null,
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<RestoreWorkspaceResponse>(
                CliOutput.JsonOptions());

            return payload is null
                ? new RestoreWorkspaceCliResult(
                    Source: "control-plane",
                    Status: "error",
                    Workspace: null,
                    Detail: ControlPlaneFailureDetails.EmptyResponse)
                : new RestoreWorkspaceCliResult(
                    Source: "control-plane",
                    Status: "ok",
                    Workspace: payload,
                    Detail: null);
        }
        catch (Exception)
        {
            return new RestoreWorkspaceCliResult(
                Source: "control-plane",
                Status: "unreachable",
                Workspace: null,
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    /// <summary>
    /// Reads the paged, redacted structured event stream for one Restore
    /// Workspace. The control plane owns redaction; the CLI never reaches into
    /// workspace files or container logs.
    /// </summary>
    public async Task<RestoreLogPageCliResult> GetRestoreLogsAsync(
        string restoreSessionId,
        RestoreLogListRequest? request = null)
    {
        await EnsureAuthenticatedAsync();

        var normalized = Normalize(request ?? new RestoreLogListRequest());
        var page = normalized.Page ?? 1;
        var pageSize = normalized.PageSize ?? 100;

        try
        {
            var response = await _client.GetAsync(
                $"{RestoreAttemptsBasePath}/{Uri.EscapeDataString(restoreSessionId)}/logs" +
                BuildRestoreLogQuery(page, pageSize, normalized));

            if (!response.IsSuccessStatusCode)
            {
                return new RestoreLogPageCliResult(
                    Source: "control-plane",
                    Status: "error",
                    Logs: null,
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<RestoreLogPage>(
                CliOutput.JsonOptions());

            return payload is null
                ? new RestoreLogPageCliResult(
                    Source: "control-plane",
                    Status: "error",
                    Logs: null,
                    Detail: ControlPlaneFailureDetails.EmptyResponse)
                : new RestoreLogPageCliResult(
                    Source: "control-plane",
                    Status: "ok",
                    Logs: payload,
                    Detail: null);
        }
        catch (Exception)
        {
            return new RestoreLogPageCliResult(
                Source: "control-plane",
                Status: "unreachable",
                Logs: null,
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    /// <summary>
    /// Reads an already-generated redacted support report. This method is
    /// intentionally read-only; report generation remains an explicit later
    /// workflow rather than a hidden side effect of inspection.
    /// </summary>
    public async Task<RestoreSupportReportCliResult> GetRestoreSupportReportAsync(
        string restoreSessionId)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(
                $"{RestoreAttemptsBasePath}/{Uri.EscapeDataString(restoreSessionId)}/support-report");

            if (!response.IsSuccessStatusCode)
            {
                return new RestoreSupportReportCliResult(
                    Source: "control-plane",
                    Status: "error",
                    Report: null,
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var payload = await response.Content.ReadFromJsonAsync<RestoreSupportReportApiResponse>(
                CliOutput.JsonOptions());

            return payload is null
                ? new RestoreSupportReportCliResult(
                    Source: "control-plane",
                    Status: "error",
                    Report: null,
                    Detail: ControlPlaneFailureDetails.EmptyResponse)
                : new RestoreSupportReportCliResult(
                    Source: "control-plane",
                    Status: "ok",
                    Report: ToCliSupportReport(payload),
                    Detail: null);
        }
        catch (Exception)
        {
            return new RestoreSupportReportCliResult(
                Source: "control-plane",
                Status: "unreachable",
                Report: null,
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    private static RestoreSupportReportCliProjection ToCliSupportReport(
        RestoreSupportReportApiResponse report)
    {
        return new RestoreSupportReportCliProjection(
            SchemaVersion: report.SchemaVersion,
            GeneratedAtUtc: report.GeneratedAtUtc,
            MemVersion: report.MemVersion,
            RestoreSessionId: report.RestoreSessionId,
            Attempt: report.Attempt,
            Source: new RestoreSupportReportCliSource(
                SourceKind: report.Source.SourceKind,
                CatalogEntryId: report.Source.CatalogEntryId,
                SourceDisplayName: report.Source.SourceDisplayName,
                SourceOriginKind: report.Source.SourceOriginKind,
                SourceStackSlug: report.Source.SourceStackSlug,
                SourceBackupId: report.Source.SourceBackupId,
                SourceDeleted: report.Source.SourceDeleted),
            Target: report.Target,
            Logs: report.Logs,
            Operations: report.Operations ?? [],
            RecentEvents: report.RecentEvents ?? [],
            Warnings: report.Warnings ?? []);
    }

    private static RestoreAttemptListCliResult EmptyRestoreAttemptList(
        string status,
        RestoreAttemptListQuery query,
        string detail)
    {
        return new RestoreAttemptListCliResult(
            Source: "control-plane",
            Status: status,
            Query: query,
            Summary: new RestoreAttemptListSummary(
                TotalSessions: 0,
                ProductionRecreateCount: 0,
                PubliclyVerifiedCount: 0,
                NeedsActionCount: 0),
            TotalSessions: 0,
            Page: query.Page,
            PageSize: query.PageSize,
            TotalPages: 0,
            HasPreviousPage: false,
            HasNextPage: false,
            TargetStacks: [],
            Sessions: [],
            Warnings: [],
            Detail: detail);
    }

    private static RestoreAttemptListRequest Normalize(
        RestoreAttemptListRequest request)
    {
        return request with
        {
            Search = NormalizeFilter(request.Search),
            Status = NormalizeFilter(request.Status),
            TargetStack = NormalizeFilter(request.TargetStack),
            SortBy = NormalizeFilter(request.SortBy),
            SortDirection = NormalizeFilter(request.SortDirection)
        };
    }

    private static RestoreLogListRequest Normalize(
        RestoreLogListRequest request)
    {
        return request with
        {
            Severity = NormalizeFilter(request.Severity),
            Stage = NormalizeFilter(request.Stage),
            Search = NormalizeFilter(request.Search)
        };
    }

    private static string? NormalizeFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value.Trim();
    }

    private static string BuildRestoreAttemptQuery(
        RestoreAttemptListRequest request)
    {
        var parameters = new List<string>();

        if (request.Page is > 0)
        {
            AddQueryParameter(parameters, "page", request.Page.Value.ToString());
        }

        if (request.PageSize is > 0)
        {
            AddQueryParameter(parameters, "pageSize", request.PageSize.Value.ToString());
        }

        AddQueryParameter(parameters, "search", request.Search);
        AddQueryParameter(parameters, "status", request.Status);
        AddQueryParameter(parameters, "targetStack", request.TargetStack);
        AddQueryParameter(parameters, "sortBy", request.SortBy);
        AddQueryParameter(parameters, "sortDirection", request.SortDirection);

        return parameters.Count == 0
            ? string.Empty
            : "?" + string.Join("&", parameters);
    }

    private static string BuildRestoreLogQuery(
        int page,
        int pageSize,
        RestoreLogListRequest request)
    {
        var parameters = new List<string>();
        AddQueryParameter(parameters, "page", page.ToString());
        AddQueryParameter(parameters, "pageSize", pageSize.ToString());
        AddQueryParameter(parameters, "severity", request.Severity);
        AddQueryParameter(parameters, "stage", request.Stage);
        AddQueryParameter(parameters, "search", request.Search);

        return "?" + string.Join("&", parameters);
    }

    private static void AddQueryParameter(
        ICollection<string> parameters,
        string name,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        parameters.Add(
            $"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
    }
}
