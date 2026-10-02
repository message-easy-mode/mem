using Mem.Migrate.Application.Assessment;
using Mem.Migrate.Application.Workflow;
using Mem.Migrate.Core.Assessment;
using Mem.Migrate.Core.Capture;
using Mem.Migrate.Core.Fingerprints;

namespace Mem.Migrate.Web.Tests;

public sealed class SourceMigrationApplicationServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
    private const string Recipient =
        "age1r5cmtjs7qft4w44jqh0y4w5w23jlcccztfrlqq8x3r2g8r60vqkqnkvu6f";

    [Fact]
    public async Task Imports_request_requires_encryption_readiness_and_selects_matching_capture()
    {
        var stackId = Guid.NewGuid().ToString("D");
        var fingerprint = new string('a', 64);
        var journal = new FakeJournal();
        var discovery = new FakeDiscovery(new EligibleSourceCapture(
            "capture-01",
            Guid.Parse(stackId),
            "tester",
            "matrix.example.test",
            Now.AddMinutes(-5),
            "/private/capture.memmigration.zip",
            4096,
            new string('b', 64),
            fingerprint,
            RehearsalOnly: true));
        var service = CreateService(journal, discovery);
        var assessment = NewAssessment(stackId, fingerprint);
        var input = NewRequest(stackId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ImportRequestAsync(
                input,
                assessment,
                stackId,
                encryptionReadinessAcknowledged: false,
                CancellationToken.None));

        var imported = await service.ImportRequestAsync(
            input,
            assessment,
            stackId,
            encryptionReadinessAcknowledged: true,
            CancellationToken.None);

        Assert.Equal("RequestValidated", imported.Workflow.Stage);
        Assert.Single(imported.Captures);
        Assert.True(imported.Captures[0].EligibleForRequest);

        var selected = await service.SelectCaptureAsync(
            imported.Workflow.WorkflowId,
            "capture-01",
            CancellationToken.None);

        Assert.Equal("capture-01", selected.CaptureId);
        Assert.True(selected.Actions.CanCreatePackage);
    }


    [Fact]
    public async Task Fresh_capture_is_bound_to_the_selected_source_stack()
    {
        var stackId = Guid.NewGuid().ToString("D");
        var fingerprint = new string('a', 64);
        var journal = new FakeJournal();
        var discovery = new FakeDiscovery(new EligibleSourceCapture(
            "capture-existing",
            Guid.Parse(stackId),
            "tester",
            "matrix.example.test",
            Now.AddMinutes(-5),
            "/private/capture.memmigration.zip",
            4096,
            new string('b', 64),
            fingerprint,
            RehearsalOnly: true));
        var captureService = new RecordingCaptureService();
        var service = CreateService(
            journal,
            discovery,
            captureService: captureService);
        var imported = await service.ImportRequestAsync(
            NewRequest(stackId),
            NewAssessment(stackId, fingerprint),
            stackId,
            true,
            CancellationToken.None);

        await service.RunCaptureAsync(
            imported.Workflow.WorkflowId,
            CancellationToken.None);

        Assert.NotNull(captureService.LastOptions);
        Assert.Equal(Guid.Parse(stackId), captureService.LastOptions.SourceStackId);
        Assert.Null(captureService.LastOptions.ExpectedSourceFingerprint);
        Assert.Equal(
            StableIdentity(stackId),
            captureService.LastOptions.ExpectedSourceIdentity);
        var latest = await service.GetLatestAsync(CancellationToken.None);
        Assert.Equal("CaptureReady", latest?.Stage);
    }

    [Fact]
    public async Task Matching_stable_identity_allows_a_preview_capture_after_exact_source_fingerprint_drift()
    {
        var stackId = Guid.NewGuid().ToString("D");
        var assessmentFingerprint = new string('a', 64);
        var captureFingerprint = new string('c', 64);
        var capture = new EligibleSourceCapture(
            "capture-drifted",
            Guid.Parse(stackId),
            "testing",
            "matrix.example.test",
            Now.AddMinutes(-5),
            "/private/capture-drifted.memmigration.zip",
            4096,
            new string('b', 64),
            captureFingerprint,
            RehearsalOnly: true,
            StableSourceIdentity: StableIdentity(stackId));
        var service = CreateService(
            new FakeJournal(),
            new FakeDiscovery(capture));

        var imported = await service.ImportRequestAsync(
            NewRequest(stackId),
            NewAssessment(stackId, assessmentFingerprint),
            stackId,
            true,
            CancellationToken.None);

        var offered = Assert.Single(imported.Captures);
        Assert.True(offered.EligibleForRequest);
        var selected = await service.SelectCaptureAsync(
            imported.Workflow.WorkflowId,
            capture.CaptureId,
            CancellationToken.None);
        Assert.Equal(capture.CaptureId, selected.CaptureId);
    }

    [Fact]
    public async Task Stable_identity_mismatch_rejects_a_capture_even_when_exact_fingerprint_matches()
    {
        var stackId = Guid.NewGuid().ToString("D");
        var fingerprint = new string('a', 64);
        var differentIdentity = SelectedSourceIdentity.Compute(new SelectedSourceIdentityInput(
            ProductName: "MatrixEasyMode",
            ProductVersion: "0.1.0",
            SourceStackId: Guid.Parse(stackId),
            Slug: "different-slug",
            MatrixServerName: "matrix.example.test",
            MatrixPublicHost: "matrix.example.test",
            ElementPublicHost: "chat.example.test"));
        var capture = new EligibleSourceCapture(
            "capture-wrong-identity",
            Guid.Parse(stackId),
            "different-slug",
            "matrix.example.test",
            Now.AddMinutes(-5),
            "/private/capture-wrong-identity.memmigration.zip",
            4096,
            new string('b', 64),
            fingerprint,
            RehearsalOnly: true,
            StableSourceIdentity: differentIdentity);
        var service = CreateService(new FakeJournal(), new FakeDiscovery(capture));
        var imported = await service.ImportRequestAsync(
            NewRequest(stackId),
            NewAssessment(stackId, fingerprint),
            stackId,
            true,
            CancellationToken.None);

        var offered = Assert.Single(imported.Captures);
        Assert.False(offered.EligibleForRequest);
        var exception = await Assert.ThrowsAsync<SourceWorkflowConflictException>(() =>
            service.SelectCaptureAsync(
                imported.Workflow.WorkflowId,
                capture.CaptureId,
                CancellationToken.None));

        Assert.Contains(
            "different selected source identity",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_workflow_without_stable_identity_keeps_exact_fingerprint_matching()
    {
        var stackId = Guid.NewGuid().ToString("D");
        var workflowId = WorkflowId(9);
        var journal = new FakeJournal();
        journal.Seed(NewWorkflowRecord(
            workflowId,
            stackId,
            SourceWorkflowStatus.Pending,
            SourceWorkflowStage.RequestValidated,
            Now));
        var capture = new EligibleSourceCapture(
            "capture-newer-state",
            Guid.Parse(stackId),
            "testing",
            "matrix.example.test",
            Now.AddMinutes(-5),
            "/private/capture-newer-state.memmigration.zip",
            4096,
            new string('b', 64),
            new string('c', 64),
            RehearsalOnly: true,
            StableSourceIdentity: StableIdentity(stackId));
        var service = CreateService(journal, new FakeDiscovery(capture));

        var exception = await Assert.ThrowsAsync<SourceWorkflowConflictException>(() =>
            service.SelectCaptureAsync(
                workflowId,
                capture.CaptureId,
                CancellationToken.None));

        Assert.Contains(
            "different source fingerprint",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Retained_capture_for_another_stack_is_not_offered_or_accepted()
    {
        var stackId = Guid.NewGuid().ToString("D");
        var fingerprint = new string('a', 64);
        var otherStackCapture = new EligibleSourceCapture(
            "capture-other",
            Guid.NewGuid(),
            "other",
            "matrix-other.example.test",
            Now.AddMinutes(-5),
            "/private/other.memmigration.zip",
            4096,
            new string('b', 64),
            fingerprint,
            RehearsalOnly: true);
        var service = CreateService(
            new FakeJournal(),
            new FakeDiscovery(otherStackCapture));
        var imported = await service.ImportRequestAsync(
            NewRequest(stackId),
            NewAssessment(stackId, fingerprint),
            stackId,
            true,
            CancellationToken.None);

        Assert.Empty(imported.Captures);
        var exception = await Assert.ThrowsAsync<SourceWorkflowConflictException>(() =>
            service.SelectCaptureAsync(
                imported.Workflow.WorkflowId,
                otherStackCapture.CaptureId,
                CancellationToken.None));
        Assert.Contains("different source stack", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Package_completion_projects_only_safe_package_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-workflow-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var stackId = Guid.NewGuid().ToString("D");
            var fingerprint = new string('a', 64);
            var capture = new EligibleSourceCapture(
                "capture-01",
                Guid.Parse(stackId),
                "tester",
                "matrix.example.test",
                Now.AddMinutes(-5),
                Path.Combine(root, "capture.memmigration.zip"),
                4096,
                new string('b', 64),
                fingerprint,
                RehearsalOnly: true);
            await File.WriteAllTextAsync(capture.ArchivePath, "capture");
            var journal = new FakeJournal();
            var discovery = new FakeDiscovery(capture);
            var packageRoot = Path.Combine(root, "packages");
            var packageService = new FakePackageService(packageRoot);
            var service = CreateService(
                journal,
                discovery,
                packageService,
                outputRoot: root);
            var imported = await service.ImportRequestAsync(
                NewRequest(stackId),
                NewAssessment(stackId, fingerprint),
                stackId,
                true,
                CancellationToken.None);
            await service.SelectCaptureAsync(
                imported.Workflow.WorkflowId,
                capture.CaptureId,
                CancellationToken.None);

            await service.RunPackageAsync(
                imported.Workflow.WorkflowId,
                CancellationToken.None);
            var completed = await service.GetLatestAsync(CancellationToken.None);

            Assert.NotNull(completed);
            Assert.Equal("Completed", completed.Status);
            Assert.NotNull(completed.Package);
            Assert.Equal("package.memmigration.zip.age", completed.Package.FileName);
            Assert.Equal("Available", completed.Package.LocalState);
            Assert.True(completed.Actions.CanDownloadPackage);
            Assert.True(completed.Actions.CanDownloadReport);
            Assert.True(completed.Actions.CanDeletePackage);
            Assert.DoesNotContain(Path.DirectorySeparatorChar, completed.Package.FileName);
            Assert.DoesNotContain(Path.AltDirectorySeparatorChar, completed.Package.FileName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Download_report_and_deletion_are_confined_to_package_root()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-workflow-lifecycle-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var stackId = Guid.NewGuid().ToString("D");
            var fingerprint = new string('a', 64);
            var capturePath = Path.Combine(root, "capture.memmigration.zip");
            await File.WriteAllTextAsync(capturePath, "plaintext capture");
            var capture = new EligibleSourceCapture(
                "capture-01",
                Guid.Parse(stackId),
                "tester",
                "matrix.example.test",
                Now.AddMinutes(-5),
                capturePath,
                new FileInfo(capturePath).Length,
                new string('b', 64),
                fingerprint,
                RehearsalOnly: true);
            var journal = new FakeJournal();
            var discovery = new FakeDiscovery(capture);
            var packageRoot = Path.Combine(root, "packages");
            var service = CreateService(
                journal,
                discovery,
                new FakePackageService(packageRoot),
                outputRoot: root);
            var imported = await service.ImportRequestAsync(
                NewRequest(stackId),
                NewAssessment(stackId, fingerprint),
                stackId,
                true,
                CancellationToken.None);
            await service.SelectCaptureAsync(
                imported.Workflow.WorkflowId,
                capture.CaptureId,
                CancellationToken.None);
            await service.RunPackageAsync(
                imported.Workflow.WorkflowId,
                CancellationToken.None);

            var download = await service.GetPackageDownloadAsync(
                imported.Workflow.WorkflowId,
                CancellationToken.None);
            Assert.Equal(
                Path.GetFullPath(packageRoot),
                Path.GetDirectoryName(download.FullPath));
            Assert.Equal(8192, download.SizeBytes);

            var safeReport = await service.GetPackageReportAsync(
                imported.Workflow.WorkflowId,
                CancellationToken.None);
            var reportText = System.Text.Encoding.UTF8.GetString(
                safeReport.Contents);
            Assert.Contains("Encrypted package SHA-256", reportText);
            Assert.DoesNotContain(root, reportText, StringComparison.Ordinal);

            var deleted = await service.DeletePackageAsync(
                imported.Workflow.WorkflowId,
                CancellationToken.None);

            Assert.Equal("PackageDeleted", deleted.Stage);
            Assert.Equal("Deleted", deleted.Package?.LocalState);
            Assert.False(deleted.Actions.CanDownloadPackage);
            Assert.False(deleted.Actions.CanDeletePackage);

            var overview = await service.GetOverviewAsync(
                stackId,
                10,
                operationRunning: false,
                CancellationToken.None);
            Assert.Null(overview.CurrentWorkflow);
            var previous = Assert.Single(overview.PreviousWorkflows);
            Assert.Equal(deleted.WorkflowId, previous.WorkflowId);
            Assert.Equal("Deleted", previous.Package?.LocalState);

            Assert.True(File.Exists(capturePath));
            Assert.False(File.Exists(download.FullPath));
            Assert.False(Directory.EnumerateFiles(packageRoot).Any());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Package_download_rejects_artifacts_outside_configured_package_root()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-workflow-path-test-{Guid.NewGuid():N}");
        var outsideRoot = Path.Combine(
            Path.GetTempPath(),
            $"mem-workflow-outside-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outsideRoot);
        try
        {
            var stackId = Guid.NewGuid().ToString("D");
            var fingerprint = new string('a', 64);
            var capturePath = Path.Combine(root, "capture.memmigration.zip");
            await File.WriteAllTextAsync(capturePath, "plaintext capture");
            var capture = new EligibleSourceCapture(
                "capture-01",
                Guid.Parse(stackId),
                "tester",
                "matrix.example.test",
                Now.AddMinutes(-5),
                capturePath,
                new FileInfo(capturePath).Length,
                new string('b', 64),
                fingerprint,
                RehearsalOnly: true);
            var journal = new FakeJournal();
            var service = CreateService(
                journal,
                new FakeDiscovery(capture),
                new FakePackageService(outsideRoot),
                outputRoot: root);
            var imported = await service.ImportRequestAsync(
                NewRequest(stackId),
                NewAssessment(stackId, fingerprint),
                stackId,
                true,
                CancellationToken.None);
            await service.SelectCaptureAsync(
                imported.Workflow.WorkflowId,
                capture.CaptureId,
                CancellationToken.None);
            await service.RunPackageAsync(
                imported.Workflow.WorkflowId,
                CancellationToken.None);

            var completed = await service.GetLatestAsync(
                CancellationToken.None);
            Assert.NotNull(completed);
            Assert.Equal("Unsafe", completed.Package?.LocalState);
            Assert.False(completed.Actions.CanDownloadPackage);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => service.GetPackageDownloadAsync(
                    imported.Workflow.WorkflowId,
                    CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outsideRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Overview_selects_newest_actionable_workflow_and_keeps_terminal_rows_in_history()
    {
        var stackId = Guid.NewGuid().ToString("D");
        var otherStackId = Guid.NewGuid().ToString("D");
        var fingerprint = new string('a', 64);
        var currentId = WorkflowId(2);
        var completedId = WorkflowId(3);
        var deletedId = WorkflowId(7);
        var cancelledId = WorkflowId(1);
        var journal = new FakeJournal();
        journal.Seed(
            NewWorkflowRecord(
                completedId,
                stackId,
                SourceWorkflowStatus.Completed,
                SourceWorkflowStage.ReadyForDownload,
                Now.AddMinutes(-1)),
            NewWorkflowRecord(
                deletedId,
                stackId,
                SourceWorkflowStatus.Completed,
                SourceWorkflowStage.PackageDeleted,
                Now.AddSeconds(-90)),
            NewWorkflowRecord(
                currentId,
                stackId,
                SourceWorkflowStatus.Pending,
                SourceWorkflowStage.RequestValidated,
                Now.AddMinutes(-2)),
            NewWorkflowRecord(
                cancelledId,
                stackId,
                SourceWorkflowStatus.Cancelled,
                SourceWorkflowStage.Capturing,
                Now.AddMinutes(-3)),
            NewWorkflowRecord(
                WorkflowId(4),
                otherStackId,
                SourceWorkflowStatus.Pending,
                SourceWorkflowStage.RequestValidated,
                Now));
        var service = CreateService(
            journal,
            new FakeDiscovery(new EligibleSourceCapture(
                "capture-01",
                Guid.Parse(stackId),
                "tester",
                "matrix.example.test",
                Now.AddMinutes(-5),
                "/private/capture.memmigration.zip",
                4096,
                new string('b', 64),
                fingerprint,
                RehearsalOnly: true)));

        var overview = await service.GetOverviewAsync(
            stackId,
            10,
            operationRunning: false,
            CancellationToken.None);

        Assert.True(overview.CanStartNewWorkflow);
        Assert.False(overview.OperationRunning);
        Assert.Null(overview.StartNewBlockedReason);
        Assert.Equal(currentId, overview.CurrentWorkflow?.WorkflowId);
        Assert.Equal(
            [completedId, deletedId, cancelledId],
            overview.PreviousWorkflows.Select(workflow => workflow.WorkflowId));

        var requested = await service.GetAsync(
            cancelledId,
            CancellationToken.None);
        Assert.Equal(cancelledId, requested?.WorkflowId);

        var json = System.Text.Json.JsonSerializer.Serialize(overview);
        Assert.DoesNotContain(Recipient, json, StringComparison.Ordinal);
        Assert.DoesNotContain("ageRecipient", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/private/", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overview_keeps_safely_retryable_failure_current_and_blocks_start_during_running_operation()
    {
        var stackId = Guid.NewGuid().ToString("D");
        var fingerprint = new string('a', 64);
        var retryableId = WorkflowId(5);
        var journal = new FakeJournal();
        journal.Seed(NewWorkflowRecord(
            retryableId,
            stackId,
            SourceWorkflowStatus.Failed,
            SourceWorkflowStage.RecoveryRequired,
            Now));
        var service = CreateService(
            journal,
            new FakeDiscovery(new EligibleSourceCapture(
                "capture-01",
                Guid.Parse(stackId),
                "tester",
                "matrix.example.test",
                Now.AddMinutes(-5),
                "/private/capture.memmigration.zip",
                4096,
                new string('b', 64),
                fingerprint,
                RehearsalOnly: true)));

        var retryable = await service.GetOverviewAsync(
            stackId,
            10,
            operationRunning: false,
            CancellationToken.None);

        Assert.Equal(retryableId, retryable.CurrentWorkflow?.WorkflowId);
        Assert.Empty(retryable.PreviousWorkflows);
        Assert.True(retryable.CanStartNewWorkflow);

        var runningId = WorkflowId(8);
        journal.Seed(NewWorkflowRecord(
            runningId,
            stackId,
            SourceWorkflowStatus.Running,
            SourceWorkflowStage.Packaging,
            Now.AddMinutes(1)));

        var blocked = await service.GetOverviewAsync(
            stackId,
            10,
            operationRunning: false,
            CancellationToken.None);

        Assert.Equal(runningId, blocked.CurrentWorkflow?.WorkflowId);
        Assert.True(blocked.OperationRunning);
        Assert.False(blocked.CanStartNewWorkflow);
        var blockedReason = Assert.IsType<string>(blocked.StartNewBlockedReason);
        Assert.Contains(
            "already running",
            blockedReason,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Overview_places_expired_workflow_in_previous_history()
    {
        var stackId = Guid.NewGuid().ToString("D");
        var fingerprint = new string('a', 64);
        var expiredId = WorkflowId(6);
        var journal = new FakeJournal();
        journal.Seed(NewWorkflowRecord(
            expiredId,
            stackId,
            SourceWorkflowStatus.Pending,
            SourceWorkflowStage.RequestValidated,
            Now,
            requestExpiresAtUtc: Now.AddMinutes(-1)));
        var service = CreateService(
            journal,
            new FakeDiscovery(new EligibleSourceCapture(
                "capture-01",
                Guid.Parse(stackId),
                "tester",
                "matrix.example.test",
                Now.AddMinutes(-5),
                "/private/capture.memmigration.zip",
                4096,
                new string('b', 64),
                fingerprint,
                RehearsalOnly: true)));

        var overview = await service.GetOverviewAsync(
            stackId,
            10,
            operationRunning: false,
            CancellationToken.None);

        Assert.Null(overview.CurrentWorkflow);
        Assert.Equal(expiredId, Assert.Single(overview.PreviousWorkflows).WorkflowId);
    }

    [Fact]
    public async Task Overview_rejects_limits_outside_the_bounded_history_contract()
    {
        var stackId = Guid.NewGuid().ToString("D");
        var fingerprint = new string('a', 64);
        var service = CreateService(
            new FakeJournal(),
            new FakeDiscovery(new EligibleSourceCapture(
                "capture-01",
                Guid.Parse(stackId),
                "tester",
                "matrix.example.test",
                Now.AddMinutes(-5),
                "/private/capture.memmigration.zip",
                4096,
                new string('b', 64),
                fingerprint,
                RehearsalOnly: true)));

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.GetOverviewAsync(
                stackId,
                11,
                operationRunning: false,
                CancellationToken.None));
    }

    private static SourceMigrationApplicationService CreateService(
        FakeJournal journal,
        FakeDiscovery discovery,
        IPackageForIntakeService? packageService = null,
        string? outputRoot = null,
        ISourceCaptureService? captureService = null)
    {
        var time = new ManualTimeProvider(Now);
        var resolvedOutputRoot = outputRoot ?? Path.GetTempPath();
        return new SourceMigrationApplicationService(
            new AssessmentOptions
            {
                WorkspacePath = Path.GetTempPath(),
                OutputPath = resolvedOutputRoot,
                NonInteractive = true
            },
            "0.2.0-test",
            journal,
            captureService ?? new FakeCaptureService(),
            discovery,
            packageService ??
                new FakePackageService(
                    Path.Combine(resolvedOutputRoot, "packages")),
            new SecureIntakeRequestValidator(time),
            time);
    }

    private static string WorkflowId(int suffix) =>
        $"source-20260728-00000{suffix}Z-{suffix:D32}";

    private static SourceWorkflowRecord NewWorkflowRecord(
        string workflowId,
        string stackId,
        SourceWorkflowStatus status,
        SourceWorkflowStage stage,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? requestExpiresAtUtc = null) =>
        new(
            WorkflowId: workflowId,
            Status: status,
            Stage: stage,
            AssessmentId: "assessment-01",
            SourceFingerprint: new string('a', 64),
            SelectedSourceStackId: stackId,
            IntakeId: $"mig_{workflowId[^8..]}",
            PackageRevisionId: null,
            RequestKind: "preview",
            AgeRecipient: Recipient,
            RecipientFingerprint: PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient),
            RequestExpiresAtUtc: requestExpiresAtUtc ?? Now.AddHours(2),
            TargetControlPlaneVersion: "0.2.0",
            EncryptionReadinessAcknowledgedAtUtc: Now,
            CaptureId: null,
            PackageReportJson: null,
            CreatedAtUtc: updatedAtUtc.AddMinutes(-1),
            UpdatedAtUtc: updatedAtUtc,
            CompletedAtUtc: status is SourceWorkflowStatus.Completed or SourceWorkflowStatus.Cancelled
                ? updatedAtUtc
                : null,
            CancellationRequestedAtUtc: null,
            FailureCode: status == SourceWorkflowStatus.Failed
                ? "operation_interrupted"
                : null,
            FailureSummary: status == SourceWorkflowStatus.Failed
                ? "The prior operation stopped and may be retried safely."
                : null,
            Revision: 1);

    private static SecureIntakeRequestInput NewRequest(string stackId) =>
        new(
            SecureIntakeRequestValidator.Schema,
            1,
            "mig_20260728_preview",
            null,
            "preview",
            Recipient,
            PackageForIntakeOptions.CalculateRecipientFingerprint(Recipient),
            Now.AddHours(2),
            "0.2.0",
            stackId);

    private static string StableIdentity(string stackId) =>
        SelectedSourceIdentity.Compute(new SelectedSourceIdentityInput(
            ProductName: "MatrixEasyMode",
            ProductVersion: "0.1.0",
            SourceStackId: Guid.Parse(stackId),
            Slug: "testing",
            MatrixServerName: "matrix.example.test",
            MatrixPublicHost: "matrix.example.test",
            ElementPublicHost: "chat.example.test"));

    private static SourceAssessmentView NewAssessment(
        string stackId,
        string fingerprint) =>
        new(
            1,
            "assessment-01",
            Now,
            "ConfirmedSupportedV010",
            "NewServerRecommended",
            true,
            fingerprint,
            new SourceHostSummary("Ubuntu", "X64", true),
            new SourceRuntimeSummary(true, "28", true, "MatrixEasyMode", "0.1.0", true),
            new SourceAssessmentCounts(5, 1, 1, 1, 1, 0, 0),
            [new SourceStackSummary(
                stackId,
                "testing",
                "Testing",
                "matrix.example.test",
                "matrix.example.test",
                "chat.example.test",
                true,
                true,
                true,
                true,
                4096,
                0,
                0,
                true)],
            []);

    private sealed class FakeJournal : ISourceWorkflowJournal
    {
        private readonly List<SourceWorkflowRecord> _records = [];

        public Task InitializeAsync(CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<SourceWorkflowRecord?> GetLatestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(_records
                .OrderByDescending(record => record.UpdatedAtUtc)
                .ThenByDescending(record => record.WorkflowId, StringComparer.Ordinal)
                .FirstOrDefault());

        public Task<IReadOnlyList<SourceWorkflowRecord>> ListRecentAsync(
            string selectedSourceStackId,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SourceWorkflowRecord>>(_records
                .Where(record => string.Equals(
                    record.SelectedSourceStackId,
                    selectedSourceStackId,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(record => record.UpdatedAtUtc)
                .ThenByDescending(record => record.WorkflowId, StringComparer.Ordinal)
                .Take(limit)
                .ToArray());

        public Task<SourceWorkflowRecord?> GetAsync(
            string workflowId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_records.FirstOrDefault(record =>
                string.Equals(record.WorkflowId, workflowId, StringComparison.Ordinal)));

        public Task SaveAsync(
            SourceWorkflowRecord workflow,
            CancellationToken cancellationToken)
        {
            _records.RemoveAll(record => string.Equals(
                record.WorkflowId,
                workflow.WorkflowId,
                StringComparison.Ordinal));
            _records.Add(workflow);
            return Task.CompletedTask;
        }

        public void Seed(params SourceWorkflowRecord[] records)
        {
            _records.Clear();
            _records.AddRange(records);
        }
    }

    private sealed class FakeDiscovery(EligibleSourceCapture capture)
        : ISourceCaptureArchiveDiscovery
    {
        public Task<IReadOnlyList<EligibleSourceCapture>> ListEligibleArchivesAsync(
            CancellationToken cancellationToken,
            bool requireFinalFrozen = false) =>
            Task.FromResult<IReadOnlyList<EligibleSourceCapture>>(
                requireFinalFrozen && capture.RehearsalOnly ? [] : [capture]);

        public Task<EligibleSourceCapture> ResolveEligibleArchiveAsync(
            string captureId,
            CancellationToken cancellationToken,
            bool requireFinalFrozen = false) =>
            string.Equals(captureId, capture.CaptureId, StringComparison.Ordinal) &&
            (!requireFinalFrozen || !capture.RehearsalOnly)
                ? Task.FromResult(capture)
                : throw new InvalidOperationException("Capture unavailable.");

        public Task<string> ResolveSingleEligibleArchiveAsync(
            CancellationToken cancellationToken,
            bool requireFinalFrozen = false) =>
            Task.FromResult(capture.ArchivePath);
    }

    private sealed class RecordingCaptureService : ISourceCaptureService
    {
        public CaptureOptions? LastOptions { get; private set; }

        public Task<CaptureReport> CaptureAsync(
            CaptureOptions options,
            CancellationToken cancellationToken)
        {
            LastOptions = options;
            var stackId = options.SourceStackId
                ?? throw new InvalidOperationException("Selected source stack was not supplied.");
            var now = Now.AddMinutes(1);
            return Task.FromResult(new CaptureReport(
                Schema: "mem-migration-capture-receipt",
                SchemaVersion: 2,
                CaptureId: options.CaptureId!,
                SourceStackId: stackId,
                SourceStackSlug: "tester",
                MatrixServerName: "matrix.example.test",
                Status: CaptureLifecycleStatus.Completed,
                StartedAtUtc: Now,
                CompletedAtUtc: now,
                StartSourceFingerprint: new string('c', 64),
                CompletionSourceFingerprint: new string('c', 64),
                SourceChangedDuringCapture: false,
                RehearsalOnly: true,
                ArchivePath: "/private/capture.memmigration.zip",
                ArchiveSha256: new string('b', 64),
                ArchiveBytes: 4096,
                EncryptedArchivePath: null,
                EncryptedArchiveSha256: null,
                EncryptedArchiveBytes: null,
                PlaintextArchiveRetained: true,
                Plan: new CapturePlanSummary(1, 4096, 8192, 4096, 10),
                Warnings: [],
                NextSteps: [],
                StableSourceIdentity: options.ExpectedSourceIdentity));
        }
    }

    private sealed class FakeCaptureService : ISourceCaptureService
    {
        public Task<CaptureReport> CaptureAsync(
            CaptureOptions options,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakePackageService(string root) : IPackageForIntakeService
    {
        public async Task<PackageForIntakeReport> PackageAsync(
            PackageForIntakeOptions options,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(root);
            var outputStem = Path.Combine(root, "package");
            var packagePath = $"{outputStem}.memmigration.zip.age";
            var jsonPath = $"{outputStem}.package-report.json";
            var markdownPath = $"{outputStem}.package-report.md";
            await File.WriteAllBytesAsync(
                packagePath,
                new byte[8192],
                cancellationToken);
            await File.WriteAllTextAsync(
                jsonPath,
                "{}",
                cancellationToken);
            await File.WriteAllTextAsync(
                markdownPath,
                "# package",
                cancellationToken);

            return new PackageForIntakeReport(
                "mem-package-for-intake-report",
                2,
                options.IntakeId,
                null,
                Now.AddMinutes(1),
                options.RecipientFingerprint,
                "any-eligible",
                "migration-01",
                "preview",
                false,
                true,
                options.SourceStackId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "tester",
                "matrix.example.test",
                1,
                options.ArchivePath,
                new string('b', 64),
                4096,
                10,
                8192,
                packagePath,
                new string('c', 64),
                8192,
                jsonPath,
                markdownPath);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
