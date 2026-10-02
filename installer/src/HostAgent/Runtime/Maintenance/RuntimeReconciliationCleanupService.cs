using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Services;

namespace HostAgent.Runtime.Maintenance;

public sealed class RuntimeReconciliationCleanupService
{
    public const string RequiredConfirmationText = "DELETE ORPHANED NPM HOSTS";

    private readonly RuntimeReconciliationReportService _reportService;
    private readonly NpmProxyHostService _npmProxyHostService;
    private readonly ILogger<RuntimeReconciliationCleanupService> _log;

    public RuntimeReconciliationCleanupService(
        RuntimeReconciliationReportService reportService,
        NpmProxyHostService npmProxyHostService,
        ILogger<RuntimeReconciliationCleanupService> log)
    {
        _reportService = reportService;
        _npmProxyHostService = npmProxyHostService;
        _log = log;
    }

    public async Task<RuntimeReconciliationNpmProxyHostCleanupResponse> DeleteSelectedOrphanedNpmProxyHostsAsync(
        RuntimeReconciliationNpmProxyHostCleanupRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var checkedAtUtc = DateTimeOffset.UtcNow;
        var requestedIds = (request.ProxyHostIds ?? [])
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (!string.Equals(
                request.ConfirmationText?.Trim(),
                RequiredConfirmationText,
                StringComparison.Ordinal))
        {
            return new RuntimeReconciliationNpmProxyHostCleanupResponse(
                Source: "control-plane",
                Status: "confirmation_required",
                CheckedAtUtc: checkedAtUtc,
                RequestedProxyHostIds: requestedIds,
                DeletedCount: 0,
                AlreadyMissingCount: 0,
                SkippedCount: requestedIds.Length,
                FailedCount: 0,
                Results: requestedIds
                    .Select(id => new RuntimeReconciliationNpmProxyHostCleanupResult(
                        id,
                        [],
                        "skipped_confirmation_required",
                        $"Type {RequiredConfirmationText} to delete selected orphaned NPM proxy hosts."))
                    .ToArray(),
                Detail: "Explicit confirmation is required before orphaned NPM proxy hosts can be deleted.");
        }

        if (requestedIds.Length == 0)
        {
            return new RuntimeReconciliationNpmProxyHostCleanupResponse(
                Source: "control-plane",
                Status: "no_selection",
                CheckedAtUtc: checkedAtUtc,
                RequestedProxyHostIds: requestedIds,
                DeletedCount: 0,
                AlreadyMissingCount: 0,
                SkippedCount: 0,
                FailedCount: 0,
                Results: [],
                Detail: "No NPM proxy host IDs were selected for cleanup.");
        }

        var report = await _reportService.BuildAsync(ct);
        var plan = RuntimeReconciliationClassifier.BuildOrphanedNpmProxyHostCleanupPlan(
            report.NpmProxyHosts,
            requestedIds);

        var results = new List<RuntimeReconciliationNpmProxyHostCleanupResult>();

        foreach (var item in plan)
        {
            if (item.Status == "already_missing" || item.Status.StartsWith("skipped_", StringComparison.Ordinal))
            {
                results.Add(item);
                continue;
            }

            if (item.Status != "pending_delete")
            {
                results.Add(item with
                {
                    Status = "skipped_unknown_plan_status",
                    Reason = $"Cleanup plan produced unsupported status: {item.Status}"
                });
                continue;
            }

            try
            {
                var deleted = await _npmProxyHostService.DeleteByIdIfExistsWithResultAsync(
                    item.ProxyHostId,
                    ct);

                results.Add(item with
                {
                    Status = deleted ? "deleted" : "already_missing",
                    Reason = deleted
                        ? "Deleted orphaned NPM proxy host."
                        : "NPM proxy host was already absent when deletion was attempted."
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                _log.LogWarning(
                    ex,
                    "Failed to delete orphaned NPM proxy host. ProxyHostId={ProxyHostId}",
                    item.ProxyHostId);

                results.Add(item with
                {
                    Status = "failed",
                    Reason = ex.Message
                });
            }
        }

        var deletedCount = results.Count(x => x.Status == "deleted");
        var alreadyMissingCount = results.Count(x => x.Status == "already_missing");
        var failedCount = results.Count(x => x.Status == "failed");
        var skippedCount = results.Count(x => x.Status.StartsWith("skipped_", StringComparison.Ordinal));

        var status = failedCount > 0
            ? "partial_failure"
            : deletedCount > 0 || alreadyMissingCount > 0
                ? "completed"
                : skippedCount > 0
                    ? "skipped"
                    : "no_selection";

        return new RuntimeReconciliationNpmProxyHostCleanupResponse(
            Source: "control-plane",
            Status: status,
            CheckedAtUtc: checkedAtUtc,
            RequestedProxyHostIds: requestedIds,
            DeletedCount: deletedCount,
            AlreadyMissingCount: alreadyMissingCount,
            SkippedCount: skippedCount,
            FailedCount: failedCount,
            Results: results,
            Detail: status switch
            {
                "completed" => "Selected orphaned NPM proxy host cleanup completed.",
                "partial_failure" => "Selected orphaned NPM proxy host cleanup completed with failures.",
                "skipped" => "No selected NPM proxy hosts were eligible for orphan cleanup.",
                _ => "No NPM proxy host cleanup was performed."
            });
    }
}
