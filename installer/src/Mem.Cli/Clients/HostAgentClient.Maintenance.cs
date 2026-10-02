using System.Net.Http.Json;
using System.Text.Json;
using Mem.Cli.Models;
using Mem.Cli.Output;

namespace Mem.Cli.Clients;

public sealed partial class HostAgentClient
{
    public async Task<RuntimeReconciliationReport> GetRuntimeReconciliationReportAsync()
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.GetAsync(
                "internal/host-agent/admin/maintenance/runtime-reconciliation");

            if (!response.IsSuccessStatusCode)
            {
                return new RuntimeReconciliationReport(
                    Source: "control-plane",
                    Status: "error",
                    CheckedAtUtc: DateTimeOffset.UtcNow,
                    Summary: new RuntimeReconciliationSummary(0, 0, 0, 0, 0, 0, 0, 0, 0),
                    ActiveRoutes: [],
                    OrphanedNpmProxyHosts: [],
                    Warnings: [],
                    Detail: ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<RuntimeReconciliationReport>(
                CliOutput.JsonOptions());

            return result ?? new RuntimeReconciliationReport(
                Source: "control-plane",
                Status: "error",
                CheckedAtUtc: DateTimeOffset.UtcNow,
                Summary: new RuntimeReconciliationSummary(0, 0, 0, 0, 0, 0, 0, 0, 0),
                ActiveRoutes: [],
                OrphanedNpmProxyHosts: [],
                Warnings: [],
                Detail: ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return new RuntimeReconciliationReport(
                Source: "control-plane",
                Status: "unreachable",
                CheckedAtUtc: DateTimeOffset.UtcNow,
                Summary: new RuntimeReconciliationSummary(0, 0, 0, 0, 0, 0, 0, 0, 0),
                ActiveRoutes: [],
                OrphanedNpmProxyHosts: [],
                Warnings: [],
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    public async Task<RuntimeReconciliationNpmProxyHostCleanupResponse> DeleteOrphanedNpmProxyHostsAsync(
        RuntimeReconciliationNpmProxyHostCleanupRequest request)
    {
        await EnsureAuthenticatedAsync();

        try
        {
            var response = await _client.PostAsJsonAsync(
                "internal/host-agent/admin/maintenance/runtime-reconciliation/npm-proxy-hosts/delete",
                request,
                CliOutput.JsonOptions());

            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadErrorDetailAsync(response);

                return new RuntimeReconciliationNpmProxyHostCleanupResponse(
                    Source: "control-plane",
                    Status: "error",
                    CheckedAtUtc: DateTimeOffset.UtcNow,
                    RequestedProxyHostIds: request.ProxyHostIds,
                    DeletedCount: 0,
                    AlreadyMissingCount: 0,
                    SkippedCount: 0,
                    FailedCount: request.ProxyHostIds.Count,
                    Results: [],
                    Detail: detail ?? ControlPlaneFailureDetails.ForHttpStatus(response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<RuntimeReconciliationNpmProxyHostCleanupResponse>(
                CliOutput.JsonOptions());

            return result ?? new RuntimeReconciliationNpmProxyHostCleanupResponse(
                Source: "control-plane",
                Status: "error",
                CheckedAtUtc: DateTimeOffset.UtcNow,
                RequestedProxyHostIds: request.ProxyHostIds,
                DeletedCount: 0,
                AlreadyMissingCount: 0,
                SkippedCount: 0,
                FailedCount: request.ProxyHostIds.Count,
                Results: [],
                Detail: ControlPlaneFailureDetails.EmptyResponse);
        }
        catch (Exception)
        {
            return new RuntimeReconciliationNpmProxyHostCleanupResponse(
                Source: "control-plane",
                Status: "unreachable",
                CheckedAtUtc: DateTimeOffset.UtcNow,
                RequestedProxyHostIds: request.ProxyHostIds,
                DeletedCount: 0,
                AlreadyMissingCount: 0,
                SkippedCount: 0,
                FailedCount: request.ProxyHostIds.Count,
                Results: [],
                Detail: ControlPlaneFailureDetails.Unreachable);
        }
    }

    private static async Task<string?> ReadErrorDetailAsync(HttpResponseMessage response)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                var code = error.GetString();
                var detail = root.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String
                    ? detailElement.GetString()
                    : null;

                return string.IsNullOrWhiteSpace(detail)
                    ? code
                    : $"{code}: {detail}";
            }

            if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String)
            {
                return status.GetString();
            }
        }
        catch
        {
            // Preserve stable fallback below.
        }

        return null;
    }
}
