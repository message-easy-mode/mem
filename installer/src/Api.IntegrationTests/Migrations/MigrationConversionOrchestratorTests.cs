using System.Security.Cryptography;
using System.Text;
using Infrastructure.Data.Entities.Migrations;
using Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Shared.RuntimeImages;
using Modules.Operator.Migrations.Conversion;

namespace Api.IntegrationTests.Migrations;

public sealed class MigrationConversionOrchestratorTests
{
    private static readonly Guid SourceStackId = Guid.Parse("9230081f-ba21-4e53-8fbe-b3cc7bd1b441");
    private static readonly Guid SecondSourceStackId = Guid.Parse("04ee7ed6-22ad-4e02-b9dd-87b9a33bf274");
    private static readonly Guid ThirdSourceStackId = Guid.Parse("1a08ff04-b03a-497e-adfd-727d1ea2c014");
    [Fact]
    public async Task Converts_validated_package_and_creates_migration_candidate_without_catalog_or_restore_rows()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.CreateValidatedIntakeAsync();
        var runner = new FakeRunner(success: true);
        var service = fixture.CreateService(runner);

        var options = await service.GetOptionsAsync(intake.IntakeId, CancellationToken.None);
        Assert.Equal(SourceStackId, options.BoundSourceStack.SourceStackId);
        Assert.Equal("tester", options.BoundSourceStack.Slug);
        Assert.Equal("matrix-tester.example.test", options.BoundSourceStack.MatrixServerName);

        var result = await service.StartAsync(intake.IntakeId, null, CancellationToken.None);

        Assert.Equal(Fixture.ApprovedSynapseImage, runner.LastRequest?.SynapseImage);
        Assert.Equal(Fixture.ApprovedPostgresImage, runner.LastRequest?.PostgresImage);
        Assert.Equal(SourceStackId, runner.LastRequest?.StackId);
        Assert.Equal("completed", result.Status);
        Assert.NotNull(result.CandidateArtifactId);
        Assert.Equal("synapse-postgresql-conversion", result.CandidateArtifactKind);
        Assert.Equal("verified", result.CandidateVerificationStatus);
        Assert.Equal(result.CandidateArtifactSha256, result.CandidateArtifactSha256?.ToLowerInvariant());
        Assert.Equal(64, result.CandidateSourcePackageSha256?.Length);
        Assert.Equal(64, result.CandidateArtifactSha256?.Length);
        Assert.Equal(64, result.CandidateManifestSha256?.Length);
        Assert.Equal(64, result.CandidateChecksumsSha256?.Length);
        Assert.NotNull(result.CandidateCreatedAtUtc);
        Assert.NotNull(result.CandidateVerifiedAtUtc);
        Assert.Equal(1, await fixture.Db.MigrationConversionAttempts.CountAsync());
        Assert.Equal(1, await fixture.Db.MigrationCandidateArtifacts.CountAsync());
        Assert.Equal(0, await fixture.Db.BackupCatalogEntries.CountAsync());
        Assert.Equal(0, await fixture.Db.RestoreAttempts.CountAsync());

        var attempt = await fixture.Db.MigrationConversionAttempts
            .Include(x => x.PackageRevision)
            .Include(x => x.CandidateArtifact)
            .SingleAsync();
        Assert.Null(attempt.ActiveMigrationKey);
        Assert.NotNull(attempt.PackageRevision);
        Assert.Equal("preview", attempt.PackageRevision!.Purpose);
        Assert.Equal(intake.PackageRevisions.Single().DecryptedArchiveSha256, attempt.PackageRevision.DecryptedArchiveSha256);
        Assert.Equal("verified", attempt.CandidateArtifact!.VerificationStatus);
        Assert.Equal("synapse-postgresql-conversion", attempt.CandidateArtifact.ArtifactKind);
    }


    [Fact]
    public async Task MIGRATION_CONVERSION_REQUEST_LIFETIME_CORR_01_request_abort_after_durable_acceptance_does_not_cancel_conversion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.CreateValidatedIntakeAsync();
        using var request = new CancellationTokenSource();
        var runner = new FakeRunner(
            success: true,
            onRun: operationToken =>
            {
                request.Cancel();
                Assert.False(operationToken.IsCancellationRequested);
            });
        var service = fixture.CreateService(runner);

        var result = await service.StartAsync(intake.IntakeId, null, request.Token);

        Assert.True(request.IsCancellationRequested);
        Assert.Equal("completed", result.Status);
        Assert.NotNull(result.CandidateArtifactId);

        var attempt = await fixture.Db.MigrationConversionAttempts.SingleAsync();
        Assert.Equal("completed", attempt.Status);
        Assert.Null(attempt.ActiveMigrationKey);
        Assert.Null(attempt.FailureCode);
        Assert.NotNull(attempt.CandidateArtifact);
    }


    [Fact]
    public async Task Converts_the_active_final_revision_from_revision_specific_storage()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.CreateValidatedIntakeAsync(final: true);
        var runner = new FakeRunner(success: true);

        var result = await fixture.CreateService(runner)
            .StartAsync(intake.IntakeId, null, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        var attempt = await fixture.Db.MigrationConversionAttempts
            .Include(x => x.PackageRevision)
            .Include(x => x.CandidateArtifact)
            .SingleAsync();
        Assert.Equal("final", attempt.PackageRevision!.Purpose);
        Assert.Equal(2, attempt.PackageRevision.RevisionNumber);
        Assert.Equal(intake.PackageRevisions.Single(x => x.Purpose == "final").DecryptedArchiveSha256, attempt.SourcePackageSha256);
        Assert.Contains(attempt.PackageRevision.PackageRevisionId, attempt.CandidateArtifact!.ProvenanceJson);
        Assert.Equal(
            Path.Combine("revisions", attempt.PackageRevision.PackageRevisionId, "source.memmigration.zip"),
            Path.GetRelativePath(
                Path.Combine(fixture.Root, "migration-intakes", intake.IntakeId),
                runner.LastRequest!.ArchivePath));
    }


    [Fact]
    public async Task Allows_preview_conversion_while_final_revision_is_awaiting_package()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.CreateValidatedIntakeAsync();
        var previewRevision = intake.PackageRevisions.Single();
        fixture.Db.MigrationPackageRevisions.Add(new MigrationPackageRevisionEntity
        {
            Id = Guid.NewGuid(),
            PackageRevisionId = "mpr_final_awaiting",
            MigrationIntake = intake,
            MigrationIntakeEntityId = intake.Id,
            RevisionNumber = 2,
            Purpose = "final",
            Status = "awaiting-package",
            RetentionState = "active",
            ActivePurposeKey = $"{intake.IntakeId}:final",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            AgeRecipient = "age1awaiting",
            RecipientFingerprint = "0000-0000-0000-0000",
            ProtectedAgeIdentity = "protected"
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.CreateService(new FakeRunner(success: true))
            .StartAsync(intake.IntakeId, null, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        var attempt = await fixture.Db.MigrationConversionAttempts.SingleAsync();
        Assert.Equal(previewRevision.Id, attempt.MigrationPackageRevisionEntityId);
    }

    [Fact]
    public async Task Rejects_retry_from_preview_after_final_revision_becomes_authority()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.CreateValidatedIntakeAsync();
        var failed = new MigrationConversionAttemptEntity
        {
            Id = Guid.NewGuid(),
            ConversionAttemptId = "conv_preview_failed",
            MigrationIntake = intake,
            MigrationIntakeEntityId = intake.Id,
            MigrationPackageRevisionEntityId = intake.PackageRevisions.Single().Id,
            PackageRevision = intake.PackageRevisions.Single(),
            SourcePackageSha256 = intake.PackageRevisions.Single().DecryptedArchiveSha256!,
            SourceAdapterId = "mem-v010",
            SourceAdapterVersion = "0.1.0",
            ConverterId = "worker",
            ConverterVersion = "v2",
            Status = "failed",
            CurrentStep = "failed",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        fixture.Db.MigrationConversionAttempts.Add(failed);
        await fixture.Db.SaveChangesAsync();
        await fixture.PromoteFinalRevisionAsync(intake);

        var exception = await Assert.ThrowsAsync<MigrationConversionException>(() =>
            fixture.CreateService(new FakeRunner(success: true))
                .StartAsync(intake.IntakeId, failed.ConversionAttemptId, CancellationToken.None));

        Assert.Equal("conversion_retry_package_superseded", exception.Code);
    }

    [Fact]
    public async Task Rejects_multi_stack_archive_before_conversion()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.CreateValidatedIntakeAsync(
            stacks: MultiStackArchive());
        var runner = new FakeRunner(success: true);

        var exception = await Assert.ThrowsAsync<MigrationConversionException>(() =>
            fixture.CreateService(runner)
                .GetOptionsAsync(intake.IntakeId, CancellationToken.None));

        Assert.Equal("conversion_package_archive_invalid", exception.Code);
        Assert.Equal(0, runner.CallCount);
        Assert.Equal(0, await fixture.Db.MigrationConversionAttempts.CountAsync());
    }

    [Fact]
    public async Task Rejects_unvalidated_package_before_starting_worker()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.CreateValidatedIntakeAsync();
        intake.PackageRevisions.Single().Status = "awaiting-package";
        await fixture.Db.SaveChangesAsync();
        var runner = new FakeRunner(success: true);

        var exception = await Assert.ThrowsAsync<MigrationConversionException>(() =>
            fixture.CreateService(runner).StartAsync(intake.IntakeId, null, CancellationToken.None));

        Assert.Equal("conversion_package_not_ready", exception.Code);
        Assert.Equal(0, runner.CallCount);
    }

    [Fact]
    public async Task Retains_failed_attempt_and_clears_active_key_when_worker_fails()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.CreateValidatedIntakeAsync();
        var runner = new FakeRunner(success: false);

        var exception = await Assert.ThrowsAsync<MigrationConversionException>(() =>
            fixture.CreateService(runner).StartAsync(intake.IntakeId, null, CancellationToken.None));

        Assert.Equal("conversion_failed", exception.Code);
        var attempt = await fixture.Db.MigrationConversionAttempts.SingleAsync();
        Assert.Equal("failed", attempt.Status);
        Assert.Null(attempt.ActiveMigrationKey);
        Assert.Equal(0, await fixture.Db.MigrationCandidateArtifacts.CountAsync());
    }


    [Fact]
    public async Task Rejects_conversion_before_worker_when_approved_postgres_runtime_is_unavailable()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.CreateValidatedIntakeAsync();
        var runner = new FakeRunner(success: true);

        var exception = await Assert.ThrowsAsync<MigrationConversionException>(() =>
            fixture.CreateService(runner, approvedPostgresRuntimeAvailable: false)
                .StartAsync(intake.IntakeId, null, CancellationToken.None));

        Assert.Equal("conversion_runtime_unavailable", exception.Code);
        Assert.Equal(0, runner.CallCount);
        Assert.Equal(0, await fixture.Db.MigrationConversionAttempts.CountAsync());
    }

    [Fact]
    public async Task Prepares_exact_operational_images_before_creating_conversion_attempt()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.CreateValidatedIntakeAsync();
        var runner = new FakeRunner(success: true);
        var operationalProvider = new FakeApprovedOperationalRuntimeImageProvider(available: true);

        var result = await fixture.CreateService(runner, operationalProvider: operationalProvider)
            .StartAsync(intake.IntakeId, null, CancellationToken.None);

        Assert.Equal("completed", result.Status);
        Assert.Equal(
            new[] { "prepare-synapse", "prepare-element", "resolve-synapse-operation", "resolve-element-operation" },
            operationalProvider.Calls);
        Assert.Equal(Fixture.ApprovedSynapseImage, runner.LastRequest!.SynapseImage);
        Assert.Equal(1, await fixture.Db.MigrationConversionAttempts.CountAsync());
    }

    [Fact]
    public async Task Rejects_conversion_without_attempt_when_operational_image_preparation_fails()
    {
        await using var fixture = await Fixture.CreateAsync();
        var intake = await fixture.CreateValidatedIntakeAsync();
        var runner = new FakeRunner(success: true);

        var exception = await Assert.ThrowsAsync<MigrationConversionException>(() =>
            fixture.CreateService(runner, approvedOperationalRuntimeAvailable: false)
                .StartAsync(intake.IntakeId, null, CancellationToken.None));

        Assert.Equal("conversion_runtime_unavailable", exception.Code);
        Assert.Equal(0, runner.CallCount);
        Assert.Equal(0, await fixture.Db.MigrationConversionAttempts.CountAsync());
    }

    private static IReadOnlyList<(Guid SourceStackId, string Slug, string MatrixServerName)> MultiStackArchive() =>
        new[]
        {
            (SourceStackId, "tester", "matrix-tester.example.test"),
            (SecondSourceStackId, "family", "matrix-family.example.test"),
            (ThirdSourceStackId, "gaming", "matrix-gaming.example.test"),
        };

    private sealed class FakeRunner(
        bool success,
        Action<CancellationToken>? onRun = null) : IMigrationConversionWorkerRunner
    {
        public int CallCount { get; private set; }
        public MigrationConversionWorkerRequest? LastRequest { get; private set; }

        public async Task<MigrationConversionWorkerRunResult> RunAsync(
            MigrationConversionWorkerRequest request,
            string requestPath,
            string eventsPath,
            string standardErrorPath,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            onRun?.Invoke(cancellationToken);
            Directory.CreateDirectory(Path.GetDirectoryName(requestPath)!);
            await File.WriteAllTextAsync(
                requestPath,
                System.Text.Json.JsonSerializer.Serialize(
                    request,
                    new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)),
                cancellationToken);
            Directory.CreateDirectory(Path.Combine(request.OutputPath, request.ConversionId));
            Directory.CreateDirectory(Path.GetDirectoryName(eventsPath)!);
            if (!success)
            {
                return new MigrationConversionWorkerRunResult(
                    70,
                    Path.Combine(request.OutputPath, request.ConversionId, "conversion-report.json"),
                    eventsPath,
                    standardErrorPath,
                    new[] { Event(request.OperationId, 1, MigrationConversionWorkerEventType.OperationFailed, "failed") },
                    null);
            }

            var outputDirectory = Path.Combine(request.OutputPath, request.ConversionId);
            var dumpPath = Path.Combine(outputDirectory, "synapse.sql");
            var evidencePath = Path.Combine(outputDirectory, "conversion-evidence.json");
            var reportPath = Path.Combine(outputDirectory, "conversion-report.json");
            await File.WriteAllTextAsync(dumpPath, "select 1;", cancellationToken);
            await File.WriteAllTextAsync(evidencePath, "{}", cancellationToken);
            var archiveHash = await HashAsync(request.ArchivePath, cancellationToken);
            var dumpHash = await HashAsync(dumpPath, cancellationToken);
            var report = new MigrationConversionReport(
                "mem-synapse-conversion-report", 1, request.ConversionId, "Completed",
                DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow,
                "source-migration", request.StackId ?? SourceStackId,
                ResolveMatrixServerName(request.StackId), request.ArchivePath,
                archiveHash, "synapse:test", "postgres:test", dumpPath, dumpHash,
                new FileInfo(dumpPath).Length, evidencePath, false, Array.Empty<object>(),
                Array.Empty<string>(), Array.Empty<string>());
            await File.WriteAllTextAsync(reportPath, System.Text.Json.JsonSerializer.Serialize(report), cancellationToken);
            return new MigrationConversionWorkerRunResult(
                0, reportPath, eventsPath, standardErrorPath,
                new[]
                {
                    Event(request.OperationId, 1, MigrationConversionWorkerEventType.OperationStarted, "running"),
                    Event(request.OperationId, 2, MigrationConversionWorkerEventType.OperationCompleted, "completed"),
                },
                report);
        }

        private static string ResolveMatrixServerName(Guid? sourceStackId)
        {
            if (sourceStackId == SecondSourceStackId)
            {
                return "matrix-family.example.test";
            }

            return sourceStackId == ThirdSourceStackId
                ? "matrix-gaming.example.test"
                : "matrix-tester.example.test";
        }

        private static MigrationConversionWorkerEvent Event(
            string operationId,
            long sequence,
            MigrationConversionWorkerEventType type,
            string status) => new(
                "mem-conversion-worker-event", 1, sequence, type, operationId,
                DateTimeOffset.UtcNow, "conversion", status, "test", new Dictionary<string, string?>());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string ApprovedPostgresImage = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public const string ApprovedSynapseImage = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        public const string ApprovedElementImage = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

        private readonly SqliteConnection _connection;
        private readonly string _root;
        public string Root => _root;
        public MemDbContext Db { get; }

        private Fixture(SqliteConnection connection, MemDbContext db, string root)
        {
            _connection = connection;
            Db = db;
            _root = root;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var root = Path.Combine(Path.GetTempPath(), "mem-conversion-orchestrator-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new Fixture(connection, db, root);
        }

        public async Task<MigrationIntakeEntity> CreateValidatedIntakeAsync(
            bool final = false,
            IReadOnlyList<(Guid SourceStackId, string Slug, string MatrixServerName)>? stacks = null)
        {
            stacks ??= new[] { (SourceStackId, "tester", "matrix-tester.example.test") };
            var intakeId = $"mig_test_{Guid.NewGuid():N}";
            var intakeRoot = Path.Combine(_root, "migration-intakes", intakeId);
            var revisionId = $"mpr_test_{Guid.NewGuid():N}";
            var archivePath = final
                ? Path.Combine(intakeRoot, "revisions", revisionId, "source.memmigration.zip")
                : Path.Combine(intakeRoot, "source.memmigration.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
            var createdArchive = await MigrationPackageArchiveValidatorTests.CreateArchiveAsync(
                final ? "final" : "preview",
                sourceFrozen: final,
                rehearsalOnly: !final,
                sourceChangedDuringCapture: false,
                stacks: stacks,
                tamperPayload: false,
                migrationId: intakeId);
            File.Move(createdArchive, archivePath, overwrite: true);
            var hash = await HashAsync(archivePath, CancellationToken.None);
            var now = DateTime.UtcNow;
            var intake = new MigrationIntakeEntity
            {
                Id = Guid.NewGuid(),
                IntakeId = intakeId,
                DisplayName = "Orchestrator test",
                CreatedAtUtc = now,
            };
            Db.MigrationPackageRevisions.Add(new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = revisionId,
                MigrationIntake = intake,
                MigrationIntakeEntityId = intake.Id,
                RevisionNumber = final ? 2 : 1,
                Purpose = final ? "final" : "preview",
                Status = "package-validated",
                RetentionState = "active",
                ActivePurposeKey = $"{intakeId}:{(final ? "final" : "preview")}",
                CreatedAtUtc = now,
                UploadedAtUtc = now,
                ValidatedAtUtc = now,
                DecryptedArchiveSha256 = hash,
                ArchiveSourceProduct = "MatrixEasyMode",
                ArchiveSourceVersion = "0.1.0",
                ArchiveStackCount = stacks.Count,
                CaptureKind = final ? "final" : "preview",
                SourceFrozen = final,
                RehearsalOnly = !final,
                ValidationCode = "validated",
            });
            Db.MigrationIntakes.Add(intake);
            await Db.SaveChangesAsync();
            return intake;
        }

        public async Task PromoteFinalRevisionAsync(MigrationIntakeEntity intake)
        {
            var revisionId = $"mpr_final_{Guid.NewGuid():N}";
            var path = Path.Combine(_root, "migration-intakes", intake.IntakeId, "revisions", revisionId, "source.memmigration.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var createdArchive = await MigrationPackageArchiveValidatorTests.CreateArchiveAsync(
                "final",
                sourceFrozen: true,
                rehearsalOnly: false,
                sourceChangedDuringCapture: false,
                sourceStackId: SourceStackId,
                matrixServerName: "matrix-tester.example.test",
                tamperPayload: false,
                migrationId: intake.IntakeId);
            File.Move(createdArchive, path, overwrite: true);
            var hash = await HashAsync(path, CancellationToken.None);
            var now = DateTime.UtcNow;

            Db.MigrationPackageRevisions.Add(new MigrationPackageRevisionEntity
            {
                Id = Guid.NewGuid(),
                PackageRevisionId = revisionId,
                MigrationIntakeEntityId = intake.Id,
                RevisionNumber = 2,
                Purpose = "final",
                Status = "package-validated",
                RetentionState = "active",
                ActivePurposeKey = $"{intake.IntakeId}:final",
                CreatedAtUtc = now,
                UploadedAtUtc = now,
                ValidatedAtUtc = now,
                DecryptedArchiveSha256 = hash,
                ArchiveSourceProduct = "MatrixEasyMode",
                ArchiveSourceVersion = "0.1.0",
                ArchiveStackCount = 1,
                CaptureKind = "final",
                SourceFrozen = true,
                RehearsalOnly = false,
                ValidationCode = "final-package-authority-selected"
            });

            await Db.SaveChangesAsync();
        }

        public MigrationConversionOrchestrator CreateService(
            IMigrationConversionWorkerRunner runner,
            bool approvedPostgresRuntimeAvailable = true,
            bool approvedOperationalRuntimeAvailable = true,
            IApprovedOperationalRuntimeImageProvider? operationalProvider = null)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["MEM_DATA_ROOT"] = _root })
                .Build();
            var postgresProvider = new FakeApprovedPostgresRuntimeProvider(approvedPostgresRuntimeAvailable);
            operationalProvider ??= new FakeApprovedOperationalRuntimeImageProvider(approvedOperationalRuntimeAvailable);
            var operationLifetime = new MigrationConversionOperationLifetime(
                new FakeHostApplicationLifetime(),
                NullLogger<MigrationConversionOperationLifetime>.Instance,
                TimeSpan.FromMinutes(1));
            return new MigrationConversionOrchestrator(
                Db,
                runner,
                operationLifetime,
                postgresProvider,
                operationalProvider,
                configuration,
                TimeProvider.System);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
            try { Directory.Delete(_root, true); } catch { }
        }
    }

    private sealed class FakeApprovedOperationalRuntimeImageProvider(bool available)
        : IApprovedOperationalRuntimeImageProvider
    {
        private const string SynapseReference =
            "matrixdotorg/synapse@sha256:6882d26594b87171e0fe807ac6bd7f0000665cd70e73fb88c58ec9bff14c19ce";
        private const string ElementReference =
            "vectorim/element-web@sha256:2a65f32acc6fd7163523d1c4b5174de354b5ceb085898b15898f4d8ea01a8e3d";

        public List<string> Calls { get; } = [];

        public ApprovedOperationalRuntimeImagePolicy GetPolicy() => new(
            SchemaVersion: 1,
            Synapse: new ApprovedOperationalRuntimeImagePolicyEntry(
                "Synapse",
                "matrixdotorg/synapse",
                "1.156.0",
                SynapseReference),
            Element: new ApprovedOperationalRuntimeImagePolicyEntry(
                "Element",
                "vectorim/element-web",
                "1.12.23",
                ElementReference));

        public Task<ApprovedOperationalRuntimeImageDescriptor> PrepareSynapseAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("prepare-synapse");
            return ResolveSynapse();
        }

        public Task<ApprovedOperationalRuntimeImageDescriptor> PrepareElementAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("prepare-element");
            return ResolveElement();
        }

        public Task<ApprovedOperationalRuntimeImageDescriptor> ResolveSynapseForOperationAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("resolve-synapse-operation");
            return ResolveSynapse();
        }

        public Task<ApprovedOperationalRuntimeImageDescriptor> ResolveElementForOperationAsync(
            CancellationToken cancellationToken)
        {
            Calls.Add("resolve-element-operation");
            return ResolveElement();
        }

        private Task<ApprovedOperationalRuntimeImageDescriptor> ResolveSynapse()
        {
            EnsureAvailable("Synapse");
            return Task.FromResult(new ApprovedOperationalRuntimeImageDescriptor(
                "Synapse",
                SynapseReference,
                Fixture.ApprovedSynapseImage,
                [SynapseReference],
                DateTime.UtcNow));
        }

        private Task<ApprovedOperationalRuntimeImageDescriptor> ResolveElement()
        {
            EnsureAvailable("Element");
            return Task.FromResult(new ApprovedOperationalRuntimeImageDescriptor(
                "Element",
                ElementReference,
                Fixture.ApprovedElementImage,
                [ElementReference],
                DateTime.UtcNow));
        }

        private void EnsureAvailable(string component)
        {
            if (!available)
            {
                throw new InvalidOperationException(
                    $"The MEM-approved {component} runtime is not available locally.");
            }
        }
    }

    private sealed class FakeApprovedPostgresRuntimeProvider(bool available)
        : IApprovedPostgresRuntimeProvider
    {
        public ApprovedPostgresRuntimePolicy GetPolicy() => new(
            "docker.io/library/postgres:16@sha256:" + new string('a', 64),
            16,
            "16.14",
            true,
            false);

        public Task<ApprovedPostgresRuntimeDescriptor> ResolveForInstallationAsync(
            CancellationToken cancellationToken) =>
            ResolveForOperationAsync(cancellationToken);

        public Task<ApprovedPostgresRuntimeDescriptor> ResolveForOperationAsync(
            CancellationToken cancellationToken)
        {
            if (!available)
            {
                throw new InvalidOperationException("The MEM-approved PostgreSQL runtime is not available locally.");
            }

            return Task.FromResult(new ApprovedPostgresRuntimeDescriptor(
                GetPolicy().ApprovedReference,
                Fixture.ApprovedPostgresImage,
                [GetPolicy().ApprovedReference],
                16,
                "16.14",
                DateTime.UtcNow));
        }
    }

    private sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication() => _stopping.Cancel();
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
