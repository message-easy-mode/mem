using System.Globalization;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Conversion;
using Mem.Migrate.Core.Cutover;
using Mem.Migrate.Core.Rehearsal;
using Mem.Migrate.Core.Qualification;
using Mem.Migrate.Core.Target;
using Mem.Migrate.Core.Worker;

namespace Mem.Migrate.Cli;

internal sealed record ParsedCommand(
    string Name,
    AssessmentOptions AssessmentOptions,
    CaptureOptions? CaptureOptions,
    ArchiveReadOptions? ArchiveOptions,
    ConversionOptions? ConversionOptions,
    CandidateOptions? CandidateOptions,
    RehearsalArtifactOptions? RehearsalArtifactOptions,
    TargetImportOptions? TargetImportOptions,
    bool ShowHelp,
    TargetPrivateStageOptions? TargetPrivateStageOptions = null,
    CutoverPrepareOptions? CutoverPrepareOptions = null,
    CutoverFreezeOptions? CutoverFreezeOptions = null,
    FinalTargetStageOptions? FinalTargetStageOptions = null,
    ActivationReadinessOptions? ActivationReadinessOptions = null,
    PackageForIntakeOptions? PackageForIntakeOptions = null,
    SourceRestorationOptions? SourceRestorationOptions = null,
    string? ConversionWorkerRequestPath = null,
    SourceQualificationOptions? SourceQualificationOptions = null);

internal static class CommandLineParser
{
    public static ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return Help();
        }

        var command = args[0].Trim().ToLowerInvariant();

        if (command is "-h" or "--help" or "help")
        {
            return Help();
        }

        if (command == "version")
        {
            return new ParsedCommand(
                "version",
                new AssessmentOptions(),
                null,
                null,
                null,
                null,
                null,
                null,
                ShowHelp: false);
        }

        if (command is "assess" or "report")
        {
            var options = ParseAssessmentOptions(args, startIndex: 1);
            return new ParsedCommand(
                command,
                options.Options,
                null,
                null,
                null,
                null,
                null,
                null,
                options.ShowHelp);
        }

        if (command == "cutover")
        {
            if (args.Length < 2)
            {
                throw new CommandLineException(
                    "The cutover command requires prepare, freeze-source, rollback-source, stage-final, or activation-readiness.");
            }

            return args[1].Trim().ToLowerInvariant() switch
            {
                "prepare" => ParseCutoverPrepare(args),
                "freeze-source" => ParseCutoverFreeze(args),
                "rollback-source" => ParseSourceRestoration(args),
                "stage-final" => ParseFinalTargetStage(args),
                "activation-readiness" => ParseActivationReadiness(args),
                _ => throw new CommandLineException(
                    $"Unknown cutover command '{args[1]}'.")
            };
        }

        if (command == "qualification")
        {
            if (args.Length < 2)
            {
                throw new CommandLineException(
                    "The qualification command requires source-evidence.");
            }

            return args[1].Trim().ToLowerInvariant() switch
            {
                "source-evidence" => ParseSourceQualification(args),
                _ => throw new CommandLineException(
                    $"Unknown qualification command '{args[1]}'.")
            };
        }

        if (command == "worker")
        {
            if (args.Length < 2 || !string.Equals(args[1], "convert", StringComparison.OrdinalIgnoreCase))
            {
                throw new CommandLineException("The worker command requires convert.");
            }

            if (args.Length == 2 || args[2] is "-h" or "--help")
            {
                return new ParsedCommand(
                    "worker-convert", new AssessmentOptions(), null, null, null, null, null, null,
                    ShowHelp: true);
            }

            if (args.Length != 4 || !string.Equals(args[2], "--request", StringComparison.Ordinal))
            {
                throw new CommandLineException(
                    "Worker convert requires exactly --request <absolute-request.json>.");
            }

            return new ParsedCommand(
                "worker-convert", new AssessmentOptions(), null, null, null, null, null, null,
                ShowHelp: false, ConversionWorkerRequestPath: args[3]);
        }

        if (command == "target")
        {
            if (args.Length < 2)
            {
                throw new CommandLineException(
                    "The target command requires convert, verify-candidate, export-stack-artifact, import, or stage-private.");
            }

            return args[1].Trim().ToLowerInvariant() switch
            {
                "convert" => ParseConversion(args),
                "verify-candidate" => ParseCandidate(args),
                "export-stack-artifact" => ParseRehearsalArtifact(args),
                "import" => ParseTargetImport(args),
                "stage-private" => ParseTargetPrivateStage(args),
                _ => throw new CommandLineException(
                    $"Unknown target command '{args[1]}'.")
            };
        }

        if (command != "source")
        {
            throw new CommandLineException(
                $"Unknown command '{args[0]}'.");
        }

        if (args.Length < 2)
        {
            throw new CommandLineException(
                "The source command requires capture, verify, inspect, or package-for-intake.");
        }

        var sourceCommand = args[1].Trim().ToLowerInvariant();

        return sourceCommand switch
        {
            "capture" => ParseCapture(args),
            "verify" => ParseArchiveRead(args, "source-verify"),
            "inspect" => ParseArchiveRead(args, "source-inspect"),
            "package-for-intake" => ParsePackageForIntake(args),
            _ => throw new CommandLineException(
                $"Unknown source command '{args[1]}'.")
        };
    }


    private static ParsedCommand ParseConversion(string[] args)
    {
        if (args.Length < 3 || args[2] is "-h" or "--help")
        {
            return new ParsedCommand(
                "target-convert",
                new AssessmentOptions(),
                null,
                null,
                null,
                null,
                null,
                null,
                ShowHelp: true);
        }

        var options = new ConversionOptions { ArchivePath = args[2] };
        var index = 3;
        var showHelp = false;

        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    index++;
                    break;
                case "--workspace":
                    options = options with { WorkspacePath = RequireValue(args, ref index, option) };
                    break;
                case "--output":
                    options = options with { OutputPath = RequireValue(args, ref index, option) };
                    break;
                case "--conversion-id":
                    options = options with { ConversionId = RequireValue(args, ref index, option) };
                    break;
                case "--stack-id":
                    if (!Guid.TryParse(RequireValue(args, ref index, option), out var stackId))
                    {
                        throw new CommandLineException("Option '--stack-id' requires a GUID.");
                    }
                    options = options with { StackId = stackId };
                    break;
                case "--docker-command":
                    options = options with { DockerCommand = RequireValue(args, ref index, option) };
                    break;
                case "--synapse-image":
                    options = options with { SynapseImage = RequireValue(args, ref index, option) };
                    break;
                case "--postgres-image":
                    options = options with { PostgresImage = RequireValue(args, ref index, option) };
                    break;
                case "--command-timeout-seconds":
                    options = options with { CommandTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) };
                    break;
                case "--readiness-timeout-seconds":
                    options = options with { ReadinessTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) };
                    break;
                case "--batch-size":
                    options = options with { BatchSize = ParseInt32(RequireValue(args, ref index, option), option) };
                    break;
                case "--required-free-space-multiplier":
                    options = options with { RequiredFreeSpaceMultiplier = ParseDouble(RequireValue(args, ref index, option), option) };
                    break;
                case "--maximum-entry-bytes":
                    options = options with { MaximumEntryBytes = ParseInt64(RequireValue(args, ref index, option), option) };
                    break;
                case "--maximum-expanded-bytes":
                    options = options with { MaximumExpandedBytes = ParseInt64(RequireValue(args, ref index, option), option) };
                    break;
                case "--maximum-entries":
                    options = options with { MaximumEntries = ParseInt32(RequireValue(args, ref index, option), option) };
                    break;
                case "--maximum-compression-ratio":
                    options = options with { MaximumCompressionRatio = ParseDouble(RequireValue(args, ref index, option), option) };
                    break;
                case "--resume":
                    options = options with { Resume = true };
                    index++;
                    break;
                case "--keep-resources":
                    options = options with { KeepResources = true };
                    index++;
                    break;
                case "--json":
                    options = options with { JsonConsoleOutput = true };
                    index++;
                    break;
                default:
                    throw new CommandLineException($"Unknown target convert option '{option}'.");
            }
        }

        return new ParsedCommand(
            "target-convert",
            new AssessmentOptions(),
            null,
            null,
            options,
            null,
            null,
            null,
            showHelp);
    }

    private static ParsedCommand ParseCandidate(string[] args)
    {
        if (args.Length < 4 || args[2] is "-h" or "--help")
        {
            return new ParsedCommand("target-verify-candidate", new AssessmentOptions(), null, null, null, null, null, null, ShowHelp: true);
        }

        var options = new CandidateOptions { ArchivePath = args[2], ConversionReportPath = args[3] };
        var index = 4;
        var showHelp = false;
        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "-h":
                case "--help": showHelp = true; index++; break;
                case "--workspace": options = options with { WorkspacePath = RequireValue(args, ref index, option) }; break;
                case "--output": options = options with { OutputPath = RequireValue(args, ref index, option) }; break;
                case "--candidate-id": options = options with { CandidateId = RequireValue(args, ref index, option) }; break;
                case "--docker-command": options = options with { DockerCommand = RequireValue(args, ref index, option) }; break;
                case "--synapse-image": options = options with { SynapseImage = RequireValue(args, ref index, option) }; break;
                case "--postgres-image": options = options with { PostgresImage = RequireValue(args, ref index, option) }; break;
                case "--command-timeout-seconds": options = options with { CommandTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--readiness-timeout-seconds": options = options with { ReadinessTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--keep-resources": options = options with { KeepResources = true }; index++; break;
                case "--json": options = options with { JsonConsoleOutput = true }; index++; break;
                default: throw new CommandLineException($"Unknown target verify-candidate option '{option}'.");
            }
        }
        return new ParsedCommand("target-verify-candidate", new AssessmentOptions(), null, null, null, options, null, null, showHelp);
    }


    private static ParsedCommand ParseRehearsalArtifact(string[] args)
    {
        if (args.Length < 4 || args[2] is "-h" or "--help")
            return new ParsedCommand("target-export-stack-artifact", new AssessmentOptions(), null, null, null, null, null, null, ShowHelp: true);

        var options = new RehearsalArtifactOptions { ArchivePath = args[2], ConversionReportPath = args[3] };
        var index = 4;
        var showHelp = false;
        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "-h":
                case "--help": showHelp = true; index++; break;
                case "--output": options = options with { OutputPath = RequireValue(args, ref index, option) }; break;
                case "--artifact-id": options = options with { ArtifactId = RequireValue(args, ref index, option) }; break;
                case "--docker-command": options = options with { DockerCommand = RequireValue(args, ref index, option) }; break;
                case "--command-timeout-seconds": options = options with { CommandTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--maximum-entry-bytes": options = options with { MaximumEntryBytes = ParseInt64(RequireValue(args, ref index, option), option) }; break;
                case "--maximum-expanded-bytes": options = options with { MaximumExpandedBytes = ParseInt64(RequireValue(args, ref index, option), option) }; break;
                case "--maximum-entries": options = options with { MaximumEntries = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--maximum-compression-ratio": options = options with { MaximumCompressionRatio = ParseDouble(RequireValue(args, ref index, option), option) }; break;
                case "--json": options = options with { JsonConsoleOutput = true }; index++; break;
                default: throw new CommandLineException($"Unknown target export-stack-artifact option '{option}'.");
            }
        }
        return new ParsedCommand("target-export-stack-artifact", new AssessmentOptions(), null, null, null, null, options, null, showHelp);
    }


    private static ParsedCommand ParseSourceQualification(string[] args)
    {
        if (args.Length < 3 || args[2] is "-h" or "--help")
        {
            return new ParsedCommand(
                "qualification-source-evidence",
                new AssessmentOptions(),
                null, null, null, null, null, null,
                ShowHelp: true);
        }

        var options = new SourceQualificationOptions();
        var index = 2;
        var showHelp = false;
        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "-h":
                case "--help": showHelp = true; index++; break;
                case "--cutover-plan-report": options = options with { CutoverPlanReportPath = RequireValue(args, ref index, option) }; break;
                case "--freeze-report": options = options with { FreezeReportPath = RequireValue(args, ref index, option) }; break;
                case "--capture-report": options = options with { CaptureReportPath = RequireValue(args, ref index, option) }; break;
                case "--package-report": options = options with { PackageReportPath = RequireValue(args, ref index, option) }; break;
                case "--expected-package-sha256": options = options with { ExpectedEncryptedPackageSha256 = RequireValue(args, ref index, option) }; break;
                case "--output": options = options with { OutputPath = RequireValue(args, ref index, option) }; break;
                case "--qualification-attempt-id": options = options with { QualificationAttemptId = RequireValue(args, ref index, option) }; break;
                case "--docker-command": options = options with { DockerCommand = RequireValue(args, ref index, option) }; break;
                case "--command-timeout-seconds": options = options with { CommandTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--json": options = options with { JsonConsoleOutput = true }; index++; break;
                default: throw new CommandLineException($"Unknown qualification source-evidence option '{option}'.");
            }
        }

        return new ParsedCommand(
            "qualification-source-evidence",
            new AssessmentOptions(),
            null, null, null, null, null, null,
            showHelp,
            SourceQualificationOptions: options);
    }

    private static ParsedCommand ParseSourceRestoration(string[] args)
    {
        if (args.Length < 2 || (args.Length > 2 && args[2] is "-h" or "--help"))
        {
            return new ParsedCommand(
                "cutover-rollback-source",
                new AssessmentOptions(),
                null, null, null, null, null, null,
                ShowHelp: true,
                SourceRestorationOptions: null);
        }

        var assessment = new AssessmentOptions();
        var options = new SourceRestorationOptions();
        var index = 2;
        var showHelp = false;
        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "-h":
                case "--help": showHelp = true; index++; break;
                case "--workspace": options = options with { WorkspacePath = RequireValue(args, ref index, option) }; break;
                case "--output": options = options with { OutputPath = RequireValue(args, ref index, option) }; break;
                case "--freeze-report": options = options with { FreezeReportPath = RequireValue(args, ref index, option) }; break;
                case "--source-handoff": options = options with { SourceHandoffPath = RequireValue(args, ref index, option) }; break;
                case "--expected-handoff-sha256": options = options with { ExpectedHandoffSha256 = RequireValue(args, ref index, option) }; break;
                case "--restoration-attempt-id": options = options with { RestorationAttemptId = RequireValue(args, ref index, option) }; break;
                case "--readiness-timeout-seconds": options = options with { ReadinessTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--development-external-control-plane-ready": options = options with { DevelopmentExternalControlPlaneReady = true }; index++; break;
                case "--docker-command": assessment = assessment with { DockerCommand = RequireValue(args, ref index, option) }; break;
                case "--command-timeout-seconds": assessment = assessment with { CommandTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--resume": options = options with { Resume = true }; index++; break;
                case "--json": options = options with { JsonConsoleOutput = true }; index++; break;
                default: throw new CommandLineException($"Unknown cutover rollback-source option '{option}'.");
            }
        }

        options = options with { Assessment = assessment };
        return new ParsedCommand(
            "cutover-rollback-source",
            assessment,
            null, null, null, null, null, null,
            showHelp,
            SourceRestorationOptions: options);
    }

    private static ParsedCommand ParseCutoverFreeze(string[] args)
    {
        if (args.Length < 2 || (args.Length > 2 && args[2] is "-h" or "--help"))
        {
            return new ParsedCommand(
                "cutover-freeze-source",
                new AssessmentOptions(),
                null, null, null, null, null, null,
                ShowHelp: true,
                CutoverFreezeOptions: null);
        }

        var assessment = new AssessmentOptions();
        var options = new CutoverFreezeOptions();
        var index = 2;
        var showHelp = false;
        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "-h":
                case "--help": showHelp = true; index++; break;
                case "--workspace": options = options with { WorkspacePath = RequireValue(args, ref index, option) }; break;
                case "--output": options = options with { OutputPath = RequireValue(args, ref index, option) }; break;
                case "--plan": options = options with { PlanPath = RequireValue(args, ref index, option) }; break;
                case "--expected-plan-hash": options = options with { ExpectedPlanHash = RequireValue(args, ref index, option) }; break;
                case "--freeze-attempt-id": options = options with { FreezeAttemptId = RequireValue(args, ref index, option) }; break;
                case "--stop-timeout-seconds": options = options with { StopTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--docker-command": assessment = assessment with { DockerCommand = RequireValue(args, ref index, option) }; break;
                case "--api-url": assessment = assessment with { ApiUrl = RequireValue(args, ref index, option) }; break;
                case "--postgres-container": assessment = assessment with { PostgresContainer = RequireValue(args, ref index, option) }; break;
                case "--command-timeout-seconds": assessment = assessment with { CommandTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--http-timeout-seconds": assessment = assessment with { HttpTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--maximum-file-scan-entries": assessment = assessment with { MaximumFileScanEntries = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--resume": options = options with { Resume = true }; index++; break;
                case "--json": options = options with { JsonConsoleOutput = true }; index++; break;
                default: throw new CommandLineException($"Unknown cutover freeze-source option '{option}'.");
            }
        }

        options = options with { Assessment = assessment };
        return new ParsedCommand(
            "cutover-freeze-source",
            assessment,
            null, null, null, null, null, null,
            showHelp,
            CutoverFreezeOptions: options);
    }

    private static ParsedCommand ParseCutoverPrepare(string[] args)
    {
        if (args.Length < 2 || (args.Length > 2 && args[2] is "-h" or "--help"))
        {
            return new ParsedCommand(
                "cutover-prepare",
                new AssessmentOptions(),
                null,
                null,
                null,
                null,
                null,
                null,
                ShowHelp: true,
                CutoverPrepareOptions: null);
        }

        var assessment = new AssessmentOptions();
        var options = new CutoverPrepareOptions();
        var index = 2;
        var showHelp = false;
        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "-h":
                case "--help": showHelp = true; index++; break;
                case "--workspace": options = options with { WorkspacePath = RequireValue(args, ref index, option) }; break;
                case "--target-workspace": options = options with { TargetWorkspacePath = RequireValue(args, ref index, option) }; break;
                case "--output": options = options with { OutputPath = RequireValue(args, ref index, option) }; break;
                case "--stage-attempt-id": options = options with { StageAttemptId = RequireValue(args, ref index, option) }; break;
                case "--expected-source-fingerprint": options = options with { ExpectedSourceFingerprint = RequireValue(args, ref index, option) }; break;
                case "--plan-id": options = options with { PlanId = RequireValue(args, ref index, option) }; break;
                case "--valid-for-minutes": options = options with { ValidForMinutes = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--development-external-control-plane": options = options with { DevelopmentExternalControlPlane = true }; index++; break;
                case "--docker-command": assessment = assessment with { DockerCommand = RequireValue(args, ref index, option) }; break;
                case "--api-url": assessment = assessment with { ApiUrl = RequireValue(args, ref index, option) }; break;
                case "--postgres-container": assessment = assessment with { PostgresContainer = RequireValue(args, ref index, option) }; break;
                case "--command-timeout-seconds": assessment = assessment with { CommandTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--http-timeout-seconds": assessment = assessment with { HttpTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--maximum-file-scan-entries": assessment = assessment with { MaximumFileScanEntries = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--resume": options = options with { Resume = true }; index++; break;
                case "--json": options = options with { JsonConsoleOutput = true }; index++; break;
                default: throw new CommandLineException($"Unknown cutover prepare option '{option}'.");
            }
        }

        options = options with { Assessment = assessment };
        return new ParsedCommand(
            "cutover-prepare",
            assessment,
            null,
            null,
            null,
            null,
            null,
            null,
            showHelp,
            CutoverPrepareOptions: options);
    }


    private static ParsedCommand ParseActivationReadiness(string[] args)
    {
        if (args.Length < 3 || args[2] is "-h" or "--help")
            return new ParsedCommand("cutover-activation-readiness", new AssessmentOptions(), null, null, null, null, null, null, true);

        var options = new ActivationReadinessOptions();
        var index = 2;
        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "-h": case "--help": return new ParsedCommand("cutover-activation-readiness", new AssessmentOptions(), null, null, null, null, null, null, true);
                case "--final-stage-report": options = options with { FinalTargetStageReportPath = RequireValue(args, ref index, option) }; break;
                case "--freeze-report": options = options with { FreezeReportPath = RequireValue(args, ref index, option) }; break;
                case "--profile": options = options with { ProfileName = RequireValue(args, ref index, option) }; break;
                case "--candidate-id": options = options with { CandidateId = RequireValue(args, ref index, option) }; break;
                case "--preview-id": options = options with { PreviewId = RequireValue(args, ref index, option) }; break;
                case "--confirmation-id": options = options with { ConfirmationId = RequireValue(args, ref index, option) }; break;
                case "--output": options = options with { OutputPath = RequireValue(args, ref index, option) }; break;
                case "--attempt-id": options = options with { AttemptId = RequireValue(args, ref index, option) }; break;
                case "--http-timeout-seconds": options = options with { HttpTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--allow-insecure-tls": options = options with { AllowInsecureTls = true }; index++; break;
                case "--json": options = options with { JsonConsoleOutput = true }; index++; break;
                default: throw new CommandLineException($"Unknown cutover activation-readiness option '{option}'.");
            }
        }
        return new ParsedCommand("cutover-activation-readiness", new AssessmentOptions(), null, null, null, null, null, null, false, ActivationReadinessOptions: options);
    }

    private static ParsedCommand ParseFinalTargetStage(string[] args)
    {
        if (args.Length < 2 || args.Any(x => x is "-h" or "--help"))
            return new ParsedCommand("cutover-stage-final", new AssessmentOptions(), null, null, null, null, null, null, true);

        var options = new FinalTargetStageOptions();
        var index = 2;
        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "--archive": options = options with { ArchivePath = RequireValue(args, ref index, option) }; break;
                case "--freeze-report": options = options with { FreezeReportPath = RequireValue(args, ref index, option) }; break;
                case "--profile": options = options with { ProfileName = RequireValue(args, ref index, option) }; break;
                case "--workspace": options = options with { WorkspacePath = RequireValue(args, ref index, option) }; break;
                case "--output": options = options with { OutputPath = RequireValue(args, ref index, option) }; break;
                case "--attempt-id": options = options with { AttemptId = RequireValue(args, ref index, option) }; break;
                case "--docker-command": options = options with { DockerCommand = RequireValue(args, ref index, option) }; break;
                case "--synapse-image": options = options with { SynapseImage = RequireValue(args, ref index, option) }; break;
                case "--postgres-image": options = options with { PostgresImage = RequireValue(args, ref index, option) }; break;
                case "--command-timeout-seconds": options = options with { CommandTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--readiness-timeout-seconds": options = options with { ReadinessTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--http-timeout-seconds": options = options with { HttpTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--allow-insecure-tls": options = options with { AllowInsecureTls = true }; index++; break;
                case "--resume": options = options with { Resume = true }; index++; break;
                case "--json": options = options with { JsonConsoleOutput = true }; index++; break;
                default: throw new CommandLineException($"Unknown cutover stage-final option '{option}'.");
            }
        }
        return new ParsedCommand("cutover-stage-final", new AssessmentOptions(), null, null, null, null, null, null, false, FinalTargetStageOptions: options);
    }

    private static ParsedCommand ParseTargetPrivateStage(string[] args)
    {
        if (args.Length < 2 || (args.Length > 2 && args[2] is "-h" or "--help"))
        {
            return new ParsedCommand("target-stage-private", new AssessmentOptions(), null, null, null, null, null, null, ShowHelp: true, TargetPrivateStageOptions: null);
        }

        var options = new TargetPrivateStageOptions();
        var index = 2;
        var showHelp = false;
        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "-h":
                case "--help": showHelp = true; index++; break;
                case "--import-attempt-id": options = options with { ImportAttemptId = RequireValue(args, ref index, option) }; break;
                case "--profile": options = options with { ProfileName = RequireValue(args, ref index, option) }; break;
                case "--workspace": options = options with { WorkspacePath = RequireValue(args, ref index, option) }; break;
                case "--output": options = options with { OutputPath = RequireValue(args, ref index, option) }; break;
                case "--stage-attempt-id": options = options with { StageAttemptId = RequireValue(args, ref index, option) }; break;
                case "--http-timeout-seconds": options = options with { HttpTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--allow-insecure-tls": options = options with { AllowInsecureTls = true }; index++; break;
                case "--destroy-after-verification": options = options with { DestroyAfterVerification = true }; index++; break;
                case "--resume": options = options with { Resume = true }; index++; break;
                case "--json": options = options with { JsonConsoleOutput = true }; index++; break;
                default: throw new CommandLineException($"Unknown target stage-private option '{option}'.");
            }
        }

        return new ParsedCommand("target-stage-private", new AssessmentOptions(), null, null, null, null, null, null, showHelp, options);
    }

    private static ParsedCommand ParseTargetImport(string[] args)
    {
        if (args.Length < 4 || args[2] is "-h" or "--help")
            return new ParsedCommand("target-import", new AssessmentOptions(), null, null, null, null, null, null, ShowHelp: true);

        var options = new TargetImportOptions
        {
            ManifestPath = args[2],
            StackExportPath = args[3]
        };
        var index = 4;
        var showHelp = false;
        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "-h":
                case "--help": showHelp = true; index++; break;
                case "--profile":
                    options = options with
                    {
                        ProfileName = RequireValue(args, ref index, option)
                    };
                    break;
                case "--target-url":
                case "--bearer-token-file":
                    throw new CommandLineException(
                        $"Target import option '{option}' is retired. " +
                        "Use --profile <name> with the stored MEM CLI " +
                        "device session created by mem login --device.");
                case "--workspace": options = options with { WorkspacePath = RequireValue(args, ref index, option) }; break;
                case "--attempt-id": options = options with { AttemptId = RequireValue(args, ref index, option) }; break;
                case "--display-name": options = options with { DisplayName = RequireValue(args, ref index, option) }; break;
                case "--http-timeout-seconds": options = options with { HttpTimeoutSeconds = ParseInt32(RequireValue(args, ref index, option), option) }; break;
                case "--allow-insecure-tls": options = options with { AllowInsecureTls = true }; index++; break;
                case "--resume": options = options with { Resume = true }; index++; break;
                case "--json": options = options with { JsonConsoleOutput = true }; index++; break;
                default: throw new CommandLineException($"Unknown target import option '{option}'.");
            }
        }
        return new ParsedCommand("target-import", new AssessmentOptions(), null, null, null, null, null, options, showHelp);
    }

    private static ParsedCommand ParseCapture(string[] args)
    {
        var assessment = new AssessmentOptions();
        var capture = new CaptureOptions();
        var index = 2;
        var showHelp = false;

        while (index < args.Length)
        {
            var option = args[index];

            switch (option)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    index++;
                    break;

                case "--workspace":
                    assessment = assessment with
                    {
                        WorkspacePath = RequireValue(args, ref index, option)
                    };
                    break;

                case "--output":
                    capture = capture with
                    {
                        OutputPath = RequireValue(args, ref index, option)
                    };
                    break;

                case "--docker-command":
                    assessment = assessment with
                    {
                        DockerCommand = RequireValue(args, ref index, option)
                    };
                    break;

                case "--api-url":
                    assessment = assessment with
                    {
                        ApiUrl = RequireValue(args, ref index, option)
                    };
                    break;

                case "--postgres-container":
                    assessment = assessment with
                    {
                        PostgresContainer = RequireValue(args, ref index, option)
                    };
                    break;

                case "--command-timeout-seconds":
                    assessment = assessment with
                    {
                        CommandTimeoutSeconds = ParseInt32(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--http-timeout-seconds":
                    assessment = assessment with
                    {
                        HttpTimeoutSeconds = ParseInt32(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--maximum-file-scan-entries":
                    assessment = assessment with
                    {
                        MaximumFileScanEntries = ParseInt32(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--expected-fingerprint":
                    capture = capture with
                    {
                        ExpectedSourceFingerprint =
                            RequireValue(args, ref index, option)
                    };
                    break;

                case "--stack-id":
                    if (!Guid.TryParse(RequireValue(args, ref index, option), out var captureStackId))
                    {
                        throw new CommandLineException("Option '--stack-id' requires a GUID.");
                    }
                    capture = capture with { SourceStackId = captureStackId };
                    break;

                case "--age-recipient":
                    capture = capture with
                    {
                        AgeRecipient = RequireValue(args, ref index, option)
                    };
                    break;

                case "--age-command":
                    capture = capture with
                    {
                        AgeCommand = RequireValue(args, ref index, option)
                    };
                    break;

                case "--capture-id":
                    capture = capture with
                    {
                        CaptureId = RequireValue(args, ref index, option)
                    };
                    break;

                case "--final-frozen":
                    capture = capture with { FinalFrozenCapture = true };
                    index++;
                    break;

                case "--freeze-report":
                    capture = capture with
                    {
                        FreezeReportPath = RequireValue(args, ref index, option)
                    };
                    break;

                case "--resume":
                    capture = capture with { Resume = true };
                    index++;
                    break;

                case "--maximum-entry-bytes":
                    capture = capture with
                    {
                        MaximumEntryBytes = ParseInt64(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--maximum-expanded-bytes":
                    capture = capture with
                    {
                        MaximumExpandedBytes = ParseInt64(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--maximum-entries":
                    capture = capture with
                    {
                        MaximumEntries = ParseInt32(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--required-free-space-multiplier":
                    capture = capture with
                    {
                        RequiredFreeSpaceMultiplier = ParseDouble(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--json":
                    capture = capture with { JsonConsoleOutput = true };
                    index++;
                    break;

                case "--non-interactive":
                    assessment = assessment with { NonInteractive = true };
                    index++;
                    break;

                default:
                    throw new CommandLineException(
                        $"Unknown source capture option '{option}'.");
            }
        }

        capture = capture with { Assessment = assessment };
        return new ParsedCommand(
            "source-capture",
            assessment,
            capture,
            null,
            null,
            null,
            null,
            null,
            showHelp);
    }


    private static ParsedCommand ParsePackageForIntake(string[] args)
    {
        var options = new PackageForIntakeOptions();
        var index = 2;
        var showHelp = false;

        while (index < args.Length)
        {
            var option = args[index];
            switch (option)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    index++;
                    break;
                case "--intake-id":
                    options = options with { IntakeId = RequireValue(args, ref index, option) };
                    break;
                case "--package-revision-id":
                    options = options with { PackageRevisionId = RequireValue(args, ref index, option) };
                    break;
                case "--age-recipient":
                    options = options with { AgeRecipient = RequireValue(args, ref index, option) };
                    break;
                case "--recipient-fingerprint":
                    options = options with { RecipientFingerprint = RequireValue(args, ref index, option) };
                    break;
                case "--archive":
                    options = options with { ArchivePath = RequireValue(args, ref index, option) };
                    break;
                case "--output-directory":
                    options = options with { OutputDirectory = RequireValue(args, ref index, option) };
                    break;
                case "--workspace":
                    options = options with { WorkspacePath = RequireValue(args, ref index, option) };
                    break;
                case "--age-command":
                    options = options with { AgeCommand = RequireValue(args, ref index, option) };
                    break;
                case "--maximum-entry-bytes":
                    options = options with { MaximumEntryBytes = ParseInt64(RequireValue(args, ref index, option), option) };
                    break;
                case "--maximum-expanded-bytes":
                    options = options with { MaximumExpandedBytes = ParseInt64(RequireValue(args, ref index, option), option) };
                    break;
                case "--maximum-entries":
                    options = options with { MaximumEntries = ParseInt32(RequireValue(args, ref index, option), option) };
                    break;
                case "--maximum-compression-ratio":
                    options = options with { MaximumCompressionRatio = ParseDouble(RequireValue(args, ref index, option), option) };
                    break;
                case "--require-final-frozen":
                    options = options with { RequireFinalFrozen = true };
                    index++;
                    break;
                case "--json":
                    options = options with { JsonConsoleOutput = true };
                    index++;
                    break;
                default:
                    throw new CommandLineException(
                        $"Unknown source package-for-intake option '{option}'.");
            }
        }

        return new ParsedCommand(
            "source-package-for-intake",
            new AssessmentOptions(),
            null,
            null,
            null,
            null,
            null,
            null,
            showHelp,
            PackageForIntakeOptions: options);
    }

    private static ParsedCommand ParseArchiveRead(
        string[] args,
        string commandName)
    {
        if (args.Length < 3 || args[2] is "-h" or "--help")
        {
            return new ParsedCommand(
                commandName,
                new AssessmentOptions(),
                null,
                null,
                null,
                null,
                null,
                null,
                ShowHelp: true);
        }

        var options = new ArchiveReadOptions { ArchivePath = args[2] };
        var index = 3;
        var showHelp = false;

        while (index < args.Length)
        {
            var option = args[index];

            switch (option)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    index++;
                    break;

                case "--workspace":
                    options = options with
                    {
                        WorkspacePath = RequireValue(args, ref index, option)
                    };
                    break;

                case "--age-identity":
                    options = options with
                    {
                        AgeIdentityPath = RequireValue(args, ref index, option)
                    };
                    break;

                case "--age-command":
                    options = options with
                    {
                        AgeCommand = RequireValue(args, ref index, option)
                    };
                    break;

                case "--maximum-entry-bytes":
                    options = options with
                    {
                        MaximumEntryBytes = ParseInt64(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--maximum-expanded-bytes":
                    options = options with
                    {
                        MaximumExpandedBytes = ParseInt64(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--maximum-entries":
                    options = options with
                    {
                        MaximumEntries = ParseInt32(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--maximum-compression-ratio":
                    options = options with
                    {
                        MaximumCompressionRatio = ParseDouble(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--json":
                    options = options with { JsonConsoleOutput = true };
                    index++;
                    break;

                default:
                    throw new CommandLineException(
                        $"Unknown archive option '{option}'.");
            }
        }

        return new ParsedCommand(
            commandName,
            new AssessmentOptions(),
            null,
            options,
            null,
            null,
            null,
            null,
            showHelp);
    }

    private static (AssessmentOptions Options, bool ShowHelp)
        ParseAssessmentOptions(string[] args, int startIndex)
    {
        var options = new AssessmentOptions();
        var index = startIndex;
        var showHelp = false;

        while (index < args.Length)
        {
            var option = args[index];

            switch (option)
            {
                case "-h":
                case "--help":
                    showHelp = true;
                    index++;
                    break;

                case "--workspace":
                    options = options with
                    {
                        WorkspacePath = RequireValue(args, ref index, option)
                    };
                    break;

                case "--output":
                    options = options with
                    {
                        OutputPath = RequireValue(args, ref index, option)
                    };
                    break;

                case "--docker-command":
                    options = options with
                    {
                        DockerCommand = RequireValue(args, ref index, option)
                    };
                    break;

                case "--api-url":
                    options = options with
                    {
                        ApiUrl = RequireValue(args, ref index, option)
                    };
                    break;

                case "--postgres-container":
                    options = options with
                    {
                        PostgresContainer = RequireValue(args, ref index, option)
                    };
                    break;

                case "--command-timeout-seconds":
                    options = options with
                    {
                        CommandTimeoutSeconds = ParseInt32(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--http-timeout-seconds":
                    options = options with
                    {
                        HttpTimeoutSeconds = ParseInt32(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--maximum-file-scan-entries":
                    options = options with
                    {
                        MaximumFileScanEntries = ParseInt32(
                            RequireValue(args, ref index, option),
                            option)
                    };
                    break;

                case "--include-sensitive-paths":
                    options = options with { IncludeSensitivePaths = true };
                    index++;
                    break;

                case "--json":
                    options = options with { JsonConsoleOutput = true };
                    index++;
                    break;

                case "--non-interactive":
                    options = options with { NonInteractive = true };
                    index++;
                    break;

                default:
                    throw new CommandLineException(
                        $"Unknown option '{option}'.");
            }
        }

        return (options, showHelp);
    }

    private static ParsedCommand Help() =>
        new(
            "help",
            new AssessmentOptions(),
            null,
            null,
            null,
            null,
            null,
            null,
            ShowHelp: true);

    private static string RequireValue(
        IReadOnlyList<string> args,
        ref int index,
        string option)
    {
        if (index + 1 >= args.Count)
        {
            throw new CommandLineException(
                $"Option '{option}' requires a value.");
        }

        var value = args[index + 1];

        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith("--", StringComparison.Ordinal))
        {
            throw new CommandLineException(
                $"Option '{option}' requires a value.");
        }

        index += 2;
        return value;
    }

    private static int ParseInt32(string value, string option)
    {
        if (!int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new CommandLineException(
                $"Option '{option}' requires an integer.");
        }

        return parsed;
    }

    private static long ParseInt64(string value, string option)
    {
        if (!long.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new CommandLineException(
                $"Option '{option}' requires an integer.");
        }

        return parsed;
    }

    private static double ParseDouble(string value, string option)
    {
        if (!double.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed))
        {
            throw new CommandLineException(
                $"Option '{option}' requires a number.");
        }

        return parsed;
    }
}

internal sealed class CommandLineException(string message)
    : Exception(message);
