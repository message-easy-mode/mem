using System.Text;
using System.Text.Json;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Fingerprints;
using Mem.Migrate.Core.Security;
using Mem.Migrate.Infrastructure.Postgres;

namespace Mem.Migrate.Legacy.V010.Capture;

public sealed class V010SourceCaptureService(
    V010AssessmentService assessmentService,
    SourceCaptureFileSystem fileSystem,
    ISqliteSnapshotter sqliteSnapshotter,
    IMigrationArchiveWriter archiveWriter,
    IMigrationArchiveReader archiveReader,
    IAgeEnvelope ageEnvelope,
    ICaptureJournal journal) : ISourceCaptureService
{
    private const string ArchiveSchema = "mem-v010-migration";
    private const int ArchiveSchemaVersion = 2;

    public async Task<CaptureReport> CaptureAsync(
        CaptureOptions rawOptions,
        CancellationToken cancellationToken)
    {
        var options = rawOptions.Normalize();
        if (options.FinalFrozenCapture)
        {
            await ValidateFreezeReportAsync(options.FreezeReportPath!, cancellationToken);
        }
        var captureId = options.CaptureId ??
            CaptureIdentifier.Create(DateTimeOffset.UtcNow);
        var attemptRoot = Path.Combine(
            options.Assessment.WorkspacePath,
            "captures",
            captureId);
        var stagingRoot = Path.Combine(attemptRoot, "staging");
        var archivePath = Path.Combine(
            options.OutputPath,
            $"mem-v010-{captureId}.memmigration.zip");
        var unverifiedArchivePath = archivePath + ".unverified";
        var encryptedArchivePath = archivePath + ".age";
        var startedAtUtc = DateTimeOffset.UtcNow;
        var journalStarted = false;
        var ownsUnverifiedArchive = false;
        var ownsPlaintextArchive = false;
        var ownsEncryptedArchive = false;
        var ownsReceiptFiles = false;

        PrivateFilePermissions.EnsureDirectory(
            Path.Combine(options.Assessment.WorkspacePath, "captures"));
        await journal.InitializeAsync(cancellationToken);

        var existing = await journal.GetAsync(captureId, cancellationToken);

        if (existing is not null)
        {
            if (!options.Resume)
            {
                throw new InvalidOperationException(
                    $"Capture '{captureId}' already exists. Use --resume only to restart an incomplete capture with the same ID.");
            }

            if (existing.Status is CaptureLifecycleStatus.Completed &&
                !string.IsNullOrWhiteSpace(existing.ReportJson))
            {
                var completedReport = JsonSerializer.Deserialize<CaptureReport>(
                        existing.ReportJson,
                        CaptureJson.Options)
                    ?? throw new InvalidDataException(
                        "The completed capture journal report is invalid.");
                await ValidateCompletedArtifactAsync(
                    completedReport,
                    cancellationToken);
                return completedReport;
            }

            if (Directory.Exists(attemptRoot))
            {
                Directory.Delete(attemptRoot, recursive: true);
            }

            File.Delete(unverifiedArchivePath);
            File.Delete(unverifiedArchivePath + ".partial");
            File.Delete(archivePath);
            File.Delete(encryptedArchivePath);
            File.Delete(encryptedArchivePath + ".partial");
            DeleteReceiptFiles(options.OutputPath, captureId);
        }

        if (existing is null &&
            (File.Exists(unverifiedArchivePath) ||
             File.Exists(unverifiedArchivePath + ".partial") ||
             File.Exists(archivePath) ||
             File.Exists(encryptedArchivePath) ||
             File.Exists(encryptedArchivePath + ".partial") ||
             ReceiptFilesExist(options.OutputPath, captureId)))
        {
            throw new IOException(
                "A migration artifact already exists for this capture ID, but no matching journal entry owns it.");
        }

        PrivateFilePermissions.EnsureDirectory(attemptRoot);
        PrivateFilePermissions.EnsureDirectory(stagingRoot);

        try
        {
            var startAssessment = await RunCaptureAssessmentAsync(
                options,
                Path.Combine(attemptRoot, "assessment-start"),
                cancellationToken);
            ValidateAssessment(startAssessment.Result, options);
            ValidateCaptureDestinations(startAssessment.Result, options);
            PrivateFilePermissions.EnsureDirectory(options.OutputPath);

            var database = startAssessment.Result.Database.Candidates
                .Single(candidate => candidate.ExactSupportedSchema);
            var selectedSourceStackId = ResolveSelectedSourceStackId(
                options.SourceStackId,
                database);
            var stableSourceIdentity = ComputeSelectedSourceIdentity(
                startAssessment.Result,
                database,
                selectedSourceStackId);
            ValidateSelectedSourceIdentity(stableSourceIdentity, options);
            var plan = fileSystem.BuildPlan(
                startAssessment.Result,
                selectedSourceStackId,
                options);

            await journal.StartAsync(
                captureId,
                startedAtUtc,
                startAssessment.Result.SourceFingerprint,
                cancellationToken);
            journalStarted = true;

            var budget = new CaptureWriteBudget(
                options.MaximumEntryBytes,
                options.MaximumExpandedBytes,
                options.MaximumEntries);

            await CaptureSelectedStackMetadataAsync(
                stagingRoot,
                startAssessment.Result,
                database,
                selectedSourceStackId,
                budget,
                cancellationToken);
            var stackDescriptors = await CaptureStacksAsync(
                stagingRoot,
                startAssessment.Result,
                database,
                selectedSourceStackId,
                budget,
                cancellationToken);
            var selectedStackDescriptor = stackDescriptors.Single();

            var completionAssessment = await RunCaptureAssessmentAsync(
                options,
                Path.Combine(attemptRoot, "assessment-completion"),
                cancellationToken);
            ValidateCompletionAssessment(
                startAssessment.Result,
                completionAssessment.Result);
            var completedAtUtc = DateTimeOffset.UtcNow;
            var sourceChanged = !string.Equals(
                startAssessment.Result.SourceFingerprint,
                completionAssessment.Result.SourceFingerprint,
                StringComparison.Ordinal);
            if (options.FinalFrozenCapture && sourceChanged)
            {
                throw new InvalidOperationException(
                    "The frozen source fingerprint changed during final capture. Keep the source stopped, investigate the drift, and repeat with a new capture ID.");
            }
            var warnings = BuildWarnings(sourceChanged, options.AgeRecipient, options.FinalFrozenCapture);
            var captureKind = options.FinalFrozenCapture ? "final" : "preview";
            var consistencyMethod = options.FinalFrozenCapture
                ? "Source writers frozen; selected Synapse SQLite online backup; selected frozen media copy"
                : "Selected Synapse SQLite online backup; selected live media copy";
            var payloadFiles = await BuildFileInventoryAsync(
                stagingRoot,
                cancellationToken);
            var finalEntryCount = checked(payloadFiles.Length + 3);
            var manifest = new MigrationArchiveManifest(
                Schema: ArchiveSchema,
                SchemaVersion: ArchiveSchemaVersion,
                Producer: new MigrationArchiveProducer(
                    "mem-migrate",
                    options.ProducerVersion),
                MigrationId: captureId,
                CreatedAtUtc: completedAtUtc,
                Source: new MigrationArchiveSource(
                    Product: V010Constants.ProductName,
                    Version: V010Constants.ProductVersion,
                    LegacyMigration: LegacyPostgresProbe.ExpectedMigrationId,
                    StartFingerprint: startAssessment.Result.SourceFingerprint,
                    CompletionFingerprint:
                        completionAssessment.Result.SourceFingerprint),
                Capture: new MigrationArchiveCapture(
                    Kind: captureKind,
                    SourceFrozen: options.FinalFrozenCapture,
                    RehearsalOnly: !options.FinalFrozenCapture,
                    SourceChangedDuringCapture: sourceChanged,
                    StartedAtUtc: startedAtUtc,
                    CompletedAtUtc: completedAtUtc,
                    ConsistencyMethod: consistencyMethod),
                Stacks: stackDescriptors,
                IncludedFiles: payloadFiles,
                Limits: new MigrationArchiveLimits(
                    options.MaximumEntryBytes,
                    options.MaximumExpandedBytes,
                    options.MaximumEntries,
                    ExpandedBytes: 0,
                    EntryCount: finalEntryCount));
            var evidence = new CaptureEvidenceReport(
                Schema: "mem-migration-capture-report",
                SchemaVersion: 2,
                CaptureId: captureId,
                SourceStackId: selectedStackDescriptor.SourceStackId,
                SourceStackSlug: selectedStackDescriptor.Slug,
                MatrixServerName: selectedStackDescriptor.MatrixServerName,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: completedAtUtc,
                StartSourceFingerprint:
                    startAssessment.Result.SourceFingerprint,
                CompletionSourceFingerprint:
                    completionAssessment.Result.SourceFingerprint,
                SourceChangedDuringCapture: sourceChanged,
                SourceFrozen: options.FinalFrozenCapture,
                RehearsalOnly: !options.FinalFrozenCapture,
                ConsistencyMethod: consistencyMethod,
                Plan: plan,
                Warnings: warnings);

            await WriteControlFilesAsync(
                stagingRoot,
                manifest,
                evidence,
                cancellationToken);

            var safetyLimits = new ArchiveSafetyLimits(
                options.MaximumEntryBytes,
                options.MaximumExpandedBytes,
                options.MaximumEntries);

            if (File.Exists(archivePath) ||
                File.Exists(unverifiedArchivePath) ||
                File.Exists(encryptedArchivePath) ||
                ReceiptFilesExist(options.OutputPath, captureId))
            {
                throw new IOException(
                    "A migration artifact already exists for this capture ID. Use --resume only for the journalled incomplete capture.");
            }

            var archive = await archiveWriter.WriteAsync(
                stagingRoot,
                unverifiedArchivePath,
                safetyLimits,
                cancellationToken);
            ownsUnverifiedArchive = true;
            var verification = await archiveReader.VerifyAsync(
                archive.ArchivePath,
                safetyLimits,
                cancellationToken);

            if (!verification.Valid)
            {
                throw new InvalidDataException(
                    "The newly created migration archive did not pass independent verification: " +
                    string.Join(
                        "; ",
                        verification.Findings.Select(finding => finding.Message)));
            }

            File.Move(unverifiedArchivePath, archivePath);
            ownsUnverifiedArchive = false;
            ownsPlaintextArchive = true;
            PrivateFilePermissions.EnsureFile(archivePath);
            archive = archive with { ArchivePath = archivePath };

            AgeEnvelopeResult? encrypted = null;
            var plaintextRetained = true;

            if (!string.IsNullOrWhiteSpace(options.AgeRecipient))
            {
                encrypted = await ageEnvelope.EncryptAsync(
                    archive.ArchivePath,
                    encryptedArchivePath,
                    options.AgeRecipient,
                    options.AgeCommand,
                    cancellationToken);
                ownsEncryptedArchive = true;

                try
                {
                    File.Delete(archive.ArchivePath);

                    if (File.Exists(archive.ArchivePath))
                    {
                        throw new IOException(
                            "The verified plaintext archive could not be removed after encryption.");
                    }
                }
                catch
                {
                    File.Delete(encrypted.Path);
                    ownsEncryptedArchive = false;
                    throw;
                }

                ownsPlaintextArchive = false;
                plaintextRetained = false;
            }

            var report = new CaptureReport(
                Schema: "mem-migration-capture-receipt",
                SchemaVersion: 2,
                CaptureId: captureId,
                SourceStackId: selectedStackDescriptor.SourceStackId,
                SourceStackSlug: selectedStackDescriptor.Slug,
                MatrixServerName: selectedStackDescriptor.MatrixServerName,
                Status: CaptureLifecycleStatus.Completed,
                StartedAtUtc: startedAtUtc,
                CompletedAtUtc: completedAtUtc,
                StartSourceFingerprint:
                    startAssessment.Result.SourceFingerprint,
                CompletionSourceFingerprint:
                    completionAssessment.Result.SourceFingerprint,
                SourceChangedDuringCapture: sourceChanged,
                RehearsalOnly: !options.FinalFrozenCapture,
                ArchivePath: archive.ArchivePath,
                ArchiveSha256: archive.Sha256,
                ArchiveBytes: archive.SizeBytes,
                EncryptedArchivePath: encrypted?.Path,
                EncryptedArchiveSha256: encrypted?.Sha256,
                EncryptedArchiveBytes: encrypted?.SizeBytes,
                PlaintextArchiveRetained: plaintextRetained,
                Plan: plan,
                Warnings: warnings,
                NextSteps: BuildNextSteps(encrypted is not null, options.FinalFrozenCapture),
                StableSourceIdentity: stableSourceIdentity);
            var reportJson = JsonSerializer.Serialize(
                report,
                CaptureJson.Options);
            Directory.Delete(stagingRoot, recursive: true);
            ownsReceiptFiles = true;
            await WriteReceiptAsync(
                options.OutputPath,
                captureId,
                report,
                reportJson,
                cancellationToken);
            await journal.CompleteAsync(
                report,
                reportJson,
                cancellationToken);
            ownsPlaintextArchive = false;
            ownsEncryptedArchive = false;
            ownsReceiptFiles = false;
            return report;
        }
        catch (OperationCanceledException)
        {
            CleanupOwnedArtifacts(
                unverifiedArchivePath,
                archivePath,
                encryptedArchivePath,
                ownsUnverifiedArchive,
                ownsPlaintextArchive,
                ownsEncryptedArchive);
            if (ownsReceiptFiles)
            {
                DeleteReceiptFiles(options.OutputPath, captureId);
            }

            if (journalStarted)
            {
                await journal.FailAsync(
                    captureId,
                    DateTimeOffset.UtcNow,
                    "capture_cancelled",
                    "The source capture was cancelled.",
                    CancellationToken.None);
            }

            throw;
        }
        catch (Exception ex)
        {
            CleanupOwnedArtifacts(
                unverifiedArchivePath,
                archivePath,
                encryptedArchivePath,
                ownsUnverifiedArchive,
                ownsPlaintextArchive,
                ownsEncryptedArchive);
            if (ownsReceiptFiles)
            {
                DeleteReceiptFiles(options.OutputPath, captureId);
            }

            if (journalStarted)
            {
                await journal.FailAsync(
                    captureId,
                    DateTimeOffset.UtcNow,
                    "capture_failed",
                    AssessmentRedactor.RedactText(ex.Message),
                    CancellationToken.None);
            }

            throw;
        }
    }

    private static async Task ValidateCompletedArtifactAsync(
        CaptureReport report,
        CancellationToken cancellationToken)
    {
        if (report.SchemaVersion != 2 ||
            report.SourceStackId == Guid.Empty ||
            string.IsNullOrWhiteSpace(report.SourceStackSlug) ||
            string.IsNullOrWhiteSpace(report.MatrixServerName))
        {
            throw new InvalidOperationException(
                "The completed capture uses the retired multi-stack capture contract. Create a fresh capture for the selected stack.");
        }

        var path = report.PlaintextArchiveRetained
            ? report.ArchivePath
            : report.EncryptedArchivePath;
        var expectedSha256 = report.PlaintextArchiveRetained
            ? report.ArchiveSha256
            : report.EncryptedArchiveSha256;

        if (string.IsNullOrWhiteSpace(path) ||
            string.IsNullOrWhiteSpace(expectedSha256) ||
            !File.Exists(path))
        {
            throw new InvalidOperationException(
                "The completed capture journal refers to an artifact that is no longer available.");
        }

        var actualSha256 = await Sha256File.ComputeAsync(
            path,
            cancellationToken);

        if (!string.Equals(
                actualSha256,
                expectedSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The completed capture artifact no longer matches its journalled SHA-256 receipt.");
        }
    }

    private async Task<AssessmentExecutionResult> RunCaptureAssessmentAsync(
        CaptureOptions options,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var assessmentOptions = options.Assessment with
        {
            OutputPath = outputPath,
            JsonConsoleOutput = false
        };
        return await assessmentService.RunAsync(
            assessmentOptions,
            cancellationToken);
    }

    private static void ValidateAssessment(
        AssessmentResult assessment,
        CaptureOptions options)
    {
        if (assessment.Classification is not
            AssessmentClassification.ConfirmedSupportedV010 ||
            !assessment.CanProceedToCapture)
        {
            throw new InvalidOperationException(
                "Source capture requires a freshly confirmed supported v0.1.0 assessment with capture allowed.");
        }

        if (!string.IsNullOrWhiteSpace(options.ExpectedSourceFingerprint) &&
            !string.Equals(
                assessment.SourceFingerprint,
                options.ExpectedSourceFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The current source fingerprint does not match the operator-reviewed expected fingerprint.");
        }

        if (assessment.Database.Candidates.Count(candidate =>
                candidate.ExactSupportedSchema) != 1)
        {
            throw new InvalidOperationException(
                "Capture requires exactly one supported legacy PostgreSQL candidate.");
        }
    }

    private static void ValidateSelectedSourceIdentity(
        string actualSourceIdentity,
        CaptureOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ExpectedSourceIdentity) &&
            !string.Equals(
                actualSourceIdentity,
                options.ExpectedSourceIdentity,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The current selected source identity does not match the operator-reviewed source stack.");
        }
    }

    private static string ComputeSelectedSourceIdentity(
        AssessmentResult assessment,
        LegacyDatabaseCandidateObservation database,
        Guid selectedSourceStackId)
    {
        var stack = database.Stacks.Single(candidate =>
            candidate.Id == selectedSourceStackId);
        var matrix = database.Services.FirstOrDefault(service =>
            service.StackId == selectedSourceStackId &&
            string.Equals(
                service.ServiceKey,
                V010Constants.MatrixServiceKey,
                StringComparison.OrdinalIgnoreCase));
        var element = database.Services.FirstOrDefault(service =>
            service.StackId == selectedSourceStackId &&
            string.Equals(
                service.ServiceKey,
                V010Constants.ElementServiceKey,
                StringComparison.OrdinalIgnoreCase));
        var files = assessment.FileSystem.Stacks.FirstOrDefault(candidate =>
            candidate.StackId == selectedSourceStackId);

        return SelectedSourceIdentity.Compute(new SelectedSourceIdentityInput(
            ProductName: assessment.SystemConfig.ProductName,
            ProductVersion: assessment.SystemConfig.ProductVersion,
            SourceStackId: stack.Id,
            Slug: stack.Slug,
            MatrixServerName: matrix?.ServerName ?? files?.ParsedConfiguration.ServerName,
            MatrixPublicHost: matrix?.MatrixPublicHost ?? matrix?.PublicDomain,
            ElementPublicHost: element?.ElementPublicHost ?? element?.PublicDomain));
    }

    private static void ValidateCaptureDestinations(
        AssessmentResult assessment,
        CaptureOptions options)
    {
        var destinations = new[]
        {
            options.Assessment.WorkspacePath,
            options.OutputPath
        };
        var relevantContainerIdentifiers = assessment.Database.Candidates
            .Select(candidate => candidate.ContainerId)
            .Concat(assessment.Database.Candidates.SelectMany(candidate =>
                candidate.Services.Select(service => service.DockerContainerId)))
            .Concat(assessment.FileSystem.Stacks.Select(stack =>
                stack.MatrixContainerId))
            .Where(identifier => !string.IsNullOrWhiteSpace(identifier))
            .Select(identifier => identifier!)
            .ToArray();
        var relevantMountRoots = assessment.Docker.Containers
            .Where(container => relevantContainerIdentifiers.Any(identifier =>
                string.Equals(container.Id, identifier, StringComparison.Ordinal) ||
                container.Id.StartsWith(identifier, StringComparison.Ordinal) ||
                identifier.StartsWith(container.Id, StringComparison.Ordinal)))
            .SelectMany(container => container.Mounts)
            .Select(mount => mount.Source);
        var protectedRoots = assessment.FileSystem.Stacks
            .SelectMany(stack => new[]
            {
                stack.DataRoot,
                stack.ElementDataRoot
            })
            .Concat(relevantMountRoots)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!))
            .Distinct(PathComparer)
            .ToArray();

        foreach (var destination in destinations.Select(Path.GetFullPath))
        {
            if (protectedRoots.Any(root => PathsOverlap(destination, root)))
            {
                throw new InvalidOperationException(
                    "The migration workspace and output directory must not overlap an assessed source data or mount root.");
            }
        }
    }

    private static bool PathsOverlap(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var leftNormalized = left.TrimEnd(Path.DirectorySeparatorChar);
        var rightNormalized = right.TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(leftNormalized, rightNormalized, comparison) ||
            leftNormalized.StartsWith(
                rightNormalized + Path.DirectorySeparatorChar,
                comparison) ||
            rightNormalized.StartsWith(
                leftNormalized + Path.DirectorySeparatorChar,
                comparison);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static void ValidateCompletionAssessment(
        AssessmentResult startAssessment,
        AssessmentResult completionAssessment)
    {
        if (completionAssessment.Classification is not
                AssessmentClassification.ConfirmedSupportedV010 ||
            !completionAssessment.CanProceedToCapture ||
            completionAssessment.Database.Candidates.Count(candidate =>
                candidate.ExactSupportedSchema) != 1)
        {
            throw new InvalidOperationException(
                "The source could not be safely re-assessed as the same supported v0.1.0 compatibility family after capture.");
        }

        var startDatabase = startAssessment.Database.Candidates
            .Single(candidate => candidate.ExactSupportedSchema);
        var completionDatabase = completionAssessment.Database.Candidates
            .Single(candidate => candidate.ExactSupportedSchema);
        var startStackIds = startDatabase.Stacks
            .Select(stack => stack.Id)
            .OrderBy(id => id)
            .ToArray();
        var completionStackIds = completionDatabase.Stacks
            .Select(stack => stack.Id)
            .OrderBy(id => id)
            .ToArray();

        if (!string.Equals(
                startDatabase.ContainerId,
                completionDatabase.ContainerId,
                StringComparison.Ordinal) ||
            !string.Equals(
                startDatabase.DatabaseName,
                completionDatabase.DatabaseName,
                StringComparison.Ordinal) ||
            !startStackIds.SequenceEqual(completionStackIds))
        {
            throw new InvalidOperationException(
                "The supported source installation identity changed during capture. The mixed capture was discarded.");
        }
    }

    private static Guid ResolveSelectedSourceStackId(
        Guid? requestedSourceStackId,
        LegacyDatabaseCandidateObservation database)
    {
        if (database.Stacks.Length == 1)
        {
            var onlyStackId = database.Stacks[0].Id;
            if (requestedSourceStackId is not null && requestedSourceStackId != onlyStackId)
            {
                throw new InvalidOperationException(
                    "The selected source stack is not present in the supported MEM 0.1.0 installation.");
            }

            return onlyStackId;
        }

        if (requestedSourceStackId is null)
        {
            throw new InvalidOperationException(
                "This MEM 0.1.0 source contains multiple stacks. Select exactly one stack before capture.");
        }

        if (database.Stacks.Count(stack => stack.Id == requestedSourceStackId.Value) != 1)
        {
            throw new InvalidOperationException(
                "The selected source stack is not present exactly once in the supported MEM 0.1.0 installation.");
        }

        return requestedSourceStackId.Value;
    }

    private static async Task CaptureSelectedStackMetadataAsync(
        string stagingRoot,
        AssessmentResult assessment,
        LegacyDatabaseCandidateObservation database,
        Guid selectedSourceStackId,
        CaptureWriteBudget budget,
        CancellationToken cancellationToken)
    {
        var selectedStack = database.Stacks.Single(stack => stack.Id == selectedSourceStackId);
        var selectedServices = database.Services
            .Where(service => service.StackId == selectedSourceStackId)
            .OrderBy(service => service.Id)
            .Select(CanonicalMigrationProjection.Service)
            .ToArray();
        var selectedMatrixServices = selectedServices
            .Where(service => string.Equals(
                service.ServiceKey,
                "matrix",
                StringComparison.Ordinal))
            .ToArray();
        if (selectedMatrixServices.Length != 1 ||
            string.IsNullOrWhiteSpace(selectedMatrixServices[0].ServerName))
        {
            throw new InvalidOperationException(
                "The selected source stack does not have exactly one complete Matrix service identity.");
        }

        var legacyRoot = Path.Combine(stagingRoot, "legacy-mem");
        PrivateFilePermissions.EnsureDirectory(legacyRoot);

        var canonicalExport = new
        {
            schema = "mem-v010-selected-stack-export",
            schemaVersion = 2,
            sourceFingerprint = assessment.SourceFingerprint,
            databaseName = database.DatabaseName,
            selectedSourceStackId,
            stack = CanonicalMigrationProjection.Stack(selectedStack),
            services = selectedServices,
            excludedSourceWideMaterial = new[]
            {
                "unselected stack records",
                "source-wide PostgreSQL dump",
                "identity and workflow tables",
                "other stacks' configuration, signing keys, databases and media"
            }
        };

        await WriteJsonAsync(
            Path.Combine(legacyRoot, "canonical-export.json"),
            canonicalExport,
            budget,
            cancellationToken);
    }

    private async Task<MigrationArchiveStack[]> CaptureStacksAsync(
        string stagingRoot,
        AssessmentResult assessment,
        LegacyDatabaseCandidateObservation database,
        Guid selectedSourceStackId,
        CaptureWriteBudget budget,
        CancellationToken cancellationToken)
    {
        var descriptors = new List<MigrationArchiveStack>();

        foreach (var observed in assessment.FileSystem.Stacks
            .Where(stack => stack.StackId == selectedSourceStackId)
            .OrderBy(stack => stack.StackId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stack = database.Stacks.Single(item => item.Id == observed.StackId);
            var matrixService = database.Services.Single(item =>
                item.Id == observed.MatrixServiceId);
            var elementService = observed.ElementServiceId is null
                ? null
                : database.Services.Single(item =>
                    item.Id == observed.ElementServiceId.Value);
            var stackId = stack.Id.ToString("D");
            var stackRoot = Path.Combine(stagingRoot, "stacks", stackId);
            var synapseRoot = Path.Combine(stackRoot, "synapse");
            var elementRoot = Path.Combine(stackRoot, "element");
            var runtimeRoot = Path.Combine(stackRoot, "runtime");
            PrivateFilePermissions.EnsureDirectory(synapseRoot);
            PrivateFilePermissions.EnsureDirectory(runtimeRoot);

            var sqliteDestination = Path.Combine(
                synapseRoot,
                "homeserver.db");
            SourceCaptureFileSystem.ValidateSourcePath(
                observed.DataRoot,
                observed.SqliteDatabase.Path);
            await sqliteSnapshotter.CreateConsistentSnapshotAsync(
                observed.SqliteDatabase.Path,
                sqliteDestination,
                cancellationToken);
            budget.Reserve(new FileInfo(sqliteDestination).Length);
            await fileSystem.CopyFileAsync(
                observed.HomeserverConfiguration.Path,
                Path.Combine(synapseRoot, "homeserver.yaml"),
                budget,
                cancellationToken,
                observed.DataRoot);
            await fileSystem.CopyFileAsync(
                observed.SigningKey.Path,
                Path.Combine(synapseRoot, "signing.key"),
                budget,
                cancellationToken,
                observed.DataRoot);
            var additionalConfigurationRoot = Path.Combine(
                synapseRoot,
                "additional-config");
            var additionalConfigurationFiles =
                await fileSystem.CopyAdditionalConfigurationAsync(
                    observed.DataRoot
                        ?? throw new InvalidDataException(
                            $"Stack '{stack.Slug}' has no assessed data root."),
                    additionalConfigurationRoot,
                    [
                        observed.SqliteDatabase.Path,
                        observed.HomeserverConfiguration.Path,
                        observed.SigningKey.Path
                    ],
                    observed.MediaStore.Exists
                        ? [observed.MediaStore.Path]
                        : [],
                    budget,
                    cancellationToken);
            var additionalConfigurationPaths = additionalConfigurationFiles
                .Select(path => ArchivePathPolicy.FromStagingPath(
                    stagingRoot,
                    path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            string? mediaArchivePath = null;

            if (observed.MediaStore.Exists)
            {
                var mediaRoot = Path.Combine(stackRoot, "media");
                await fileSystem.CopyDirectoryAsync(
                    observed.MediaStore.Path,
                    mediaRoot,
                    budget,
                    cancellationToken,
                    observed.DataRoot);
                mediaArchivePath = ArchivePath(
                    $"stacks/{stackId}/media");
            }

            string? elementConfigurationPath = null;

            if (observed.ElementConfiguration is not null &&
                observed.ElementConfiguration.Exists)
            {
                PrivateFilePermissions.EnsureDirectory(elementRoot);
                await fileSystem.CopyFileAsync(
                    observed.ElementConfiguration.Path,
                    Path.Combine(elementRoot, "config.json"),
                    budget,
                    cancellationToken,
                    observed.ElementDataRoot);
                elementConfigurationPath = ArchivePath(
                    $"stacks/{stackId}/element/config.json");
            }

            var matrixContainer = FindContainer(
                assessment.Docker.Containers,
                matrixService.DockerContainerId ?? observed.MatrixContainerId);
            var elementContainer = FindContainer(
                assessment.Docker.Containers,
                elementService?.DockerContainerId);
            await WriteJsonAsync(
                Path.Combine(runtimeRoot, "docker-inspect.json"),
                new
                {
                    note = "Typed and redacted Docker snapshot; raw environment values are intentionally excluded.",
                    matrix = matrixContainer,
                    element = elementContainer
                },
                budget,
                cancellationToken);
            await WriteJsonAsync(
                Path.Combine(runtimeRoot, "image-identity.json"),
                new
                {
                    matrix = matrixContainer is null
                        ? null
                        : new
                        {
                            matrixContainer.Image,
                            matrixContainer.ImageId,
                            matrixContainer.CreatedAtUtc
                        },
                    element = elementContainer is null
                        ? null
                        : new
                        {
                            elementContainer.Image,
                            elementContainer.ImageId,
                            elementContainer.CreatedAtUtc
                        }
                },
                budget,
                cancellationToken);
            await WriteJsonAsync(
                Path.Combine(runtimeRoot, "routes.json"),
                new
                {
                    matrix = RouteSnapshot(matrixService),
                    element = elementService is null
                        ? null
                        : RouteSnapshot(elementService)
                },
                budget,
                cancellationToken);

            var serverName = observed.ParsedConfiguration.ServerName
                ?? matrixService.ServerName
                ?? throw new InvalidDataException(
                    $"Stack '{stack.Slug}' has no assessed Matrix server name.");
            var descriptor = new MigrationArchiveStack(
                SourceStackId: stack.Id,
                MatrixServiceId: matrixService.Id,
                ElementServiceId: elementService?.Id,
                Slug: stack.Slug,
                DisplayName: stack.Name,
                MatrixServerName: serverName,
                MatrixPublicUrl: CanonicalMigrationProjection.PublicUrl(
                    matrixService.MatrixPublicHost ??
                    matrixService.PublicDomain ??
                    matrixService.BaseUrl),
                ElementPublicUrl: CanonicalMigrationProjection.PublicUrl(
                    elementService?.ElementPublicHost ??
                    elementService?.PublicDomain ??
                    elementService?.BaseUrl),
                SynapseImage: matrixContainer?.Image ?? matrixService.Image,
                SynapseImageId: matrixContainer?.ImageId,
                SqlitePath: ArchivePath(
                    $"stacks/{stackId}/synapse/homeserver.db"),
                HomeserverConfigurationPath: ArchivePath(
                    $"stacks/{stackId}/synapse/homeserver.yaml"),
                SigningKeyPath: ArchivePath(
                    $"stacks/{stackId}/synapse/signing.key"),
                AdditionalConfigurationPaths: additionalConfigurationPaths,
                MediaPath: mediaArchivePath,
                ElementConfigurationPath: elementConfigurationPath);
            descriptors.Add(descriptor);

            await WriteJsonAsync(
                Path.Combine(stackRoot, "stack-manifest.json"),
                new
                {
                    schema = "mem-v010-stack-capture",
                    schemaVersion = 1,
                    descriptor,
                    sourceStack = CanonicalMigrationProjection.Stack(stack),
                    matrixService = CanonicalMigrationProjection.Service(matrixService),
                    elementService = elementService is null
                        ? null
                        : CanonicalMigrationProjection.Service(elementService),
                    files = observed,
                    sourceReadiness = new
                    {
                        matrixState = matrixContainer?.State,
                        matrixHealth = matrixContainer?.Health,
                        elementState = elementContainer?.State,
                        elementHealth = elementContainer?.Health
                    }
                },
                budget,
                cancellationToken);
        }

        if (descriptors.Count != 1)
        {
            throw new InvalidOperationException(
                "The single-stack capture did not produce exactly one stack descriptor.");
        }

        return descriptors.ToArray();
    }

    private static DockerContainerObservation? FindContainer(
        IEnumerable<DockerContainerObservation> containers,
        string? identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        return containers.FirstOrDefault(container =>
            string.Equals(container.Id, identifier, StringComparison.Ordinal) ||
            container.Id.StartsWith(identifier, StringComparison.Ordinal) ||
            identifier.StartsWith(container.Id, StringComparison.Ordinal));
    }

    private static object RouteSnapshot(LegacyServiceRecord service) => new
    {
        service.PublicRouteId,
        service.PublicDomain,
        service.InternalRouteId,
        service.InternalDomain,
        service.ForwardHost,
        service.ForwardPort,
        service.MatrixPublicHost,
        service.ElementPublicHost,
        evidenceOnly = true,
        note = "Observed source forwarding values are evidence only and must never become target transport settings."
    };

    private static async Task<MigrationArchiveIncludedFile[]>
        BuildFileInventoryAsync(
            string stagingRoot,
            CancellationToken cancellationToken)
    {
        var files = new List<MigrationArchiveIncludedFile>();

        foreach (var information in EnumerateStagingFiles(stagingRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = ArchivePathPolicy.FromStagingPath(
                stagingRoot,
                information.FullName);
            files.Add(new MigrationArchiveIncludedFile(
                relative,
                information.Length,
                await Sha256File.ComputeAsync(
                    information.FullName,
                    cancellationToken)));
        }

        ArchivePathPolicy.EnsureUnique(files.Select(file => file.Path));
        return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    private static async Task WriteControlFilesAsync(
        string stagingRoot,
        MigrationArchiveManifest initialManifest,
        CaptureEvidenceReport evidence,
        CancellationToken cancellationToken)
    {
        var evidencePath = Path.Combine(
            stagingRoot,
            "evidence",
            "capture-report.json");
        await WriteJsonWithoutBudgetAsync(
            evidencePath,
            evidence,
            cancellationToken);
        var manifestPath = Path.Combine(
            stagingRoot,
            "migration-manifest.json");
        var checksumPath = Path.Combine(
            stagingRoot,
            "checksums",
            "sha256.json");
        var manifest = initialManifest;

        for (var iteration = 0; iteration < 10; iteration++)
        {
            await WriteJsonWithoutBudgetAsync(
                manifestPath,
                manifest,
                cancellationToken);
            var checksums = await BuildChecksumIndexAsync(
                stagingRoot,
                checksumPath,
                cancellationToken);
            await WriteJsonWithoutBudgetAsync(
                checksumPath,
                checksums,
                cancellationToken);
            var expandedBytes = EnumerateStagingFiles(stagingRoot)
                .Sum(file => file.Length);

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
            "The migration archive control-file metadata did not converge deterministically.");
    }

    private static async Task<MigrationArchiveChecksumIndex>
        BuildChecksumIndexAsync(
            string stagingRoot,
            string checksumPath,
            CancellationToken cancellationToken)
    {
        var checksumFullPath = Path.GetFullPath(checksumPath);
        var files = new List<MigrationArchiveChecksum>();

        foreach (var information in EnumerateStagingFiles(stagingRoot))
        {
            if (string.Equals(
                    information.FullName,
                    checksumFullPath,
                    StringComparison.Ordinal))
            {
                continue;
            }

            files.Add(new MigrationArchiveChecksum(
                ArchivePathPolicy.FromStagingPath(
                    stagingRoot,
                    information.FullName),
                information.Length,
                await Sha256File.ComputeAsync(
                    information.FullName,
                    cancellationToken)));
        }

        return new MigrationArchiveChecksumIndex(
            "mem-migration-sha256",
            1,
            files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray());
    }

    private static FileInfo[] EnumerateStagingFiles(string stagingRoot)
    {
        var root = new DirectoryInfo(Path.GetFullPath(stagingRoot));
        UnixFileTypeSafety.EnsureDirectory(root.FullName);
        var pending = new Stack<DirectoryInfo>();
        var files = new List<FileInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var child in directory
                .EnumerateFileSystemInfos()
                .OrderByDescending(item => item.Name, StringComparer.Ordinal))
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    child.LinkTarget is not null)
                {
                    throw new InvalidDataException(
                        $"Private archive staging contains a symbolic link: {child.FullName}");
                }

                if (child is DirectoryInfo childDirectory)
                {
                    UnixFileTypeSafety.EnsureDirectory(childDirectory.FullName);
                    pending.Push(childDirectory);
                    continue;
                }

                if (child is not FileInfo file)
                {
                    throw new InvalidDataException(
                        $"Private archive staging contains a non-regular entry: {child.FullName}");
                }

                UnixFileTypeSafety.EnsureRegularFile(file.FullName);

                if (SqliteSnapshotArtifactPolicy.IsTemporarySidecar(
                        file.FullName))
                {
                    continue;
                }

                files.Add(file);
            }
        }

        return files
            .OrderBy(file => file.FullName, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CaptureWriteBudget budget,
        CancellationToken cancellationToken)
    {
        await WriteJsonWithoutBudgetAsync(path, value, cancellationToken);
        budget.Reserve(new FileInfo(path).Length);
    }

    private static async Task WriteJsonWithoutBudgetAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "JSON capture path has no parent directory.");
        PrivateFilePermissions.EnsureDirectory(parent);
        var json = JsonSerializer.Serialize(value, CaptureJson.Options);
        await File.WriteAllTextAsync(
            path,
            json + Environment.NewLine,
            Encoding.UTF8,
            cancellationToken);
        PrivateFilePermissions.EnsureFile(path);
    }

    private static async Task WriteReceiptAsync(
        string outputPath,
        string captureId,
        CaptureReport report,
        string reportJson,
        CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(
            outputPath,
            $"mem-v010-{captureId}.capture.json");
        var markdownPath = Path.Combine(
            outputPath,
            $"mem-v010-{captureId}.capture.md");
        await WriteTextAtomicallyAsync(
            jsonPath,
            reportJson + Environment.NewLine,
            cancellationToken);
        await WriteTextAtomicallyAsync(
            markdownPath,
            RenderMarkdown(report) + Environment.NewLine,
            cancellationToken);
    }

    private static async Task WriteTextAtomicallyAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            throw new IOException($"Capture receipt already exists: {path}");
        }

        var partialPath = path + ".partial";
        File.Delete(partialPath);
        var ownsDestination = false;

        try
        {
            await File.WriteAllTextAsync(
                partialPath,
                content,
                Encoding.UTF8,
                cancellationToken);
            PrivateFilePermissions.EnsureFile(partialPath);
            File.Move(partialPath, path);
            ownsDestination = true;
            PrivateFilePermissions.EnsureFile(path);
        }
        catch
        {
            File.Delete(partialPath);

            if (ownsDestination)
            {
                File.Delete(path);
            }

            throw;
        }
    }

    private static void CleanupOwnedArtifacts(
        string unverifiedArchivePath,
        string archivePath,
        string encryptedArchivePath,
        bool ownsUnverifiedArchive,
        bool ownsPlaintextArchive,
        bool ownsEncryptedArchive)
    {
        if (ownsUnverifiedArchive)
        {
            File.Delete(unverifiedArchivePath);
        }

        File.Delete(unverifiedArchivePath + ".partial");

        if (ownsPlaintextArchive)
        {
            File.Delete(archivePath);
        }

        if (ownsEncryptedArchive)
        {
            File.Delete(encryptedArchivePath);
        }

        File.Delete(encryptedArchivePath + ".partial");
    }

    private static bool ReceiptFilesExist(
        string outputPath,
        string captureId) =>
        new[] { ".capture.json", ".capture.md" }
            .Select(extension => Path.Combine(
                outputPath,
                $"mem-v010-{captureId}{extension}"))
            .Any(path => File.Exists(path) || File.Exists(path + ".partial"));

    private static void DeleteReceiptFiles(
        string outputPath,
        string captureId)
    {
        foreach (var extension in new[] { ".capture.json", ".capture.md" })
        {
            var path = Path.Combine(
                outputPath,
                $"mem-v010-{captureId}{extension}");
            File.Delete(path);
            File.Delete(path + ".partial");
        }
    }

    private static string RenderMarkdown(CaptureReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# MEM migration source capture");
        builder.AppendLine();
        builder.AppendLine($"- Capture ID: `{report.CaptureId}`");
        builder.AppendLine($"- Source stack ID: `{report.SourceStackId:D}`");
        builder.AppendLine($"- Source stack: `{report.SourceStackSlug}`");
        builder.AppendLine($"- Matrix server: `{report.MatrixServerName}`");
        builder.AppendLine($"- Status: `{report.Status}`");
        builder.AppendLine($"- Rehearsal only: `{report.RehearsalOnly}`");
        builder.AppendLine($"- Source changed during capture: `{report.SourceChangedDuringCapture}`");
        builder.AppendLine($"- Start fingerprint: `{report.StartSourceFingerprint}`");
        builder.AppendLine($"- Completion fingerprint: `{report.CompletionSourceFingerprint}`");
        builder.AppendLine($"- Plain ZIP SHA-256: `{report.ArchiveSha256}`");

        if (!string.IsNullOrWhiteSpace(report.EncryptedArchivePath))
        {
            builder.AppendLine($"- Encrypted archive: `{report.EncryptedArchivePath}`");
            builder.AppendLine($"- Encrypted SHA-256: `{report.EncryptedArchiveSha256}`");
        }
        else
        {
            builder.AppendLine($"- Local archive: `{report.ArchivePath}`");
        }

        builder.AppendLine();
        builder.AppendLine("## Warnings");

        foreach (var warning in report.Warnings)
        {
            builder.AppendLine($"- {warning}");
        }

        builder.AppendLine();
        builder.AppendLine("## Next steps");

        foreach (var nextStep in report.NextSteps)
        {
            builder.AppendLine($"- {nextStep}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string[] BuildWarnings(
        bool sourceChanged,
        string? ageRecipient,
        bool finalFrozenCapture)
    {
        var warnings = new List<string>();
        if (finalFrozenCapture)
        {
            warnings.Add(
                "This is a final frozen-source capture. Keep all source writers stopped and restart-disabled until target activation succeeds or rollback is explicitly performed.");
        }
        else
        {
            warnings.Add(
                "This is a live preview capture. It is rehearsal-only and must never be treated as a final cutover capture.");
            warnings.Add(
                "The source containers were not stopped or restarted. Media may have changed while it was copied.");
        }

        if (sourceChanged)
        {
            warnings.Add(
                "The source fingerprint changed during capture. Review source activity and repeat the capture if a stable comparison is required.");
        }

        if (string.IsNullOrWhiteSpace(ageRecipient))
        {
            warnings.Add(
                "The archive is plaintext and may remain only in the private local migration workspace. Cross-server transfer requires an age recipient.");
        }

        return warnings.ToArray();
    }

    private static string[] BuildNextSteps(bool encrypted, bool finalFrozenCapture)
    {
        if (finalFrozenCapture)
        {
            return encrypted
                ?
                [
                    "Transfer only the final .memmigration.zip.age artifact to the intended target.",
                    "Verify its encrypted SHA-256 receipt before decryption.",
                    "Run final conversion and private target verification while the source remains frozen.",
                    "Do not change public routing until final verification passes."
                ]
                :
                [
                    "Verify the final archive locally.",
                    "Encrypt it for the intended target before any cross-server transfer.",
                    "Run final conversion and private target verification while the source remains frozen.",
                    "Do not change public routing until final verification passes."
                ];
        }

        return encrypted
            ?
            [
                "Transfer only the .memmigration.zip.age artifact to the intended target.",
                "Compare the encrypted SHA-256 receipt before target-side decryption.",
                "Run source verify or target import with the matching age identity.",
                "Proceed to MM-04 conversion proof only after archive verification passes."
            ]
            :
            [
                "Inspect and verify the local archive.",
                "Create a target age recipient and repeat capture before cross-server transfer.",
                "Proceed to MM-04 conversion proof only after archive verification passes."
            ];
    }

    private static async Task ValidateFreezeReportAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The MM-06B freeze report was not found.", path);
        }

        await using var stream = File.OpenRead(path);
        var report = await JsonSerializer.DeserializeAsync<CutoverFreezeReport>(
            stream, CaptureJson.Options, cancellationToken);
        if (report is null ||
            !report.SourceFrozen ||
            report.PublicRoutingMutationOccurred ||
            !string.Equals(report.Status, "Frozen", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The supplied freeze report does not prove a completed private source freeze.");
        }

        if (report.Containers.Length == 0 ||
            report.Containers.Any(container =>
                !container.Stopped ||
                !container.RestartPolicyDisabled ||
                !string.Equals(container.FinalRestartPolicy, "no", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "The supplied freeze report does not prove every planned source writer is stopped and restart-disabled.");
        }
    }

    private static string ArchivePath(string relative) =>
        ArchivePathPolicy.Normalize(
            $"{ArchivePathPolicy.Root}/{relative}");
}
