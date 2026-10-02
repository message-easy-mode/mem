using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Modules.Operator.Migrations;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationPackageArchiveValidatorTests
{
    private static readonly Guid SourceStackId =
        Guid.Parse("9230081f-ba21-4e53-8fbe-b3cc7bd1b441");
    private const string Fingerprint =
        "76c31508f9a852b6a2bb85fabed3c6354d06c9ff7085a75dd72f29588287b1f8";

    [Fact]
    public async Task Verifies_every_indexed_file_and_returns_complete_source_identity()
    {
        var path = await CreateArchiveAsync(
            captureKind: "final",
            sourceFrozen: true,
            rehearsalOnly: false,
            sourceChangedDuringCapture: false,
            sourceStackId: SourceStackId,
            matrixServerName: "matrix-davids.deltabox.dev",
            tamperPayload: false);
        try
        {
            var summary = await MigrationPackageArchiveValidator.ValidateAsync(
                path,
                CancellationToken.None);

            Assert.Equal("capture-final-20260715", summary.MigrationId);
            Assert.Equal("MatrixEasyMode", summary.SourceProduct);
            Assert.Equal("0.1.0", summary.SourceVersion);
            Assert.Equal("20260507085953_InitialApplicationSchema", summary.LegacyMigration);
            Assert.Equal(Fingerprint, summary.StartSourceFingerprint);
            Assert.Equal(Fingerprint, summary.CompletionSourceFingerprint);
            var stack = Assert.Single(summary.Stacks);
            Assert.Equal(SourceStackId, stack.SourceStackId);
            Assert.Equal("davids-stack", stack.Slug);
            Assert.Equal("matrix-davids.deltabox.dev", stack.MatrixServerName);
            Assert.Equal("final", summary.CaptureKind);
            Assert.True(summary.SourceFrozen);
            Assert.False(summary.RehearsalOnly);
            Assert.False(summary.SourceChangedDuringCapture);
            Assert.Equal(new DateTimeOffset(2026, 7, 20, 23, 50, 0, TimeSpan.Zero), summary.CaptureStartedAtUtc);
            Assert.Equal(new DateTimeOffset(2026, 7, 20, 23, 57, 7, TimeSpan.Zero), summary.CaptureCompletedAtUtc);
            Assert.True(summary.VerifiedFileCount > 0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Rejects_source_drift_even_when_capture_flags_otherwise_look_final()
    {
        var path = await CreateArchiveAsync(
            captureKind: "final",
            sourceFrozen: true,
            rehearsalOnly: false,
            sourceChangedDuringCapture: true,
            sourceStackId: SourceStackId,
            matrixServerName: "matrix-davids.deltabox.dev",
            tamperPayload: false);
        try
        {
            var exception = await Assert.ThrowsAsync<SecureMigrationIntakeException>(
                () => MigrationPackageArchiveValidator.ValidateAsync(
                    path,
                    CancellationToken.None));

            Assert.Equal("migration_archive_capture_invalid", exception.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Rejects_one_byte_payload_tampering()
    {
        var path = await CreateArchiveAsync(
            captureKind: "final",
            sourceFrozen: true,
            rehearsalOnly: false,
            sourceChangedDuringCapture: false,
            sourceStackId: SourceStackId,
            matrixServerName: "matrix-davids.deltabox.dev",
            tamperPayload: true);
        try
        {
            var exception = await Assert.ThrowsAsync<SecureMigrationIntakeException>(
                () => MigrationPackageArchiveValidator.ValidateAsync(
                    path,
                    CancellationToken.None));

            Assert.Equal("migration_archive_checksum_mismatch", exception.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }


    [Fact]
    public async Task Rejects_selected_stack_provenance_that_does_not_match_the_manifest()
    {
        var path = await CreateArchiveAsync(
            captureKind: "preview",
            sourceFrozen: false,
            rehearsalOnly: true,
            sourceChangedDuringCapture: false,
            sourceStackId: SourceStackId,
            matrixServerName: "matrix-davids.deltabox.dev",
            tamperPayload: false,
            canonicalSelectedSourceStackId: Guid.Parse("04ee7ed6-22ad-4e02-b9dd-87b9a33bf274"));
        try
        {
            var exception = await Assert.ThrowsAsync<SecureMigrationIntakeException>(() =>
                MigrationPackageArchiveValidator.ValidateAsync(path, CancellationToken.None));

            Assert.Equal("migration_archive_selected_stack_export_invalid", exception.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Rejects_packages_that_contain_more_than_one_source_stack()
    {
        var path = await CreateArchiveAsync(
            captureKind: "preview",
            sourceFrozen: false,
            rehearsalOnly: true,
            sourceChangedDuringCapture: false,
            stacks: new[]
            {
                (SourceStackId, "tester", "matrix-tester.example.test"),
                (Guid.Parse("04ee7ed6-22ad-4e02-b9dd-87b9a33bf274"), "family", "matrix-family.example.test"),
            },
            tamperPayload: false);
        try
        {
            var exception = await Assert.ThrowsAsync<SecureMigrationIntakeException>(() =>
                MigrationPackageArchiveValidator.ValidateAsync(path, CancellationToken.None));

            Assert.Equal("migration_archive_manifest_invalid", exception.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    internal static Task<string> CreateArchiveAsync(
        string captureKind,
        bool sourceFrozen,
        bool rehearsalOnly,
        bool sourceChangedDuringCapture,
        Guid sourceStackId,
        string matrixServerName,
        bool tamperPayload,
        string migrationId = "capture-final-20260715",
        string legacyMigration = "20260507085953_InitialApplicationSchema",
        Guid? canonicalSelectedSourceStackId = null) =>
        CreateArchiveAsync(
            captureKind,
            sourceFrozen,
            rehearsalOnly,
            sourceChangedDuringCapture,
            new[] { (sourceStackId, "davids-stack", matrixServerName) },
            tamperPayload,
            migrationId,
            legacyMigration,
            canonicalSelectedSourceStackId);

    internal static async Task<string> CreateArchiveAsync(
        string captureKind,
        bool sourceFrozen,
        bool rehearsalOnly,
        bool sourceChangedDuringCapture,
        IReadOnlyList<(Guid SourceStackId, string Slug, string MatrixServerName)> stacks,
        bool tamperPayload,
        string migrationId = "capture-final-20260715",
        string legacyMigration = "20260507085953_InitialApplicationSchema",
        Guid? canonicalSelectedSourceStackId = null)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-package-archive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var archivePath = root + ".zip";

        var selected = stacks[0];
        var canonicalExport = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "mem-v010-selected-stack-export",
            schemaVersion = 2,
            selectedSourceStackId = canonicalSelectedSourceStackId ?? selected.SourceStackId,
            stack = new
            {
                id = canonicalSelectedSourceStackId ?? selected.SourceStackId,
                slug = selected.Slug,
            },
            services = new[]
            {
                new
                {
                    id = Guid.Parse("8f223a04-a2f1-4ec3-a984-768617476b85"),
                    stackId = selected.SourceStackId,
                    serviceKey = "matrix",
                    serverName = selected.MatrixServerName,
                },
            },
        });

        var files = new Dictionary<string, byte[]>
        {
            ["mem-migration/legacy-mem/canonical-export.json"] = canonicalExport,
            ["mem-migration/evidence/capture-report.json"] = "{}"u8.ToArray(),
        };

        foreach (var stack in stacks)
        {
            files[$"mem-migration/stacks/{stack.SourceStackId:D}/synapse/homeserver.db"] =
                tamperPayload ? "tampered"u8.ToArray() : "database"u8.ToArray();
        }

        var checksumsForOriginalPayload = files.ToDictionary(
            pair => pair.Key,
            pair => new
            {
                path = pair.Key,
                sizeBytes = pair.Key.EndsWith(
                    "homeserver.db",
                    StringComparison.Ordinal)
                    ? "database"u8.ToArray().LongLength
                    : pair.Value.LongLength,
                sha256 = Hash(pair.Key.EndsWith(
                    "homeserver.db",
                    StringComparison.Ordinal)
                    ? "database"u8.ToArray()
                    : pair.Value),
            });

        var included = checksumsForOriginalPayload.Values
            .Where(file =>
                file.path != "mem-migration/evidence/capture-report.json")
            .OrderBy(file => file.path, StringComparer.Ordinal)
            .ToArray();

        var manifest = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "mem-v010-migration",
            schemaVersion = 2,
            migrationId,
            source = new
            {
                product = "MatrixEasyMode",
                version = "0.1.0",
                legacyMigration,
                startFingerprint = Fingerprint,
                completionFingerprint = Fingerprint,
            },
            capture = new
            {
                kind = captureKind,
                sourceFrozen,
                rehearsalOnly,
                sourceChangedDuringCapture,
                startedAtUtc = new DateTimeOffset(2026, 7, 20, 23, 50, 0, TimeSpan.Zero),
                completedAtUtc = new DateTimeOffset(2026, 7, 20, 23, 57, 7, TimeSpan.Zero),
            },
            stacks = stacks.Select(stack => new
            {
                sourceStackId = stack.SourceStackId,
                slug = stack.Slug,
                matrixServerName = stack.MatrixServerName,
            }).ToArray(),
            includedFiles = included,
        });

        files["mem-migration/migration-manifest.json"] = manifest;

        var checksumFiles = files
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                if (pair.Key.EndsWith("homeserver.db", StringComparison.Ordinal))
                {
                    var original = "database"u8.ToArray();
                    return new
                    {
                        path = pair.Key,
                        sizeBytes = original.LongLength,
                        sha256 = Hash(original),
                    };
                }

                return new
                {
                    path = pair.Key,
                    sizeBytes = pair.Value.LongLength,
                    sha256 = Hash(pair.Value),
                };
            })
            .ToArray();

        var checksumIndex = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = "mem-migration-sha256",
            schemaVersion = 1,
            files = checksumFiles,
        });

        await using (var output = File.Create(archivePath))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
        {
            foreach (var pair in files)
            {
                var entry = archive.CreateEntry(
                    pair.Key,
                    CompressionLevel.NoCompression);
                await using var entryStream = entry.Open();
                await entryStream.WriteAsync(pair.Value);
            }

            var checksumEntry = archive.CreateEntry(
                "mem-migration/checksums/sha256.json",
                CompressionLevel.NoCompression);
            await using var checksumStream = checksumEntry.Open();
            await checksumStream.WriteAsync(checksumIndex);
        }

        Directory.Delete(root, recursive: true);
        return archivePath;
    }

    private static string Hash(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
