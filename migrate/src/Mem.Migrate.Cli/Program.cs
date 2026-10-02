using System.Reflection;
using System.Text.Json;
using Mem.Migrate.Application.Assessment;
using Mem.Migrate.Core;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Conversion;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Reporting;
using Mem.Migrate.Core.Rehearsal;
using Mem.Migrate.Core.Qualification;
using Mem.Migrate.Core.Security;
using Mem.Migrate.Core.Target;
using Mem.Migrate.Core.Worker;
using Mem.Migrate.Infrastructure.Archive;
using Mem.Migrate.Infrastructure.Docker;
using Mem.Migrate.Infrastructure.Conversion;
using Mem.Migrate.Infrastructure.Encryption;
using Mem.Migrate.Infrastructure.FileSystem;
using Mem.Migrate.Infrastructure.Host;
using Mem.Migrate.Infrastructure.Http;
using Mem.Migrate.Infrastructure.Persistence;
using Mem.Migrate.Infrastructure.Postgres;
using Mem.Migrate.Infrastructure.Rehearsal;
using Mem.Migrate.Infrastructure.Qualification;
using Mem.Migrate.Infrastructure.Processes;
using Mem.Migrate.Infrastructure.Sqlite;
using Mem.Migrate.Infrastructure.Target;
using Mem.Migrate.Legacy.V010;
using Mem.Migrate.Legacy.V010.Capture;
using Mem.Migrate.Legacy.V010.Cutover;
using Mem.Migrate.Legacy.V010.Qualification;

namespace Mem.Migrate.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        ParsedCommand command;

        try
        {
            command = CommandLineParser.Parse(args);
        }
        catch (CommandLineException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            Console.Error.WriteLine();
            PrintUsage();
            return ExitCodes.InvalidArguments;
        }

        if (command.ShowHelp || command.Name == "help")
        {
            PrintUsage();
            return ExitCodes.Success;
        }

        if (command.Name == "version")
        {
            Console.WriteLine(GetVersion());
            return ExitCodes.Success;
        }

        try
        {
            return command.Name switch
            {
                "assess" => await RunAssessmentAsync(
                    command.AssessmentOptions,
                    cancellation.Token),
                "report" => await RunReportAsync(
                    command.AssessmentOptions,
                    cancellation.Token),
                "source-capture" => await RunCaptureAsync(
                    command.CaptureOptions
                        ?? throw new InvalidOperationException(
                            "Capture options were not parsed."),
                    cancellation.Token),
                "source-verify" => await RunVerifyAsync(
                    command.ArchiveOptions
                        ?? throw new InvalidOperationException(
                            "Archive options were not parsed."),
                    cancellation.Token),
                "source-inspect" => await RunInspectAsync(
                    command.ArchiveOptions
                        ?? throw new InvalidOperationException(
                            "Archive options were not parsed."),
                    cancellation.Token),
                "source-package-for-intake" => await RunPackageForIntakeAsync(
                    command.PackageForIntakeOptions
                        ?? throw new InvalidOperationException(
                            "Package-for-intake options were not parsed."),
                    cancellation.Token),
                "target-convert" => await RunConversionAsync(
                    command.ConversionOptions
                        ?? throw new InvalidOperationException(
                            "Conversion options were not parsed."),
                    cancellation.Token),
                "worker-convert" => await RunConversionWorkerAsync(
                    command.ConversionWorkerRequestPath
                        ?? throw new InvalidOperationException(
                            "Conversion worker request path was not parsed."),
                    cancellation.Token),
                "target-verify-candidate" => await RunCandidateAsync(
                    command.CandidateOptions
                        ?? throw new InvalidOperationException(
                            "Candidate options were not parsed."),
                    cancellation.Token),
                "target-export-stack-artifact" => await RunRehearsalArtifactAsync(
                    command.RehearsalArtifactOptions
                        ?? throw new InvalidOperationException(
                            "Rehearsal artifact options were not parsed."),
                    cancellation.Token),
                "target-import" => await RunTargetImportAsync(
                    command.TargetImportOptions
                        ?? throw new InvalidOperationException(
                            "Target import options were not parsed."),
                    cancellation.Token),
                "target-stage-private" => await RunTargetPrivateStageAsync(
                    command.TargetPrivateStageOptions
                        ?? throw new InvalidOperationException(
                            "Target private staging options were not parsed."),
                    cancellation.Token),
                "cutover-prepare" => await RunCutoverPrepareAsync(
                    command.CutoverPrepareOptions
                        ?? throw new InvalidOperationException(
                            "Cutover preparation options were not parsed."),
                    cancellation.Token),
                "cutover-freeze-source" => await RunCutoverFreezeAsync(
                    command.CutoverFreezeOptions
                        ?? throw new InvalidOperationException(
                            "Cutover source-freeze options were not parsed."),
                    cancellation.Token),
                "cutover-rollback-source" => await RunSourceRestorationAsync(
                    command.SourceRestorationOptions
                        ?? throw new InvalidOperationException(
                            "Cutover source-restoration options were not parsed."),
                    cancellation.Token),
                "cutover-stage-final" => await RunFinalTargetStageAsync(
                    command.FinalTargetStageOptions
                        ?? throw new InvalidOperationException(
                            "Final target stage options were not parsed."),
                    cancellation.Token),
                "cutover-activation-readiness" => await RunActivationReadinessAsync(
                    command.ActivationReadinessOptions
                        ?? throw new InvalidOperationException(
                            "Activation readiness options were not parsed."),
                    cancellation.Token),
                "qualification-source-evidence" => await RunSourceQualificationAsync(
                    command.SourceQualificationOptions
                        ?? throw new InvalidOperationException(
                            "Source qualification options were not parsed."),
                    cancellation.Token),
                _ => ExitCodes.InvalidArguments
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return ExitCodes.ExecutionFailure;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return ExitCodes.InvalidArguments;
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return command.Name is "source-verify" or "source-inspect"
                ? ExitCodes.InvalidArchive
                : ExitCodes.ExecutionFailure;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return ExitCodes.ExecutionFailure;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return command.Name is "source-verify" or "source-inspect"
                ? ExitCodes.InvalidArchive
                : ExitCodes.ExecutionFailure;
        }
        catch (Exception)
        {
            Console.Error.WriteLine(command.Name == "cutover-rollback-source"
                ? "ERROR: An unexpected source-restoration failure occurred. Restoration may be partially complete; inspect the durable journal and retry with --resume."
                : "ERROR: An unexpected mem-migrate failure occurred. Source mutation was not requested.");
            return ExitCodes.ExecutionFailure;
        }
    }

    private static async Task<int> RunAssessmentAsync(
        AssessmentOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        SourceWorkspaceLocator.TryRemember(normalized.WorkspacePath);
        var service = CreateAssessmentService(normalized);

        if (!normalized.JsonConsoleOutput)
        {
            Console.Error.WriteLine(
                "Running read-only MEM v0.1.0 assessment...");
        }

        var execution = await service.RunAsync(
            normalized,
            cancellationToken);

        if (normalized.JsonConsoleOutput)
        {
            Console.WriteLine(execution.Reports.PublicJson);
        }
        else
        {
            Console.WriteLine(
                $"Classification: {execution.Result.Classification}");
            Console.WriteLine(
                $"Recommendation: {execution.Result.Recommendation}");
            Console.WriteLine(
                $"Capture allowed: {execution.Result.CanProceedToCapture}");
            Console.WriteLine(
                $"Source fingerprint: {execution.Result.SourceFingerprint}");
            Console.WriteLine(
                $"Report: {Path.Combine(normalized.OutputPath, "assessment.md")}");
            Console.WriteLine(
                $"JSON: {Path.Combine(normalized.OutputPath, "assessment.json")}");
            Console.WriteLine(
                $"Private inventory: {Path.Combine(normalized.WorkspacePath, "assessment.private.json")}");
        }

        return MapExitCode(execution.Result);
    }

    private static async Task<int> RunReportAsync(
        AssessmentOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        var journal = new SqliteAssessmentJournal(
            normalized.WorkspacePath);
        await journal.InitializeAsync(cancellationToken);

        var report = await journal.GetLatestAsync(cancellationToken);

        if (report is null)
        {
            Console.Error.WriteLine(
                "No assessment exists in the selected workspace.");
            return ExitCodes.ExecutionFailure;
        }

        Console.WriteLine(
            normalized.JsonConsoleOutput
                ? report.PublicJson
                : report.PublicMarkdown);

        return report.Classification switch
        {
            AssessmentClassification.ConfirmedSupportedV010 =>
                ExitCodes.Success,
            AssessmentClassification.ProbableV010 or
            AssessmentClassification.PartialRepairableV010 or
            AssessmentClassification.Blocked =>
                ExitCodes.Blocked,
            AssessmentClassification.AmbiguousMultipleInstallations =>
                ExitCodes.Ambiguous,
            AssessmentClassification.CurrentV011Present =>
                ExitCodes.CurrentTargetAlreadyPresent,
            AssessmentClassification.UnsupportedSource =>
                ExitCodes.Unsupported,
            _ => ExitCodes.Blocked
        };
    }

    private static async Task<int> RunCaptureAsync(
        CaptureOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = (options with
        {
            ProducerVersion = GetVersion()
        }).Normalize();
        SourceWorkspaceLocator.TryRemember(normalized.Assessment.WorkspacePath);
        var processRunner = new ProcessRunner();
        var assessmentService = CreateAssessmentService(
            normalized.Assessment,
            processRunner);
        var service = new V010SourceCaptureService(
            assessmentService,
            new SourceCaptureFileSystem(),
            new SqliteSnapshotter(),
            new MigrationArchiveWriter(),
            new MigrationArchiveReader(),
            new AgeEnvelope(processRunner),
            new SqliteCaptureJournal(
                normalized.Assessment.WorkspacePath));

        if (!normalized.JsonConsoleOutput)
        {
            Console.Error.WriteLine(normalized.FinalFrozenCapture
                ? "Creating final capture from the MM-06B frozen source. Public routing will not be changed..."
                : "Creating read-only live rehearsal capture. Source services will not be stopped or restarted...");
        }

        var report = await service.CaptureAsync(
            normalized,
            cancellationToken);

        if (normalized.JsonConsoleOutput)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(report, CaptureJson.Options));
        }
        else
        {
            Console.WriteLine($"Capture ID: {report.CaptureId}");
            Console.WriteLine($"Status: {report.Status}");
            Console.WriteLine($"Rehearsal only: {report.RehearsalOnly}");
            Console.WriteLine(
                $"Source changed during capture: {report.SourceChangedDuringCapture}");
            Console.WriteLine(
                $"Source fingerprint: {report.StartSourceFingerprint}");
            Console.WriteLine(
                $"Plain ZIP SHA-256: {report.ArchiveSha256}");

            if (!string.IsNullOrWhiteSpace(report.EncryptedArchivePath))
            {
                Console.WriteLine(
                    $"Encrypted archive: {report.EncryptedArchivePath}");
                Console.WriteLine(
                    $"Encrypted SHA-256: {report.EncryptedArchiveSha256}");
            }
            else
            {
                Console.WriteLine($"Local archive: {report.ArchivePath}");
            }

            foreach (var warning in report.Warnings)
            {
                Console.Error.WriteLine($"WARNING: {warning}");
            }
        }

        return report.SourceChangedDuringCapture || report.Warnings.Length > 2
            ? ExitCodes.SupportedWithWarnings
            : ExitCodes.Success;
    }



    private static async Task<int> RunPackageForIntakeAsync(
        PackageForIntakeOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        if (string.IsNullOrWhiteSpace(normalized.ArchivePath))
        {
            var workspacePath = SourceWorkspaceLocator.Resolve(
                normalized.WorkspacePath);
            var discovery = new SourceCaptureArchiveDiscovery(workspacePath);
            normalized = normalized with
            {
                ArchivePath = await discovery.ResolveSingleEligibleArchiveAsync(
                    cancellationToken,
                    normalized.RequireFinalFrozen)
            };
        }

        var service = new PackageForIntakeService(
            new MigrationArchiveReader(),
            new AgeEnvelope(new ProcessRunner()));

        if (!normalized.JsonConsoleOutput)
        {
            Console.Error.WriteLine(
                normalized.RequireFinalFrozen
                    ? "Verifying the final frozen source capture and encrypting it for the target package revision..."
                    : "Verifying the selected source capture and encrypting it for the target secure intake...");
            Console.Error.WriteLine(
                $"Selected source archive: {normalized.ArchivePath}");
            Console.Error.WriteLine(
                $"Output directory: {normalized.OutputDirectory}");
        }

        PackageForIntakeReport report;
        try
        {
            report = await service.PackageAsync(normalized, cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            Console.Error.WriteLine($"ERROR: {exception.Message}");
            return ExitCodes.InvalidArchive;
        }

        if (normalized.JsonConsoleOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, CaptureJson.Options));
        }
        else
        {
            Console.WriteLine($"Intake ID: {report.IntakeId}");
            if (!string.IsNullOrWhiteSpace(report.PackageRevisionId))
            {
                Console.WriteLine($"Package revision ID: {report.PackageRevisionId}");
            }
            Console.WriteLine($"Requested capture kind: {report.RequestedCaptureKind}");
            Console.WriteLine($"Recipient fingerprint: {report.RecipientFingerprint}");
            Console.WriteLine($"Migration ID: {report.MigrationId}");
            Console.WriteLine($"Capture kind: {report.CaptureKind}");
            Console.WriteLine($"Source frozen: {report.SourceFrozen}");
            Console.WriteLine($"Rehearsal only: {report.RehearsalOnly}");
            Console.WriteLine($"Stacks: {report.StackCount}");
            Console.WriteLine($"Source archive SHA-256: {report.SourceArchiveSha256}");
            Console.WriteLine($"Verified files: {report.VerifiedFileCount}");
            Console.WriteLine($"Encrypted package: {report.EncryptedPackagePath}");
            Console.WriteLine($"Encrypted package SHA-256: {report.EncryptedPackageSha256}");
            Console.WriteLine($"Encrypted package bytes: {report.EncryptedPackageBytes}");
            Console.WriteLine($"JSON report: {report.JsonReportPath}");
            Console.WriteLine($"Markdown report: {report.MarkdownReportPath}");
        }

        return ExitCodes.Success;
    }

    private static async Task<int> RunConversionWorkerAsync(
        string requestPath,
        CancellationToken cancellationToken)
    {
        ConversionWorkerRequest? request = null;
        long sequence = 0;

        try
        {
            request = await ConversionWorkerProtocol.ReadRequestAsync(
                requestPath, cancellationToken);
            var options = request.ToConversionOptions();

            await ConversionWorkerProtocol.WriteEventAsync(
                Console.Out,
                ConversionWorkerProtocol.CreateEvent(
                    ++sequence, ConversionWorkerEventType.OperationStarted,
                    request.OperationId, "conversion", "running",
                    "Conversion worker accepted the versioned request.",
                    new Dictionary<string, string?>
                    {
                        ["conversionId"] = options.ConversionId,
                        ["archivePath"] = options.ArchivePath
                    }),
                cancellationToken);

            await ConversionWorkerProtocol.WriteEventAsync(
                Console.Out,
                ConversionWorkerProtocol.CreateEvent(
                    ++sequence, ConversionWorkerEventType.StepStarted,
                    request.OperationId, "synapse-sqlite-to-postgres", "running",
                    "Starting the existing isolated Synapse conversion engine."),
                cancellationToken);

            var processRunner = new ProcessRunner();
            var archiveReader = new MigrationArchiveReader();
            var service = new SynapseConversionService(
                archiveReader,
                new VerifiedArchiveExtractor(archiveReader, options.ToSafetyLimits()),
                processRunner,
                new BinaryProcessRunner(),
                new SqliteConversionJournal(options.WorkspacePath));
            var report = await service.ConvertAsync(options, cancellationToken);
            var reportPath = Path.Combine(
                options.OutputPath, options.ConversionId!, "conversion-report.json");

            await ConversionWorkerProtocol.WriteEventAsync(
                Console.Out,
                ConversionWorkerProtocol.CreateEvent(
                    ++sequence, ConversionWorkerEventType.StepCompleted,
                    request.OperationId, "synapse-sqlite-to-postgres", "completed",
                    "The isolated Synapse conversion completed and reconciled table counts.",
                    new Dictionary<string, string?>
                    {
                        ["conversionId"] = report.ConversionId,
                        ["postgresqlDumpSha256"] = report.PostgreSqlDumpSha256,
                        ["evidencePath"] = report.EvidencePath
                    }),
                cancellationToken);

            await ConversionWorkerProtocol.WriteEventAsync(
                Console.Out,
                ConversionWorkerProtocol.CreateEvent(
                    ++sequence, ConversionWorkerEventType.OperationCompleted,
                    request.OperationId, "conversion", "completed",
                    "The conversion worker completed successfully.",
                    new Dictionary<string, string?>
                    {
                        ["conversionId"] = report.ConversionId,
                        ["reportPath"] = reportPath,
                        ["archiveSha256"] = report.ArchiveSha256,
                        ["postgresqlDumpPath"] = report.PostgreSqlDumpPath,
                        ["postgresqlDumpSha256"] = report.PostgreSqlDumpSha256,
                        ["warningCount"] = report.Warnings.Length.ToString()
                    }),
                cancellationToken);

            return report.Warnings.Length > 0
                ? ExitCodes.SupportedWithWarnings
                : ExitCodes.Success;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or InvalidOperationException or IOException)
        {
            var operationId = request?.OperationId ?? "request-unavailable";
            await ConversionWorkerProtocol.WriteEventAsync(
                Console.Out,
                ConversionWorkerProtocol.CreateEvent(
                    ++sequence, ConversionWorkerEventType.OperationFailed,
                    operationId, "conversion", "failed",
                    ex.Message,
                    new Dictionary<string, string?>
                    {
                        ["errorType"] = ex.GetType().Name
                    }),
                CancellationToken.None);

            return ex is InvalidDataException
                ? ExitCodes.InvalidArchive
                : ex is ArgumentException
                    ? ExitCodes.InvalidArguments
                    : ExitCodes.ExecutionFailure;
        }
    }

    private static async Task<int> RunConversionAsync(
        ConversionOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        var processRunner = new ProcessRunner();
        var archiveReader = new MigrationArchiveReader();
        var service = new SynapseConversionService(
            archiveReader,
            new VerifiedArchiveExtractor(
                archiveReader,
                normalized.ToSafetyLimits()),
            processRunner,
            new BinaryProcessRunner(),
            new SqliteConversionJournal(normalized.WorkspacePath));

        if (!normalized.JsonConsoleOutput)
        {
            Console.Error.WriteLine(
                "Converting a verified MM-03 Synapse SQLite snapshot in an isolated private Docker network...");
        }

        var report = await service.ConvertAsync(normalized, cancellationToken);
        if (normalized.JsonConsoleOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, CaptureJson.Options));
        }
        else
        {
            Console.WriteLine($"Conversion ID: {report.ConversionId}");
            Console.WriteLine($"Status: {report.Status}");
            Console.WriteLine($"Migration ID: {report.MigrationId}");
            Console.WriteLine($"Stack ID: {report.SourceStackId}");
            Console.WriteLine($"Matrix server name: {report.MatrixServerName}");
            Console.WriteLine($"Synapse image: {report.SynapseImage}");
            Console.WriteLine($"PostgreSQL image: {report.PostgresImage}");
            Console.WriteLine($"PostgreSQL dump: {report.PostgreSqlDumpPath}");
            Console.WriteLine($"PostgreSQL dump SHA-256: {report.PostgreSqlDumpSha256}");
            Console.WriteLine($"Evidence: {report.EvidencePath}");
            Console.WriteLine($"Temporary resources retained: {report.ResourcesRetained}");
            foreach (var item in report.TableCounts)
            {
                Console.WriteLine($"Rows {item.Table}: {item.SourceRows} -> {item.TargetRows}");
            }
            foreach (var warning in report.Warnings)
            {
                Console.Error.WriteLine($"WARNING: {warning}");
            }
        }

        return report.Warnings.Length > 0
            ? ExitCodes.SupportedWithWarnings
            : ExitCodes.Success;
    }

    private static async Task<int> RunCandidateAsync(
        CandidateOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        var processRunner = new ProcessRunner();
        var archiveReader = new MigrationArchiveReader();
        var service = new PrivateCandidateService(
            archiveReader,
            new VerifiedArchiveExtractor(archiveReader, normalized.ToSafetyLimits()),
            processRunner);

        if (!normalized.JsonConsoleOutput)
        {
            Console.Error.WriteLine(
                "Restoring the MM-04A PostgreSQL dump and starting an isolated private Synapse candidate...");
        }

        var report = await service.VerifyAsync(normalized, cancellationToken);
        if (normalized.JsonConsoleOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, CaptureJson.Options));
        }
        else
        {
            Console.WriteLine($"Candidate ID: {report.CandidateId}");
            Console.WriteLine($"Status: {report.Status}");
            Console.WriteLine($"Go: {report.Go}");
            Console.WriteLine($"Conversion ID: {report.ConversionId}");
            Console.WriteLine($"Matrix server name: {report.MatrixServerName}");
            Console.WriteLine($"Reported server name: {report.Identity.ReportedServerName}");
            Console.WriteLine($"Signing key ID: {report.Identity.SigningKeyId}");
            Console.WriteLine($"Published ports absent: {report.Go}");
            Console.WriteLine($"Evidence: {report.EvidencePath}");
            Console.WriteLine($"Candidate log: {report.CandidateLogPath}");
            Console.WriteLine($"Temporary resources retained: {report.ResourcesRetained}");
            foreach (var item in report.TableCounts)
                Console.WriteLine($"Rows {item.Table}: {item.SourceRows} -> {item.TargetRows}");
            foreach (var warning in report.Warnings)
                Console.Error.WriteLine($"WARNING: {warning}");
        }

        if (!report.Go) return ExitCodes.ExecutionFailure;
        return report.Warnings.Length > 0 ? ExitCodes.SupportedWithWarnings : ExitCodes.Success;
    }

    private static async Task<int> RunRehearsalArtifactAsync(
        RehearsalArtifactOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        if (!normalized.JsonConsoleOutput)
            Console.Error.WriteLine("Emitting a standard MEM stack export and neutral migration intake manifest...");

        var report = await new RehearsalArtifactService(
                new MigrationArchiveReader(),
                new BinaryProcessRunner())
            .ExportAsync(normalized, cancellationToken);

        if (normalized.JsonConsoleOutput)
            Console.WriteLine(JsonSerializer.Serialize(report, CaptureJson.Options));
        else
        {
            Console.WriteLine($"Artifact ID: {report.ArtifactId}");
            Console.WriteLine($"Migration ID: {report.MigrationId}");
            Console.WriteLine($"Conversion ID: {report.ConversionId}");
            Console.WriteLine($"Matrix server name: {report.MatrixServerName}");
            Console.WriteLine($"Stack export: {report.StackExportPath}");
            Console.WriteLine($"Stack export SHA-256: {report.StackExportSha256}");
            Console.WriteLine($"Neutral manifest: {report.NeutralManifestPath}");
            Console.WriteLine($"Neutral manifest SHA-256: {report.NeutralManifestSha256}");
            Console.WriteLine($"Media: {report.MediaFiles} files, {report.MediaBytes} bytes");
            foreach (var warning in report.Warnings) Console.Error.WriteLine($"WARNING: {warning}");
        }
        return report.Warnings.Length > 0 ? ExitCodes.SupportedWithWarnings : ExitCodes.Success;
    }


    private static async Task<int> RunCutoverFreezeAsync(
        CutoverFreezeOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        var processRunner = new ProcessRunner();
        var service = new V010CutoverFreezeService(
            CreateAssessmentService(normalized.Assessment, processRunner),
            new DockerCutoverSourceFreezer(processRunner),
            new SqliteCutoverFreezeJournal(normalized.WorkspacePath));

        if (!normalized.JsonConsoleOutput)
        {
            Console.Error.WriteLine(
                "Freezing reviewed MEM v0.1.0 source writers. Public routing will not be changed...");
        }

        var report = await service.FreezeAsync(normalized, cancellationToken);
        if (normalized.JsonConsoleOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, CaptureJson.Options));
        }
        else
        {
            Console.WriteLine($"Freeze attempt ID: {report.FreezeAttemptId}");
            Console.WriteLine($"Status: {report.Status}");
            Console.WriteLine($"Plan ID: {report.PlanId}");
            Console.WriteLine($"Plan hash: {report.PlanHash}");
            Console.WriteLine($"Source fingerprint: {report.SourceFingerprint}");
            Console.WriteLine($"Source frozen: {report.SourceFrozen}");
            Console.WriteLine($"Frozen containers: {report.Containers.Length}");
            Console.WriteLine($"Public routing mutation occurred: {report.PublicRoutingMutationOccurred}");
            Console.WriteLine($"JSON: {report.JsonPath}");
            Console.WriteLine($"Markdown: {report.MarkdownPath}");
            foreach (var warning in report.Warnings)
            {
                Console.Error.WriteLine($"WARNING: {warning}");
            }
        }

        return ExitCodes.Success;
    }

    private static async Task<int> RunSourceRestorationAsync(
        SourceRestorationOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        var processRunner = new ProcessRunner();
        var service = new V010SourceRestorationService(
            new DockerCutoverSourceRestorer(processRunner),
            new SqliteSourceRestorationJournal(normalized.WorkspacePath));

        if (!normalized.JsonConsoleOutput)
        {
            Console.Error.WriteLine(
                "Restoring the exact frozen MEM v0.1.0 source from target rollback authority...");
        }

        var report = await service.RestoreAsync(normalized, cancellationToken);
        if (normalized.JsonConsoleOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, CaptureJson.Options));
        }
        else
        {
            Console.WriteLine($"Restoration attempt ID: {report.RestorationAttemptId}");
            Console.WriteLine($"Status: {report.Status}");
            Console.WriteLine($"Source handoff ID: {report.SourceHandoffId}");
            Console.WriteLine($"Target rollback execution ID: {report.TargetRollbackExecutionId}");
            Console.WriteLine($"Freeze attempt ID: {report.FreezeAttemptId}");
            Console.WriteLine($"Source fingerprint: {report.SourceFingerprint}");
            Console.WriteLine($"Source restored: {report.SourceRestored}");
            Console.WriteLine($"Matrix verified: {report.MatrixVerified}");
            Console.WriteLine($"Element verified: {report.ElementVerified}");
            Console.WriteLine($"Completion evidence: {report.CompletionEvidencePath}");
            Console.WriteLine($"Completion evidence SHA-256: {report.CompletionEvidenceSha256}");
            Console.WriteLine($"JSON: {report.JsonPath}");
            Console.WriteLine($"Markdown: {report.MarkdownPath}");
            foreach (var warning in report.Warnings)
            {
                Console.Error.WriteLine($"WARNING: {warning}");
            }
        }

        return report.Warnings.Length > 0
            ? ExitCodes.SupportedWithWarnings
            : ExitCodes.Success;
    }

    private static async Task<int> RunSourceQualificationAsync(
        SourceQualificationOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        var service = new V010SourceQualificationService(
            new SourceQualificationEnvironmentProbe(new ProcessRunner()));

        if (!normalized.JsonConsoleOutput)
        {
            Console.Error.WriteLine(
                "Collecting hash-bound source evidence for supported two-server production qualification...");
        }

        var report = await service.RunAsync(normalized, cancellationToken);
        if (normalized.JsonConsoleOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, CaptureJson.Options));
        }
        else
        {
            Console.WriteLine($"Qualification attempt ID: {report.QualificationAttemptId}");
            Console.WriteLine($"Status: {report.Status}");
            Console.WriteLine($"Migration ID: {report.MigrationId}");
            Console.WriteLine($"Package revision ID: {report.PackageRevisionId}");
            Console.WriteLine($"Encrypted package SHA-256: {report.EncryptedPackageSha256}");
            Console.WriteLine($"Freeze attempt ID: {report.FreezeAttemptId}");
            Console.WriteLine($"Source fingerprint: {report.SourceFingerprint}");
            Console.WriteLine($"Machine ID SHA-256: {report.SourceHost.MachineIdSha256}");
            Console.WriteLine($"Docker Engine ID SHA-256: {report.SourceHost.DockerEngineIdSha256}");
            Console.WriteLine($"Source remains frozen: {report.SourceFrozen}");
            Console.WriteLine($"Evidence: {report.EvidencePath}");
            Console.WriteLine($"Evidence SHA-256: {report.EvidenceSha256}");
            Console.WriteLine($"JSON: {report.JsonPath}");
            Console.WriteLine($"Markdown: {report.MarkdownPath}");
        }

        return ExitCodes.Success;
    }

    private static async Task<int> RunCutoverPrepareAsync(
        CutoverPrepareOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = (options with
        {
            ProducerVersion = GetVersion()
        }).Normalize();
        var service = new V010CutoverPreparationService(
            CreateAssessmentService(normalized.Assessment),
            new SqliteTargetPrivateStageJournal(normalized.TargetWorkspacePath!),
            new SqliteCutoverPlanJournal(normalized.WorkspacePath));

        if (!normalized.JsonConsoleOutput)
        {
            Console.Error.WriteLine(
                "Preparing a non-mutating reviewed cutover plan from fresh source assessment and completed MM-05D evidence...");
        }

        var report = await service.PrepareAsync(
            normalized,
            cancellationToken);
        if (normalized.JsonConsoleOutput)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(report, CaptureJson.Options));
        }
        else
        {
            Console.WriteLine($"Plan ID: {report.Plan.PlanId}");
            Console.WriteLine($"Status: {report.Status}");
            Console.WriteLine($"Plan hash: {report.PlanHash}");
            Console.WriteLine($"Valid until: {report.Plan.ValidUntilUtc:O}");
            Console.WriteLine($"Source fingerprint: {report.Plan.SourceFingerprint}");
            Console.WriteLine($"Source containers to freeze: {report.Plan.SourceContainersToFreeze.Length}");
            Console.WriteLine($"Development external control plane: {report.Plan.DevelopmentExternalControlPlane}");
            Console.WriteLine($"Stacks: {report.Plan.Stacks.Length}");
            Console.WriteLine($"Target stage attempt: {report.Plan.TargetEvidence.StageAttemptId}");
            Console.WriteLine($"Source mutation occurred: {report.Plan.SourceMutationOccurred}");
            Console.WriteLine($"Public routing mutation occurred: {report.Plan.PublicRoutingMutationOccurred}");
            Console.WriteLine($"JSON: {report.JsonPath}");
            Console.WriteLine($"Markdown: {report.MarkdownPath}");
            foreach (var warning in report.Plan.Warnings)
            {
                Console.Error.WriteLine($"WARNING: {warning}");
            }
        }

        return ExitCodes.Success;
    }


    private static async Task<int> RunActivationReadinessAsync(
        ActivationReadinessOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        using var handler = new HttpClientHandler();
        if (normalized.AllowInsecureTls)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        using var client = new HttpClient(handler);
        var service = new ActivationReadinessService(client, new MemCliTargetProfileCredentialResolver());
        if (!normalized.JsonConsoleOutput)
            Console.Error.WriteLine("Checking final migration evidence against target pre-cutover readiness. No public mutation will be performed...");
        var report = await service.RunAsync(normalized, cancellationToken);
        if (normalized.JsonConsoleOutput) Console.WriteLine(JsonSerializer.Serialize(report, CaptureJson.Options));
        else
        {
            Console.WriteLine($"Attempt ID: {report.AttemptId}");
            Console.WriteLine($"Status: {report.Status}");
            Console.WriteLine($"Catalog entry ID: {report.CatalogEntryId}");
            Console.WriteLine($"Candidate ID: {report.CandidateId}");
            Console.WriteLine($"Preview ID: {report.PreviewId}");
            Console.WriteLine($"Confirmation ID: {report.ConfirmationId}");
            Console.WriteLine($"Source frozen: {report.SourceFrozen}");
            Console.WriteLine($"Final candidate retained: {report.FinalCandidateRetained}");
            Console.WriteLine($"Execution ready: {report.ExecutionReady}");
            Console.WriteLine($"Report: {report.ReportPath}");
            foreach (var blocker in report.Blockers) Console.Error.WriteLine($"BLOCKER: {blocker}");
            foreach (var warning in report.Warnings) Console.Error.WriteLine($"WARNING: {warning}");
        }
        return report.ExecutionReady ? (report.Warnings.Length > 0 ? ExitCodes.SupportedWithWarnings : ExitCodes.Success) : ExitCodes.SupportedWithWarnings;
    }

    private static async Task<int> RunFinalTargetStageAsync(
        FinalTargetStageOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        var archiveReader = new MigrationArchiveReader();
        var processRunner = new ProcessRunner();
        var conversion = new SynapseConversionService(
            archiveReader,
            new VerifiedArchiveExtractor(archiveReader, normalized.ToSafetyLimits()),
            processRunner,
            new BinaryProcessRunner(),
            new SqliteConversionJournal(normalized.WorkspacePath));
        var artifact = new RehearsalArtifactService(archiveReader, new BinaryProcessRunner());
        using var importHandler = new HttpClientHandler();
        using var stageHandler = new HttpClientHandler();
        if (normalized.AllowInsecureTls)
        {
            importHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            stageHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        using var importClient = new HttpClient(importHandler);
        using var stageClient = new HttpClient(stageHandler);
        var resolver = new MemCliTargetProfileCredentialResolver();
        var service = new FinalTargetStageService(
            archiveReader, conversion, artifact,
            new TargetImportService(importClient, new SqliteTargetImportJournal(normalized.WorkspacePath), resolver),
            new TargetPrivateStageService(stageClient, new SqliteTargetImportJournal(normalized.WorkspacePath),
                new SqliteTargetPrivateStageJournal(normalized.WorkspacePath), resolver));

        if (!normalized.JsonConsoleOutput)
            Console.Error.WriteLine("Converting the final frozen archive, importing it, and retaining a healthy private final target candidate...");
        var report = await service.RunAsync(normalized, cancellationToken);
        if (normalized.JsonConsoleOutput) Console.WriteLine(JsonSerializer.Serialize(report, CaptureJson.Options));
        else
        {
            Console.WriteLine($"Attempt ID: {report.AttemptId}");
            Console.WriteLine($"Status: {report.Status}");
            Console.WriteLine($"Final archive: {report.FinalArchive}");
            Console.WriteLine($"Source frozen: {report.SourceFrozen}");
            Console.WriteLine($"Catalog entry ID: {report.CatalogEntryId}");
            Console.WriteLine($"Restore session ID: {report.RestoreSessionId}");
            Console.WriteLine($"Staging ID: {report.StagingId}");
            Console.WriteLine($"Private only: {report.PrivateOnly}");
            Console.WriteLine($"Published routes absent: {report.PublishedRoutesAbsent}");
            Console.WriteLine($"Candidate retained: {report.CandidateRetained}");
            Console.WriteLine($"Report: {report.ReportPath}");
            foreach (var warning in report.Warnings) Console.Error.WriteLine($"WARNING: {warning}");
        }
        return report.Warnings.Length > 0 ? ExitCodes.SupportedWithWarnings : ExitCodes.Success;
    }

    private static async Task<int> RunTargetPrivateStageAsync(
        TargetPrivateStageOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        using var handler = new HttpClientHandler();
        if (normalized.AllowInsecureTls)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        using var client = new HttpClient(handler);
        var service = new TargetPrivateStageService(
            client,
            new SqliteTargetImportJournal(normalized.WorkspacePath),
            new SqliteTargetPrivateStageJournal(normalized.WorkspacePath),
            new MemCliTargetProfileCredentialResolver());

        if (!normalized.JsonConsoleOutput)
        {
            Console.Error.WriteLine(
                "Creating a catalog-backed private target staging runtime and collecting verification evidence...");
        }

        var report = await service.RunAsync(normalized, cancellationToken);
        if (normalized.JsonConsoleOutput)
        {
            Console.WriteLine(JsonSerializer.Serialize(report, CaptureJson.Options));
        }
        else
        {
            Console.WriteLine($"Stage attempt ID: {report.StageAttemptId}");
            Console.WriteLine($"Status: {report.Status}");
            Console.WriteLine($"Profile: {report.ProfileName}");
            Console.WriteLine($"Target: {report.TargetBaseUrl}");
            Console.WriteLine($"Catalog entry ID: {report.CatalogEntryId}");
            Console.WriteLine($"Restore session ID: {report.RestoreSessionId}");
            Console.WriteLine($"Staging ID: {report.StagingId}");
            Console.WriteLine($"Private only: {report.PrivateOnly}");
            Console.WriteLine($"Database import succeeded: {report.DatabaseImportSucceeded}");
            Console.WriteLine($"Synapse health passed: {report.SynapseHealthPassed}");
            Console.WriteLine($"Published routes absent: {report.PublishedRoutesAbsent}");
            Console.WriteLine($"Destroyed after verification: {report.DestroySucceeded}");
            Console.WriteLine($"Evidence: {report.WorkspaceEvidencePath}");
            foreach (var warning in report.Warnings)
                Console.Error.WriteLine($"WARNING: {warning}");
        }

        return report.Warnings.Length > 0
            ? ExitCodes.SupportedWithWarnings
            : ExitCodes.Success;
    }

    private static async Task<int> RunTargetImportAsync(
        TargetImportOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        using var handler = new HttpClientHandler();
        if (normalized.AllowInsecureTls)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        using var client = new HttpClient(handler);
        var service = new TargetImportService(
            client,
            new SqliteTargetImportJournal(normalized.WorkspacePath),
            new MemCliTargetProfileCredentialResolver());

        if (!normalized.JsonConsoleOutput)
            Console.Error.WriteLine("Importing the neutral manifest and MEM stack export into the private target control plane...");

        var report = await service.ImportAsync(normalized, cancellationToken);
        if (normalized.JsonConsoleOutput)
            Console.WriteLine(JsonSerializer.Serialize(report, CaptureJson.Options));
        else
        {
            Console.WriteLine($"Attempt ID: {report.AttemptId}");
            Console.WriteLine($"Status: {report.Status}");
            Console.WriteLine($"Profile: {report.TargetProfileName}");
            Console.WriteLine($"Target: {report.TargetBaseUrl}");
            Console.WriteLine($"Artifact ID: {report.ArtifactId}");
            Console.WriteLine($"Intake ID: {report.IntakeId}");
            Console.WriteLine($"Validation ID: {report.ValidationId}");
            Console.WriteLine($"Catalog entry ID: {report.CatalogEntryId}");
            Console.WriteLine($"Binding status: {report.BindingStatus}");
            Console.WriteLine($"SHA-256 matched: {string.Equals(report.ExpectedSha256, report.ActualSha256, StringComparison.OrdinalIgnoreCase)}");
            Console.WriteLine($"Bytes matched: {report.ExpectedBytes == report.ActualBytes}");
            foreach (var warning in report.Warnings) Console.Error.WriteLine($"WARNING: {warning}");
        }
        return report.Warnings.Length > 0 ? ExitCodes.SupportedWithWarnings : ExitCodes.Success;
    }

    private static async Task<int> RunVerifyAsync(
        ArchiveReadOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        var prepared = await PrepareArchiveAsync(
            normalized,
            cancellationToken);

        try
        {
            var result = await new MigrationArchiveReader().VerifyAsync(
                prepared.PlaintextPath,
                ToSafetyLimits(normalized),
                cancellationToken);
            result = result with
            {
                InputPath = normalized.ArchivePath,
                InputSha256 = prepared.InputSha256 ?? result.InputSha256,
                InputBytes = prepared.InputBytes ?? result.InputBytes,
                EncryptedInput = prepared.Encrypted
            };

            if (normalized.JsonConsoleOutput)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(result, CaptureJson.Options));
            }
            else
            {
                Console.WriteLine($"Valid: {result.Valid}");
                Console.WriteLine($"Input: {result.InputPath}");
                Console.WriteLine($"Input SHA-256: {result.InputSha256}");

                if (result.EncryptedInput)
                {
                    Console.WriteLine(
                        $"Verified ZIP SHA-256: {result.VerifiedZipSha256}");
                }
                Console.WriteLine(
                    $"Verified files: {result.VerifiedFileCount}");
                Console.WriteLine(
                    $"Verified expanded bytes: {result.VerifiedExpandedBytes}");

                foreach (var finding in result.Findings)
                {
                    Console.Error.WriteLine(
                        $"ERROR: {finding.Code}: {finding.Message}");
                }
            }

            return result.Valid
                ? ExitCodes.Success
                : ExitCodes.InvalidArchive;
        }
        finally
        {
            prepared.Dispose();
        }
    }

    private static async Task<int> RunInspectAsync(
        ArchiveReadOptions options,
        CancellationToken cancellationToken)
    {
        var normalized = options.Normalize();
        var prepared = await PrepareArchiveAsync(
            normalized,
            cancellationToken);

        try
        {
            var result = await new MigrationArchiveReader().InspectAsync(
                prepared.PlaintextPath,
                ToSafetyLimits(normalized),
                cancellationToken);
            result = result with
            {
                InputPath = normalized.ArchivePath,
                InputSha256 = prepared.InputSha256 ?? result.InputSha256,
                InputBytes = prepared.InputBytes ?? result.InputBytes,
                EncryptedInput = prepared.Encrypted
            };

            if (normalized.JsonConsoleOutput)
            {
                Console.WriteLine(
                    JsonSerializer.Serialize(result, CaptureJson.Options));
            }
            else
            {
                Console.WriteLine(
                    $"Migration ID: {result.Manifest.MigrationId}");
                Console.WriteLine(
                    $"Producer: {result.Manifest.Producer.Product} {result.Manifest.Producer.Version}");
                Console.WriteLine(
                    $"Source: {result.Manifest.Source.Product} {result.Manifest.Source.Version}");
                Console.WriteLine(
                    $"Start fingerprint: {result.Manifest.Source.StartFingerprint}");
                Console.WriteLine(
                    $"Completion fingerprint: {result.Manifest.Source.CompletionFingerprint}");
                Console.WriteLine(
                    $"Rehearsal only: {result.Manifest.Capture.RehearsalOnly}");
                Console.WriteLine(
                    $"Source changed: {result.Manifest.Capture.SourceChangedDuringCapture}");
                Console.WriteLine(
                    $"Stacks: {result.Manifest.Stacks.Length}");
                Console.WriteLine(
                    $"Entries: {result.Manifest.Limits.EntryCount}");
                Console.WriteLine(
                    $"Expanded bytes: {result.Manifest.Limits.ExpandedBytes}");
                Console.WriteLine($"Input SHA-256: {result.InputSha256}");

                if (result.EncryptedInput)
                {
                    Console.WriteLine(
                        $"Verified ZIP SHA-256: {result.VerifiedZipSha256}");
                }
            }

            return ExitCodes.Success;
        }
        finally
        {
            prepared.Dispose();
        }
    }

    private static async Task<PreparedArchive> PrepareArchiveAsync(
        ArchiveReadOptions options,
        CancellationToken cancellationToken)
    {
        var encrypted = options.ArchivePath.EndsWith(
            ".age",
            StringComparison.OrdinalIgnoreCase);

        if (!encrypted)
        {
            return new PreparedArchive(
                options.ArchivePath,
                encrypted: false,
                temporaryDirectory: null,
                inputSha256: null,
                inputBytes: null);
        }

        if (string.IsNullOrWhiteSpace(options.AgeIdentityPath))
        {
            throw new ArgumentException(
                "Encrypted archives require --age-identity <path>.");
        }

        var temporaryDirectory = Path.Combine(
            options.WorkspacePath,
            "archive-read",
            Guid.NewGuid().ToString("N"));
        PrivateFilePermissions.EnsureDirectory(temporaryDirectory);
        var plaintextPath = Path.Combine(
            temporaryDirectory,
            "migration.memmigration.zip");

        try
        {
            var inputBytes = new FileInfo(options.ArchivePath).Length;
            var maximumEncryptedBytes = options.MaximumExpandedBytes >
                    long.MaxValue - 1024L * 1024 * 1024
                ? long.MaxValue
                : options.MaximumExpandedBytes + 1024L * 1024 * 1024;

            if (inputBytes > maximumEncryptedBytes)
            {
                throw new InvalidDataException(
                    "The encrypted migration archive exceeds the supported envelope-size limit.");
            }

            var inputSha256 = await Sha256File.ComputeAsync(
                options.ArchivePath,
                cancellationToken);
            await new AgeEnvelope(new ProcessRunner()).DecryptAsync(
                options.ArchivePath,
                plaintextPath,
                options.AgeIdentityPath,
                options.AgeCommand,
                cancellationToken);
            return new PreparedArchive(
                plaintextPath,
                encrypted: true,
                temporaryDirectory,
                inputSha256,
                inputBytes);
        }
        catch (InvalidOperationException ex)
        {
            Directory.Delete(temporaryDirectory, recursive: true);
            throw new InvalidDataException(
                "The encrypted migration archive could not be decrypted with the supplied age identity.",
                ex);
        }
        catch
        {
            Directory.Delete(temporaryDirectory, recursive: true);
            throw;
        }
    }

    private static ArchiveSafetyLimits ToSafetyLimits(
        ArchiveReadOptions options) =>
        new(
            options.MaximumEntryBytes,
            options.MaximumExpandedBytes,
            options.MaximumEntries,
            options.MaximumCompressionRatio);

    private static V010AssessmentService CreateAssessmentService(
        AssessmentOptions options,
        ProcessRunner? processRunner = null) =>
        SourceAssessmentRuntimeFactory.CreateService(options, processRunner);

    private static int MapExitCode(AssessmentResult result)
    {
        return result.Classification switch
        {
            AssessmentClassification.ConfirmedSupportedV010
                when result.Findings.Any(
                    finding => finding.Severity is FindingSeverity.Warning) =>
                ExitCodes.SupportedWithWarnings,
            AssessmentClassification.ConfirmedSupportedV010 =>
                ExitCodes.Success,
            AssessmentClassification.ProbableV010 or
            AssessmentClassification.PartialRepairableV010 or
            AssessmentClassification.Blocked =>
                ExitCodes.Blocked,
            AssessmentClassification.AmbiguousMultipleInstallations =>
                ExitCodes.Ambiguous,
            AssessmentClassification.CurrentV011Present =>
                ExitCodes.CurrentTargetAlreadyPresent,
            AssessmentClassification.UnsupportedSource =>
                ExitCodes.Unsupported,
            AssessmentClassification.ExecutionFailed =>
                ExitCodes.ExecutionFailure,
            _ => ExitCodes.Blocked
        };
    }

    private static string GetVersion()
    {
        var assembly = typeof(Program).Assembly;
        return assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            mem-migrate - MEM v0.1.0 assessment and protected source capture

            Usage:
              mem-migrate version
              mem-migrate assess [options]
              mem-migrate report [options]
              mem-migrate source capture [options]
              mem-migrate source verify <archive> [options]
              mem-migrate source inspect <archive> [options]
              mem-migrate source package-for-intake [options]
              mem-migrate target convert <archive> [options]
              mem-migrate target verify-candidate <archive> <conversion-report.json> [options]
              mem-migrate target export-stack-artifact <archive> <conversion-report.json> [options]
              mem-migrate target import <migration-intake-manifest.json> <memstack.zip> [options]
              mem-migrate target stage-private [options]
              mem-migrate cutover prepare [options]
              mem-migrate cutover freeze-source [options]
              mem-migrate cutover rollback-source [options]
              mem-migrate cutover stage-final [options]
              mem-migrate cutover activation-readiness [options]
              mem-migrate qualification source-evidence [options]


            Cutover freeze-source options:
              --plan <cutover-plan.json>
                  Completed MM-06A cutover preparation report.

              --expected-plan-hash <sha256>
                  Exact operator-reviewed plan hash. Required.

              --freeze-attempt-id <id>
                  Explicit durable freeze attempt ID.

              --workspace <path>
                  Private source-freeze journal and assessment workspace.

              --output <path>
                  Source-freeze report and rollback checkpoint output.

              --stop-timeout-seconds <seconds>
                  Docker graceful stop timeout. Default: 30.

              --resume
                  Return a completed matching freeze idempotently or resume a journaled attempt.

              --docker-command <path>
              --api-url <url>
              --postgres-container <name-or-id>
              --command-timeout-seconds <seconds>
              --http-timeout-seconds <seconds>
              --maximum-file-scan-entries <count>
              --json

            Source capture options:
              --workspace <path>
                  Private workspace and durable journal. Default: .workspace

              --output <path>
                  Capture receipt and archive output directory. Default: current directory

              --expected-fingerprint <sha256>
                  Require the fresh pre-capture assessment to match an operator-reviewed fingerprint.

              --stack-id <guid>
                  Capture exactly one selected source stack. Required when the source contains more than one stack.

              --age-recipient <recipient>
                  Encrypt the verified ZIP for cross-server transfer using age.
                  The plaintext ZIP is removed after encryption succeeds.

              --age-command <path>
                  age executable. Default: age

              --capture-id <id>
                  Explicit durable capture ID.

              --final-frozen
                  Mark the archive as the final cutover capture. Requires a completed MM-06B freeze report.

              --freeze-report <path>
                  MM-06B source-freeze report proving source writers are stopped and restart-disabled.

              --resume
                  Restart an incomplete capture using the supplied --capture-id.
                  Completed captures are returned idempotently.

              --maximum-entry-bytes <bytes>
                  Default: 10737418240 (10 GiB)

              --maximum-expanded-bytes <bytes>
                  Default: 26843545600 (25 GiB)

              --maximum-entries <count>
                  Default: 250000

              --required-free-space-multiplier <number>
                  Default: 2.25

              --docker-command <path>
              --api-url <url>
              --postgres-container <name-or-id>
              --command-timeout-seconds <seconds>
              --http-timeout-seconds <seconds>
              --maximum-file-scan-entries <count>
              --json

            Target conversion options:
              --workspace <path>
                  Private conversion workspace and durable journal.

              --output <path>
                  Conversion report, evidence, log, and PostgreSQL dump output.

              --conversion-id <id>
                  Explicit durable conversion attempt ID.

              --stack-id <guid>
                  Required when the archive contains more than one stack.

              --synapse-image <digest-or-image-id>
                  Approved local Synapse image. It must match the MM-03 image ID.
                  Default: immutable image identity captured in the archive.

              --postgres-image <digest>
                  Approved local PostgreSQL image. Automatic pulls are prohibited.

              --batch-size <count>
                  synapse_port_db batch size. Default: 1000

              --command-timeout-seconds <seconds>
              --readiness-timeout-seconds <seconds>
              --required-free-space-multiplier <number>
              --resume
              --keep-resources
                  Retain the isolated PostgreSQL container and internal network for
                  controlled MM-04B diagnostics. No ports are published.
              --json

            Private candidate options:
              --workspace <path>
              --output <path>
              --candidate-id <id>
              --synapse-image <digest-or-image-id>
              --postgres-image <digest>
              --command-timeout-seconds <seconds>
              --readiness-timeout-seconds <seconds>
              --keep-resources
              --json

            Target import options:
              --profile <name>
                  Existing named MEM CLI profile. The target URL comes from the
                  non-secret profile and the device credential is read from the
                  same OS Secret Service item created by mem login --device.
              --workspace <path>
                  Durable target-import journal.
              --attempt-id <id>
              --display-name <name>
              --http-timeout-seconds <seconds>
              --allow-insecure-tls
                  Explicit development-only self-signed TLS override.
              --resume
              --json

            Private target staging options:
              --import-attempt-id <id>
                  Completed MM-05C target import attempt to stage.
              --profile <name>
                  Existing MEM CLI device profile. Must match the import target.
              --workspace <path>
              --output <path>
              --stage-attempt-id <id>
              --http-timeout-seconds <seconds>
              --destroy-after-verification
                  Destroy the retained private runtime after evidence is captured.
              --resume
              --json

            Cutover preparation options:
              --workspace <path>
                  Private MM-06 cutover-plan journal and fresh assessment workspace.
              --target-workspace <path>
                  Existing MM-05C/MM-05D journal workspace. Defaults to --workspace.
              --output <path>
                  Plan, Markdown review, and fresh assessment output root.
              --stage-attempt-id <id>
                  Completed and destroyed MM-05D private-stage attempt.
              --expected-source-fingerprint <sha256>
                  Operator-reviewed rehearsal source fingerprint. Fresh drift fails closed.
              --plan-id <id>
                  Explicit durable plan ID. Required with --resume.
              --valid-for-minutes <minutes>
                  Plan validity window. Default: 30; allowed: 5-240.
              --development-external-control-plane
                  Development-only coexistence override. Requires the legacy API/Web
                  to be stopped outside Docker before preparation; they are omitted
                  from Docker freeze and rollback inventory. Never use in production.
              --docker-command <path>
              --api-url <url>
              --postgres-container <name-or-id>
              --command-timeout-seconds <seconds>
              --http-timeout-seconds <seconds>
              --maximum-file-scan-entries <count>
              --resume
              --json


            Source restoration options:
              --freeze-report <cutover-freeze-report.json>
                  Completed MM-06B source-freeze report and rollback checkpoint.
              --source-handoff <source-restoration-handoff.json>
                  Step-up-protected handoff downloaded from the rolled-back MEM 0.2.0 target.
              --expected-handoff-sha256 <sha256>
                  Operator-reviewed payload SHA-256 displayed by the target.
              --workspace <path>
                  Private durable source-restoration journal.
              --output <path>
                  Source restoration JSON and Markdown evidence output root.
              --restoration-attempt-id <id>
                  Explicit durable restoration attempt ID. Required with --resume.
              --docker-command <path>
              --command-timeout-seconds <seconds>
              --readiness-timeout-seconds <seconds>
              --development-external-control-plane-ready
                  Development-only acknowledgement that host-managed legacy API/Web have
                  been restarted and verified manually. Never use in production.
              --resume
              --json

              This command validates the target rollback handoff and exact MM-06B freeze
              checkpoint, restores captured Docker restart policies, starts only containers
              that were running before freeze in the recorded order, verifies Matrix and
              Element runtime state, and emits durable source restoration evidence. It does
              not connect back to or mutate the MEM 0.2.0 target.


            Activation readiness options:
              --final-stage-report <final-target-stage-report.json>
                  Completed MM-06D final target stage report.
              --freeze-report <cutover-freeze-report.json>
                  Completed MM-06B source-freeze report.
              --profile <name>
                  Existing named MEM CLI device profile for the target.
              --candidate-id <id>
              --preview-id <id>
              --confirmation-id <id>
                  Existing target-side production candidate, cutover preview, and confirmation IDs.
              --output <path>
              --attempt-id <id>
              --http-timeout-seconds <seconds>
              --allow-insecure-tls
              --json

              This command is read-only. It validates frozen-source and retained-final-stage evidence,
              then queries the target pre-cutover readiness gate. It does not create candidates,
              mutate routes, execute cutover, or accept the migration.


            Package for secure intake options:
              --intake-id <id>
                  Target secure-intake ID. Required.

              --age-recipient <age1...>
                  Public target age recipient. Required.

              --recipient-fingerprint <XXXX-XXXX-XXXX-XXXX>
                  Recipient fingerprint displayed by the target. Required.

              --archive <path>
                  Advanced override for a specific verified plaintext
                  .memmigration.zip. By default, mem-migrate selects the only
                  eligible completed plaintext capture from its source journal.

              --output-directory <path>
                  Advanced output override. By default, the encrypted package
                  and evidence reports are written into the current directory.

              --workspace <path>
                  Advanced source-journal override. By default, mem-migrate uses
                  the last remembered source workspace, MEM_MIGRATE_WORKSPACE,
                  or a conventional source workspace.

              --age-command <path>
                  age executable. Default: age

              --maximum-entry-bytes <bytes>
              --maximum-expanded-bytes <bytes>
              --maximum-entries <count>
              --maximum-compression-ratio <number>
              --json

              The normal command needs no archive path, output directory, or
              filename. It discovers the eligible source capture, independently
              recalculates the recipient fingerprint, verifies the archive, writes
              a UTC timestamped package into the current directory, encrypts through
              a private .partial file, refuses overwrite, emits mode-600 output, and
              records JSON and Markdown evidence. It never handles the target private
              age identity.

            Verify and inspect options:
              --workspace <path>
                  Private temporary decryption workspace. Default: .workspace

              --age-identity <path>
                  Required for a .zip.age archive.

              --age-command <path>
                  age executable. Default: age

              --maximum-entry-bytes <bytes>
              --maximum-expanded-bytes <bytes>
              --maximum-entries <count>
              --maximum-compression-ratio <number>
              --json

            Two-server source qualification options:
              --cutover-plan-report <cutover-plan.json>
                  Completed production cutover plan. Development external-control-plane plans are rejected.
              --freeze-report <cutover-freeze-report.json>
                  Completed source-freeze report bound to the reviewed plan.
              --capture-report <capture-report.json>
                  Completed final capture report from the unchanged frozen source.
              --package-report <package-report.json>
                  Final package-for-intake report with a package revision ID.
              --expected-package-sha256 <sha256>
                  Exact operator-reviewed encrypted package SHA-256. Required.
              --output <path>
                  Private source qualification evidence output.
              --qualification-attempt-id <id>
                  Optional explicit qualification evidence identity.
              --docker-command <path>
              --command-timeout-seconds <seconds>
              --json

              This command is read-only. It rejects the same-host development topology,
              revalidates the final package bytes and frozen-source chain, hashes the Linux
              machine ID and Docker Engine ID, and proves exact source writer containers remain
              stopped and restart-disabled. It emits the source half of the two-server
              qualification envelope for import by the MEM 0.2.0 target.

            Assessment options:
              --workspace <path>
              --output <path>
              --docker-command <path>
              --api-url <url>
              --postgres-container <name-or-id>
              --command-timeout-seconds <seconds>
              --http-timeout-seconds <seconds>
              --maximum-file-scan-entries <count>
              --include-sensitive-paths
              --json
              --non-interactive

            Examples:
              mem-migrate source capture \
                --workspace /var/lib/mem-migrate/work \
                --output /var/lib/mem-migrate/artifacts \
                --expected-fingerprint <reviewed-sha256>

              mem-migrate source capture \
                --workspace /var/lib/mem-migrate/work \
                --output /var/lib/mem-migrate/artifacts \
                --age-recipient age1...

              mem-migrate source capture \
                --workspace /var/lib/mem-migrate/work \
                --output /var/lib/mem-migrate/artifacts \
                --capture-id mm06c-final-<timestamp> \
                --final-frozen \
                --freeze-report /var/lib/mem-migrate/mm06b/<attempt>/cutover-freeze-report.json


              cd /path/where/the/encrypted-package-should-be-created
              mem-migrate source package-for-intake \
                --intake-id mig_20260715-example \
                --age-recipient age1... \
                --recipient-fingerprint 0000-0000-0000-0000

              mem-migrate source package-for-intake \
                --intake-id mig_20260715-example \
                --package-revision-id mpr_20260718-example \
                --age-recipient age1... \
                --recipient-fingerprint 0000-0000-0000-0000 \
                --require-final-frozen

              mem-migrate source verify \
                artifact.memmigration.zip.age \
                --age-identity /var/lib/mem-migrate/target-age-key.txt

              mem-migrate target convert \
                artifact.memmigration.zip \
                --workspace /var/lib/mem-migrate/work \
                --output /var/lib/mem-migrate/artifacts

              mem-migrate target verify-candidate \
                artifact.memmigration.zip \
                /var/lib/mem-migrate/artifacts/<conversion-id>/conversion-report.json \
                --workspace /var/lib/mem-migrate/work \
                --output /var/lib/mem-migrate/artifacts

              mem-migrate target export-stack-artifact \
                artifact.memmigration.zip \
                /var/lib/mem-migrate/artifacts/<conversion-id>/conversion-report.json \
                --output /var/lib/mem-migrate/artifacts/mm05a

              mem-migrate target import \
                migration-intake-manifest.json \
                davids-stack.memstack.zip \
                --profile target-server

              mem-migrate qualification source-evidence \
                --cutover-plan-report /var/lib/mem-migrate/mm06a/<plan>/cutover-plan.json \
                --freeze-report /var/lib/mem-migrate/mm06b/<attempt>/cutover-freeze-report.json \
                --capture-report /var/lib/mem-migrate/captures/<capture>/capture-report.json \
                --package-report /secure-transfer/mem-migration-final.package-report.json \
                --expected-package-sha256 <reviewed-sha256> \
                --output /var/lib/mem-migrate/qualification

            Safety:
              MM-03 capture is read-only with respect to the detected source.
              It never stops or restarts source containers. A live capture is
              always marked rehearsal-only. Cross-server transfer uses age.
              MM-04A conversion accepts only a verified plaintext local ZIP, uses
              exact local image identities with pulls disabled, creates an internal
              Docker network, publishes no ports, and never contacts the source host.
              MM-04B restores only the verified MM-04A dump, starts Synapse on an
              internal network with no published ports, verifies server_name and the
              captured signing key identity, then removes migration-owned resources.
              MM-05A emits the existing MEM mem-stack-export ZIP shape plus a neutral
              mem-migration-import/v1 manifest; it does not contact or modify MEM.
              MM-05C contacts only the private target MEM API selected by a named
              MEM CLI profile, reads the device session from OS Secret Service,
              journals target identities, uploads and binds the artifact, and creates
              no stack runtime or public route.
            """);

        Console.WriteLine();
        Console.WriteLine("Conversion worker options:");
        Console.WriteLine("  mem-migrate worker convert --request <absolute-request.json>");
        Console.WriteLine();
        Console.WriteLine("  Internal target worker contract. The request file must use schema");
        Console.WriteLine("  mem-conversion-worker-request version 2 and contain absolute, server-owned");
        Console.WriteLine("  archive, workspace, and output paths. Stdout is JSON Lines using");
        Console.WriteLine("  mem-conversion-worker-event version 1. No daemon or listener is created.");
    }

    private sealed class PreparedArchive(
        string plaintextPath,
        bool encrypted,
        string? temporaryDirectory,
        string? inputSha256,
        long? inputBytes) : IDisposable
    {
        public string PlaintextPath { get; } = plaintextPath;
        public bool Encrypted { get; } = encrypted;
        public string? InputSha256 { get; } = inputSha256;
        public long? InputBytes { get; } = inputBytes;

        public void Dispose()
        {
            if (!string.IsNullOrWhiteSpace(temporaryDirectory) &&
                Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }
}
