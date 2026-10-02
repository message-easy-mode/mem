using System.Text.Json;
using Docker.DotNet.Models;
using HostAgent.Runtime.Migrations.Staging.Retirement;
using Microsoft.Extensions.Configuration;
using Shared.ControlPlane.Runtime;
using Xunit;

namespace HostAgent.Tests.Runtime.Migrations.Staging.Retirement;

public sealed class MigrationStagingRetirementResourceSafetyTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static Dictionary<string,string> Labels() => new()
    {
        ["mem.managed-by"] = "mem-host-agent", ["mem.component"] = "restore-staging",
        ["mem.restore-staging.id"] = "stage_one", ["mem.restore-staging.source-kind"] = "migration-candidate",
        ["mem.migration-staging.candidate-id"] = "mca_one", ["mem.service"] = "synapse"
    };

    [Fact]
    public void Exact_staging_labels_match_but_never_replace_recorded_resource_id_checks() =>
        MigrationStagingRetirementRuntime.ValidateLabels(Labels(), "stage_one", "mca_one", "synapse", Owner);

    [Theory]
    [InlineData("mem.managed-by")]
    [InlineData("mem.component")]
    [InlineData("mem.restore-staging.id")]
    [InlineData("mem.restore-staging.source-kind")]
    [InlineData("mem.migration-staging.candidate-id")]
    [InlineData("mem.service")]
    public void Missing_or_conflicting_staging_identity_fails_closed(string key)
    {
        var labels = Labels();
        labels.Remove(key);
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateLabels(labels,"stage_one","mca_one","synapse",Owner));
        labels[key] = "different";
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateLabels(labels,"stage_one","mca_one","synapse",Owner));
    }

    [Fact]
    public void Foreign_control_plane_and_restore_catalog_labels_are_refused()
    {
        var labels = Labels();
        labels[MemDockerOwnershipLabels.ControlPlaneInstanceKey] = Guid.NewGuid().ToString();
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateLabels(labels,"stage_one","mca_one","synapse",Owner));
        labels.Remove(MemDockerOwnershipLabels.ControlPlaneInstanceKey);
        labels["mem.restore-staging.catalog-entry-id"] = "bkp_other";
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateLabels(labels,"stage_one","mca_one","synapse",Owner));
    }

    [Fact]
    public void Canonical_Element_starter_requires_the_same_persistent_Control_Plane_authority()
    {
        var labels = new Dictionary<string,string>
        {
            ["mem.component"]="element-web", ["mem.managed-by"]="host-agent", ["mem.service"]="element",
            [MemDockerOwnershipLabels.ManagedKey]="true", [MemDockerOwnershipLabels.ServiceKey]="element",
            [MemDockerOwnershipLabels.ResourceKey]="matrix-stack-service", [MemDockerOwnershipLabels.ControlPlaneInstanceKey]=Owner.ToString()
        };
        MigrationStagingRetirementRuntime.ValidateLabels(labels,"stage_one","mca_one","element",Owner);
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateLabels(labels,"stage_one","mca_one","element",Guid.NewGuid()));
        labels["mem.migration-staging.candidate-id"]="mca_foreign";
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateLabels(labels,"stage_one","mca_one","element",Owner));
    }

    [Fact]
    public async Task Real_filesystem_retirement_removes_only_derived_workspace_and_retains_history_and_candidate_data()
    {
        using var f = new HistoryFixture();
        await f.WriteAsync();
        var (_, plan) = await f.History.ReadAsync("stage_one", "mca_one", default);
        f.History.RemoveWorkspace("stage_one", default);
        await f.History.RecordRetiredAsync(plan, default);
        var (recorded, after) = await f.History.ReadAsync("stage_one", "mca_one", default);
        Assert.Equal(plan, after); // immutable identity survives interrupted/repeated cleanup
        Assert.Equal("destroyed", recorded.Status);
        Assert.True(recorded.Destroy!.WorkspaceRemoved);
        Assert.False(Directory.Exists(f.Workspace));
        Assert.True(File.Exists(f.Candidate));
        Assert.True(File.Exists(f.ResultPath));
        f.History.RemoveWorkspace("stage_one", default); // genuine absence is idempotent
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/tmp/outside")]
    [InlineData("stage/one")]
    public void Request_ids_cannot_select_paths(string id)
    {
        using var f = new HistoryFixture();
        Assert.Throws<MigrationStagingRetirementException>(() => f.History.Workspace(id));
    }

    [Fact]
    public async Task Recorded_workspace_outside_the_derived_boundary_is_rejected_without_deletion()
    {
        using var f = new HistoryFixture();
        await f.WriteAsync(workspaceOverride: f.Root);
        await Assert.ThrowsAsync<MigrationStagingRetirementException>(() => f.History.ReadAsync("stage_one","mca_one",default));
        Assert.True(File.Exists(f.Candidate));
        Assert.True(Directory.Exists(f.Workspace));
    }

    [Theory]
    [InlineData("stage_one", "short-id")]
    [InlineData("different-stage", null)]
    public async Task History_identity_or_nonexact_Docker_id_is_rejected(string id, string? dockerId)
    {
        using var f = new HistoryFixture();
        await f.WriteAsync(stagingOverride:id, containerOverride:dockerId);
        await Assert.ThrowsAsync<MigrationStagingRetirementException>(() => f.History.ReadAsync("stage_one","mca_one",default));
        Assert.True(Directory.Exists(f.Workspace));
    }

    [Fact]
    public async Task A_symlink_inside_the_disposable_workspace_blocks_recursive_removal()
    {
        using var f = new HistoryFixture();
        await f.WriteAsync();
        var outside = Path.Combine(f.Root,"retained");
        Directory.CreateSymbolicLink(Path.Combine(f.Workspace,"linked"), outside);
        Assert.Throws<MigrationStagingRetirementException>(() => f.History.RemoveWorkspace("stage_one",default));
        Assert.True(File.Exists(f.Candidate));
    }

    [Theory]
    [InlineData("/data/run", "/data/run/media", true)]
    [InlineData("/data/run", "/data", true)]
    [InlineData("/data/run", "/data/runtime", false)]
    public void Workspace_overlap_checks_both_ancestors_and_descendants(string a,string b,bool expected) =>
        Assert.Equal(expected, MigrationStagingRetirementHistory.Overlaps(a,b));

    [Theory]
    [InlineData("mem-restore-staging-synapse-stage_one")]
    [InlineData("172.22.0.3")]
    public void Public_forwarding_to_selected_names_or_addresses_blocks_retirement(string host)
    {
        var error = Assert.Throws<MigrationStagingRetirementException>(() =>
            MigrationStagingRetirementRuntime.ValidatePublicRoute(host, null, null,
                ["mem-restore-staging-synapse-stage_one", "172.22.0.3"]));
        Assert.Equal("public-resource", error.Code);
    }

    [Theory]
    [InlineData("$variable", null)]
    [InlineData("", null)]
    [InlineData("unrelated", "location / { proxy_pass http://custom; }")]
    [InlineData("unrelated", "upstream custom { server backend; }")]
    public void Unrelated_dynamic_or_custom_routes_do_not_veto_already_proven_private_topology(string host, string? advanced)
    {
        MigrationStagingRetirementRuntime.ValidatePublicRoute(host, advanced, null, ["stage_one"]);
    }

    [Fact]
    public void Custom_locations_and_advanced_config_still_block_explicit_staging_targets()
    {
        var locationError = Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidatePublicRoute(
            "ordinary-server", null, new[] { new { forward_host = "172.22.0.3" } }, ["172.22.0.3"]));
        Assert.Equal("public-resource", locationError.Code);

        var advancedError = Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidatePublicRoute(
            "ordinary-server", "location / { proxy_pass http://mem-restore-staging-synapse-stage_one; }", null,
            ["mem-restore-staging-synapse-stage_one"]));
        Assert.Equal("public-resource", advancedError.Code);

        MigrationStagingRetirementRuntime.ValidatePublicRoute("ordinary-server", "add_header X-Test allowed;", null, ["stage_one"]);
    }

    [Theory]
    [InlineData("host")]
    [InlineData("container:another")]
    public void Host_and_shared_container_network_modes_are_refused(string mode)
    {
        var current = new ContainerInspectResponse
        {
            HostConfig = new HostConfig { NetworkMode = mode },
            NetworkSettings = new NetworkSettings { Networks = new Dictionary<string, EndpointSettings>() }
        };
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateContainerTopology(current, "network-one", "synapse"));
    }

    [Fact]
    public void An_extra_network_or_unknown_topology_blocks_retirement()
    {
        var current = new ContainerInspectResponse
        {
            HostConfig = new HostConfig(), NetworkSettings = new NetworkSettings
            { Networks = new Dictionary<string, EndpointSettings> { ["other"] = new() { NetworkID = "foreign" } } }
        };
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateContainerTopology(current, "network-one", "synapse"));
        current.NetworkSettings = null!;
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateContainerTopology(current, "network-one", "synapse"));
    }

    [Fact]
    public void Host_port_publication_is_refused_even_on_an_internal_network()
    {
        var current = new ContainerInspectResponse
        {
            HostConfig = new HostConfig { PortBindings = new Dictionary<string, IList<PortBinding>>
                { ["8008/tcp"] = new List<PortBinding> { new() { HostIP = "127.0.0.1", HostPort = "8008" } } } },
            NetworkSettings = new NetworkSettings { Networks = new Dictionary<string, EndpointSettings>() }
        };
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateContainerTopology(current, "network-one", "synapse"));
    }


    [Fact]
    public void Exact_staging_Postgres_image_data_volume_is_allowed_but_other_volume_mounts_are_refused()
    {
        var postgres = new ContainerInspectResponse
        {
            HostConfig = new HostConfig(),
            NetworkSettings = new NetworkSettings
            {
                Networks = new Dictionary<string, EndpointSettings>
                {
                    ["staging"] = new() { NetworkID = "network-one" }
                }
            },
            Mounts =
            [
                new MountPoint { Type = "bind", Destination = "/restore", Source = "/tmp/staging" },
                new MountPoint { Type = "volume", Name = "anonymous-postgres-volume", Destination = "/var/lib/postgresql/data" }
            ]
        };

        MigrationStagingRetirementRuntime.ValidateContainerTopology(postgres, "network-one", "postgres");
        Assert.Throws<MigrationStagingRetirementException>(() =>
            MigrationStagingRetirementRuntime.ValidateContainerTopology(postgres, "network-one", "synapse"));

        postgres.Mounts =
        [
            new MountPoint { Type = "volume", Name = "unexpected", Destination = "/unexpected" }
        ];
        Assert.Throws<MigrationStagingRetirementException>(() =>
            MigrationStagingRetirementRuntime.ValidateContainerTopology(postgres, "network-one", "postgres"));
    }

    [Fact]
    public void Postgres_data_volume_requires_a_Docker_volume_identity_and_only_one_volume_mount()
    {
        var postgres = new ContainerInspectResponse
        {
            HostConfig = new HostConfig(),
            NetworkSettings = new NetworkSettings
            {
                Networks = new Dictionary<string, EndpointSettings>
                {
                    ["staging"] = new() { NetworkID = "network-one" }
                }
            },
            Mounts =
            [
                new MountPoint { Type = "volume", Destination = "/var/lib/postgresql/data" }
            ]
        };
        Assert.Throws<MigrationStagingRetirementException>(() =>
            MigrationStagingRetirementRuntime.ValidateContainerTopology(postgres, "network-one", "postgres"));

        postgres.Mounts =
        [
            new MountPoint { Type = "volume", Name = "one", Destination = "/var/lib/postgresql/data" },
            new MountPoint { Type = "volume", Name = "two", Destination = "/var/lib/postgresql/data" }
        ];
        Assert.Throws<MigrationStagingRetirementException>(() =>
            MigrationStagingRetirementRuntime.ValidateContainerTopology(postgres, "network-one", "postgres"));
    }

    [Fact]
    public void Internal_network_must_have_no_unrelated_endpoints()
    {
        var selected = new HashSet<string> { "selected" };
        var network = JsonSerializer.Deserialize<NetworkResponse>(
            "{\"Internal\":true,\"Containers\":{\"foreign\":{}}}")!;
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateNetworkTopology(network, selected));
        network.Containers.Clear();
        MigrationStagingRetirementRuntime.ValidateNetworkTopology(network, selected);
        network.Internal = false;
        Assert.Throws<MigrationStagingRetirementException>(() => MigrationStagingRetirementRuntime.ValidateNetworkTopology(network, selected));
    }

    private sealed class HistoryFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),"mem-retirement-test-"+Guid.NewGuid().ToString("N"));
        public string Workspace => Path.Combine(Root,"restore-staging","runs","stage_one");
        public string ResultPath => Path.Combine(Root,"restore-staging","history","stage_one","restore-staging-result.json");
        public string Candidate => Path.Combine(Root,"retained","candidate.txt");
        public MigrationStagingRetirementHistory History { get; }
        public HistoryFixture()
        {
            Directory.CreateDirectory(Root);
            History = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["MEM_DATA_ROOT"]=Root }).Build());
        }
        public async Task WriteAsync(string? workspaceOverride=null,string stagingOverride="stage_one",string? containerOverride=null)
        {
            Directory.CreateDirectory(Workspace);
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(Candidate)!);
            await File.WriteAllTextAsync(Candidate,"Retain this candidate");
            await File.WriteAllTextAsync(Path.Combine(Workspace,"copied-test-data"),"Disposable");
            await File.WriteAllTextAsync(ResultPath, JsonSerializer.Serialize(new
            {
                source="control-plane", status="ready", sourceKind="migration-candidate", stagingId=stagingOverride,
                startedAtUtc=DateTimeOffset.Parse("2026-09-24T01:00:00Z"), workspacePath=workspaceOverride ?? Workspace,
                networkName="mem-restore-staging-stage_one", networkId=new string('d',64),
                synapseContainerName="mem-restore-staging-synapse-stage_one", synapseContainerId=containerOverride ?? new string('b',64),
                postgresContainerName="mem-restore-staging-postgres-stage_one", postgresContainerId=new string('c',64),
                elementContainerName="mem-restore-staging-element-stage_one", elementContainerId=new string('a',64),
                safety=new { privateOnly=true,dockerNetworkInternal=true,publicRoutesCreated=false,productionContainersTouched=false,productionDatabasesTouched=false },
                runtime=new { }, checks=Array.Empty<string>(), warnings=Array.Empty<string>(), errors=Array.Empty<string>()
            }));
        }
        public void Dispose() { if(Directory.Exists(Root)) Directory.Delete(Root,true); }
    }
}
