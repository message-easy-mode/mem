using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.InstallRuns;

namespace Api.IntegrationTests.Setup;

public sealed class InstallationProgressStoreTests
{
    [Fact]
    public async Task STARTUP_INSTALL_REL_01J_CORR_02_progress_survives_store_recreation_and_tracks_subphases()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "mem-install-progress-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var installationId = Guid.NewGuid();
            var stepId = Guid.NewGuid();
            var context = new InstallStepContext(
                installationId,
                stepId,
                InstallStepNames.IssueAndImportPlatformCertificate,
                Sequence: 8,
                ConfigJson: null,
                AttemptNumber: 2);

            var store = new InstallProgressStore(directory);
            var reporter = new InstallProgressReporter(
                store,
                TimeProvider.System,
                NullLogger<InstallProgressReporter>.Instance);

            await reporter.BeginStepAsync(context, CancellationToken.None);
            await reporter.BeginPhaseAsync(
                context,
                "certificate.prepare",
                "Preparing the reviewed TLS certificate request.",
                CancellationToken.None);
            await reporter.BeginPhaseAsync(
                context,
                "certificate.issue",
                "Requesting the wildcard TLS certificate from Let's Encrypt.",
                CancellationToken.None);
            await reporter.HeartbeatCurrentAsync(
                installationId,
                "Certificate issuance is still in progress.",
                CancellationToken.None);

            var reloadedStore = new InstallProgressStore(directory);
            var reloaded = await reloadedStore.GetAsync(
                installationId,
                CancellationToken.None);

            Assert.NotNull(reloaded);
            Assert.Equal(installationId, reloaded.InstallationId);
            Assert.Equal(stepId, reloaded.StepId);
            Assert.Equal(8, reloaded.StepSequence);
            Assert.Equal(2, reloaded.AttemptNumber);
            Assert.Equal(InstallProgressStatuses.Running, reloaded.StepStatus);
            Assert.Equal("certificate.issue", reloaded.PhaseCode);
            Assert.Equal(InstallProgressStatuses.Running, reloaded.PhaseStatus);
            Assert.True(reloaded.LastActivityAtUtc >= reloaded.StepStartedAtUtc);

            Assert.Collection(
                reloaded.Phases,
                prepare =>
                {
                    Assert.Equal("certificate.prepare", prepare.Code);
                    Assert.Equal(InstallProgressStatuses.Succeeded, prepare.Status);
                    Assert.NotNull(prepare.CompletedAtUtc);
                },
                issue =>
                {
                    Assert.Equal("certificate.issue", issue.Code);
                    Assert.Equal(InstallProgressStatuses.Running, issue.Status);
                    Assert.Null(issue.CompletedAtUtc);
                });

            await reporter.CompleteStepAsync(
                context,
                "Certificate step completed.",
                CancellationToken.None);

            var completed = await reloadedStore.GetAsync(
                installationId,
                CancellationToken.None);

            Assert.NotNull(completed);
            Assert.Equal(InstallProgressStatuses.Succeeded, completed.StepStatus);
            Assert.Equal(InstallProgressStatuses.Succeeded, completed.PhaseStatus);
            Assert.Equal(
                InstallProgressStatuses.Succeeded,
                completed.Phases[^1].Status);
            Assert.NotNull(completed.Phases[^1].CompletedAtUtc);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
    [Fact]
    public async Task STARTUP_INSTALL_REL_01J_CORR_02_progress_failure_is_supplemental_and_never_blocks_installation()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "mem-install-progress-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var blockingFile = Path.Combine(root, "not-a-directory");
        await File.WriteAllTextAsync(blockingFile, "occupied");

        try
        {
            var context = new InstallStepContext(
                Guid.NewGuid(),
                Guid.NewGuid(),
                InstallStepNames.StartPostgres,
                Sequence: 5,
                ConfigJson: null,
                AttemptNumber: 1);

            var store = new InstallProgressStore(blockingFile);
            var reporter = new InstallProgressReporter(
                store,
                TimeProvider.System,
                NullLogger<InstallProgressReporter>.Instance);

            var exception = await Record.ExceptionAsync(() =>
                reporter.BeginStepAsync(context, CancellationToken.None));

            Assert.Null(exception);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }


    [Fact]
    public async Task INSTALL_PROGRESS_HISTORY_01_retains_completed_step_history_and_retry_attempts()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "mem-install-progress-history-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var installationId = Guid.NewGuid();
            var npmStepId = Guid.NewGuid();
            var certificateStepId = Guid.NewGuid();

            var store = new InstallProgressStore(directory);
            var reporter = new InstallProgressReporter(
                store,
                TimeProvider.System,
                NullLogger<InstallProgressReporter>.Instance);

            var npm = new InstallStepContext(
                installationId,
                npmStepId,
                InstallStepNames.StartNpmIngress,
                Sequence: 7,
                ConfigJson: null,
                AttemptNumber: 1);

            await reporter.BeginStepAsync(npm, CancellationToken.None);
            await reporter.BeginPhaseAsync(
                npm,
                "npm.inspect",
                "Inspecting Nginx Proxy Manager.",
                CancellationToken.None);
            await reporter.BeginPhaseAsync(
                npm,
                "npm.readiness",
                "Waiting for Nginx Proxy Manager readiness.",
                CancellationToken.None);
            await reporter.CompleteStepAsync(
                npm,
                "Nginx Proxy Manager is ready.",
                CancellationToken.None);

            var certificateAttempt1 = new InstallStepContext(
                installationId,
                certificateStepId,
                InstallStepNames.IssueAndImportPlatformCertificate,
                Sequence: 8,
                ConfigJson: null,
                AttemptNumber: 1);

            await reporter.BeginStepAsync(certificateAttempt1, CancellationToken.None);
            await reporter.BeginPhaseAsync(
                certificateAttempt1,
                "certificate.dns-stability",
                "Allowing deSEC authoritative DNS visibility to settle.",
                CancellationToken.None);
            await reporter.MarkFailedAsync(
                certificateAttempt1,
                "Authoritative DNS settling timed out safely.",
                CancellationToken.None);

            var certificateAttempt2 = certificateAttempt1 with
            {
                AttemptNumber = 2
            };

            await reporter.BeginStepAsync(certificateAttempt2, CancellationToken.None);
            await reporter.BeginPhaseAsync(
                certificateAttempt2,
                "certificate.dns-stability",
                "Allowing deSEC authoritative DNS visibility to settle.",
                CancellationToken.None);
            await reporter.BeginPhaseAsync(
                certificateAttempt2,
                "certificate.acme-validation",
                "Validating the ACME challenge.",
                CancellationToken.None);
            await reporter.CompleteStepAsync(
                certificateAttempt2,
                "Certificate issued and imported.",
                CancellationToken.None);

            var recreated = new InstallProgressStore(directory);
            var history = await recreated.GetHistoryAsync(
                installationId,
                CancellationToken.None);

            Assert.Equal(3, history.Count);

            var npmHistory = Assert.Single(history.Where(x => x.StepId == npmStepId));
            Assert.Equal(InstallProgressStatuses.Succeeded, npmHistory.StepStatus);
            Assert.Equal(
                new[] { "npm.inspect", "npm.readiness" },
                npmHistory.Phases.Select(x => x.Code).ToArray());
            Assert.All(
                npmHistory.Phases,
                phase => Assert.Equal(InstallProgressStatuses.Succeeded, phase.Status));

            var certificateHistory = history
                .Where(x => x.StepId == certificateStepId)
                .OrderBy(x => x.AttemptNumber)
                .ToArray();

            Assert.Equal(2, certificateHistory.Length);
            Assert.Equal(1, certificateHistory[0].AttemptNumber);
            Assert.Equal(InstallProgressStatuses.Failed, certificateHistory[0].StepStatus);
            Assert.Equal(2, certificateHistory[1].AttemptNumber);
            Assert.Equal(InstallProgressStatuses.Succeeded, certificateHistory[1].StepStatus);
            Assert.All(
                certificateHistory[1].Phases,
                phase => Assert.Equal(InstallProgressStatuses.Succeeded, phase.Status));

            var current = await recreated.GetAsync(
                installationId,
                CancellationToken.None);

            Assert.NotNull(current);
            Assert.Equal(certificateStepId, current.StepId);
            Assert.Equal(2, current.AttemptNumber);
            Assert.Equal(InstallProgressStatuses.Succeeded, current.StepStatus);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }


    [Fact]
    public async Task STARTUP_INSTALL_REL_01J_CORR_04_internal_ACME_recovery_is_truthful_without_failing_the_step()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "mem-install-progress-recovery-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            var installationId = Guid.NewGuid();
            var context = new InstallStepContext(
                installationId,
                Guid.NewGuid(),
                InstallStepNames.IssueAndImportPlatformCertificate,
                Sequence: 8,
                ConfigJson: null,
                AttemptNumber: 1);

            var store = new InstallProgressStore(directory);
            var reporter = new InstallProgressReporter(
                store,
                TimeProvider.System,
                NullLogger<InstallProgressReporter>.Instance);

            await reporter.BeginStepAsync(context, CancellationToken.None);
            await reporter.BeginPhaseAsync(
                context,
                "certificate.acme-validation",
                "Let's Encrypt is validating the DNS challenge.",
                CancellationToken.None);
            await reporter.SetPhaseRecoveryStatusAsync(
                installationId,
                "certificate.acme-validation",
                recovered: false,
                safeSummary: "Secondary validation missed the TXT record; recovering automatically.",
                cancellationToken: CancellationToken.None);
            await reporter.BeginPhaseAsync(
                context,
                "certificate.acme-dns-recovery",
                "Re-establishing the bounded authoritative DNS settling window.",
                CancellationToken.None);
            await reporter.BeginPhaseAsync(
                context,
                "certificate.acme-validation-retry",
                "Retrying Let's Encrypt DNS validation.",
                CancellationToken.None);

            var recovering = await store.GetAsync(installationId, CancellationToken.None);

            Assert.NotNull(recovering);
            Assert.Equal(InstallProgressStatuses.Running, recovering.StepStatus);
            Assert.Equal("certificate.acme-validation-retry", recovering.PhaseCode);
            Assert.Equal(
                InstallProgressStatuses.Recovering,
                Assert.Single(recovering.Phases.Where(x => x.Code == "certificate.acme-validation")).Status);

            await reporter.SetPhaseRecoveryStatusAsync(
                installationId,
                "certificate.acme-validation",
                recovered: true,
                safeSummary: "The secondary DNS miss recovered automatically.",
                cancellationToken: CancellationToken.None);
            await reporter.CompleteStepAsync(
                context,
                "Certificate issued and imported.",
                CancellationToken.None);

            var completed = await store.GetAsync(installationId, CancellationToken.None);

            Assert.NotNull(completed);
            Assert.Equal(InstallProgressStatuses.Succeeded, completed.StepStatus);
            Assert.Equal(
                InstallProgressStatuses.Recovered,
                Assert.Single(completed.Phases.Where(x => x.Code == "certificate.acme-validation")).Status);
            Assert.Equal(
                InstallProgressStatuses.Succeeded,
                Assert.Single(completed.Phases.Where(x => x.Code == "certificate.acme-validation-retry")).Status);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }


}
