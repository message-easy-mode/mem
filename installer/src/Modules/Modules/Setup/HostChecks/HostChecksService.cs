using Microsoft.Extensions.Logging;
using Modules.Setup.HostChecks.Checks;
using Modules.Setup.InstallPlans;

namespace Modules.Setup.HostChecks;

public sealed class HostChecksService
{
    private readonly IReadOnlyList<IHostCheck> _checks;
    private readonly HostCheckRunStore _store;
    private readonly InstallPlanService _installPlanService;
    private readonly ILogger<HostChecksService> _logger;

    public HostChecksService(
        IEnumerable<IHostCheck> checks,
        HostCheckRunStore store,
        InstallPlanService installPlanService,
        ILogger<HostChecksService> logger)
    {
        _checks = checks.ToArray();
        _store = store;
        _installPlanService = installPlanService;
        _logger = logger;
    }

    public HostCheckRunResponse? GetLatest() => _store.GetLatest();

    public HostCheckRunResponse? GetById(string id) => _store.GetById(id);

    public async Task<HostCheckRunResponse> RunAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation("Starting host check run {RunId}", runId);

        var results = new List<HostCheckResultWithGroup>();

        foreach (var check in _checks)
        {
            try
            {
                var result = await check.RunAsync(cancellationToken);

                results.Add(new HostCheckResultWithGroup(
                    check.GroupKey,
                    check.GroupTitle,
                    check.GroupDescription,
                    result));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Host check {CheckKey} failed unexpectedly", check.Key);

                results.Add(new HostCheckResultWithGroup(
                    check.GroupKey,
                    check.GroupTitle,
                    check.GroupDescription,
                    new HostCheckResultDto(
                        check.Key,
                        check.Key,
                        HostCheckStatus.Unknown,
                        Blocking: false,
                        Summary: "The check failed unexpectedly.",
                        Details: ex.Message,
                        WhyItMatters: "Unexpected diagnostic failures reduce confidence in the installer readiness report.",
                        RecommendedAction: "Review installer logs and retry the host checks.",
                        Evidence: [])));
            }
        }

        var groups = results
            .GroupBy(x => x.GroupKey, StringComparer.Ordinal)
            .Select(group =>
            {
                var metadata = group.First();
                return new HostCheckGroupDto(
                    group.Key,
                    metadata.GroupTitle,
                    metadata.GroupDescription,
                    group.Select(x => x.Result).ToArray());
            })
            .ToArray();

        var allChecks = groups.SelectMany(x => x.Checks).ToArray();

        var summary = new HostCheckSummaryDto(
            Passed: allChecks.Count(x => x.Status == HostCheckStatus.Pass),
            Warnings: allChecks.Count(x => x.Status == HostCheckStatus.Warning),
            Failed: allChecks.Count(x => x.Status == HostCheckStatus.Fail),
            Skipped: allChecks.Count(x => x.Status == HostCheckStatus.Skipped),
            Unavailable: allChecks.Count(x => x.Status == HostCheckStatus.Unavailable),
            Unknown: allChecks.Count(x => x.Status == HostCheckStatus.Unknown));

        var runStatus =
            summary.Failed > 0 ? HostCheckRunStatus.Failed :
            summary.Warnings > 0 ? HostCheckRunStatus.SucceededWithWarnings :
            HostCheckRunStatus.Succeeded;

        var run = new HostCheckRunResponse(
            runId,
            runStatus,
            startedAt,
            DateTimeOffset.UtcNow,
            summary,
            groups);

        var snapshotPersisted = await _installPlanService.RecordPreflightSnapshotAsync(
            run,
            cancellationToken);

        if (!snapshotPersisted)
        {
            _logger.LogWarning(
                "Host check run {RunId} completed without an active Draft/Ready first-time setup authority to receive its preflight snapshot.",
                runId);
        }

        _store.SetLatest(run);

        _logger.LogInformation(
            "Completed host check run {RunId}. Status: {Status} Unavailable={Unavailable}",
            runId,
            run.Status,
            run.Summary.Unavailable);

        return run;
    }

    private sealed record HostCheckResultWithGroup(
        string GroupKey,
        string GroupTitle,
        string GroupDescription,
        HostCheckResultDto Result);
}