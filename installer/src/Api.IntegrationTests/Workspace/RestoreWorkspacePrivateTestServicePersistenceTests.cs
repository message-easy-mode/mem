using System.Text.Json;
using HostAgent.Runtime.Backups.Artifacts.LocalBackups.History;
using HostAgent.Runtime.Backups.Artifacts.ValidatedImports;
using HostAgent.Runtime.Backups.Catalog;
using HostAgent.Runtime.Backups.Coordination;
using HostAgent.Runtime.Backups.Observability;
using HostAgent.Runtime.Backups.Verification.PrivateRuntime.PrivateStaging;
using HostAgent.Runtime.Backups.Workspace;
using HostAgent.Runtime.Backups.Workspace.PrivateTest;
using HostAgent.Runtime.Operations;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.IntegrationTests.Workspace;

public sealed class RestoreWorkspacePrivateTestServicePersistenceTests
{
    [Fact]
    public async Task Local_catalog_workspace_runs_catalog_private_test_and_projects_safe_workspace_evidence()
    {
        await using var fixture = await PrivateTestFixture.CreateAsync(
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            validationId: null,
            runs: [CreateRun(
                status: "ready",
                sourceKind: PrivateStagingSourceKinds.BackupCatalog,
                catalogEntryId: "bkp_workspace_local",
                validationId: null,
                stagingId: "staging-local")]);

        var response = await fixture.Service.RunAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.Equal("ready", response.Status);
        Assert.Equal("backup-catalog", response.SourceKind);
        Assert.Equal("bkp_workspace_local", response.CatalogEntryId);
        Assert.Equal("staging-local", response.StagingId);
        Assert.True(response.PrivateOnly is true);
        Assert.True(response.DatabaseImportSucceeded is true);
        Assert.True(response.SynapseHealthPassed is true);
        Assert.True(response.RequiresExplicitDestroy is true);

        Assert.Equal(["bkp_workspace_local"], fixture.Runner.CatalogEntryIds);
        Assert.All(fixture.Runner.Requests, request => Assert.True(request.KeepOnFailure));

        var attempt = await fixture.Db.RestoreAttempts.SingleAsync();
        Assert.Equal(RestoreAttemptStatuses.Ready, attempt.Status);
        Assert.Equal(RestoreAttemptStages.BackupReady, attempt.CurrentStage);
        Assert.Equal(response.OperationId, attempt.RuntimeOperationId);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync();
        Assert.Equal("restore.private-test", operation.Operation);
        Assert.Equal("succeeded", operation.Status);
        Assert.Equal(attempt.Id, operation.RestoreAttemptId);
        Assert.Equal("private-staging-ready", operation.CurrentStep);

        var logPage = await fixture.Logs.ListAsync(
            fixture.RestoreSessionId,
            new RestoreLogQuery(1, 50, null, null, null),
            CancellationToken.None);

        Assert.NotNull(logPage);
        Assert.Contains(logPage!.Events, entry => entry.EventCode == "restore.private-test.requested");

        var privateTestStarted = Assert.Single(
            logPage.Events,
            entry => entry.EventCode == "restore.private-test.started");
        Assert.NotNull(privateTestStarted.Details);
        Assert.Equal(
            "backup-catalog",
            privateTestStarted.Details!["sourceKind"]);
        Assert.Equal(
            "bkp_workspace_local",
            privateTestStarted.Details["catalogEntryId"]);
        Assert.Equal(
            fixture.RestoreSessionId,
            privateTestStarted.Details["restoreSessionId"]);

        Assert.Contains(logPage.Events, entry => entry.EventCode == "restore.private-test.passed");

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.NotNull(workspace);
        Assert.Equal("ready", workspace!.OverallStatus.Code);

        var privateTestStage = Assert.Single(
            workspace.StandardStages,
            stage => stage.Code == "private-test");
        Assert.Equal(RestoreWorkspaceStageStates.Completed, privateTestStage.State);
        Assert.NotNull(privateTestStage.OperationSummary);
        Assert.Equal("restore.private-test", privateTestStage.OperationSummary!.Operation);
        Assert.Equal("succeeded", privateTestStage.OperationSummary.Status);

        var privateTestExecutionEvidence = Assert.IsType<RestoreWorkspacePrivateTestEvidence>(
            privateTestStage.PrivateTestEvidence);
        Assert.Equal("backup-catalog", privateTestExecutionEvidence.SourceKind);
        Assert.Equal("bkp_workspace_local", privateTestExecutionEvidence.CatalogEntryId);
        Assert.Equal("staging-local", privateTestExecutionEvidence.StagingId);
        Assert.Equal("matrix.workspace.test", privateTestExecutionEvidence.MatrixServerName);
        Assert.Equal("ready", privateTestExecutionEvidence.Status);
        Assert.True(privateTestExecutionEvidence.PrivateOnly);
        Assert.True(privateTestExecutionEvidence.DockerNetworkInternal);
        Assert.True(privateTestExecutionEvidence.DatabaseImportSucceeded);
        Assert.True(privateTestExecutionEvidence.SynapseHealthPassed);
        Assert.True(privateTestExecutionEvidence.RequiresExplicitDestroy);
        Assert.NotNull(privateTestExecutionEvidence.CompletedAtUtc);
        Assert.Equal("unknown", privateTestExecutionEvidence.StagingRuntimeStatus);
        Assert.Null(privateTestExecutionEvidence.StagingRuntimeDestroyed);
        Assert.Null(privateTestExecutionEvidence.DestroyAvailable);
        Assert.Null(privateTestExecutionEvidence.DestroyedAtUtc);

        var privateTestEvidence = Assert.Single(
            workspace.Evidence.Categories,
            category => category.Code == "private-test");
        Assert.Contains(
            privateTestEvidence.Items,
            item => item.EventCode == "restore.private-test.passed");
    }

    [Fact]
    public async Task Materialised_imported_zip_catalog_workspace_uses_catalog_runner_and_keeps_validation_receipt_out_of_private_test_evidence()
    {
        await using var fixture = await PrivateTestFixture.CreateAsync(
            originKind: BackupCatalogOriginKinds.ImportedZip,
            validationId: "validated-import-001",
            runs: [CreateRun(
                status: "ready",
                sourceKind: PrivateStagingSourceKinds.BackupCatalog,
                catalogEntryId: "bkp_workspace_imported",
                validationId: null,
                stagingId: "staging-imported")]);

        var response = await fixture.Service.RunAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.Equal("ready", response.Status);
        Assert.Equal("bkp_workspace_imported", response.CatalogEntryId);
        Assert.Equal(["bkp_workspace_imported"], fixture.Runner.CatalogEntryIds);

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.NotNull(workspace);

        var privateTestEvidence = Assert.IsType<RestoreWorkspacePrivateTestEvidence>(
            workspace!
                .StandardStages
                .Single(stage => stage.Code == "private-test")
                .PrivateTestEvidence);

        var evidenceJson = JsonSerializer.Serialize(privateTestEvidence);
        Assert.False(
            evidenceJson.Contains(
                "validationId",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_04_request_abort_after_durable_acceptance_does_not_cancel_private_test()
    {
        var runnerEntered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        await using var fixture = await PrivateTestFixture.CreateAsync(
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            validationId: null,
            runs: [CreateRun(
                status: "ready",
                sourceKind: PrivateStagingSourceKinds.BackupCatalog,
                catalogEntryId: "bkp_workspace_local",
                validationId: null,
                stagingId: "staging-request-abort")],
            runnerEntered: runnerEntered,
            runnerRelease: runnerRelease);

        using var request = new CancellationTokenSource();
        var runTask = fixture.Service.RunAsync(
            fixture.RestoreSessionId,
            request.Token);

        await runnerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        request.Cancel();
        runnerRelease.TrySetResult(true);

        var response = await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("ready", response.Status);
        Assert.True(request.IsCancellationRequested);

        var operation = await fixture.Db.RuntimeOperations.SingleAsync();
        Assert.Equal("succeeded", operation.Status);
        Assert.Equal("private-staging-ready", operation.CurrentStep);

        var attempt = await fixture.Db.RestoreAttempts.SingleAsync();
        Assert.Equal(RestoreAttemptStatuses.Ready, attempt.Status);
        Assert.Null(attempt.LastErrorCode);

        var logPage = await fixture.Logs.ListAsync(
            fixture.RestoreSessionId,
            new RestoreLogQuery(1, 100, null, null, null),
            CancellationToken.None);

        Assert.NotNull(logPage);
        Assert.Contains(
            logPage!.Events,
            entry => entry.EventCode == "restore.private-test.operation-lifetime.server-owned");
        Assert.DoesNotContain(
            logPage.Events,
            entry => entry.EventCode == "restore.private-test.failed");
    }

    [Fact]
    public async Task Catalog_payload_failure_is_recorded_as_safe_workspace_evidence()
    {
        const string rawFailureDetail = "/workspace/mem-data/catalog/private/runtime details must not reach the workspace";

        await using var fixture = await PrivateTestFixture.CreateAsync(
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            validationId: null,
            runs: [CreateRun(
                status: "ready",
                sourceKind: PrivateStagingSourceKinds.BackupCatalog,
                catalogEntryId: "bkp_workspace_local",
                validationId: null,
                stagingId: "unused")],
            runnerException: new BackupCatalogPayloadResolutionException(
                "backup_catalog_private_staging_payload_incomplete",
                rawFailureDetail));

        var response = await fixture.Service.RunAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.Equal("needs-attention", response.Status);
        Assert.Equal("bkp_workspace_local", response.CatalogEntryId);
        Assert.Null(response.StagingId);
        Assert.False(response.Detail.Contains(rawFailureDetail, StringComparison.Ordinal));

        var attempt = await fixture.Db.RestoreAttempts.SingleAsync();
        Assert.Equal(RestoreAttemptStatuses.NeedsAttention, attempt.Status);
        Assert.Equal("backup_catalog_private_staging_payload_incomplete", attempt.LastErrorCode);
        Assert.False((attempt.LastErrorSummary ?? string.Empty).Contains(rawFailureDetail, StringComparison.Ordinal));

        var operation = await fixture.Db.RuntimeOperations.SingleAsync();
        Assert.Equal("failed", operation.Status);
        Assert.False((operation.LastError ?? string.Empty).Contains(rawFailureDetail, StringComparison.Ordinal));

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.NotNull(workspace);
        Assert.Equal("needs-attention", workspace!.OverallStatus.Code);
        Assert.Equal(
            RestoreWorkspaceStageStates.Failed,
            workspace.StandardStages.Single(stage => stage.Code == "private-test").State);
        Assert.Contains(
            workspace.Evidence.Categories.Single(category => category.Code == "private-test").Items,
            item => item.EventCode == "restore.private-test.failed");
    }

    [Fact]
    public async Task Failed_private_test_records_needs_attention_and_can_retry_from_the_same_catalog_workspace()
    {
        await using var fixture = await PrivateTestFixture.CreateAsync(
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            validationId: null,
            runs:
            [
                CreateRun(
                    status: "failed",
                    sourceKind: PrivateStagingSourceKinds.BackupCatalog,
                    catalogEntryId: "bkp_workspace_retry",
                    validationId: null,
                    stagingId: "staging-failed",
                    databaseImportSucceeded: false,
                    synapseHealthPassed: false),
                CreateRun(
                    status: "ready",
                    sourceKind: PrivateStagingSourceKinds.BackupCatalog,
                    catalogEntryId: "bkp_workspace_retry",
                    validationId: null,
                    stagingId: "staging-retry")
            ]);

        var failed = await fixture.Service.RunAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.Equal("needs-attention", failed.Status);
        Assert.Equal("staging-failed", failed.StagingId);

        var failedAttempt = await fixture.Db.RestoreAttempts.SingleAsync();
        Assert.Equal(RestoreAttemptStatuses.NeedsAttention, failedAttempt.Status);
        Assert.Equal(RestoreAttemptStages.NeedsAttention, failedAttempt.CurrentStage);
        Assert.Equal("restore.private-test.failed", failedAttempt.LastErrorCode);

        var retry = await fixture.Service.RunAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.Equal("ready", retry.Status);
        Assert.Equal("staging-retry", retry.StagingId);

        var operations = await fixture.Db.RuntimeOperations
            .OrderBy(operation => operation.RequestedAtUtc)
            .ToListAsync();

        Assert.Equal(2, operations.Count);
        Assert.Equal("failed", operations[0].Status);
        Assert.Equal("succeeded", operations[1].Status);
        Assert.All(operations, operation => Assert.Equal("restore.private-test", operation.Operation));

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.NotNull(workspace);
        var privateTestStage = Assert.Single(
            workspace!.StandardStages,
            stage => stage.Code == "private-test");
        Assert.Equal(RestoreWorkspaceStageStates.Completed, privateTestStage.State);
        Assert.Contains(
            workspace.Evidence.Categories.Single(category => category.Code == "private-test").Items,
            item => item.EventCode == "restore.private-test.failed");
        Assert.Contains(
            workspace.Evidence.Categories.Single(category => category.Code == "private-test").Items,
            item => item.EventCode == "restore.private-test.passed");
    }

    [Fact]
    public async Task Private_test_workspace_retains_historical_evidence_after_staging_runtime_is_destroyed()
    {
        var successfulRun = CreateRun(
            status: "ready",
            sourceKind: PrivateStagingSourceKinds.BackupCatalog,
            catalogEntryId: "bkp_workspace_local",
            validationId: null,
            stagingId: "staging-destroyed");

        await using var fixture = await PrivateTestFixture.CreateAsync(
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            validationId: null,
            runs: [successfulRun]);

        var response = await fixture.Service.RunAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);
        Assert.Equal("ready", response.Status);

        var destroyedAtUtc = DateTimeOffset.UtcNow;
        await fixture.WritePrivateStagingHistoryAsync(
            successfulRun with
            {
                Status = "destroyed",
                FinishedAtUtc = destroyedAtUtc,
                Destroy = new PrivateStagingDestroySummary(
                    DestroyedAtUtc: destroyedAtUtc,
                    SynapseContainerRemoved: true,
                    PostgresContainerRemoved: true,
                    NetworkRemoved: true,
                    WorkspaceRemoved: true,
                    Warnings: [])
            });

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        var privateTestStage = Assert.Single(
            workspace!.StandardStages,
            stage => stage.Code == "private-test");
        var privateTestEvidence = Assert.IsType<RestoreWorkspacePrivateTestEvidence>(
            privateTestStage.PrivateTestEvidence);

        Assert.Equal("staging-destroyed", privateTestEvidence.StagingId);
        Assert.Equal("matrix.workspace.test", privateTestEvidence.MatrixServerName);
        Assert.True(privateTestEvidence.DatabaseImportSucceeded);
        Assert.True(privateTestEvidence.SynapseHealthPassed);
        Assert.True(privateTestEvidence.PrivateOnly);
        Assert.Equal("destroyed", privateTestEvidence.StagingRuntimeStatus);
        Assert.True(privateTestEvidence.StagingRuntimeDestroyed is true);
        Assert.NotNull(privateTestEvidence.DestroyAvailable);
        Assert.False(privateTestEvidence.DestroyAvailable!.Value);
        Assert.Equal(destroyedAtUtc, privateTestEvidence.DestroyedAtUtc);
        Assert.Equal(
            "Private test completed. The isolated staging environment was explicitly destroyed; historical evidence remains available.",
            privateTestStage.Summary);
    }

    [Fact]
    public async Task RESTORE_PRIVATE_TEST_DESTROY_AUDIT_CORR_01_persists_cleanup_in_logs_and_evidence_and_retains_it_after_cancellation()
    {
        var successfulRun = CreateRun(
            status: "ready",
            sourceKind: PrivateStagingSourceKinds.BackupCatalog,
            catalogEntryId: "bkp_workspace_local",
            validationId: null,
            stagingId: "staging-destroy-audit");

        await using var fixture = await PrivateTestFixture.CreateAsync(
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            validationId: null,
            runs: [successfulRun]);

        var response = await fixture.Service.RunAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);
        Assert.Equal("ready", response.Status);

        var auditContext = await fixture.RetirementAudit.ResolveAsync(
            "staging-destroy-audit",
            CancellationToken.None);
        Assert.NotNull(auditContext);
        Assert.Equal(fixture.RestoreSessionId, auditContext!.RestoreSessionId);
        Assert.Equal(response.OperationId, auditContext.PrivateTestOperationId);

        await fixture.RetirementAudit.RecordDestroyRequestedAsync(
            auditContext,
            CancellationToken.None);

        var destroyedAtUtc = DateTimeOffset.UtcNow;
        var destroy = new PrivateStagingDestroySummary(
            DestroyedAtUtc: destroyedAtUtc,
            SynapseContainerRemoved: true,
            PostgresContainerRemoved: true,
            NetworkRemoved: true,
            WorkspaceRemoved: true,
            Warnings: [],
            ElementContainerRemoved: true);

        await fixture.WritePrivateStagingHistoryAsync(
            successfulRun with
            {
                Status = "destroyed",
                FinishedAtUtc = destroyedAtUtc,
                Destroy = destroy
            });

        await fixture.RetirementAudit.RecordDestroyedIfMissingAsync(
            auditContext,
            destroy,
            CancellationToken.None);
        await fixture.RetirementAudit.RecordDestroyedIfMissingAsync(
            auditContext,
            destroy,
            CancellationToken.None);

        await fixture.Coordinator.CancelAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        var logPage = await fixture.Logs.ListAsync(
            fixture.RestoreSessionId,
            new RestoreLogQuery(1, 100, null, null, null),
            CancellationToken.None);

        Assert.NotNull(logPage);
        Assert.Single(
            logPage!.Events,
            entry => entry.EventCode == "restore.private-test.destroy.requested");

        var destroyedEvent = Assert.Single(
            logPage.Events,
            entry => entry.EventCode == "restore.private-test.destroyed");
        Assert.Equal(response.OperationId, destroyedEvent.OperationId);
        Assert.NotNull(destroyedEvent.Details);
        Assert.Equal("staging-destroy-audit", destroyedEvent.Details!["stagingId"]);
        Assert.Equal(
            destroyedAtUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            destroyedEvent.Details["destroyedAtUtc"]);
        Assert.Equal("true", destroyedEvent.Details["elementContainerRemoved"]);
        Assert.Equal("true", destroyedEvent.Details["synapseContainerRemoved"]);
        Assert.Equal("true", destroyedEvent.Details["postgresContainerRemoved"]);
        Assert.Equal("true", destroyedEvent.Details["networkRemoved"]);
        Assert.Equal("true", destroyedEvent.Details["workspaceRemoved"]);
        Assert.Equal("0", destroyedEvent.Details["warningCount"]);
        Assert.Contains(
            logPage.Events,
            entry => entry.EventCode == "restore.cancelled");

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        Assert.NotNull(workspace);
        var privateTestEvidence = Assert.Single(
            workspace!.Evidence.Categories,
            category => category.Code == "private-test");
        var destroyedEvidence = Assert.Single(
            privateTestEvidence.Items,
            item => item.EventCode == "restore.private-test.destroyed");
        Assert.Equal("succeeded", destroyedEvidence.Status);
        Assert.Contains("staging-destroy-audit", destroyedEvidence.Description);

        var restoreManagementEvidence = Assert.Single(
            workspace.Evidence.Categories,
            category => category.Code == "restore-management");
        Assert.Contains(
            restoreManagementEvidence.Items,
            item => item.EventCode == "restore.cancelled");
    }

    [Fact]
    public async Task PLATFORM_TURN_01E_CLEANROOM_CORR_04_CORR_03_partial_retirement_remains_retryable_and_needs_attention()
    {
        var successfulRun = CreateRun(
            status: "ready",
            sourceKind: PrivateStagingSourceKinds.BackupCatalog,
            catalogEntryId: "bkp_workspace_local",
            validationId: null,
            stagingId: "staging-partial-retirement");

        await using var fixture = await PrivateTestFixture.CreateAsync(
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            validationId: null,
            runs: [successfulRun]);

        var response = await fixture.Service.RunAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);
        Assert.Equal("ready", response.Status);

        await fixture.WritePrivateStagingHistoryAsync(
            successfulRun with
            {
                Status = "destroy-needs-attention",
                FinishedAtUtc = DateTimeOffset.UtcNow,
                Destroy = new PrivateStagingDestroySummary(
                    DestroyedAtUtc: DateTimeOffset.UtcNow,
                    SynapseContainerRemoved: true,
                    PostgresContainerRemoved: true,
                    NetworkRemoved: false,
                    WorkspaceRemoved: true,
                    Warnings: ["network cleanup interrupted"],
                    ElementContainerRemoved: true)
            });

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        var privateTestStage = Assert.Single(
            workspace!.StandardStages,
            stage => stage.Code == "private-test");
        var privateTestEvidence = Assert.IsType<RestoreWorkspacePrivateTestEvidence>(
            privateTestStage.PrivateTestEvidence);

        Assert.Equal(
            "retirement-needs-attention",
            privateTestEvidence.StagingRuntimeStatus);
        Assert.False(privateTestEvidence.StagingRuntimeDestroyed is true);
        Assert.True(privateTestEvidence.DestroyAvailable is true);
        Assert.Null(privateTestEvidence.DestroyedAtUtc);
    }

    [Fact]
    public async Task Private_test_workspace_recovers_matrix_server_name_from_matching_history_when_operation_evidence_is_incomplete()
    {
        var successfulRun = CreateRun(
            status: "ready",
            sourceKind: PrivateStagingSourceKinds.BackupCatalog,
            catalogEntryId: "bkp_workspace_local",
            validationId: null,
            stagingId: "staging-history-matrix");

        await using var fixture = await PrivateTestFixture.CreateAsync(
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            validationId: null,
            runs: [successfulRun]);

        var response = await fixture.Service.RunAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);
        Assert.Equal("ready", response.Status);

        await fixture.WritePrivateStagingHistoryAsync(successfulRun);
        await fixture.ClearPersistedPrivateTestMatrixServerNameAsync();

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None);

        var privateTestStage = Assert.Single(
            workspace!.StandardStages,
            stage => stage.Code == "private-test");
        var privateTestEvidence = Assert.IsType<RestoreWorkspacePrivateTestEvidence>(
            privateTestStage.PrivateTestEvidence);

        Assert.Equal("matrix.workspace.test", privateTestEvidence.MatrixServerName);
        Assert.Equal("retained", privateTestEvidence.StagingRuntimeStatus);
    }

    [Fact]
    public async Task Catalog_workspace_projects_matrix_identity_from_homeserver_before_private_test()
    {
        await using var fixture = await PrivateTestFixture.CreateAsync(
            originKind: BackupCatalogOriginKinds.LocalCaptured,
            validationId: null,
            runs: [CreateRun(
                status: "ready",
                sourceKind: PrivateStagingSourceKinds.BackupCatalog,
                catalogEntryId: "bkp_workspace_local",
                validationId: null,
                stagingId: "staging-local")]);

        var workspace = await fixture.Workspace.GetAsync(
            fixture.RestoreSessionId,
            CancellationToken.None)
            ?? throw new InvalidOperationException("Workspace projection was not returned.");

        Assert.Equal("backup-catalog", workspace.Source.Kind);
        Assert.Equal("matrix.workspace.test", workspace.Source.MatrixHost);
        Assert.Equal("chat.workspace.test", workspace.Source.ElementHost);
        Assert.Equal("available", workspace.Source.ValidationStatus);
        Assert.Equal(
            "The managed Backup Catalog payload is available for restoration.",
            workspace.Source.ValidationSummary);
    }

    private static PrivateStagingRunResult CreateRun(
        string status,
        string sourceKind,
        string catalogEntryId,
        string? validationId,
        string stagingId,
        bool databaseImportSucceeded = true,
        bool synapseHealthPassed = true) =>
        new(
            Source: "control-plane",
            Status: status,
            Mode: "private-synapse-staging",
            StagingId: stagingId,
            ValidationId: validationId,
            StartedAtUtc: DateTimeOffset.UtcNow,
            FinishedAtUtc: DateTimeOffset.UtcNow,
            UploadedZipPath: null,
            WorkspacePath: "/mem-data/restore-staging/runs/" + stagingId,
            RuntimePath: "/mem-data/restore-staging/runs/" + stagingId + "/runtime",
            MatrixDataPath: "/mem-data/restore-staging/runs/" + stagingId + "/runtime/matrix",
            ElementDataPath: "/mem-data/restore-staging/runs/" + stagingId + "/runtime/element",
            DatabaseDumpPath: "/mem-data/restore-staging/runs/" + stagingId + "/database/synapse.sql",
            NetworkName: "mem-restore-staging-" + stagingId,
            NetworkId: "network-" + stagingId,
            PostgresContainerName: "mem-restore-staging-postgres-" + stagingId,
            PostgresContainerId: "postgres-" + stagingId,
            SynapseContainerName: "mem-restore-staging-synapse-" + stagingId,
            SynapseContainerId: "synapse-" + stagingId,
            PostgresImage: "postgres:16",
            SynapseImage: "matrixdotorg/synapse:latest",
            DatabaseName: "synapse_restore_staging",
            DatabaseUser: "mem_restore_staging",
            MatrixServerName: "matrix.workspace.test",
            TargetStackSlug: "workspace-test",
            Safety: new PrivateStagingSafetySummary(
                PrivateOnly: true,
                DockerNetworkInternal: true,
                PublicRoutesCreated: false,
                DnsChanged: false,
                CertificatesChanged: false,
                ProductionContainersTouched: false,
                ProductionDatabasesTouched: false,
                RequiresExplicitDestroy: true,
                Notes: []),
            Database: new PrivateStagingDatabaseSummary(
                ImportSucceeded: databaseImportSucceeded,
                PublicTableCount: 173,
                SynapseKnownTableCount: 6,
                UsersCount: 0,
                EventsCount: 0,
                RoomsCount: 0,
                StateEventsCount: 0),
            Runtime: new PrivateStagingRuntimeSummary(
                HomeserverConfigExtracted: true,
                HomeserverConfigPatched: true,
                SigningKeyExtracted: true,
                MediaStoreExtracted: true,
                MediaFiles: 0,
                MediaBytes: 0,
                ElementConfigExtracted: true,
                PostgresContainerStarted: true,
                SynapseContainerStarted: true,
                SynapseHealthPassed: synapseHealthPassed,
                HealthResponse: synapseHealthPassed ? "OK" : null,
                SynapseLogsTail: null),
            Checks: [],
            Warnings: [],
            Errors: [],
            Destroy: null,
            Detail: "Test private staging run.",
            SourceKind: sourceKind,
            CatalogEntryId: catalogEntryId);

    private sealed class PrivateTestFixture : IAsyncDisposable
    {
        private PrivateTestFixture(
            string dataRoot,
            string databasePath,
            MemDbContext db,
            RestoreStructuredLogService logs,
            RestoreWorkspacePrivateTestService service,
            RestoreWorkspaceService workspace,
            RestorePrivateTestRetirementAuditService retirementAudit,
            RestoreAttemptCoordinator coordinator,
            RecordingPrivateStagingRunner runner,
            string restoreSessionId)
        {
            DataRoot = dataRoot;
            DatabasePath = databasePath;
            Db = db;
            Logs = logs;
            Service = service;
            Workspace = workspace;
            RetirementAudit = retirementAudit;
            Coordinator = coordinator;
            Runner = runner;
            RestoreSessionId = restoreSessionId;
        }

        public string DataRoot { get; }
        public string DatabasePath { get; }
        public MemDbContext Db { get; }
        public RestoreStructuredLogService Logs { get; }
        public RestoreWorkspacePrivateTestService Service { get; }
        public RestoreWorkspaceService Workspace { get; }
        public RestorePrivateTestRetirementAuditService RetirementAudit { get; }
        public RestoreAttemptCoordinator Coordinator { get; }
        public RecordingPrivateStagingRunner Runner { get; }
        public string RestoreSessionId { get; }

        public async Task ClearPersistedPrivateTestMatrixServerNameAsync()
        {
            var operation = await Db.RuntimeOperations.SingleAsync(
                value => value.Operation == "restore.private-test");

            var evidence = JsonSerializer.Deserialize<RestorePrivateTestEvidence>(
                operation.EvidenceJson ?? throw new InvalidOperationException(
                    "Private-test operation evidence was not recorded."),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException(
                    "Private-test operation evidence could not be read.");

            operation.EvidenceJson = JsonSerializer.Serialize(
                evidence with { MatrixServerName = null },
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            await Db.SaveChangesAsync();
        }

        public async Task WritePrivateStagingHistoryAsync(
            PrivateStagingRunResult result)
        {
            var resultPath = Path.Combine(
                DataRoot,
                "restore-staging",
                "history",
                result.StagingId,
                "restore-staging-result.json");

            Directory.CreateDirectory(Path.GetDirectoryName(resultPath)!);
            await File.WriteAllTextAsync(
                resultPath,
                JsonSerializer.Serialize(
                    result,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }

        public static async Task<PrivateTestFixture> CreateAsync(
            string originKind,
            string? validationId,
            IEnumerable<PrivateStagingRunResult> runs,
            Exception? runnerException = null,
            TaskCompletionSource<bool>? runnerEntered = null,
            TaskCompletionSource<bool>? runnerRelease = null)
        {
            var dataRoot = Path.Combine(
                Path.GetTempPath(),
                $"mem-workspace-private-test-{Guid.NewGuid():N}");
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-workspace-private-test-{Guid.NewGuid():N}.db");
            var payloadRoot = Path.Combine(dataRoot, "catalog-payload");

            Directory.CreateDirectory(payloadRoot);
            var matrixDirectory = Path.Combine(payloadRoot, "matrix");
            Directory.CreateDirectory(matrixDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(matrixDirectory, "homeserver.yaml"),
                "server_name: matrix.workspace.test");

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = dataRoot
                })
                .Build();

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var catalogEntryId = originKind == BackupCatalogOriginKinds.LocalCaptured
                ? "bkp_workspace_local"
                : "bkp_workspace_imported";

            if (runs.First().CatalogEntryId == "bkp_workspace_retry")
            {
                catalogEntryId = "bkp_workspace_retry";
            }

            var entry = new BackupCatalogEntryEntity
            {
                Id = Guid.NewGuid(),
                CatalogEntryId = catalogEntryId,
                OriginKind = originKind,
                DisplayName = "Workspace private test backup",
                PayloadState = BackupCatalogPayloadStates.Available,
                PayloadStorageKind = BackupCatalogPayloadStorageKinds.CatalogManagedDirectory,
                PayloadDirectoryPath = payloadRoot,
                SourceStackSlug = "workspace-test",
                SourceBackupId = "20260629-workspace-test",
                ValidationId = validationId,
                IntegrityStatus = BackupCatalogIntegrityStatuses.Valid,
                IntegritySummary = "Payload is available.",
                MatrixHost = "matrix.workspace.test",
                ElementHost = "chat.workspace.test",
                WarningCount = 0,
                PayloadBytes = 1,
                CreatedAtUtc = DateTime.UtcNow,
                CapturedAtUtc = DateTime.UtcNow
            };

            db.BackupCatalogEntries.Add(entry);
            await db.SaveChangesAsync();

            var workspaceStore = new RestoreAttemptWorkspaceStore(configuration);
            var operationStore = new RuntimeOperationStore(db);
            var logs = new RestoreStructuredLogService(
                db,
                workspaceStore,
                NullLogger<RestoreStructuredLogService>.Instance);
            var coordinator = new RestoreAttemptCoordinator(
                db,
                workspaceStore,
                operationStore,
                logs,
                NullLogger<RestoreAttemptCoordinator>.Instance);
            var catalogStore = new BackupCatalogStore(db);
            var catalogPayloadResolver = new BackupCatalogPayloadResolver(catalogStore);
            var catalogRestoreSessions = new CatalogRestoreSessionService(
                catalogStore,
                coordinator);
            var prepared = await catalogRestoreSessions.PrepareAsync(
                entry.CatalogEntryId,
                CancellationToken.None);

            var runner = new RecordingPrivateStagingRunner(
                runs,
                runnerException,
                runnerEntered,
                runnerRelease);
            var operationLifetime = new RestoreWorkspacePrivateTestOperationLifetime(
                new FakeHostApplicationLifetime(),
                NullLogger<RestoreWorkspacePrivateTestOperationLifetime>.Instance);
            var service = new RestoreWorkspacePrivateTestService(
                coordinator,
                catalogStore,
                runner,
                logs,
                operationLifetime);

            var privateStagingHistory = new PrivateStagingHistoryService(
                configuration);
            var workspace = new RestoreWorkspaceService(
                db,
                logs,
                workspaceStore,
                catalogStore,
                catalogPayloadResolver,
                privateStagingHistory);
            var retirementAudit = new RestorePrivateTestRetirementAuditService(
                db,
                logs);

            return new PrivateTestFixture(
                dataRoot,
                databasePath,
                db,
                logs,
                service,
                workspace,
                retirementAudit,
                coordinator,
                runner,
                prepared.RestoreSessionId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();

            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, recursive: true);
            }

            foreach (var path in new[] { DatabasePath, DatabasePath + "-shm", DatabasePath + "-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private sealed class RecordingPrivateStagingRunner : IPrivateStagingRunner
    {
        private readonly Queue<PrivateStagingRunResult> _runs;
        private readonly Exception? _exception;
        private readonly TaskCompletionSource<bool>? _entered;
        private readonly TaskCompletionSource<bool>? _release;

        public RecordingPrivateStagingRunner(
            IEnumerable<PrivateStagingRunResult> runs,
            Exception? exception,
            TaskCompletionSource<bool>? entered = null,
            TaskCompletionSource<bool>? release = null)
        {
            _runs = new Queue<PrivateStagingRunResult>(runs);
            _exception = exception;
            _entered = entered;
            _release = release;
        }

        public List<string> CatalogEntryIds { get; } = [];
        public List<PrivateStagingRunRequest> Requests { get; } = [];

        public async Task<PrivateStagingRunResult> CreatePrivateSynapseFromCatalogAsync(
            string catalogEntryId,
            PrivateStagingRunRequest request,
            CancellationToken ct)
        {
            CatalogEntryIds.Add(catalogEntryId);
            Requests.Add(request);
            _entered?.TrySetResult(true);

            if (_release is not null)
            {
                await _release.Task.WaitAsync(ct);
            }

            if (_exception is not null)
            {
                throw _exception;
            }

            if (_runs.Count == 0)
            {
                throw new InvalidOperationException("No private staging result was configured for this test.");
            }

            return _runs.Dequeue();
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
}
