using System.Security.Cryptography;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Infrastructure.Encryption;

namespace Mem.Migrate.UnitTests;

public sealed class PackageForIntakeServiceTests
{
    private static readonly Guid SourceStackId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Recipient =
        "age1r5cmtjs7qft4w44jqh0y4w5w23jlcccztfrlqq8x3r2g8r60vqkqnkvu6f";

    [Fact]
    public void Options_reject_an_empty_source_stack_id()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new PackageForIntakeOptions
            {
                IntakeId = "mig_20260715-example",
                AgeRecipient = Recipient,
                RecipientFingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient),
                SourceStackId = Guid.Empty,
                ArchivePath = "source.memmigration.zip",
                OutputDirectory = "output"
            }.Normalize());

        Assert.Contains("Source stack ID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verifies_encrypts_and_writes_private_evidence()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = Path.Combine(root, "final.memmigration.zip");
            await File.WriteAllTextAsync(archivePath, "verified archive");
            var output = Path.Combine(root, "output");
            var reader = new StubArchiveReader(ValidVerification(archivePath));
            var envelope = new StubAgeEnvelope();
            var completedAtUtc = new DateTimeOffset(2026, 7, 16, 2, 3, 4, TimeSpan.Zero);
            var service = new PackageForIntakeService(
                reader,
                envelope,
                new FixedTimeProvider(completedAtUtc));
            var fingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient);

            var report = await service.PackageAsync(
                new PackageForIntakeOptions
                {
                    IntakeId = "mig_20260715-example",
                    PackageRevisionId = "mpr_20260718-final",
                    AgeRecipient = Recipient,
                    RecipientFingerprint = fingerprint,
                    SourceStackId = SourceStackId,
                    ArchivePath = archivePath,
                    OutputDirectory = output,
                    RequireFinalFrozen = true
                },
                CancellationToken.None);

            Assert.True(reader.VerifyCalled);
            Assert.Equal(Recipient, envelope.Recipient);
            Assert.Equal("mpr_20260718-final", report.PackageRevisionId);
            Assert.Equal("final", report.RequestedCaptureKind);
            Assert.Equal("capture-final-20260715", report.MigrationId);
            Assert.Equal("final", report.CaptureKind);
            Assert.True(report.SourceFrozen);
            Assert.False(report.RehearsalOnly);
            Assert.Equal(SourceStackId, report.SourceStackId);
            Assert.Equal("stack", report.SourceStackSlug);
            Assert.Equal("matrix.example.test", report.MatrixServerName);
            Assert.Equal(1, report.StackCount);
            Assert.Equal(completedAtUtc, report.CompletedAtUtc);
            Assert.EndsWith(
                "mem-migration-final-20260716-020304Z.memmigration.zip.age",
                report.EncryptedPackagePath,
                StringComparison.Ordinal);
            Assert.EndsWith(
                "mem-migration-final-20260716-020304Z.package-report.json",
                report.JsonReportPath,
                StringComparison.Ordinal);
            Assert.EndsWith(
                "mem-migration-final-20260716-020304Z.package-report.md",
                report.MarkdownReportPath,
                StringComparison.Ordinal);
            Assert.True(File.Exists(report.EncryptedPackagePath));
            Assert.True(File.Exists(report.JsonReportPath));
            Assert.True(File.Exists(report.MarkdownReportPath));
            Assert.DoesNotContain(
                "AGE-SECRET-KEY-",
                await File.ReadAllTextAsync(report.JsonReportPath),
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }


    [Fact]
    public async Task Final_handoff_rejects_a_preview_archive_before_encryption()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var archivePath = Path.Combine(root, "preview.memmigration.zip");
            await File.WriteAllTextAsync(archivePath, "verified preview archive");
            var verification = ValidVerification(archivePath) with
            {
                Manifest = ValidVerification(archivePath).Manifest! with
                {
                    Capture = new MigrationArchiveCapture(
                        "preview",
                        SourceFrozen: false,
                        RehearsalOnly: true,
                        SourceChangedDuringCapture: false,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        "live rehearsal")
                }
            };
            var envelope = new StubAgeEnvelope();
            var service = new PackageForIntakeService(new StubArchiveReader(verification), envelope);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.PackageAsync(
                    new PackageForIntakeOptions
                    {
                        IntakeId = "mig_20260715-example",
                        PackageRevisionId = "mpr_20260718-final",
                        AgeRecipient = Recipient,
                        RecipientFingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient),
                        SourceStackId = SourceStackId,
                        ArchivePath = archivePath,
                        OutputDirectory = Path.Combine(root, "output"),
                        RequireFinalFrozen = true
                    },
                    CancellationToken.None));

            Assert.Contains("not an eligible final frozen capture", exception.Message, StringComparison.Ordinal);
            Assert.Null(envelope.Recipient);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rejects_retired_multi_stack_capture_before_encryption()
    {
        var verification = ValidVerification("source.memmigration.zip");
        verification = verification with
        {
            Manifest = verification.Manifest! with
            {
                Stacks =
                [
                    .. verification.Manifest.Stacks,
                    verification.Manifest.Stacks[0] with
                    {
                        SourceStackId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        Slug = "other",
                        MatrixServerName = "matrix-other.example.test"
                    }
                ]
            }
        };
        var envelope = new StubAgeEnvelope();
        var service = new PackageForIntakeService(new StubArchiveReader(verification), envelope);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.PackageAsync(
                new PackageForIntakeOptions
                {
                    IntakeId = "mig_20260715-example",
                    AgeRecipient = Recipient,
                    RecipientFingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient),
                    SourceStackId = SourceStackId,
                    ArchivePath = "source.memmigration.zip",
                    OutputDirectory = "output"
                },
                CancellationToken.None));

        Assert.Contains("single-stack", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(envelope.Recipient);
    }

    [Fact]
    public async Task Rejects_capture_bound_to_a_different_source_stack_before_encryption()
    {
        var envelope = new StubAgeEnvelope();
        var service = new PackageForIntakeService(
            new StubArchiveReader(ValidVerification("source.memmigration.zip")),
            envelope);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.PackageAsync(
                new PackageForIntakeOptions
                {
                    IntakeId = "mig_20260715-example",
                    AgeRecipient = Recipient,
                    RecipientFingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient),
                    SourceStackId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                    ArchivePath = "source.memmigration.zip",
                    OutputDirectory = "output"
                },
                CancellationToken.None));

        Assert.Contains("does not match", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(envelope.Recipient);
    }

    [Fact]
    public async Task Requires_archive_resolution_before_packaging()
    {
        var reader = new StubArchiveReader(ValidVerification("source.memmigration.zip"));
        var envelope = new StubAgeEnvelope();
        var service = new PackageForIntakeService(reader, envelope);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => service.PackageAsync(
                new PackageForIntakeOptions
                {
                    IntakeId = "mig_20260715-example",
                    AgeRecipient = Recipient,
                    RecipientFingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient)
                },
                CancellationToken.None));

        Assert.Contains("Archive path was not resolved", exception.Message, StringComparison.Ordinal);
        Assert.False(reader.VerifyCalled);
        Assert.Null(envelope.Recipient);
    }

    [Fact]
    public async Task Rejects_recipient_fingerprint_mismatch_before_archive_or_age()
    {
        var reader = new StubArchiveReader(ValidVerification("source.memmigration.zip"));
        var envelope = new StubAgeEnvelope();
        var service = new PackageForIntakeService(reader, envelope);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PackageAsync(
                new PackageForIntakeOptions
                {
                    IntakeId = "mig_20260715-example",
                    AgeRecipient = Recipient,
                    RecipientFingerprint = "0000-0000-0000-0000",
                    ArchivePath = "source.memmigration.zip",
                    OutputDirectory = "output"
                },
                CancellationToken.None));

        Assert.Contains("fingerprint mismatch", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(reader.VerifyCalled);
        Assert.Null(envelope.Recipient);
    }

    [Fact]
    public async Task Invalid_archive_is_not_encrypted()
    {
        var reader = new StubArchiveReader(
            ValidVerification("source.memmigration.zip") with
            {
                Valid = false,
                Manifest = null,
                Findings = [new("checksum_mismatch", "Payload changed.")]
            });
        var envelope = new StubAgeEnvelope();
        var service = new PackageForIntakeService(reader, envelope);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.PackageAsync(
                new PackageForIntakeOptions
                {
                    IntakeId = "mig_20260715-example",
                    AgeRecipient = Recipient,
                    RecipientFingerprint = PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient),
                    SourceStackId = SourceStackId,
                    ArchivePath = "source.memmigration.zip",
                    OutputDirectory = "output"
                },
                CancellationToken.None));

        Assert.Null(envelope.Recipient);
    }

    private static MigrationArchiveVerificationResult ValidVerification(string archivePath)
    {
        var manifest = new MigrationArchiveManifest(
            "mem-v010-migration",
            2,
            new MigrationArchiveProducer("mem-migrate", "0.2.0"),
            "capture-final-20260715",
            DateTimeOffset.UtcNow,
            new MigrationArchiveSource(
                "MatrixEasyMode",
                "0.1.0",
                "legacy",
                new string('a', 64),
                new string('a', 64)),
            new MigrationArchiveCapture(
                "final",
                SourceFrozen: true,
                RehearsalOnly: false,
                SourceChangedDuringCapture: false,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "frozen"),
            [new MigrationArchiveStack(
                SourceStackId,
                Guid.NewGuid(),
                null,
                "stack",
                "Stack",
                "matrix.example.test",
                null,
                null,
                null,
                null,
                "db",
                "yaml",
                "key",
                [],
                null,
                null)],
            [],
            new MigrationArchiveLimits(1, 1, 1, 1, 1));

        return new MigrationArchiveVerificationResult(
            Valid: true,
            InputPath: archivePath,
            InputSha256: new string('b', 64),
            InputBytes: 16,
            VerifiedZipSha256: new string('b', 64),
            VerifiedZipBytes: 16,
            EncryptedInput: false,
            Manifest: manifest,
            VerifiedFileCount: 14,
            VerifiedExpandedBytes: 2_000,
            Findings: []);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mem-package-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubArchiveReader(MigrationArchiveVerificationResult result)
        : IMigrationArchiveReader
    {
        public bool VerifyCalled { get; private set; }

        public Task<MigrationArchiveInspection> InspectAsync(
            string archivePath,
            ArchiveSafetyLimits limits,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<MigrationArchiveVerificationResult> VerifyAsync(
            string archivePath,
            ArchiveSafetyLimits limits,
            CancellationToken cancellationToken)
        {
            VerifyCalled = true;
            return Task.FromResult(result);
        }
    }

    private sealed class StubAgeEnvelope : IAgeEnvelope
    {
        public string? Recipient { get; private set; }

        public async Task<AgeEnvelopeResult> EncryptAsync(
            string plaintextPath,
            string encryptedPath,
            string recipient,
            string ageCommand,
            CancellationToken cancellationToken)
        {
            Recipient = recipient;
            Directory.CreateDirectory(Path.GetDirectoryName(encryptedPath)!);
            await File.WriteAllTextAsync(encryptedPath, "encrypted", cancellationToken);
            var bytes = await File.ReadAllBytesAsync(encryptedPath, cancellationToken);
            return new AgeEnvelopeResult(
                encryptedPath,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                bytes.LongLength);
        }

        public Task DecryptAsync(
            string encryptedPath,
            string plaintextPath,
            string identityPath,
            string ageCommand,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
