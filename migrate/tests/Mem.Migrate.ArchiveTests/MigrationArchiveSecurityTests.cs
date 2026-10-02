using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Security;
using Mem.Migrate.Infrastructure.Archive;

namespace Mem.Migrate.ArchiveTests;

public sealed class MigrationArchiveSecurityTests
{
    private static readonly ArchiveSafetyLimits Limits = new(
        MaximumEntryBytes: 32 * 1024 * 1024,
        MaximumExpandedBytes: 128 * 1024 * 1024,
        MaximumEntries: 1000,
        MaximumCompressionRatio: 100);

    [Fact]
    public async Task Valid_archive_round_trips_and_verifies_every_file()
    {
        using var fixture = await ArchiveFixture.CreateAsync();
        var written = await new MigrationArchiveWriter().WriteAsync(
            fixture.StagingRoot,
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);
        var result = await new MigrationArchiveReader().VerifyAsync(
            written.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.True(result.Valid);
        Assert.Empty(result.Findings);
        Assert.Equal(10, result.VerifiedFileCount);
        Assert.Equal(fixture.MigrationId, result.Manifest?.MigrationId);
        Assert.Equal(written.Sha256, result.VerifiedZipSha256);
    }

    [Fact]
    public async Task Stable_final_frozen_archive_round_trips_and_verifies()
    {
        using var fixture = await ArchiveFixture.CreateAsync(finalFrozen: true);
        var written = await new MigrationArchiveWriter().WriteAsync(
            fixture.StagingRoot,
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);
        var result = await new MigrationArchiveReader().VerifyAsync(
            written.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.True(result.Valid);
        Assert.Empty(result.Findings);
        var manifest = Assert.IsType<MigrationArchiveManifest>(result.Manifest);
        Assert.Equal("final", manifest.Capture.Kind);
        Assert.True(manifest.Capture.SourceFrozen);
        Assert.False(manifest.Capture.RehearsalOnly);
        Assert.False(manifest.Capture.SourceChangedDuringCapture);
    }

    [Fact]
    public async Task Contradictory_final_capture_mode_is_rejected()
    {
        using var fixture = await ArchiveFixture.CreateAsync(
            captureOverride: new MigrationArchiveCapture(
                "final",
                SourceFrozen: false,
                RehearsalOnly: false,
                SourceChangedDuringCapture: false,
                DateTimeOffset.Parse("2026-07-12T22:00:00Z"),
                DateTimeOffset.Parse("2026-07-12T22:00:01Z"),
                "test"));
        var written = await new MigrationArchiveWriter().WriteAsync(
            fixture.StagingRoot,
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);
        var result = await new MigrationArchiveReader().VerifyAsync(
            written.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Findings, finding =>
            finding.Code == "archive_invalid" &&
            finding.Message.Contains(
                "capture mode is invalid",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task Writer_output_is_deterministic_for_the_same_staging_tree()
    {
        using var fixture = await ArchiveFixture.CreateAsync();
        var firstPath = Path.Combine(fixture.Root, "first.zip");
        var secondPath = Path.Combine(fixture.Root, "second.zip");
        var writer = new MigrationArchiveWriter();
        var first = await writer.WriteAsync(
            fixture.StagingRoot,
            firstPath,
            Limits,
            CancellationToken.None);
        var second = await writer.WriteAsync(
            fixture.StagingRoot,
            secondPath,
            Limits,
            CancellationToken.None);

        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(
            await File.ReadAllBytesAsync(firstPath),
            await File.ReadAllBytesAsync(secondPath));
    }


    [Fact]
    public async Task Writer_excludes_transient_sqlite_snapshot_sidecars()
    {
        using var fixture = await ArchiveFixture.CreateAsync();
        var synapseDirectory = Path.Combine(
            fixture.StagingRoot,
            "stacks",
            "01234567-89ab-cdef-0123-456789abcdef",
            "synapse");
        Directory.CreateDirectory(synapseDirectory);
        var transientPaths = new[]
        {
            Path.Combine(
                synapseDirectory,
                "homeserver.db.partial-wal"),
            Path.Combine(
                synapseDirectory,
                "homeserver.db.partial-shm"),
            Path.Combine(
                synapseDirectory,
                "homeserver.db.partial-journal")
        };

        foreach (var path in transientPaths)
        {
            await File.WriteAllTextAsync(path, "transient");
        }

        var written = await new MigrationArchiveWriter().WriteAsync(
            fixture.StagingRoot,
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);

        using (var archive = ZipFile.OpenRead(written.ArchivePath))
        {
            Assert.DoesNotContain(archive.Entries, entry =>
                SqliteSnapshotArtifactPolicy.IsTemporarySidecar(
                    entry.FullName));
        }

        var verification = await new MigrationArchiveReader().VerifyAsync(
            written.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.True(verification.Valid);
        Assert.Empty(verification.Findings);
    }

    [Fact]
    public async Task Writer_refuses_to_overwrite_an_existing_archive()
    {
        using var fixture = await ArchiveFixture.CreateAsync();
        await File.WriteAllTextAsync(fixture.ArchivePath, "existing");

        await Assert.ThrowsAsync<IOException>(() =>
            new MigrationArchiveWriter().WriteAsync(
                fixture.StagingRoot,
                fixture.ArchivePath,
                Limits,
                CancellationToken.None));
    }

    [Fact]
    public async Task Writer_refuses_a_symbolic_link_in_staging()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var fixture = await ArchiveFixture.CreateAsync();
        var link = Path.Combine(fixture.StagingRoot, "linked-payload");
        File.CreateSymbolicLink(link, Path.Combine(fixture.StagingRoot, "payload.txt"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MigrationArchiveWriter().WriteAsync(
                fixture.StagingRoot,
                fixture.ArchivePath,
                Limits,
                CancellationToken.None));
    }

    [Fact]
    public async Task Writer_refuses_a_symbolic_link_directory_in_staging()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        using var fixture = await ArchiveFixture.CreateAsync();
        var outside = Path.Combine(fixture.Root, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(
            Path.Combine(outside, "secret.txt"),
            "secret-canary");
        Directory.CreateSymbolicLink(
            Path.Combine(fixture.StagingRoot, "linked-directory"),
            outside);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new MigrationArchiveWriter().WriteAsync(
                fixture.StagingRoot,
                fixture.ArchivePath,
                Limits,
                CancellationToken.None));
    }

    [Fact]
    public async Task One_byte_payload_change_is_detected()
    {
        using var fixture = await ArchiveFixture.CreateAsync();
        await new MigrationArchiveWriter().WriteAsync(
            fixture.StagingRoot,
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);

        using (var stream = new FileStream(
            fixture.ArchivePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("mem-migration/stacks/11111111-1111-1111-1111-111111111111/synapse/homeserver.db")
                ?? throw new InvalidOperationException();
            entry.Delete();
            var replacement = archive.CreateEntry(
                "mem-migration/stacks/11111111-1111-1111-1111-111111111111/synapse/homeserver.db",
                CompressionLevel.NoCompression);
            replacement.LastWriteTime = new DateTimeOffset(
                1980,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.Zero);
            await using var target = replacement.Open();
            await target.WriteAsync("changed"u8.ToArray());
        }

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Findings, finding =>
            finding.Code == "checksum_mismatch" ||
            finding.Code == "archive_invalid");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("mem-migration/../escape")]
    [InlineData("mem-migration/e\u0301.txt")]
    public async Task Unsafe_or_noncanonical_archive_paths_are_rejected(
        string hostilePath)
    {
        using var fixture = ArchiveFixture.CreateEmpty();
        CreateRawArchive(
            fixture.ArchivePath,
            CompressionLevel.NoCompression,
            (hostilePath, "hostile", 0));

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.False(result.Valid);
        Assert.Contains(result.Findings, finding =>
            finding.Code == "archive_invalid");
    }

    [Fact]
    public async Task Unknown_top_level_sections_are_rejected()
    {
        using var fixture = ArchiveFixture.CreateEmpty();
        CreateRawArchive(
            fixture.ArchivePath,
            CompressionLevel.NoCompression,
            ("mem-migration/unknown/payload", "hostile", 0));

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task Duplicate_archive_paths_are_rejected()
    {
        using var fixture = ArchiveFixture.CreateEmpty();
        CreateRawArchive(
            fixture.ArchivePath,
            CompressionLevel.NoCompression,
            ("mem-migration/stacks/01234567-89ab-cdef-0123-456789abcdef/media/duplicate", "one", 0),
            ("mem-migration/stacks/01234567-89ab-cdef-0123-456789abcdef/media/duplicate", "two", 0));

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task Case_colliding_archive_paths_are_rejected()
    {
        using var fixture = ArchiveFixture.CreateEmpty();
        CreateRawArchive(
            fixture.ArchivePath,
            CompressionLevel.NoCompression,
            ("mem-migration/stacks/01234567-89ab-cdef-0123-456789abcdef/media/Case", "one", 0),
            ("mem-migration/stacks/01234567-89ab-cdef-0123-456789abcdef/media/case", "two", 0));

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.False(result.Valid);
    }

    [Theory]
    [InlineData(0xA000)]
    [InlineData(0x1000)]
    [InlineData(0x6000)]
    [InlineData(0xC000)]
    public async Task Unix_special_file_entries_are_rejected(int fileType)
    {
        using var fixture = ArchiveFixture.CreateEmpty();
        var attributes = unchecked((int)(((uint)fileType | 0x01FFu) << 16));
        CreateRawArchive(
            fixture.ArchivePath,
            CompressionLevel.NoCompression,
            ("mem-migration/stacks/01234567-89ab-cdef-0123-456789abcdef/media/special", "target", attributes));

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task A_symlink_disguised_as_a_directory_is_rejected()
    {
        using var fixture = ArchiveFixture.CreateEmpty();
        CreateRawArchive(
            fixture.ArchivePath,
            CompressionLevel.NoCompression,
            ("mem-migration/stacks/01234567-89ab-cdef-0123-456789abcdef/media/", "", unchecked((int)(0xA1FFu << 16))));

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task Ordinary_directory_entries_are_ignored()
    {
        using var fixture = await ArchiveFixture.CreateAsync();
        await new MigrationArchiveWriter().WriteAsync(
            fixture.StagingRoot,
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);

        using (var stream = new FileStream(
            fixture.ArchivePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
        {
            var directory = archive.CreateEntry(
                "mem-migration/stacks/01234567-89ab-cdef-0123-456789abcdef/media/");
            directory.ExternalAttributes = unchecked((int)(0x41EDu << 16)) |
                (int)FileAttributes.Directory;
        }

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);

        Assert.True(result.Valid);
    }

    [Fact]
    public async Task Per_entry_limit_is_enforced_before_content_is_read()
    {
        using var fixture = ArchiveFixture.CreateEmpty();
        CreateRawArchive(
            fixture.ArchivePath,
            CompressionLevel.NoCompression,
            ("mem-migration/source-assessment.json", new string('x', 1024), 0));
        var tinyLimits = Limits with { MaximumEntryBytes = 128 };

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            tinyLimits,
            CancellationToken.None);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task Expanded_size_limit_is_enforced()
    {
        using var fixture = await ArchiveFixture.CreateAsync();
        await new MigrationArchiveWriter().WriteAsync(
            fixture.StagingRoot,
            fixture.ArchivePath,
            Limits,
            CancellationToken.None);
        var tinyLimits = Limits with { MaximumExpandedBytes = 128 };

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            tinyLimits,
            CancellationToken.None);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task Entry_count_limit_includes_directory_entries()
    {
        using var fixture = ArchiveFixture.CreateEmpty();
        CreateRawArchive(
            fixture.ArchivePath,
            CompressionLevel.NoCompression,
            ("mem-migration/one", "1", 0),
            ("mem-migration/two", "2", 0));
        var oneEntryLimit = Limits with { MaximumEntries = 1 };

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            oneEntryLimit,
            CancellationToken.None);

        Assert.False(result.Valid);
    }

    [Fact]
    public async Task Compression_ratio_limit_rejects_a_zip_bomb_shape()
    {
        using var fixture = ArchiveFixture.CreateEmpty();
        CreateRawArchive(
            fixture.ArchivePath,
            CompressionLevel.SmallestSize,
            ("mem-migration/source-assessment.json", new string('x', 512 * 1024), 0));
        var strictRatio = Limits with { MaximumCompressionRatio = 2 };

        var result = await new MigrationArchiveReader().VerifyAsync(
            fixture.ArchivePath,
            strictRatio,
            CancellationToken.None);

        Assert.False(result.Valid);
    }

    private static void CreateRawArchive(
        string path,
        CompressionLevel compressionLevel,
        params (string Path, string Content, int ExternalAttributes)[] entries)
    {
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Path, compressionLevel);
            entry.ExternalAttributes = item.ExternalAttributes;
            using var writer = new StreamWriter(
                entry.Open(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(item.Content);
        }
    }

    private sealed class ArchiveFixture : IDisposable
    {
        private ArchiveFixture(string root)
        {
            Root = root;
            StagingRoot = Path.Combine(root, "staging");
            ArchivePath = Path.Combine(root, "capture.memmigration.zip");
            MigrationId = "20260712-220000Z-0123456789abcdef0123456789abcdef";
        }

        public string Root { get; }
        public string StagingRoot { get; }
        public string ArchivePath { get; }
        public string MigrationId { get; }

        public static ArchiveFixture CreateEmpty()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "mem-migrate-archive-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new ArchiveFixture(root);
        }

        public static async Task<ArchiveFixture> CreateAsync(
            bool finalFrozen = false,
            MigrationArchiveCapture? captureOverride = null)
        {
            var fixture = CreateEmpty();
            Directory.CreateDirectory(fixture.StagingRoot);
            var sourceStackId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var stackRoot = $"stacks/{sourceStackId:D}";
            var payloadFiles = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["legacy-mem/canonical-export.json"] = JsonSerializer.Serialize(new
                {
                    schema = "mem-v010-selected-stack-export",
                    schemaVersion = 2,
                    selectedSourceStackId = sourceStackId,
                    stack = new { id = sourceStackId, slug = "tester" },
                    services = new[]
                    {
                        new
                        {
                            id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                            stackId = sourceStackId,
                            serviceKey = "matrix",
                            serverName = "matrix.example.test"
                        }
                    }
                }, CaptureJson.Options) + "\n",
                [$"{stackRoot}/synapse/homeserver.db"] = "sqlite-db",
                [$"{stackRoot}/synapse/homeserver.yaml"] = "server_name: matrix.example.test\n",
                [$"{stackRoot}/synapse/signing.key"] = "signing-key",
                [$"{stackRoot}/runtime/docker-inspect.json"] = "{}\n",
                [$"{stackRoot}/runtime/image-identity.json"] = "{}\n",
                [$"{stackRoot}/runtime/routes.json"] = "{}\n",
                [$"{stackRoot}/stack-manifest.json"] = "{}\n"
            };
            var includedFiles = new List<MigrationArchiveIncludedFile>();

            foreach (var pair in payloadFiles)
            {
                var path = Path.Combine(
                    fixture.StagingRoot,
                    pair.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, pair.Value);
                includedFiles.Add(new MigrationArchiveIncludedFile(
                    $"mem-migration/{pair.Key}",
                    new FileInfo(path).Length,
                    await Sha256File.ComputeAsync(
                        path,
                        CancellationToken.None)));
            }

            var manifest = new MigrationArchiveManifest(
                "mem-v010-migration",
                2,
                new MigrationArchiveProducer("mem-migrate", "0.2.0-alpha.1"),
                fixture.MigrationId,
                DateTimeOffset.Parse("2026-07-12T22:00:00Z"),
                new MigrationArchiveSource(
                    "MatrixEasyMode",
                    "0.1.0",
                    "20260507085953_InitialApplicationSchema",
                    new string('a', 64),
                    new string('a', 64)),
                captureOverride ?? new MigrationArchiveCapture(
                    finalFrozen ? "final" : "preview",
                    SourceFrozen: finalFrozen,
                    RehearsalOnly: !finalFrozen,
                    SourceChangedDuringCapture: false,
                    DateTimeOffset.Parse("2026-07-12T22:00:00Z"),
                    DateTimeOffset.Parse("2026-07-12T22:00:01Z"),
                    "test"),
                [new MigrationArchiveStack(
                    sourceStackId,
                    Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    Guid.Parse("33333333-3333-3333-3333-333333333333"),
                    "tester",
                    "Tester",
                    "matrix.example.test",
                    "https://matrix.example.test",
                    "https://chat.example.test",
                    "synapse:test",
                    "sha256:" + new string('a', 64),
                    $"mem-migration/{stackRoot}/synapse/homeserver.db",
                    $"mem-migration/{stackRoot}/synapse/homeserver.yaml",
                    $"mem-migration/{stackRoot}/synapse/signing.key",
                    [],
                    null,
                    null)],
                includedFiles
                    .OrderBy(file => file.Path, StringComparer.Ordinal)
                    .ToArray(),
                new MigrationArchiveLimits(
                    Limits.MaximumEntryBytes,
                    Limits.MaximumExpandedBytes,
                    Limits.MaximumEntries,
                    ExpandedBytes: 0,
                    EntryCount: payloadFiles.Count + 3));
            var evidencePath = Path.Combine(
                fixture.StagingRoot,
                "evidence",
                "capture-report.json");
            Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
            await File.WriteAllTextAsync(evidencePath, "{\"schema\":\"test\"}\n");
            await WriteStableControlFilesAsync(fixture.StagingRoot, manifest);
            return fixture;
        }

        private static async Task WriteStableControlFilesAsync(
            string stagingRoot,
            MigrationArchiveManifest initialManifest)
        {
            var manifestPath = Path.Combine(stagingRoot, "migration-manifest.json");
            var checksumPath = Path.Combine(
                stagingRoot,
                "checksums",
                "sha256.json");
            Directory.CreateDirectory(Path.GetDirectoryName(checksumPath)!);
            var manifest = initialManifest;

            for (var iteration = 0; iteration < 10; iteration++)
            {
                await File.WriteAllTextAsync(
                    manifestPath,
                    JsonSerializer.Serialize(manifest, CaptureJson.Options) + "\n");
                var checksums = new List<MigrationArchiveChecksum>();

                foreach (var path in Directory.EnumerateFiles(
                    stagingRoot,
                    "*",
                    SearchOption.AllDirectories)
                    .Where(path => !string.Equals(
                        Path.GetFullPath(path),
                        Path.GetFullPath(checksumPath),
                        StringComparison.Ordinal))
                    .OrderBy(path => path, StringComparer.Ordinal))
                {
                    checksums.Add(new MigrationArchiveChecksum(
                        ArchivePathPolicy.FromStagingPath(stagingRoot, path),
                        new FileInfo(path).Length,
                        await Sha256File.ComputeAsync(
                            path,
                            CancellationToken.None)));
                }

                await File.WriteAllTextAsync(
                    checksumPath,
                    JsonSerializer.Serialize(
                        new MigrationArchiveChecksumIndex(
                            "mem-migration-sha256",
                            1,
                            checksums.ToArray()),
                        CaptureJson.Options) + "\n");
                var expandedBytes = Directory.EnumerateFiles(
                        stagingRoot,
                        "*",
                        SearchOption.AllDirectories)
                    .Sum(path => new FileInfo(path).Length);

                if (expandedBytes == manifest.Limits.ExpandedBytes)
                {
                    return;
                }

                manifest = manifest with
                {
                    Limits = manifest.Limits with
                    {
                        ExpandedBytes = expandedBytes
                    }
                };
            }

            throw new InvalidOperationException(
                "The archive test fixture did not converge.");
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
