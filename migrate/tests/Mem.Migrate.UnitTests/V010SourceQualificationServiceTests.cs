using System.Security.Cryptography;
using System.Text.Json;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Qualification;
using Mem.Migrate.Legacy.V010.Qualification;

namespace Mem.Migrate.UnitTests;

public sealed class V010SourceQualificationServiceTests
{
    [Fact]
    public async Task Creates_hash_bound_source_evidence_for_a_frozen_production_source()
    {
        var fixture = await QualificationFixture.CreateAsync(developmentExternalControlPlane: false);
        try
        {
            var service = new V010SourceQualificationService(
                new StubEnvironmentProbe(fixture.Observation),
                new FixedTimeProvider(new DateTimeOffset(2026, 7, 20, 1, 30, 0, TimeSpan.Zero)));

            var report = await service.RunAsync(fixture.Options, CancellationToken.None);

            Assert.Equal("QualifiedSourceEvidence", report.Status);
            Assert.True(report.SourceFrozen);
            Assert.True(report.DistinctHostEvidenceReady);
            Assert.False(report.DevelopmentExternalControlPlane);
            Assert.All(
                report.Containers.Where(item => item.WriterContainer),
                item => Assert.True(item.FrozenStatePreserved));
            Assert.True(File.Exists(report.EvidencePath));
            var envelope = JsonSerializer.Deserialize<SourceQualificationEnvelope>(
                await File.ReadAllTextAsync(report.EvidencePath),
                CaptureJson.Options);
            Assert.NotNull(envelope);
            Assert.Equal(report.EvidenceSha256, envelope.PayloadSha256);
            Assert.Equal(
                SourceQualificationRenderer.ComputePayloadSha256(envelope.Payload),
                envelope.PayloadSha256);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Rejects_same_host_development_cutover_authority()
    {
        var fixture = await QualificationFixture.CreateAsync(developmentExternalControlPlane: true);
        try
        {
            var service = new V010SourceQualificationService(
                new StubEnvironmentProbe(fixture.Observation));

            var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.RunAsync(fixture.Options, CancellationToken.None));

            Assert.Contains("Same-host development", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private sealed class StubEnvironmentProbe(
        SourceQualificationEnvironmentObservation observation) :
        ISourceQualificationEnvironmentProbe
    {
        public Task<SourceQualificationEnvironmentObservation> ObserveAsync(
            CutoverPlanDocument plan,
            string dockerCommand,
            int commandTimeoutSeconds,
            CancellationToken cancellationToken) =>
            Task.FromResult(observation);
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class QualificationFixture : IDisposable
    {
        private QualificationFixture(
            string root,
            SourceQualificationOptions options,
            SourceQualificationEnvironmentObservation observation)
        {
            Root = root;
            Options = options;
            Observation = observation;
        }

        public string Root { get; }
        public SourceQualificationOptions Options { get; }
        public SourceQualificationEnvironmentObservation Observation { get; }

        public static async Task<QualificationFixture> CreateAsync(
            bool developmentExternalControlPlane)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "mem-source-qualification-service-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var packagePath = Path.Combine(root, "final.memmigration.zip.age");
            await File.WriteAllBytesAsync(packagePath, "qualified-package"u8.ToArray());
            var packageSha = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(packagePath)))
                .ToLowerInvariant();
            var fingerprint = new string('a', 64);
            var sourceStackId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var writer = new CutoverSourceContainer(
                "matrix",
                "writer-id",
                "legacy-matrix",
                "matrix-image",
                "matrix-image-id",
                "running",
                "healthy",
                "unless-stopped",
                true,
                Guid.NewGuid(),
                Guid.NewGuid());
            var retained = new CutoverRetainedContainer(
                "postgres",
                "postgres-id",
                "legacy-postgres",
                "postgres-image",
                "postgres-image-id",
                "running",
                "unless-stopped",
                "retain-running-database");
            var stack = new CutoverStack(
                sourceStackId,
                "legacy-stack",
                "Legacy Stack",
                "matrix.example.test",
                Guid.NewGuid(),
                writer.ContainerId,
                Guid.NewGuid(),
                "element-id",
                "matrix.example.test",
                "chat.example.test");
            var plan = new CutoverPlanDocument(
                "mem-cutover-plan",
                2,
                "mm06a-proof",
                "0.2.0-test",
                DateTimeOffset.Parse("2026-07-20T00:00:00Z"),
                DateTimeOffset.Parse("2026-07-20T01:00:00Z"),
                "assessment-proof",
                fingerprint,
                AssessmentClassification.ConfirmedSupportedV010,
                false,
                false,
                [writer],
                [retained],
                [stack],
                [],
                new CutoverTargetEvidence(
                    "stage-proof",
                    "import-proof",
                    "target",
                    "https://target.example.test",
                    "catalog-proof",
                    "restore-proof",
                    "staging-proof",
                    DateTimeOffset.Parse("2026-07-19T23:00:00Z"),
                    true,
                    true,
                    true,
                    true,
                    true),
                [],
                [],
                [],
                [],
                [],
                developmentExternalControlPlane);
            var planHash = CutoverPlanHash.Compute(plan);
            var planReport = new CutoverPreparationReport(
                "mem-cutover-preparation-report",
                1,
                "Prepared",
                planHash,
                plan,
                string.Empty,
                string.Empty);
            var freezeResult = new CutoverFreezeContainerResult(
                writer.Role,
                writer.ContainerId,
                writer.ContainerName,
                writer.RestartPolicy,
                writer.WasRunning,
                "exited",
                "no",
                true,
                true);
            var checkpoint = new CutoverRollbackCheckpoint(
                plan.PlanId,
                planHash,
                fingerprint,
                DateTimeOffset.Parse("2026-07-20T00:10:00Z"),
                [writer],
                [retained],
                [],
                ["matrix"],
                "restore exact source state");
            var freeze = new CutoverFreezeReport(
                "mem-cutover-source-freeze-report",
                1,
                "Frozen",
                "mm06b-proof",
                plan.PlanId,
                planHash,
                fingerprint,
                DateTimeOffset.Parse("2026-07-20T00:05:00Z"),
                DateTimeOffset.Parse("2026-07-20T00:10:00Z"),
                true,
                false,
                [freezeResult],
                checkpoint,
                string.Empty,
                string.Empty,
                [],
                []);
            var capture = new CaptureReport(
                "mem-migration-capture-receipt",
                2,
                "capture-proof",
                sourceStackId,
                "legacy-stack",
                "matrix.example.test",
                CaptureLifecycleStatus.Completed,
                DateTimeOffset.Parse("2026-07-20T00:15:00Z"),
                DateTimeOffset.Parse("2026-07-20T00:20:00Z"),
                fingerprint,
                fingerprint,
                false,
                false,
                Path.Combine(root, "final.memmigration.zip"),
                new string('b', 64),
                4096,
                null,
                null,
                null,
                true,
                new CapturePlanSummary(1, 4096, 8192, 4096, 10),
                [],
                []);
            var package = new PackageForIntakeReport(
                "mem-package-for-intake-report",
                2,
                "mig-proof",
                "mpr-final",
                DateTimeOffset.Parse("2026-07-20T00:30:00Z"),
                "AAAA-BBBB-CCCC-DDDD",
                "final",
                "source-migration-proof",
                "final",
                true,
                false,
                sourceStackId,
                "legacy-stack",
                "matrix.example.test",
                1,
                capture.ArchivePath,
                capture.ArchiveSha256,
                capture.ArchiveBytes,
                10,
                4096,
                packagePath,
                packageSha,
                new FileInfo(packagePath).Length,
                string.Empty,
                string.Empty);
            var planPath = Path.Combine(root, "cutover-plan.json");
            var freezePath = Path.Combine(root, "freeze.json");
            var capturePath = Path.Combine(root, "capture.json");
            var packageReportPath = Path.Combine(root, "package.json");
            await File.WriteAllTextAsync(
                planPath,
                JsonSerializer.Serialize(planReport, CaptureJson.Options));
            await File.WriteAllTextAsync(
                freezePath,
                JsonSerializer.Serialize(freeze, CaptureJson.Options));
            await File.WriteAllTextAsync(
                capturePath,
                JsonSerializer.Serialize(capture, CaptureJson.Options));
            await File.WriteAllTextAsync(
                packageReportPath,
                JsonSerializer.Serialize(package, CaptureJson.Options));

            var options = new SourceQualificationOptions
            {
                CutoverPlanReportPath = planPath,
                FreezeReportPath = freezePath,
                CaptureReportPath = capturePath,
                PackageReportPath = packageReportPath,
                ExpectedEncryptedPackageSha256 = packageSha,
                OutputPath = Path.Combine(root, "output"),
                QualificationAttemptId = "mm01e-source-proof"
            };
            var observation = new SourceQualificationEnvironmentObservation(
                new QualificationHostIdentity(
                    new string('c', 64),
                    "source-host",
                    "Linux",
                    "X64",
                    new string('d', 64),
                    "source-docker",
                    "27.0"),
                [
                    new SourceQualificationObservedContainer(
                        writer.Role,
                        writer.ContainerId,
                        writer.ContainerName,
                        writer.ImageId,
                        "exited",
                        false,
                        "no",
                        true),
                    new SourceQualificationObservedContainer(
                        retained.Role,
                        retained.ContainerId,
                        retained.ContainerName,
                        retained.ImageId,
                        "running",
                        true,
                        retained.RestartPolicy,
                        false)
                ]);
            return new QualificationFixture(root, options, observation);
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
