using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.InstallRuns;

namespace Api.IntegrationTests.Setup;

public sealed class SetupHandoffProgressHistoryTests
{
    [Fact]
    public async Task INSTALL_PROGRESS_HISTORY_01_handoff_completion_turns_the_final_progress_record_green()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-install-progress-handoff-{Guid.NewGuid():N}.db");
        var progressDirectory = Path.Combine(
            Path.GetTempPath(),
            "mem-install-progress-handoff",
            Guid.NewGuid().ToString("N"));

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var installationId = Guid.NewGuid();
            var handoffStepId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            db.Installations.Add(new InstallationEntity
            {
                Id = installationId,
                Status = "Succeeded",
                CreatedAtUtc = now.AddMinutes(-5),
                UpdatedAtUtc = now,
                StartedAtUtc = now.AddMinutes(-4),
                CompletedAtUtc = now.AddMinutes(-1)
            });
            db.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
            {
                Id = Guid.NewGuid(),
                InstallationId = installationId,
                StepName = InstallStepNames.RunVerificationChecks,
                Sequence = 10,
                Status = "Succeeded",
                Message = "Verification passed.",
                AttemptCount = 1,
                StartedAtUtc = now.AddMinutes(-2),
                CompletedAtUtc = now.AddMinutes(-1)
            });
            db.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
            {
                Id = handoffStepId,
                InstallationId = installationId,
                StepName = InstallStepNames.CompleteSetupHandoff,
                Sequence = 11,
                Status = "WaitingForUser",
                Message = "Finish setup.",
                AttemptCount = 1,
                StartedAtUtc = now.AddMinutes(-1),
                CompletedAtUtc = null
            });
            await db.SaveChangesAsync();

            var store = new InstallProgressStore(progressDirectory);
            var reporter = new InstallProgressReporter(
                store,
                TimeProvider.System,
                NullLogger<InstallProgressReporter>.Instance);

            var handoffContext = new InstallStepContext(
                installationId,
                handoffStepId,
                InstallStepNames.CompleteSetupHandoff,
                Sequence: 11,
                ConfigJson: null,
                AttemptNumber: 1);

            await reporter.BeginStepAsync(handoffContext, CancellationToken.None);
            await reporter.MarkWaitingForUserAsync(
                handoffContext,
                "Verification passed. Finish setup.",
                CancellationToken.None);

            var service = new SetupHandoffService(
                db,
                NullLogger<SetupHandoffService>.Instance,
                reporter);

            var completion = await service.CompleteAsync(
                installationId,
                CancellationToken.None);

            Assert.NotNull(completion);
            Assert.True(completion.Completed);

            var history = await store.GetHistoryAsync(
                installationId,
                CancellationToken.None);
            var finalAttempt = Assert.Single(history);

            Assert.Equal(handoffStepId, finalAttempt.StepId);
            Assert.Equal(InstallProgressStatuses.Succeeded, finalAttempt.StepStatus);
            Assert.Equal(InstallProgressStatuses.Succeeded, finalAttempt.PhaseStatus);
            Assert.Contains("acknowledged", finalAttempt.SafeSummary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            foreach (var path in new[]
                     {
                         databasePath,
                         databasePath + "-shm",
                         databasePath + "-wal"
                     })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            if (Directory.Exists(progressDirectory))
            {
                Directory.Delete(progressDirectory, recursive: true);
            }
        }
    }
}
