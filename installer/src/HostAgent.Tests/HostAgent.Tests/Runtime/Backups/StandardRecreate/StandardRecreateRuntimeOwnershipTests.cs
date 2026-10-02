using HostAgent.Runtime.Backups.StandardRecreate;

namespace HostAgent.Tests.Runtime.Backups.StandardRecreate;

public sealed class StandardRecreateRuntimeOwnershipTests
{
    [Fact]
    public void NormalizeLinux_root_control_plane_assigns_entire_restored_tree_to_synapse_runtime_identity()
    {
        using var fixture = Fixture.Create();
        var changed = new List<(string Path, uint Uid, uint Gid)>();

        var result = StandardRecreateRuntimeOwnership.NormalizeLinux(
            fixture.MatrixDataPath,
            effectiveUid: 0,
            effectiveGid: 0,
            (path, uid, gid) => changed.Add((path, uid, gid)));

        Assert.True(result.OwnershipChanged);
        Assert.Equal("991:991", result.ContainerUser);
        Assert.Equal(StandardRecreateRuntimeOwnership.DefaultSynapseUid, result.TargetUid);
        Assert.Equal(StandardRecreateRuntimeOwnership.DefaultSynapseGid, result.TargetGid);

        Assert.Contains(changed, item => item.Path == fixture.MatrixDataPath);
        Assert.Contains(changed, item => item.Path == fixture.MediaStorePath);
        Assert.Contains(changed, item => item.Path == fixture.LocalContentPath);
        Assert.Contains(changed, item => item.Path == fixture.MediaFilePath);
        Assert.All(changed, item =>
        {
            Assert.Equal(StandardRecreateRuntimeOwnership.DefaultSynapseUid, item.Uid);
            Assert.Equal(StandardRecreateRuntimeOwnership.DefaultSynapseGid, item.Gid);
        });
    }

    [Fact]
    public void NormalizeLinux_non_root_control_plane_keeps_matching_bind_mount_identity()
    {
        using var fixture = Fixture.Create();
        var changed = new List<string>();

        var result = StandardRecreateRuntimeOwnership.NormalizeLinux(
            fixture.MatrixDataPath,
            effectiveUid: 1000,
            effectiveGid: 1000,
            (path, _, _) => changed.Add(path));

        Assert.False(result.OwnershipChanged);
        Assert.Equal("1000:1000", result.ContainerUser);
        Assert.Equal((uint?)1000, result.TargetUid);
        Assert.Equal((uint?)1000, result.TargetGid);
        Assert.Empty(changed);
    }

    [Fact]
    public void NormalizeLinux_refuses_symbolic_links_before_changing_ownership_through_them()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = Fixture.Create();
        var external = Path.Combine(fixture.Root, "external");
        Directory.CreateDirectory(external);
        Directory.CreateSymbolicLink(
            Path.Combine(fixture.MatrixDataPath, "unsafe-link"),
            external);

        var exception = Assert.Throws<InvalidDataException>(() =>
            StandardRecreateRuntimeOwnership.NormalizeLinux(
                fixture.MatrixDataPath,
                effectiveUid: 0,
                effectiveGid: 0,
                (_, _, _) => { }));

        Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Media_store_write_probe_runs_as_the_resolved_synapse_identity_and_cleans_itself_up()
    {
        var parameters = StandardRecreateService.CreateMediaStoreWriteProbeExecParameters(
            "991:991",
            "qa_restore_probe");

        Assert.Equal("991:991", parameters.User);
        Assert.Equal(new[] { "sh", "-lc" }, parameters.Cmd!.Take(2));
        Assert.Contains("/data/media_store/.qa_restore_probe", parameters.Cmd![2], StringComparison.Ordinal);
        Assert.Contains("rm -rf", parameters.Cmd![2], StringComparison.Ordinal);
    }

    private sealed class Fixture : IDisposable
    {
        private Fixture(
            string root,
            string matrixDataPath,
            string mediaStorePath,
            string localContentPath,
            string mediaFilePath)
        {
            Root = root;
            MatrixDataPath = matrixDataPath;
            MediaStorePath = mediaStorePath;
            LocalContentPath = localContentPath;
            MediaFilePath = mediaFilePath;
        }

        public string Root { get; }
        public string MatrixDataPath { get; }
        public string MediaStorePath { get; }
        public string LocalContentPath { get; }
        public string MediaFilePath { get; }

        public static Fixture Create()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"mem-standard-recreate-ownership-{Guid.NewGuid():N}");
            var matrixDataPath = Path.Combine(root, "matrix");
            var mediaStorePath = Path.Combine(matrixDataPath, "media_store");
            var localContentPath = Path.Combine(mediaStorePath, "local_content", "ab", "cd");
            Directory.CreateDirectory(localContentPath);

            File.WriteAllText(Path.Combine(matrixDataPath, "homeserver.yaml"), "server_name: matrix.example.test");
            File.WriteAllText(Path.Combine(matrixDataPath, "signing.key"), "key");
            var mediaFilePath = Path.Combine(localContentPath, "media");
            File.WriteAllText(mediaFilePath, "payload");

            return new Fixture(
                root,
                matrixDataPath,
                mediaStorePath,
                localContentPath,
                mediaFilePath);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
