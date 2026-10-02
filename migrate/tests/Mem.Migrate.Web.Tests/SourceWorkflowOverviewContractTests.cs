using System.Text.Json;
using Mem.Migrate.Application.Workflow;
using Mem.Migrate.Web.Contracts;

namespace Mem.Migrate.Web.Tests;

public sealed class SourceWorkflowOverviewContractTests
{
    [Fact]
    public void Serializes_the_bounded_overview_contract_without_protected_request_material()
    {
        var workflow = new SourceWorkflowView(
            SchemaVersion: 1,
            WorkflowId: "source-20260728-000001Z-11111111111111111111111111111111",
            Status: "Completed",
            Stage: "ReadyForDownload",
            AssessmentId: "assessment-01",
            SelectedSourceStackId: "stack-01",
            Request: new SourceWorkflowRequestView(
                IntakeId: "mig_20260728_preview",
                PackageRevisionId: null,
                RequestKind: "preview",
                RecipientFingerprint: "1111-2222-3333-4444",
                ExpiresAtUtc: new DateTimeOffset(2026, 7, 28, 2, 0, 0, TimeSpan.Zero),
                TargetControlPlaneVersion: "0.2.0"),
            EncryptionReadinessAcknowledgedAtUtc: new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
            CaptureId: "capture-01",
            Package: new SourcePackageView(
                FileName: "tester.memmigration.zip.age",
                SizeBytes: 8192,
                Sha256: new string('a', 64),
                CompletedAtUtc: new DateTimeOffset(2026, 7, 28, 1, 0, 0, TimeSpan.Zero),
                CaptureKind: "preview",
                SourceFrozen: false,
                RehearsalOnly: true,
                StackCount: 1,
                LocalState: "Available",
                PackageFileAvailable: true,
                ReportAvailable: true),
            CreatedAtUtc: new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero),
            UpdatedAtUtc: new DateTimeOffset(2026, 7, 28, 1, 0, 0, TimeSpan.Zero),
            CompletedAtUtc: new DateTimeOffset(2026, 7, 28, 1, 0, 0, TimeSpan.Zero),
            FailureCode: null,
            FailureSummary: null,
            Actions: new SourceWorkflowActions(
                CanCreatePreviewCapture: false,
                CanSelectCapture: false,
                CanCreatePackage: false,
                CanCancel: false,
                CanDownloadPackage: true,
                CanDownloadReport: true,
                CanDeletePackage: true,
                BlockedReason: "This workflow already produced an encrypted package."));
        var response = new SourceWorkflowOverviewResponse(
            SchemaVersion: 1,
            SourceStackId: "stack-01",
            OperationRunning: false,
            CanStartNewWorkflow: true,
            StartNewBlockedReason: null,
            CurrentWorkflow: null,
            PreviousWorkflows: [workflow]);

        var json = JsonSerializer.Serialize(
            response,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"sourceStackId\":\"stack-01\"", json, StringComparison.Ordinal);
        Assert.Contains("\"canStartNewWorkflow\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"previousWorkflows\"", json, StringComparison.Ordinal);
        Assert.Contains("tester.memmigration.zip.age", json, StringComparison.Ordinal);
        Assert.DoesNotContain("ageRecipient", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AGE-SECRET-KEY", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/var/lib/mem-migrate", json, StringComparison.Ordinal);
    }
}
