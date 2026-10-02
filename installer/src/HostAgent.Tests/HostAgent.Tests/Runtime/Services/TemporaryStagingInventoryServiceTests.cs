using System.Text.Json;
using HostAgent.Runtime.Services.TemporaryStaging;
using Shared.ControlPlane.Runtime;

namespace HostAgent.Tests.Runtime.Services;

public sealed class TemporaryStagingInventoryServiceTests
{
    private const string StagingId = "20260924-stage";
    private static readonly Guid InstanceId = Guid.Parse("cd3ab822-fde5-4fea-a2e8-c620e223a1b8");

    [Fact]
    public void Matches_migration_from_durable_binding_exact_resource_ids_and_labels_without_retirement_authority()
    {
        var result = Project(Snapshot());
        var item = Assert.Single(result.Items);
        Assert.Equal("complete", result.Status);
        Assert.Equal("matched", item.OwnershipStatus);
        Assert.Equal("/migrations/mig_one", item.Owner?.WorkspaceHref);
        Assert.Equal("running", item.RuntimeStatus);
        Assert.Equal(2, item.ContainerCount);
        Assert.Equal(1, item.NetworkCount);
        Assert.False(item.CanRetire);
    }

    [Fact]
    public void Restore_binding_uses_the_recorded_session_and_catalog_identity()
    {
        var snapshot = Snapshot("restore");
        var item = Assert.Single(Project(snapshot).Items);
        Assert.Equal("/restores/rs_one", item.Owner?.WorkspaceHref);
        Assert.False(item.CanRetire);
        var wrongCatalog = snapshot with { History = [snapshot.History[0] with { CatalogEntryId = "bkp_other" }] };
        Assert.Null(Assert.Single(Project(wrongCatalog).Items).Owner);
    }

    [Fact]
    public void Labels_without_a_durable_workflow_never_establish_an_owner()
    {
        var item = Assert.Single(Project(Snapshot() with { Owners = [] }).Items);
        Assert.Equal("unresolved", item.OwnershipStatus);
        Assert.Null(item.Owner);
        Assert.False(item.CanRetire);
    }

    [Fact]
    public void Names_only_are_discovery_not_ownership()
    {
        var observed = Resource("container", "unknown-id", "postgres") with { Labels = new Dictionary<string, string>() };
        var item = Assert.Single(Project(Snapshot() with { History = [], Owners = [], Resources = [observed] }).Items);
        Assert.Null(item.Owner);
        Assert.Equal(1, item.ContainerCount);
        Assert.False(item.CanRetire);
    }

    [Fact]
    public void Same_name_replacement_with_different_container_id_is_unresolved()
    {
        var snapshot = Snapshot();
        var resource = snapshot.Resources[0] with { Id = "replacement-id" };
        var item = Assert.Single(Project(snapshot with { Resources = [resource] }).Items);
        Assert.Equal("unresolved", item.OwnershipStatus);
        Assert.Contains("resource-evidence-mismatch", item.ReasonCodes);
        Assert.Null(item.Owner);
    }

    [Theory]
    [InlineData("mem.migration-staging.candidate-id", "mca_other")]
    [InlineData("mem.managed-by", "unrelated")]
    [InlineData("mem.restore-staging.id", "another-stage")]
    [InlineData("mem.restore-staging.source-kind", "backup-catalog")]
    [InlineData("mem.component", "production")]
    [InlineData("mem.service", "element")]
    [InlineData("mem.restore-staging.catalog-entry-id", "unexpected-catalog")]
    public void Conflicting_labels_fail_closed(string key, string value)
    {
        var snapshot = Snapshot();
        var resource = snapshot.Resources[0];
        var labels = resource.Labels.ToDictionary(pair => pair.Key, pair => pair.Value);
        labels[key] = value;
        var item = Assert.Single(Project(snapshot with { Resources = [resource with { Labels = labels }] }).Items);
        Assert.Null(item.Owner);
        Assert.False(item.CanRetire);
    }

    [Fact]
    public void Two_workflow_owners_fail_closed()
    {
        var snapshot = Snapshot();
        var item = Assert.Single(Project(snapshot with
        {
            Owners = [snapshot.Owners[0], snapshot.Owners[0] with { OwnerId = "mig_other" }]
        }).Items);
        Assert.Contains("multiple-owners", item.ReasonCodes);
        Assert.Null(item.Owner);
    }

    [Fact]
    public void Candidate_bound_to_another_migration_is_not_adopted()
    {
        var snapshot = Snapshot();
        var item = Assert.Single(Project(snapshot with { Owners = [snapshot.Owners[0] with { BindingValid = false }] }).Items);
        Assert.Null(item.Owner);
    }

    [Fact]
    public void Postgres_only_leftover_remains_visible()
    {
        var snapshot = Snapshot();
        var item = Assert.Single(Project(snapshot with { Resources = [snapshot.Resources[0]] }).Items);
        Assert.Equal(1, item.ContainerCount);
        Assert.Equal("/migrations/mig_one", item.Owner?.WorkspaceHref);
    }

    [Fact]
    public void Network_only_leftover_remains_visible()
    {
        var snapshot = Snapshot();
        var item = Assert.Single(Project(snapshot with { Resources = [snapshot.Resources[2]] }).Items);
        Assert.Equal("network-only", item.RuntimeStatus);
        Assert.Equal(0, item.ContainerCount);
        Assert.Equal(1, item.NetworkCount);
        Assert.Equal("matched", item.OwnershipStatus);
    }

    [Fact]
    public void Retained_record_without_observed_runtime_does_not_claim_cleanup()
    {
        var item = Assert.Single(Project(Snapshot() with { Resources = [] }).Items);
        Assert.Equal("recorded", item.OwnershipStatus);
        Assert.Equal("not-observed", item.RuntimeStatus);
        Assert.Equal("retained", item.RecordedStatus);
        Assert.False(item.CanRetire);
    }

    [Theory]
    [InlineData("migration")]
    [InlineData("restore")]
    public void Completed_retirement_with_no_resources_is_not_inventory(string kind)
    {
        var snapshot = Snapshot(kind);
        var result = Project(snapshot with
        {
            History = [snapshot.History[0] with { Destroyed = true }],
            Owners = [snapshot.Owners[0] with { Retired = kind == "migration" }],
            Resources = []
        });
        Assert.Empty(result.Items);
    }

    [Fact]
    public void Resources_found_after_recorded_retirement_remain_visible_for_review()
    {
        var snapshot = Snapshot();
        var item = Assert.Single(Project(snapshot with
        {
            History = [snapshot.History[0] with { Destroyed = true }],
            Owners = [snapshot.Owners[0] with { Retired = true }]
        }).Items);
        Assert.Equal("needs-attention", item.RecordedStatus);
        Assert.Contains("resources-after-retirement", item.ReasonCodes);
        Assert.False(item.CanRetire);
    }

    [Fact]
    public void Docker_failure_is_not_an_empty_or_stopped_runtime()
    {
        var result = Project(Snapshot() with { Resources = [], ContainersAvailable = false, WarningCodes = ["containers-unavailable"] });
        Assert.Equal("partial", result.Status);
        Assert.Equal("unavailable", Assert.Single(result.Items).RuntimeStatus);
        Assert.Contains("containers-unavailable", result.WarningCodes);
    }

    [Fact]
    public void Unreadable_history_blocks_matching_but_preserves_observed_resources()
    {
        var result = Project(Snapshot() with { HistoryAvailable = false, History = [], WarningCodes = ["history-unavailable"] });
        Assert.Equal("partial", result.Status);
        var item = Assert.Single(result.Items);
        Assert.Null(item.Owner);
        Assert.Equal(2, item.ContainerCount);
    }

    [Fact]
    public void Unrelated_containers_do_not_join_staging_or_leak_raw_labels()
    {
        var snapshot = Snapshot();
        var resources = snapshot.Resources.Concat(new[]
        {
            new StagingObservedResource("container", "live-postgres", "mem-postgres", true,
                new Dictionary<string, string> { ["secret"] = "/private/secret-do-not-project" })
        }).ToArray();
        var result = Project(snapshot with { Resources = resources });
        Assert.Single(result.Items);
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("live-postgres", json);
        Assert.DoesNotContain("secret-do-not-project", json);
        Assert.DoesNotContain("mem.managed-by", json);
    }

    [Fact]
    public void Normal_element_labels_require_the_recorded_id_and_current_control_plane_authority()
    {
        var snapshot = WithNormalElement(Snapshot());
        var item = Assert.Single(Project(snapshot).Items);
        Assert.Equal("matched", item.OwnershipStatus);
        Assert.Equal(3, item.ContainerCount);
        Assert.Null(Assert.Single(Project(snapshot with { ControlPlaneInstanceId = Guid.NewGuid() }).Items).Owner);
        Assert.Null(Assert.Single(Project(snapshot with { ControlPlaneInstanceId = null }).Items).Owner);
    }

    [Fact]
    public void Element_name_or_owning_control_plane_alone_is_not_sufficient()
    {
        var snapshot = WithNormalElement(Snapshot());
        var element = snapshot.Resources.Last() with { Id = "replacement-element" };
        var item = Assert.Single(Project(snapshot with { Resources = [element] }).Items);
        Assert.Null(item.Owner);
    }

    [Fact]
    public void Shared_recorded_resource_ids_across_different_runs_do_not_pick_a_winner()
    {
        var snapshot = Snapshot();
        var result = Project(snapshot with { History = [snapshot.History[0], snapshot.History[0] with { StagingId = "stage-other" }] });
        Assert.All(result.Items.Where(item => item.ContainerCount > 0 || item.NetworkCount > 0), item => Assert.Null(item.Owner));
    }

    [Fact]
    public async Task Read_preserves_request_cancellation_and_does_not_invoke_a_mutator()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var service = new TemporaryStagingInventoryService(new CancelledSource(), TimeProvider.System);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAsync(cancelled.Token));
    }

    private static TemporaryStagingInventoryResponse Project(StagingInventorySnapshot snapshot) =>
        TemporaryStagingInventoryService.Project(snapshot, DateTimeOffset.Parse("2026-09-24T03:00:00Z"));

    private static StagingInventorySnapshot Snapshot(string kind = "migration")
    {
        var identity = kind == "migration" ? "mca_one" : "bkp_one";
        return new StagingInventorySnapshot(true, true, true, true,
            [Resource("container", "postgres-id", "postgres", kind), Resource("container", "synapse-id", "synapse", kind), Resource("network", "network-id", "network", kind)],
            [new StagingRecordedRuntime(StagingId, kind == "migration" ? "migration-candidate" : "backup-catalog", kind == "restore" ? identity : null, "ready", false,
                [new("container", "postgres", "postgres-id"), new("container", "synapse", "synapse-id"), new("network", "network", "network-id")])],
            [new StagingRecordedOwner(StagingId, kind, kind == "migration" ? "mig_one" : "rs_one", "Example", identity, true, false)], [], InstanceId);
    }

    private static StagingObservedResource Resource(string kind, string id, string service, string ownerKind = "migration") =>
        new(kind, id, kind == "network" ? $"mem-restore-staging-{StagingId}" : $"mem-restore-staging-{service}-{StagingId}", kind == "container",
            new Dictionary<string, string>
            {
                ["mem.managed-by"] = "mem-host-agent",
                ["mem.component"] = "restore-staging",
                ["mem.restore-staging.id"] = StagingId,
                ["mem.restore-staging.source-kind"] = ownerKind == "migration" ? "migration-candidate" : "backup-catalog",
                [ownerKind == "migration" ? "mem.migration-staging.candidate-id" : "mem.restore-staging.catalog-entry-id"] = ownerKind == "migration" ? "mca_one" : "bkp_one",
                ["mem.service"] = service
            });

    private static StagingInventorySnapshot WithNormalElement(StagingInventorySnapshot snapshot)
    {
        var labels = new Dictionary<string, string>
        {
            ["mem.managed-by"] = "host-agent", ["mem.component"] = "element-web", ["mem.service"] = "element",
            [MemDockerOwnershipLabels.ManagedKey] = "true", [MemDockerOwnershipLabels.ServiceKey] = "element",
            [MemDockerOwnershipLabels.ControlPlaneInstanceKey] = InstanceId.ToString("D"),
            [MemDockerOwnershipLabels.ResourceKey] = "matrix-stack-service"
        };
        return snapshot with
        {
            Resources = snapshot.Resources.Append(new StagingObservedResource("container", "element-id", $"mem-restore-staging-element-{StagingId}", true, labels)).ToArray(),
            History = [snapshot.History[0] with { Resources = snapshot.History[0].Resources.Append(new StagingRecordedResource("container", "element", "element-id")).ToArray() }]
        };
    }

    private sealed class CancelledSource : ITemporaryStagingInventorySource
    {
        public Task<StagingInventorySnapshot> ReadAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(Snapshot()); }
    }
}
