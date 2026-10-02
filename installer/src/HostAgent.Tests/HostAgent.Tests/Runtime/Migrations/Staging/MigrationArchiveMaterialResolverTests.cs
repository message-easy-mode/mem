using System.IO.Compression;
using System.Text.Json;
using HostAgent.Runtime.Migrations.Staging;

namespace HostAgent.Tests.Runtime.Migrations.Staging;

public sealed class MigrationArchiveMaterialResolverTests
{
    [Fact]
    public void Resolves_manifest_owned_canonical_material_paths_for_the_candidate_stack()
    {
        var stackId = Guid.Parse("9230081f-ba21-4e53-8fbe-b3cc7bd1b441");
        using var fixture = ArchiveFixture.Create(stackId, "matrix.example.test");

        var result = MigrationArchiveMaterialResolver.Resolve(
            fixture.Archive,
            stackId,
            "matrix.example.test");

        var root = $"mem-migration/stacks/{stackId:D}";
        Assert.Equal(stackId, result.SourceStackId);
        Assert.Equal("matrix.example.test", result.MatrixServerName);
        Assert.Equal($"{root}/synapse/homeserver.yaml", result.HomeserverConfigurationPath);
        Assert.Equal($"{root}/synapse/signing.key", result.SigningKeyPath);
        Assert.Equal($"{root}/media", result.MediaPath);
        Assert.Equal($"{root}/element/config.json", result.ElementConfigurationPath);
    }

    [Fact]
    public void Rejects_candidate_identity_that_does_not_match_the_validated_manifest()
    {
        var stackId = Guid.NewGuid();
        using var fixture = ArchiveFixture.Create(stackId, "matrix.example.test");

        var wrongStack = Assert.Throws<InvalidDataException>(() =>
            MigrationArchiveMaterialResolver.Resolve(
                fixture.Archive,
                Guid.NewGuid(),
                "matrix.example.test"));
        Assert.Contains("does not contain exactly one stack", wrongStack.Message, StringComparison.Ordinal);

        var wrongServer = Assert.Throws<InvalidDataException>(() =>
            MigrationArchiveMaterialResolver.Resolve(
                fixture.Archive,
                stackId,
                "other.example.test"));
        Assert.Contains("does not match", wrongServer.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_non_canonical_or_missing_manifest_material()
    {
        var stackId = Guid.NewGuid();
        using var wrongPath = ArchiveFixture.Create(
            stackId,
            "matrix.example.test",
            homeserverPath: $"stacks/{stackId:D}/synapse/homeserver.yaml");

        var nonCanonical = Assert.Throws<InvalidDataException>(() =>
            MigrationArchiveMaterialResolver.Resolve(
                wrongPath.Archive,
                stackId,
                "matrix.example.test"));
        Assert.Contains("non-canonical homeserver configuration", nonCanonical.Message, StringComparison.Ordinal);

        using var missingFile = ArchiveFixture.Create(
            stackId,
            "matrix.example.test",
            includeHomeserver: false);

        var missing = Assert.Throws<InvalidDataException>(() =>
            MigrationArchiveMaterialResolver.Resolve(
                missingFile.Archive,
                stackId,
                "matrix.example.test"));
        Assert.Contains("missing required entry", missing.Message, StringComparison.Ordinal);
    }

    private sealed class ArchiveFixture : IDisposable
    {
        private readonly MemoryStream _stream;

        private ArchiveFixture(MemoryStream stream, ZipArchive archive)
        {
            _stream = stream;
            Archive = archive;
        }

        public ZipArchive Archive { get; }

        public static ArchiveFixture Create(
            Guid stackId,
            string matrixServerName,
            string? homeserverPath = null,
            bool includeHomeserver = true)
        {
            var root = $"mem-migration/stacks/{stackId:D}";
            homeserverPath ??= $"{root}/synapse/homeserver.yaml";
            var signingPath = $"{root}/synapse/signing.key";
            var mediaPath = $"{root}/media";
            var elementPath = $"{root}/element/config.json";

            var stream = new MemoryStream();
            using (var writer = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifest = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    schema = "mem-v010-migration",
                    schemaVersion = 2,
                    stacks = new[]
                    {
                        new
                        {
                            sourceStackId = stackId,
                            matrixServerName,
                            homeserverConfigurationPath = homeserverPath,
                            signingKeyPath = signingPath,
                            mediaPath,
                            elementConfigurationPath = elementPath,
                        },
                    },
                });

                Add(writer, "mem-migration/migration-manifest.json", manifest);
                if (includeHomeserver) Add(writer, homeserverPath, "server_name: matrix.example.test"u8.ToArray());
                Add(writer, signingPath, "ed25519:test secret"u8.ToArray());
                Add(writer, elementPath, "{}"u8.ToArray());
                Add(writer, mediaPath + "/sample", "media"u8.ToArray());
            }

            stream.Position = 0;
            return new ArchiveFixture(
                stream,
                new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true));
        }

        private static void Add(ZipArchive archive, string path, byte[] bytes)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
            using var output = entry.Open();
            output.Write(bytes);
        }

        public void Dispose()
        {
            Archive.Dispose();
            _stream.Dispose();
        }
    }
}
