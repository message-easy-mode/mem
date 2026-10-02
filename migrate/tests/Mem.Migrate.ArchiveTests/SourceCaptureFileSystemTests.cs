using Mem.Migrate.Legacy.V010.Capture;

namespace Mem.Migrate.ArchiveTests;

public sealed class SourceCaptureFileSystemTests
{
    [Fact]
    public async Task Additional_configuration_capture_excludes_known_files_and_media()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-source-capture-tests",
            Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");

        try
        {
            Directory.CreateDirectory(Path.Combine(source, "workers"));
            Directory.CreateDirectory(Path.Combine(source, "media"));
            var homeserver = Path.Combine(source, "homeserver.yaml");
            var worker = Path.Combine(source, "workers", "worker.yaml");
            await File.WriteAllTextAsync(homeserver, "server_name: example.test\n");
            await File.WriteAllTextAsync(worker, "worker_app: synapse.app.generic_worker\n");
            await File.WriteAllTextAsync(
                Path.Combine(source, "media", "metadata.json"),
                "{}\n");

            var copied = await new SourceCaptureFileSystem()
                .CopyAdditionalConfigurationAsync(
                    source,
                    destination,
                    [homeserver],
                    [Path.Combine(source, "media")],
                    new CaptureWriteBudget(1024 * 1024, 16 * 1024 * 1024, 100),
                    CancellationToken.None);

            Assert.Single(copied);
            Assert.Equal(
                Path.Combine(destination, "workers", "worker.yaml"),
                copied[0]);
            Assert.True(File.Exists(copied[0]));
            Assert.False(File.Exists(Path.Combine(destination, "homeserver.yaml")));
            Assert.False(File.Exists(
                Path.Combine(destination, "media", "metadata.json")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Source_path_validation_rejects_a_symbolic_link_segment()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-migrate-source-capture-tests",
            Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(root, "outside");
        var source = Path.Combine(root, "source");

        try
        {
            Directory.CreateDirectory(outside);
            Directory.CreateDirectory(source);
            var outsideFile = Path.Combine(outside, "secret.yaml");
            File.WriteAllText(outsideFile, "secret: canary\n");
            var link = Path.Combine(source, "linked");
            Directory.CreateSymbolicLink(link, outside);

            Assert.Throws<InvalidDataException>(() =>
                SourceCaptureFileSystem.ValidateSourcePath(
                    source,
                    Path.Combine(link, "secret.yaml")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
