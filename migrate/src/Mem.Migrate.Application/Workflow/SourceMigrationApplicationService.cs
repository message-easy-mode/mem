using System.Text.Json;
using Mem.Migrate.Application.Assessment;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Fingerprints;
using Mem.Migrate.Core.Security;

namespace Mem.Migrate.Application.Workflow;

public interface ISourceMigrationApplicationService
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<SourceWorkflowView?> GetLatestAsync(CancellationToken cancellationToken);

    Task<SourceWorkflowView?> GetAsync(
        string workflowId,
        CancellationToken cancellationToken);

    Task<SourceWorkflowOverviewView> GetOverviewAsync(
        string selectedSourceStackId,
        int limit,
        bool operationRunning,
        CancellationToken cancellationToken);

    Task<SourceWorkflowImportResult> ImportRequestAsync(
        SecureIntakeRequestInput input,
        SourceAssessmentView assessment,
        string selectedSourceStackId,
        bool encryptionReadinessAcknowledged,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceCaptureOptionView>> ListCapturesAsync(
        string workflowId,
        CancellationToken cancellationToken);

    Task<SourceWorkflowView> SelectCaptureAsync(
        string workflowId,
        string captureId,
        CancellationToken cancellationToken);

    Task RunCaptureAsync(
        string workflowId,
        CancellationToken cancellationToken);

    Task RunPackageAsync(
        string workflowId,
        CancellationToken cancellationToken);

    Task<SourceWorkflowView> RequestCancellationAsync(
        string workflowId,
        CancellationToken cancellationToken);
}

public sealed class SourceMigrationApplicationService(
    AssessmentOptions assessmentOptions,
    string producerVersion,
    ISourceWorkflowJournal journal,
    ISourceCaptureService captureService,
    ISourceCaptureArchiveDiscovery captureDiscovery,
    IPackageForIntakeService packageService,
    SecureIntakeRequestValidator requestValidator,
    TimeProvider? timeProvider = null) :
    ISourceMigrationApplicationService,
    ISourcePackageLifecycleService
{
    private readonly AssessmentOptions _assessmentOptions = assessmentOptions.Normalize();
    private readonly string _producerVersion = string.IsNullOrWhiteSpace(producerVersion)
        ? "unknown"
        : producerVersion.Trim();
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _journalLock = new(1, 1);

    public Task InitializeAsync(CancellationToken cancellationToken) =>
        journal.InitializeAsync(cancellationToken);

    public async Task<SourceWorkflowView?> GetLatestAsync(
        CancellationToken cancellationToken)
    {
        var record = await journal.GetLatestAsync(cancellationToken);
        return record is null ? null : Project(record);
    }

    public async Task<SourceWorkflowView?> GetAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        ValidateWorkflowId(workflowId);
        var record = await journal.GetAsync(workflowId, cancellationToken);
        return record is null ? null : Project(record);
    }

    public async Task<SourceWorkflowOverviewView> GetOverviewAsync(
        string selectedSourceStackId,
        int limit,
        bool operationRunning,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selectedSourceStackId))
        {
            throw new ArgumentException(
                "A selected source stack ID is required.",
                nameof(selectedSourceStackId));
        }

        if (limit is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "The recent workflow limit must be between 1 and 10.");
        }

        var sourceStackId = selectedSourceStackId.Trim();
        var records = await journal.ListRecentAsync(
            sourceStackId,
            limit,
            cancellationToken);
        var projected = records
            .Select(record => new WorkflowProjection(record, Project(record)))
            .ToArray();

        var current = projected.FirstOrDefault(candidate =>
            IsCurrentWorkflow(candidate.Record, candidate.View));
        var durableRunning = projected.Any(candidate =>
            candidate.Record.Status == SourceWorkflowStatus.Running);
        var activeOperation = operationRunning || durableRunning;

        return new SourceWorkflowOverviewView(
            SchemaVersion: 1,
            SourceStackId: sourceStackId,
            OperationRunning: activeOperation,
            CanStartNewWorkflow: !activeOperation,
            StartNewBlockedReason: activeOperation
                ? "A source capture or packaging operation is already running. Continue that workflow before starting another migration."
                : null,
            CurrentWorkflow: current?.View,
            PreviousWorkflows: projected
                .Where(candidate => !ReferenceEquals(candidate, current))
                .Select(candidate => candidate.View)
                .ToArray());
    }

    public async Task<SourceWorkflowImportResult> ImportRequestAsync(
        SecureIntakeRequestInput input,
        SourceAssessmentView assessment,
        string selectedSourceStackId,
        bool encryptionReadinessAcknowledged,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        if (!encryptionReadinessAcknowledged)
        {
            throw new ArgumentException(
                "Acknowledge the encrypted-message recovery warning before preparing a source package.");
        }

        if (!assessment.CanProceedToCapture)
        {
            throw new SourceWorkflowConflictException(
                "The latest source assessment does not permit capture.");
        }

        var selectedStack = assessment.Stacks.FirstOrDefault(stack =>
            string.Equals(
                stack.SourceStackId,
                selectedSourceStackId,
                StringComparison.OrdinalIgnoreCase));
        if (selectedStack is null)
        {
            throw new ArgumentException(
                "Select a stack from the latest source assessment before importing a migration request.");
        }

        if (!selectedStack.SourceFilesReady)
        {
            throw new SourceWorkflowConflictException(
                "The selected stack source files require review before capture.");
        }

        var request = requestValidator.Validate(input);
        if (request.SourceStackId is not null &&
            !string.Equals(
                request.SourceStackId,
                selectedStack.SourceStackId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The target migration request expects a different source stack.");
        }

        await _journalLock.WaitAsync(cancellationToken);
        try
        {
            var latest = await journal.GetLatestAsync(cancellationToken);
            if (latest?.Status == SourceWorkflowStatus.Running)
            {
                throw new SourceWorkflowConflictException(
                    "A source capture or packaging operation is already running.");
            }

            var now = _timeProvider.GetUtcNow();
            var stableSourceIdentity = ComputeSelectedSourceIdentity(
                assessment,
                selectedStack);
            var record = new SourceWorkflowRecord(
                WorkflowId: $"source-{CaptureIdentifier.Create(now)}",
                Status: SourceWorkflowStatus.Pending,
                Stage: SourceWorkflowStage.RequestValidated,
                AssessmentId: assessment.AssessmentId,
                SourceFingerprint: assessment.SourceFingerprint,
                SelectedSourceStackId: selectedStack.SourceStackId,
                IntakeId: request.IntakeId,
                PackageRevisionId: request.PackageRevisionId,
                RequestKind: request.RequestKind,
                AgeRecipient: request.AgeRecipient,
                RecipientFingerprint: request.RecipientFingerprint,
                RequestExpiresAtUtc: request.ExpiresAtUtc,
                TargetControlPlaneVersion: request.TargetControlPlaneVersion,
                EncryptionReadinessAcknowledgedAtUtc: now,
                CaptureId: null,
                PackageReportJson: null,
                CreatedAtUtc: now,
                UpdatedAtUtc: now,
                CompletedAtUtc: null,
                CancellationRequestedAtUtc: null,
                FailureCode: null,
                FailureSummary: null,
                Revision: 1,
                StableSourceIdentity: stableSourceIdentity);
            await journal.SaveAsync(record, cancellationToken);
            return new SourceWorkflowImportResult(
                Project(record),
                await ListCapturesCoreAsync(record, cancellationToken));
        }
        finally
        {
            _journalLock.Release();
        }
    }

    public async Task<IReadOnlyList<SourceCaptureOptionView>> ListCapturesAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        var record = await RequireAsync(workflowId, cancellationToken);
        return await ListCapturesCoreAsync(record, cancellationToken);
    }

    public async Task<SourceWorkflowView> SelectCaptureAsync(
        string workflowId,
        string captureId,
        CancellationToken cancellationToken)
    {
        await _journalLock.WaitAsync(cancellationToken);
        try
        {
            var record = await RequireAsync(workflowId, cancellationToken);
            EnsureNotRunning(record);
            EnsureNotCompleted(record);
            EnsureRequestCurrent(record);
            var capture = await captureDiscovery.ResolveEligibleArchiveAsync(
                captureId,
                cancellationToken,
                RequireFinalFrozen(record));
            EnsureCaptureMatchesSource(record, capture);

            var updated = record with
            {
                Status = SourceWorkflowStatus.Pending,
                Stage = SourceWorkflowStage.CaptureSelected,
                CaptureId = capture.CaptureId,
                PackageReportJson = null,
                UpdatedAtUtc = _timeProvider.GetUtcNow(),
                CompletedAtUtc = null,
                FailureCode = null,
                FailureSummary = null,
                Revision = record.Revision + 1
            };
            await journal.SaveAsync(updated, cancellationToken);
            return Project(updated);
        }
        finally
        {
            _journalLock.Release();
        }
    }

    public async Task RunCaptureAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        SourceWorkflowRecord running;
        await _journalLock.WaitAsync(cancellationToken);
        try
        {
            var record = await RequireAsync(workflowId, cancellationToken);
            EnsureNotRunning(record);
            EnsureNotCompleted(record);
            EnsureRequestCurrent(record);
            if (RequireFinalFrozen(record))
            {
                throw new SourceWorkflowConflictException(
                    "A fresh final frozen capture requires the controlled source-freeze workflow and cannot be started from this screen.");
            }

            var now = _timeProvider.GetUtcNow();
            running = record with
            {
                Status = SourceWorkflowStatus.Running,
                Stage = SourceWorkflowStage.Capturing,
                CaptureId = CaptureIdentifier.Create(now),
                PackageReportJson = null,
                UpdatedAtUtc = now,
                CompletedAtUtc = null,
                CancellationRequestedAtUtc = null,
                FailureCode = null,
                FailureSummary = null,
                Revision = record.Revision + 1
            };
            await journal.SaveAsync(running, cancellationToken);
        }
        finally
        {
            _journalLock.Release();
        }

        try
        {
            var captureOutput = Path.Combine(_assessmentOptions.OutputPath, "captures");
            PrivateFilePermissions.EnsureDirectory(captureOutput);
            var report = await captureService.CaptureAsync(
                new CaptureOptions
                {
                    Assessment = _assessmentOptions,
                    OutputPath = captureOutput,
                    ExpectedSourceFingerprint = string.IsNullOrWhiteSpace(running.StableSourceIdentity)
                        ? running.SourceFingerprint
                        : null,
                    ExpectedSourceIdentity = running.StableSourceIdentity,
                    SourceStackId = ParseSelectedSourceStackId(running.SelectedSourceStackId),
                    CaptureId = running.CaptureId,
                    ProducerVersion = _producerVersion,
                    JsonConsoleOutput = false,
                    FinalFrozenCapture = false
                },
                cancellationToken);

            var completed = running with
            {
                Status = SourceWorkflowStatus.Pending,
                Stage = SourceWorkflowStage.CaptureReady,
                CaptureId = report.CaptureId,
                SourceFingerprint = report.StartSourceFingerprint,
                StableSourceIdentity = report.StableSourceIdentity ?? running.StableSourceIdentity,
                UpdatedAtUtc = _timeProvider.GetUtcNow(),
                FailureCode = null,
                FailureSummary = null,
                Revision = running.Revision + 1
            };
            await journal.SaveAsync(completed, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await SaveTerminalAsync(
                running,
                SourceWorkflowStatus.Cancelled,
                "capture_cancelled",
                "The source capture was cancelled. Incomplete owned artifacts were removed by the capture service.",
                clearCapture: true);
        }
        catch (Exception exception)
        {
            await SaveTerminalAsync(
                running,
                SourceWorkflowStatus.Failed,
                "capture_failed",
                AssessmentRedactor.RedactText(exception.Message),
                clearCapture: true);
        }
    }

    public async Task RunPackageAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        SourceWorkflowRecord running;
        EligibleSourceCapture capture;
        await _journalLock.WaitAsync(cancellationToken);
        try
        {
            var record = await RequireAsync(workflowId, cancellationToken);
            EnsureNotRunning(record);
            EnsureNotCompleted(record);
            EnsureRequestCurrent(record);
            if (string.IsNullOrWhiteSpace(record.CaptureId))
            {
                throw new SourceWorkflowConflictException(
                    "Select or create an eligible source capture first.");
            }

            capture = await captureDiscovery.ResolveEligibleArchiveAsync(
                record.CaptureId,
                cancellationToken,
                RequireFinalFrozen(record));
            EnsureCaptureMatchesSource(record, capture);

            running = record with
            {
                Status = SourceWorkflowStatus.Running,
                Stage = SourceWorkflowStage.Packaging,
                PackageReportJson = null,
                UpdatedAtUtc = _timeProvider.GetUtcNow(),
                CompletedAtUtc = null,
                CancellationRequestedAtUtc = null,
                FailureCode = null,
                FailureSummary = null,
                Revision = record.Revision + 1
            };
            await journal.SaveAsync(running, cancellationToken);
        }
        finally
        {
            _journalLock.Release();
        }

        try
        {
            var packageOutput = Path.Combine(_assessmentOptions.OutputPath, "packages");
            PrivateFilePermissions.EnsureDirectory(packageOutput);
            var report = await packageService.PackageAsync(
                new PackageForIntakeOptions
                {
                    IntakeId = running.IntakeId,
                    PackageRevisionId = running.PackageRevisionId ?? string.Empty,
                    AgeRecipient = running.AgeRecipient,
                    RecipientFingerprint = running.RecipientFingerprint,
                    SourceStackId = ParseSelectedSourceStackId(running.SelectedSourceStackId),
                    ArchivePath = capture.ArchivePath,
                    OutputDirectory = packageOutput,
                    WorkspacePath = _assessmentOptions.WorkspacePath,
                    RequireFinalFrozen = RequireFinalFrozen(running),
                    JsonConsoleOutput = false
                },
                cancellationToken);

            var completedAtUtc = _timeProvider.GetUtcNow();
            var completed = running with
            {
                Status = SourceWorkflowStatus.Completed,
                Stage = SourceWorkflowStage.ReadyForDownload,
                PackageReportJson = JsonSerializer.Serialize(report, CaptureJson.Options),
                UpdatedAtUtc = completedAtUtc,
                CompletedAtUtc = completedAtUtc,
                FailureCode = null,
                FailureSummary = null,
                Revision = running.Revision + 1
            };
            await journal.SaveAsync(completed, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await SaveTerminalAsync(
                running,
                SourceWorkflowStatus.Cancelled,
                "package_cancelled",
                "Package creation was cancelled. Partial encrypted output was removed.");
        }
        catch (Exception exception)
        {
            await SaveTerminalAsync(
                running,
                SourceWorkflowStatus.Failed,
                "package_failed",
                AssessmentRedactor.RedactText(exception.Message));
        }
    }

    public async Task<SourcePackageDownloadDescriptor> GetPackageDownloadAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        var record = await RequireAsync(workflowId, cancellationToken);
        EnsurePackageNotDeleted(record);
        var report = RequirePackageReport(record);
        var packagePath = ResolveOwnedPackagePath(
            report.EncryptedPackagePath,
            ".memmigration.zip.age");

        EnsureAvailablePackageFile(packagePath, report.EncryptedPackageBytes);

        return new SourcePackageDownloadDescriptor(
            FullPath: packagePath,
            FileName: Path.GetFileName(packagePath),
            ContentType: "application/octet-stream",
            SizeBytes: report.EncryptedPackageBytes,
            Sha256: report.EncryptedPackageSha256,
            LastModifiedUtc: File.GetLastWriteTimeUtc(packagePath));
    }

    public async Task<SourcePackageReportDescriptor> GetPackageReportAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        var record = await RequireAsync(workflowId, cancellationToken);
        EnsurePackageNotDeleted(record);
        var report = RequirePackageReport(record);
        var packagePath = ResolveOwnedPackagePath(
            report.EncryptedPackagePath,
            ".memmigration.zip.age");

        EnsureAvailablePackageFile(packagePath, report.EncryptedPackageBytes);

        var jsonReportPath = ResolveOwnedPackagePath(
            report.JsonReportPath,
            ".package-report.json");
        var markdownReportPath = ResolveOwnedPackagePath(
            report.MarkdownReportPath,
            ".package-report.md");
        if (!IsSafeRegularFile(jsonReportPath) ||
            !IsSafeRegularFile(markdownReportPath))
        {
            throw new SourceWorkflowConflictException(
                "The local package reports are no longer available.");
        }

        var fileName = Path.GetFileName(markdownReportPath);
        var markdown = RenderSafePackageReport(report);
        return new SourcePackageReportDescriptor(
            FileName: fileName,
            ContentType: "text/markdown; charset=utf-8",
            Contents: System.Text.Encoding.UTF8.GetBytes(
                markdown + Environment.NewLine));
    }

    public async Task<SourceWorkflowView> DeletePackageAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        await _journalLock.WaitAsync(cancellationToken);
        try
        {
            var record = await RequireAsync(workflowId, cancellationToken);
            EnsureNotRunning(record);
            var report = RequirePackageReport(record);
            var packagePath = ResolveOwnedPackagePath(
                report.EncryptedPackagePath,
                ".memmigration.zip.age");
            var jsonReportPath = ResolveOwnedPackagePath(
                report.JsonReportPath,
                ".package-report.json");
            var markdownReportPath = ResolveOwnedPackagePath(
                report.MarkdownReportPath,
                ".package-report.md");

            var existingPaths = new[]
            {
                packagePath,
                jsonReportPath,
                markdownReportPath
            }
            .Where(File.Exists)
            .ToArray();

            if (existingPaths.Length == 0)
            {
                throw new SourceWorkflowConflictException(
                    "The local encrypted package and its reports are already absent.");
            }

            foreach (var path in existingPaths)
            {
                UnixFileTypeSafety.EnsureRegularFile(path);
            }

            var failures = new List<string>();
            foreach (var path in existingPaths)
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    failures.Add(Path.GetFileName(path));
                }
            }

            if (failures.Count > 0)
            {
                throw new IOException(
                    "The Source Assistant could not delete all local package artifacts: " +
                    string.Join(", ", failures));
            }

            var now = _timeProvider.GetUtcNow();
            var updated = record with
            {
                Status = SourceWorkflowStatus.Completed,
                Stage = SourceWorkflowStage.PackageDeleted,
                UpdatedAtUtc = now,
                FailureCode = null,
                FailureSummary = null,
                Revision = record.Revision + 1
            };
            await journal.SaveAsync(updated, CancellationToken.None);
            return Project(updated);
        }
        finally
        {
            _journalLock.Release();
        }
    }

    public async Task<SourceWorkflowView> RequestCancellationAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        await _journalLock.WaitAsync(cancellationToken);
        try
        {
            var record = await RequireAsync(workflowId, cancellationToken);
            if (record.Status != SourceWorkflowStatus.Running)
            {
                throw new SourceWorkflowConflictException(
                    "No source capture or packaging operation is currently running.");
            }

            var updated = record with
            {
                CancellationRequestedAtUtc = _timeProvider.GetUtcNow(),
                UpdatedAtUtc = _timeProvider.GetUtcNow(),
                Revision = record.Revision + 1
            };
            await journal.SaveAsync(updated, cancellationToken);
            return Project(updated);
        }
        finally
        {
            _journalLock.Release();
        }
    }

    private async Task<IReadOnlyList<SourceCaptureOptionView>> ListCapturesCoreAsync(
        SourceWorkflowRecord record,
        CancellationToken cancellationToken)
    {
        var captures = await captureDiscovery.ListEligibleArchivesAsync(
            cancellationToken,
            RequireFinalFrozen(record));
        var selectedSourceStackId = ParseSelectedSourceStackId(record.SelectedSourceStackId);
        return captures
            .Where(capture => capture.SourceStackId == selectedSourceStackId)
            .Select(capture => new SourceCaptureOptionView(
                capture.CaptureId,
                capture.SourceStackSlug,
                capture.MatrixServerName,
                capture.CompletedAtUtc,
                capture.ArchiveBytes,
                capture.ArchiveSha256,
                capture.SourceFingerprint,
                capture.RehearsalOnly ? "preview" : "final",
                CaptureMatchesSourceIdentity(record, capture),
                string.Equals(
                    capture.CaptureId,
                    record.CaptureId,
                    StringComparison.Ordinal)))
            .ToArray();
    }

    private async Task<SourceWorkflowRecord> RequireAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        ValidateWorkflowId(workflowId);
        return await journal.GetAsync(workflowId, cancellationToken)
            ?? throw new SourceWorkflowNotFoundException(
                "The source workflow was not found.");
    }

    private static void ValidateWorkflowId(string workflowId)
    {
        const string prefix = "source-";
        if (string.IsNullOrWhiteSpace(workflowId) ||
            !workflowId.StartsWith(prefix, StringComparison.Ordinal) ||
            !CaptureIdentifier.IsValid(workflowId[prefix.Length..]))
        {
            throw new ArgumentException(
                "Workflow ID contains unsupported characters.",
                nameof(workflowId));
        }
    }

    private static void EnsurePackageNotDeleted(
        SourceWorkflowRecord record)
    {
        if (record.Stage == SourceWorkflowStage.PackageDeleted)
        {
            throw new SourceWorkflowConflictException(
                "The local encrypted package was deleted from this source server.");
        }
    }

    private PackageForIntakeReport RequirePackageReport(
        SourceWorkflowRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.PackageReportJson))
        {
            throw new SourceWorkflowConflictException(
                "This workflow has not produced an encrypted package.");
        }

        return JsonSerializer.Deserialize<PackageForIntakeReport>(
                record.PackageReportJson,
                CaptureJson.Options)
            ?? throw new InvalidDataException(
                "The source package report could not be read.");
    }

    private string ResolveOwnedPackagePath(
        string path,
        string expectedSuffix)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidDataException(
                "The source package report did not identify a local artifact.");
        }

        var root = Path.GetFullPath(
            Path.Combine(_assessmentOptions.OutputPath, "packages"));
        PrivateFilePermissions.EnsureDirectory(root);
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(
                Path.GetDirectoryName(fullPath),
                root,
                comparison))
        {
            throw new InvalidDataException(
                "The source package artifact is outside the configured package root.");
        }

        if (!Path.GetFileName(fullPath).EndsWith(
                expectedSuffix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The source package artifact has an unexpected file type.");
        }

        return fullPath;
    }

    private static void EnsureAvailablePackageFile(
        string packagePath,
        long expectedBytes)
    {
        if (!File.Exists(packagePath))
        {
            throw new SourceWorkflowConflictException(
                "The local encrypted package is no longer available.");
        }

        UnixFileTypeSafety.EnsureRegularFile(packagePath);
        var actualBytes = new FileInfo(packagePath).Length;
        if (actualBytes != expectedBytes)
        {
            throw new InvalidDataException(
                "The local encrypted package size no longer matches its durable evidence.");
        }
    }

    private PackageArtifactDisposition InspectPackageArtifacts(
        SourceWorkflowRecord record,
        PackageForIntakeReport report)
    {
        if (record.Stage == SourceWorkflowStage.PackageDeleted)
        {
            return new PackageArtifactDisposition(
                "Deleted",
                PackageFileAvailable: false,
                ReportAvailable: false,
                CanDelete: false);
        }

        try
        {
            var packagePath = ResolveOwnedPackagePath(
                report.EncryptedPackagePath,
                ".memmigration.zip.age");
            var jsonReportPath = ResolveOwnedPackagePath(
                report.JsonReportPath,
                ".package-report.json");
            var markdownReportPath = ResolveOwnedPackagePath(
                report.MarkdownReportPath,
                ".package-report.md");

            var packageState = InspectArtifactFile(
                packagePath,
                report.EncryptedPackageBytes);
            var jsonReportState = InspectArtifactFile(jsonReportPath);
            var markdownReportState = InspectArtifactFile(markdownReportPath);

            if (packageState == ArtifactFileState.Unsafe ||
                jsonReportState == ArtifactFileState.Unsafe ||
                markdownReportState == ArtifactFileState.Unsafe)
            {
                return new PackageArtifactDisposition(
                    "Unsafe",
                    PackageFileAvailable: false,
                    ReportAvailable: false,
                    CanDelete: false);
            }

            var packageAvailable = packageState == ArtifactFileState.Safe;
            var reportAvailable =
                jsonReportState == ArtifactFileState.Safe &&
                markdownReportState == ArtifactFileState.Safe;
            var anyArtifactExists =
                packageState == ArtifactFileState.Safe ||
                jsonReportState == ArtifactFileState.Safe ||
                markdownReportState == ArtifactFileState.Safe;

            var state = packageAvailable
                ? "Available"
                : anyArtifactExists
                    ? "Partial"
                    : "Missing";

            return new PackageArtifactDisposition(
                state,
                packageAvailable,
                packageAvailable && reportAvailable,
                anyArtifactExists);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new PackageArtifactDisposition(
                "Unsafe",
                PackageFileAvailable: false,
                ReportAvailable: false,
                CanDelete: false);
        }
    }

    private static bool IsSafeRegularFile(
        string path,
        long? expectedBytes = null) =>
        InspectArtifactFile(path, expectedBytes) == ArtifactFileState.Safe;

    private static ArtifactFileState InspectArtifactFile(
        string path,
        long? expectedBytes = null)
    {
        if (!File.Exists(path))
        {
            return ArtifactFileState.Missing;
        }

        try
        {
            UnixFileTypeSafety.EnsureRegularFile(path);
            if (expectedBytes is not null &&
                new FileInfo(path).Length != expectedBytes.Value)
            {
                return ArtifactFileState.Unsafe;
            }

            return ArtifactFileState.Safe;
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return ArtifactFileState.Unsafe;
        }
    }

    private static string RenderSafePackageReport(
        PackageForIntakeReport report) =>
        $"""
        # MEM Migration Encrypted Package

        - Intake ID: `{report.IntakeId}`
        - Package revision ID: `{report.PackageRevisionId ?? "not supplied"}`
        - Completed UTC: `{report.CompletedAtUtc:O}`
        - Recipient fingerprint: `{report.RecipientFingerprint}`
        - Migration ID: `{report.MigrationId}`
        - Capture kind: `{report.CaptureKind}`
        - Source frozen: `{report.SourceFrozen}`
        - Rehearsal only: `{report.RehearsalOnly}`
        - Stacks in archive: `{report.StackCount}`
        - Source archive SHA-256: `{report.SourceArchiveSha256}`
        - Verified files: `{report.VerifiedFileCount}`
        - Verified expanded bytes: `{report.VerifiedExpandedBytes}`
        - Encrypted package filename: `{Path.GetFileName(report.EncryptedPackagePath)}`
        - Encrypted package SHA-256: `{report.EncryptedPackageSha256}`
        - Encrypted package bytes: `{report.EncryptedPackageBytes}`

        This browser-safe report omits source and target filesystem paths.
        The target private age identity and Matrix E2EE recovery secrets are not present.
        """;

    private enum ArtifactFileState
    {
        Missing,
        Safe,
        Unsafe
    }

    private sealed record PackageArtifactDisposition(
        string LocalState,
        bool PackageFileAvailable,
        bool ReportAvailable,
        bool CanDelete);

    private bool IsCurrentWorkflow(
        SourceWorkflowRecord record,
        SourceWorkflowView view)
    {
        if (record.Status == SourceWorkflowStatus.Running)
        {
            return true;
        }

        if (record.Status is SourceWorkflowStatus.Completed or
            SourceWorkflowStatus.Cancelled)
        {
            return false;
        }

        if (record.Stage == SourceWorkflowStage.PackageDeleted ||
            record.RequestExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            return false;
        }

        return view.Actions.CanCreatePreviewCapture ||
            view.Actions.CanSelectCapture ||
            view.Actions.CanCreatePackage ||
            view.Actions.CanCancel;
    }

    private void EnsureRequestCurrent(SourceWorkflowRecord record)
    {
        if (record.RequestExpiresAtUtc <= _timeProvider.GetUtcNow())
        {
            throw new SourceWorkflowConflictException(
                "The target migration request has expired. Import a fresh request before continuing.");
        }
    }

    private static void EnsureNotRunning(SourceWorkflowRecord record)
    {
        if (record.Status == SourceWorkflowStatus.Running)
        {
            throw new SourceWorkflowConflictException(
                "A source capture or packaging operation is already running.");
        }
    }

    private static void EnsureNotCompleted(SourceWorkflowRecord record)
    {
        if (record.Status == SourceWorkflowStatus.Completed &&
            !string.IsNullOrWhiteSpace(record.PackageReportJson))
        {
            throw new SourceWorkflowConflictException(
                "This workflow already produced an encrypted package. Import a fresh target request to create another package.");
        }
    }

    private static void EnsureCaptureMatchesSource(
        SourceWorkflowRecord record,
        EligibleSourceCapture capture)
    {
        if (!CaptureMatchesSourceIdentity(record, capture))
        {
            throw new SourceWorkflowConflictException(
                string.IsNullOrWhiteSpace(record.StableSourceIdentity)
                    ? "The selected capture belongs to a different source fingerprint. Run a fresh source assessment and capture."
                    : "The selected capture belongs to a different selected source identity. Run a fresh source assessment and capture.");
        }

        if (capture.SourceStackId != ParseSelectedSourceStackId(record.SelectedSourceStackId))
        {
            throw new SourceWorkflowConflictException(
                "The selected capture belongs to a different source stack. Create or select a capture for the current stack.");
        }
    }

    private static bool CaptureMatchesSourceIdentity(
        SourceWorkflowRecord record,
        EligibleSourceCapture capture)
    {
        if (!string.IsNullOrWhiteSpace(record.StableSourceIdentity) &&
            !string.IsNullOrWhiteSpace(capture.StableSourceIdentity))
        {
            return string.Equals(
                record.StableSourceIdentity,
                capture.StableSourceIdentity,
                StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(
            record.SourceFingerprint,
            capture.SourceFingerprint,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSelectedSourceIdentity(
        SourceAssessmentView assessment,
        SourceStackSummary selectedStack)
    {
        if (!Guid.TryParse(selectedStack.SourceStackId, out var stackId) ||
            stackId == Guid.Empty)
        {
            throw new SourceWorkflowConflictException(
                "The selected source stack identity is invalid. Select the source stack again before continuing.");
        }

        return SelectedSourceIdentity.Compute(new SelectedSourceIdentityInput(
            ProductName: assessment.Runtime.ProductName,
            ProductVersion: assessment.Runtime.ProductVersion,
            SourceStackId: stackId,
            Slug: selectedStack.Slug,
            MatrixServerName: selectedStack.MatrixServerName,
            MatrixPublicHost: selectedStack.MatrixPublicHost,
            ElementPublicHost: selectedStack.ElementPublicHost));
    }

    private static Guid ParseSelectedSourceStackId(string value)
    {
        if (!Guid.TryParse(value, out var stackId) || stackId == Guid.Empty)
        {
            throw new SourceWorkflowConflictException(
                "The selected source stack identity is invalid. Select the source stack again before continuing.");
        }

        return stackId;
    }

    private static bool RequireFinalFrozen(SourceWorkflowRecord record) =>
        string.Equals(record.RequestKind, "final", StringComparison.Ordinal);

    private async Task SaveTerminalAsync(
        SourceWorkflowRecord running,
        SourceWorkflowStatus status,
        string failureCode,
        string failureSummary,
        bool clearCapture = false)
    {
        var now = _timeProvider.GetUtcNow();
        await journal.SaveAsync(
            running with
            {
                Status = status,
                CaptureId = clearCapture ? null : running.CaptureId,
                UpdatedAtUtc = now,
                CompletedAtUtc = now,
                FailureCode = failureCode,
                FailureSummary = failureSummary,
                Revision = running.Revision + 1
            },
            CancellationToken.None);
    }

    private sealed record WorkflowProjection(
        SourceWorkflowRecord Record,
        SourceWorkflowView View);

    private SourceWorkflowView Project(SourceWorkflowRecord record)
    {
        PackageForIntakeReport? package = null;
        if (!string.IsNullOrWhiteSpace(record.PackageReportJson))
        {
            package = JsonSerializer.Deserialize<PackageForIntakeReport>(
                record.PackageReportJson,
                CaptureJson.Options);
        }

        var disposition = package is null
            ? null
            : InspectPackageArtifacts(record, package);
        var expired = record.RequestExpiresAtUtc <= _timeProvider.GetUtcNow();
        var running = record.Status == SourceWorkflowStatus.Running;
        var final = RequireFinalFrozen(record);
        var hasCapture = !string.IsNullOrWhiteSpace(record.CaptureId);
        var packageComplete = package is not null;
        var blockedReason = packageComplete
            ? "This workflow already produced an encrypted package."
            : expired
            ? "The target migration request has expired."
            : running
                ? "Wait for the active source operation to finish or cancel it."
                : final && !hasCapture
                    ? "A final request requires an eligible frozen capture created by the controlled freeze workflow."
                    : null;

        return new SourceWorkflowView(
            SchemaVersion: 1,
            WorkflowId: record.WorkflowId,
            Status: record.Status.ToString(),
            Stage: record.Stage.ToString(),
            AssessmentId: record.AssessmentId,
            SelectedSourceStackId: record.SelectedSourceStackId,
            Request: new SourceWorkflowRequestView(
                record.IntakeId,
                record.PackageRevisionId,
                record.RequestKind,
                record.RecipientFingerprint,
                record.RequestExpiresAtUtc,
                record.TargetControlPlaneVersion),
            EncryptionReadinessAcknowledgedAtUtc: record.EncryptionReadinessAcknowledgedAtUtc,
            CaptureId: record.CaptureId,
            Package: package is null
                ? null
                : new SourcePackageView(
                    Path.GetFileName(package.EncryptedPackagePath),
                    package.EncryptedPackageBytes,
                    package.EncryptedPackageSha256,
                    package.CompletedAtUtc,
                    package.CaptureKind,
                    package.SourceFrozen,
                    package.RehearsalOnly,
                    package.StackCount,
                    disposition?.LocalState ?? "Missing",
                    disposition?.PackageFileAvailable ?? false,
                    disposition?.ReportAvailable ?? false),
            CreatedAtUtc: record.CreatedAtUtc,
            UpdatedAtUtc: record.UpdatedAtUtc,
            CompletedAtUtc: record.CompletedAtUtc,
            FailureCode: record.FailureCode,
            FailureSummary: record.FailureSummary,
            Actions: new SourceWorkflowActions(
                CanCreatePreviewCapture: !packageComplete && !expired && !running && !final,
                CanSelectCapture: !packageComplete && !expired && !running,
                CanCreatePackage: !packageComplete && !expired && !running && hasCapture,
                CanCancel: running,
                CanDownloadPackage:
                    packageComplete &&
                    !running &&
                    disposition?.PackageFileAvailable == true,
                CanDownloadReport:
                    packageComplete &&
                    !running &&
                    disposition?.ReportAvailable == true,
                CanDeletePackage:
                    packageComplete &&
                    !running &&
                    disposition?.CanDelete == true,
                BlockedReason: blockedReason));
    }
}
