using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;

namespace Api.IntegrationTests.Setup;

[CollectionDefinition(InstallationRunOwnershipCollection.Name, DisableParallelization = true)]
public sealed class InstallationRunOwnershipCollection
{
    public const string Name = "Installation run ownership";
}

[Collection(InstallationRunOwnershipCollection.Name)]
public sealed class InstallationRunOwnershipTests
{
    [Fact]
    public async Task STARTUP_INSTALL_REL_01B_coordinator_owns_one_execution_per_installation()
    {
        var runner = new BlockingInstallRunner();
        await using var services = new ServiceCollection()
            .AddScoped<IInstallRunner>(_ => runner)
            .BuildServiceProvider();

        var coordinator = new InstallRunCoordinator(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<InstallRunCoordinator>.Instance);

        await coordinator.StartAsync(CancellationToken.None);
        try
        {
            var installationId = Guid.NewGuid();
            var first = coordinator.Queue(installationId);
            var duplicate = coordinator.Queue(installationId);

            Assert.True(first.Queued);
            Assert.False(first.AlreadyOwned);
            Assert.False(duplicate.Queued);
            Assert.True(duplicate.AlreadyOwned);

            // Duplicate ownership is a synchronous coordinator contract and must not
            // depend on how quickly the background worker is scheduled by a busy
            // integration-test process. Separately prove that the accepted queue item
            // is eventually executed once.
            await runner.Started.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(1, runner.RunCount);
        }
        finally
        {
            runner.Release();
            await coordinator.StopAsync(CancellationToken.None);
            coordinator.Dispose();
        }
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01B_recovery_resets_interrupted_step_and_preserves_attempt_history()
    {
        var databasePath = TempDatabasePath();
        try
        {
            var options = Options(databasePath);
            var installationId = Guid.NewGuid();
            var interruptedStepId = Guid.NewGuid();

            await using (var db = new MemDbContext(options))
            {
                await db.Database.MigrateAsync();
                db.Installations.Add(Installation(installationId, InstallationStatuses.Running));
                db.InstallationStepExecutions.AddRange(
                    Step(installationId, 1, InstallStepNames.ValidateInstallPlan, InstallationStepStatuses.Succeeded, attemptCount: 1),
                    Step(installationId, 2, InstallStepNames.CreateOrVerifyDockerNetwork, InstallationStepStatuses.Running, attemptCount: 2, id: interruptedStepId),
                    Step(installationId, 3, InstallStepNames.CreatePersistentVolumes, InstallationStepStatuses.Pending, attemptCount: 0));
                await db.SaveChangesAsync();
            }

            await using (var db = new MemDbContext(options))
            {
                var recovery = new InstallationRunRecoveryService(
                    db,
                    NullLogger<InstallationRunRecoveryService>.Instance);

                var recovered = await recovery.RecoverInterruptedRunsAsync(CancellationToken.None);

                Assert.Equal(new[] { installationId }, recovered);
            }

            await using (var db = new MemDbContext(options))
            {
                var installation = await db.Installations
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == installationId);
                var steps = await db.InstallationStepExecutions
                    .AsNoTracking()
                    .Where(x => x.InstallationId == installationId)
                    .OrderBy(x => x.Sequence)
                    .ToListAsync();

                Assert.Equal(InstallationStatuses.Running, installation.Status);
                Assert.Null(installation.CompletedAtUtc);

                Assert.Equal(InstallationStepStatuses.Succeeded, steps[0].Status);
                Assert.Equal(InstallationStepStatuses.Pending, steps[1].Status);
                Assert.Equal(2, steps[1].AttemptCount);
                Assert.Null(steps[1].StartedAtUtc);
                Assert.Null(steps[1].CompletedAtUtc);
                Assert.Contains("interrupted", steps[1].Message, StringComparison.OrdinalIgnoreCase);

                Assert.Equal(InstallationStepStatuses.Pending, steps[2].Status);
                Assert.Equal(0, steps[2].AttemptCount);
            }
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01B_startup_recovery_queues_durable_running_installation()
    {
        var databasePath = TempDatabasePath();
        try
        {
            var options = Options(databasePath);
            var installationId = Guid.NewGuid();

            await using (var db = new MemDbContext(options))
            {
                await db.Database.MigrateAsync();
                db.Installations.Add(Installation(installationId, InstallationStatuses.Running));
                db.InstallationStepExecutions.Add(
                    Step(
                        installationId,
                        1,
                        InstallStepNames.ValidateInstallPlan,
                        InstallationStepStatuses.Running,
                        attemptCount: 1));
                await db.SaveChangesAsync();
            }

            var coordinator = new RecordingInstallRunCoordinator();
            await using var services = new ServiceCollection()
                .AddScoped<MemDbContext>(_ => new MemDbContext(options))
                .AddScoped<InstallationRunRecoveryService>()
                .AddLogging()
                .BuildServiceProvider();

            var hosted = new InstallationRecoveryHostedService(
                services.GetRequiredService<IServiceScopeFactory>(),
                coordinator,
                NullLogger<InstallationRecoveryHostedService>.Instance);

            await hosted.StartAsync(CancellationToken.None);

            Assert.Equal(1, coordinator.QueueCalls);

            await using var verify = new MemDbContext(options);
            var recoveredStep = await verify.InstallationStepExecutions
                .AsNoTracking()
                .SingleAsync(x => x.InstallationId == installationId);

            Assert.Equal(InstallationStepStatuses.Pending, recoveredStep.Status);
            Assert.Equal(1, recoveredStep.AttemptCount);
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01B_unexpected_worker_failure_becomes_durable_failed_state()
    {
        var databasePath = TempDatabasePath();
        try
        {
            var options = Options(databasePath);
            var installationId = Guid.NewGuid();
            var stepId = Guid.NewGuid();

            await using (var db = new MemDbContext(options))
            {
                await db.Database.MigrateAsync();
                db.Installations.Add(Installation(installationId, InstallationStatuses.Running));
                db.InstallationStepExecutions.Add(
                    Step(
                        installationId,
                        1,
                        InstallStepNames.ValidateInstallPlan,
                        InstallationStepStatuses.Running,
                        attemptCount: 1,
                        id: stepId));
                await db.SaveChangesAsync();

                var recovery = new InstallationRunRecoveryService(
                    db,
                    NullLogger<InstallationRunRecoveryService>.Instance);

                await recovery.MarkUnexpectedRunnerFailureAsync(
                    installationId,
                    new InvalidOperationException("synthetic runner failure"),
                    CancellationToken.None);
            }

            await using (var db = new MemDbContext(options))
            {
                var installation = await db.Installations
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == installationId);
                var step = await db.InstallationStepExecutions
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == stepId);

                Assert.Equal(InstallationStatuses.Failed, installation.Status);
                Assert.NotNull(installation.CompletedAtUtc);
                Assert.Contains("worker", installation.LastError, StringComparison.OrdinalIgnoreCase);

                Assert.Equal(InstallationStepStatuses.Failed, step.Status);
                Assert.NotNull(step.CompletedAtUtc);
                Assert.Contains("worker", step.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("synthetic", step.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01B_runner_never_marks_success_while_a_running_step_remains()
    {
        var databasePath = TempDatabasePath();
        try
        {
            var options = Options(databasePath);
            var installationId = Guid.NewGuid();

            await using (var db = new MemDbContext(options))
            {
                await db.Database.MigrateAsync();
                db.Installations.Add(Installation(installationId, InstallationStatuses.Running));
                db.InstallationStepExecutions.Add(
                    Step(
                        installationId,
                        1,
                        InstallStepNames.ValidateInstallPlan,
                        InstallationStepStatuses.Running,
                        attemptCount: 1));
                await db.SaveChangesAsync();

                var runner = new InstallRunner(
                    db,
                    stepExecutor: null!,
                    NullLogger<InstallRunner>.Instance);

                await runner.RunAsync(installationId, CancellationToken.None);
            }

            await using (var db = new MemDbContext(options))
            {
                var installation = await db.Installations
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == installationId);

                Assert.Equal(InstallationStatuses.Running, installation.Status);
                Assert.Null(installation.CompletedAtUtc);
            }
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01B_concurrent_start_requests_create_one_step_set_and_one_owned_run()
    {
        var databasePath = TempDatabasePath();
        try
        {
            var options = Options(databasePath);
            var installationId = Guid.NewGuid();

            await using (var seed = new MemDbContext(options))
            {
                await seed.Database.MigrateAsync();
                seed.Installations.Add(ReviewedInstallation(installationId));
                await seed.SaveChangesAsync();
            }

            var coordinator = new RecordingInstallRunCoordinator();
            await using var db1 = new MemDbContext(options);
            await using var db2 = new MemDbContext(options);

            var service1 = new InstallPlanService(
                db1,
                NullLogger<InstallPlanService>.Instance,
                coordinator);
            var service2 = new InstallPlanService(
                db2,
                NullLogger<InstallPlanService>.Instance,
                coordinator);

            var results = await Task.WhenAll(
                service1.RunAsync(installationId, CancellationToken.None),
                service2.RunAsync(installationId, CancellationToken.None));

            Assert.All(results, result =>
            {
                Assert.NotNull(result);
                Assert.Equal(InstallationStatuses.Running, result!.Status);
            });

            await using var verify = new MemDbContext(options);
            var stepCount = await verify.InstallationStepExecutions
                .AsNoTracking()
                .CountAsync(x => x.InstallationId == installationId);

            Assert.Equal(InstallStepNames.InitialSteps.Count, stepCount);
            Assert.Equal(2, coordinator.QueueCalls);
            Assert.Equal(1, coordinator.AcceptedQueueCount);
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    private static InstallationEntity ReviewedInstallation(Guid id)
    {
        var intent = InstallPlanFactory.CreateDefault() with
        {
            Preflight = new PreflightSetupConfig(
                RunId: "ownership-test",
                CompletedAtUtc: DateTimeOffset.UtcNow,
                RunStatus: "Succeeded",
                Passed: 1,
                Warnings: 0,
                Failed: 0,
                Skipped: 0,
                Unavailable: 0,
                Unknown: 0,
                BlockingIssueCount: 0,
                WarningCheckKeys: [],
                UnavailableCheckKeys: []),
            PublicAccess = InstallPlanFactory.CreateDefault().PublicAccess with
            {
                Domain = "*.example.com",
                Zone = "example.com",
                AcmeEmail = "admin@example.com",
                Preparation = new DomainPreparationSetupConfig(
                    Status: "Validated",
                    ValidatedAtUtc: DateTime.UtcNow,
                    ProviderAccessConfirmed: true,
                    ProviderCredentialStored: true)
            }
        };
        var reviewed = intent with
        {
            Review = new ReviewSetupConfig(
                DateTime.UtcNow,
                SetupReviewPlanFingerprint.Compute(intent))
        };
        var json = JsonSerializer.Serialize(reviewed, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        return new InstallationEntity
        {
            Id = id,
            Status = InstallationStatuses.Ready,
            ConfigJson = json,
            FrozenConfigJson = json,
            LastError = null,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-3),
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            StartedAtUtc = null,
            CompletedAtUtc = null
        };
    }

    private static InstallationEntity Installation(Guid id, string status) => new()
    {
        Id = id,
        Status = status,
        ConfigJson = null,
        FrozenConfigJson = null,
        LastError = null,
        CreatedAtUtc = DateTime.UtcNow.AddMinutes(-3),
        UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
        StartedAtUtc = status == InstallationStatuses.Draft
            ? null
            : DateTime.UtcNow.AddMinutes(-2),
        CompletedAtUtc = null
    };

    private static InstallationStepExecutionEntity Step(
        Guid installationId,
        int sequence,
        string stepName,
        string status,
        int attemptCount,
        Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        InstallationId = installationId,
        StepName = stepName,
        Sequence = sequence,
        Status = status,
        Message = status == InstallationStepStatuses.Running ? "Running..." : null,
        ErrorMessage = null,
        AttemptCount = attemptCount,
        StartedAtUtc = status == InstallationStepStatuses.Pending
            ? null
            : DateTime.UtcNow.AddMinutes(-1),
        CompletedAtUtc = status == InstallationStepStatuses.Succeeded
            ? DateTime.UtcNow
            : null
    };

    private static string TempDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        $"mem-startup-install-rel-01b-{Guid.NewGuid():N}.db");

    private static DbContextOptions<MemDbContext> Options(string databasePath) =>
        new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

    private static void DeleteSqliteArtifacts(string databasePath)
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
    }
}
