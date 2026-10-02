using System.Text.RegularExpressions;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Runtime.Services.TemporaryStaging;

/// <summary>
/// Reconciles recorded workflows with observed resources for navigation only.
/// Names discover unresolved leftovers; names never establish ownership.
/// </summary>
public sealed class TemporaryStagingInventoryService(
    ITemporaryStagingInventorySource source,
    TimeProvider timeProvider)
{
    public async Task<TemporaryStagingInventoryResponse> GetAsync(CancellationToken ct) =>
        Project(await source.ReadAsync(ct), timeProvider.GetUtcNow());

    internal static bool IsSafeId(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 160 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    internal static TemporaryStagingInventoryResponse Project(
        StagingInventorySnapshot snapshot,
        DateTimeOffset observedAtUtc)
    {
        var histories = snapshot.History
            .Where(item => IsSafeId(item.StagingId))
            .ToLookup(item => item.StagingId, StringComparer.Ordinal);
        var owners = snapshot.Owners
            .Where(item => IsSafeId(item.StagingId))
            .ToLookup(item => item.StagingId, StringComparer.Ordinal);
        var groups = new Dictionary<string, List<StagingObservedResource>>(StringComparer.Ordinal);

        foreach (var resource in snapshot.Resources)
        {
            // Exact recorded IDs also retain renamed resources, but any contradictory
            // labels will make the group unresolved. Do not adopt by a name prefix.
            var recordedIds = snapshot.History.Where(history => history.Resources.Any(expected =>
                    expected.Kind == resource.Kind && expected.Id == resource.Id))
                .Select(history => history.StagingId).Distinct(StringComparer.Ordinal).ToArray();
            var labelledId = Label(resource, "mem.restore-staging.id");
            var discoveredId = DiscoverFromName(resource);
            var relevant = recordedIds.Length > 0 || labelledId.Length > 0 || discoveredId is not null ||
                           Label(resource, "mem.component") == "restore-staging";
            if (!relevant) continue;

            // Conflicting history identities must stay visibly unresolved, not pick
            // one owner by ordering. The synthetic key is not an executable selector.
            var groupId = recordedIds.Length == 1 && IsSafeId(recordedIds[0])
                ? recordedIds[0]
                : recordedIds.Length == 0 && IsSafeId(labelledId)
                    ? labelledId
                    : recordedIds.Length == 0 && IsSafeId(discoveredId)
                        ? discoveredId!
                        : $"unresolved-{resource.Kind}-{resource.Id}";
            if (!groups.TryGetValue(groupId, out var resources))
                groups[groupId] = resources = [];
            resources.Add(resource);
        }

        foreach (var id in histories.Select(group => group.Key).Concat(owners.Select(group => group.Key)))
            groups.TryAdd(id, []);

        var items = new List<TemporaryStagingInventoryItem>();
        foreach (var (groupId, resources) in groups.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var historyRows = histories[groupId].ToArray();
            var ownerRows = owners[groupId].Distinct().ToArray();
            var history = historyRows.Length == 1 ? historyRows[0] : null;
            var owner = ownerRows.Length == 1 ? ownerRows[0] : null;
            var runtimeAvailable = snapshot.ContainersAvailable && snapshot.NetworksAvailable;
            if (resources.Count == 0 && runtimeAvailable && snapshot.HistoryAvailable &&
                ((history is { Destroyed: true } && ownerRows.All(row => row.Kind == "restore" || row.Retired)) ||
                 (historyRows.Length == 0 && ownerRows.Length > 0 && ownerRows.All(row => row.Retired))))
                continue;

            var reasons = new List<string>();
            var bindingMatches = snapshot.OwnershipAvailable && snapshot.HistoryAvailable &&
                history is not null && owner is { BindingValid: true } && IsSafeId(owner.OwnerId) &&
                ((owner.Kind == "migration" && history.SourceKind == "migration-candidate") ||
                 (owner.Kind == "restore" && history.SourceKind == "backup-catalog" &&
                  owner.SourceIdentity == history.CatalogEntryId));
            var liveMatches = bindingMatches && resources.All(resource => Matches(resource, history!, owner!, snapshot.ControlPlaneInstanceId));
            var ownership = !liveMatches ? "unresolved" : resources.Count > 0 ? "matched" : "recorded";
            if (!liveMatches) reasons.Add("ownership-unresolved");
            if (historyRows.Length > 1 || ownerRows.Length > 1) reasons.Add("multiple-owners");
            if (bindingMatches && !liveMatches) reasons.Add("resource-evidence-mismatch");
            if (!runtimeAvailable) reasons.Add("runtime-unavailable");
            if (history is null) reasons.Add("history-unavailable");
            if (resources.Count > 0 && (history?.Destroyed == true || owner?.Retired == true))
                reasons.Add("resources-after-retirement");
            if (history?.Status == "destroy-needs-attention" || owner?.RetirementStatus == "needs-attention") reasons.Add("retirement-needs-attention");

            var containers = resources.Where(resource => resource.Kind == "container")
                .DistinctBy(resource => resource.Id).ToArray();
            var running = containers.Count(resource => resource.Running);
            var runtimeStatus = !runtimeAvailable ? "unavailable"
                : containers.Length == 0 ? resources.Count == 0 ? "not-observed" : "network-only"
                : running == containers.Length ? "running"
                : running > 0 ? "partially-running" : "stopped";
            var recordedStatus = reasons.Contains("resources-after-retirement") ||
                                 reasons.Contains("retirement-needs-attention") ? "needs-attention"
                : history is null ? "unavailable" : history.Destroyed ? "retired" : "retained";

            items.Add(new TemporaryStagingInventoryItem(
                groupId,
                history is not null || owner is not null || !groupId.StartsWith("unresolved-", StringComparison.Ordinal)
                    ? groupId : null,
                ownership,
                liveMatches ? new TemporaryStagingOwner(
                    owner!.Kind, owner.OwnerId, owner.DisplayName,
                    $"/{(owner.Kind == "migration" ? "migrations" : "restores")}/{Uri.EscapeDataString(owner.OwnerId)}") : null,
                runtimeStatus,
                recordedStatus,
                containers.Length,
                running,
                resources.Where(resource => resource.Kind == "network").DistinctBy(resource => resource.Id).Count(),
                containers.Select(resource => resource.Id).ToArray(),
                reasons.Distinct(StringComparer.Ordinal).ToArray(),
                RetirementReview: liveMatches && owner!.Kind == "migration" && IsSafeId(owner.StagingRunId)
                    ? new TemporaryStagingRetirementReviewTarget(owner.OwnerId, owner.StagingRunId!, owner.RetirementStatus)
                    : null));
        }

        var complete = snapshot.ContainersAvailable && snapshot.NetworksAvailable &&
                       snapshot.OwnershipAvailable && snapshot.HistoryAvailable;
        return new TemporaryStagingInventoryResponse(
            "control-plane", observedAtUtc, complete ? "complete" : "partial",
            snapshot.WarningCodes.Distinct(StringComparer.Ordinal).ToArray(), items);
    }

    private static bool Matches(
        StagingObservedResource resource,
        StagingRecordedRuntime history,
        StagingRecordedOwner owner,
        Guid? controlPlaneInstanceId)
    {
        var expected = history.Resources.Where(item => item.Kind == resource.Kind && item.Id == resource.Id).ToArray();
        if (expected.Length != 1) return false;

        // The current staging engine delegates Element to the normal starter.
        // That starter writes authority-scoped Element labels, not staging labels.
        // Accept this existing contract only with the exact recorded ID and the
        // current persistent Control Plane identity, never the container's name.
        if (resource.Kind == "container" && expected[0].Service == "element" &&
            Label(resource, "mem.component") == "element-web")
        {
            return controlPlaneInstanceId is { } instanceId && instanceId != Guid.Empty &&
                   Label(resource, "mem.managed-by") == "host-agent" &&
                   Label(resource, "mem.service") == "element" &&
                   Label(resource, MemDockerOwnershipLabels.ManagedKey) == "true" &&
                   Label(resource, MemDockerOwnershipLabels.ServiceKey) == "element" &&
                   Label(resource, MemDockerOwnershipLabels.ResourceKey) == "matrix-stack-service" &&
                   Guid.TryParse(Label(resource, MemDockerOwnershipLabels.ControlPlaneInstanceKey), out var observedInstance) &&
                   observedInstance == instanceId &&
                   Label(resource, "mem.restore-staging.id").Length == 0 &&
                   Label(resource, "mem.restore-staging.source-kind").Length == 0 &&
                   Label(resource, "mem.restore-staging.catalog-entry-id").Length == 0 &&
                   Label(resource, "mem.migration-staging.candidate-id").Length == 0;
        }

        if (Label(resource, "mem.managed-by") != "mem-host-agent" ||
            Label(resource, "mem.component") != "restore-staging" ||
            Label(resource, "mem.restore-staging.id") != history.StagingId ||
            Label(resource, "mem.restore-staging.source-kind") != history.SourceKind)
            return false;

        var identityLabel = owner.Kind == "migration"
            ? "mem.migration-staging.candidate-id" : "mem.restore-staging.catalog-entry-id";
        var conflictingLabel = owner.Kind == "migration"
            ? "mem.restore-staging.catalog-entry-id" : "mem.migration-staging.candidate-id";
        return Label(resource, identityLabel) == owner.SourceIdentity &&
               Label(resource, conflictingLabel).Length == 0 &&
               (resource.Kind == "network" || Label(resource, "mem.service") == expected[0].Service);
    }

    private static string Label(StagingObservedResource resource, string key) =>
        resource.Labels.TryGetValue(key, out var value) ? value : string.Empty;

    private static string? DiscoverFromName(StagingObservedResource resource)
    {
        var name = resource.Name.TrimStart('/');
        var pattern = resource.Kind == "network"
            ? @"^mem-restore-staging-(.+)$"
            : @"^mem-restore-staging-(?:postgres|synapse|matrix|element-web|element)-(.+)$";
        var match = Regex.Match(name, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        return match.Success ? match.Groups[1].Value : null;
    }
}
