using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.HostChecks.Runtime;
using Modules.Setup.Start;
using Modules.Setup.InstallRuns;

namespace Api.IntegrationTests.Setup;

public sealed class PlatformCoturnSetupIntegrationTests
{
    [Fact]
    public void Required_platform_TURN_steps_are_ordered_before_optional_support_tools_and_final_verification()
    {
        var steps = InstallStepNames.InitialSteps.ToArray();
        var certificate = Array.IndexOf(steps, InstallStepNames.IssueAndImportPlatformCertificate);
        var install = Array.IndexOf(steps, InstallStepNames.InstallSharedPlatformTurn);
        var verify = Array.IndexOf(steps, InstallStepNames.VerifySharedPlatformTurn);
        var supportTools = Array.IndexOf(steps, InstallStepNames.StartSelectedSupportTools);
        var finalVerification = Array.IndexOf(steps, InstallStepNames.RunVerificationChecks);

        Assert.True(certificate >= 0);
        Assert.True(install > certificate);
        Assert.True(verify > install);
        Assert.True(supportTools > verify);
        Assert.True(finalVerification > supportTools);
    }

    [Fact]
    public async Task Incomplete_pre_TURN_step_graph_gains_required_steps_without_losing_existing_evidence()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-platform-turn-step-graph-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var installationId = Guid.NewGuid();
            var legacySteps = InstallStepNames.InitialSteps
                .Where(step =>
                    step != InstallStepNames.InstallSharedPlatformTurn &&
                    step != InstallStepNames.VerifySharedPlatformTurn)
                .ToArray();

            await using (var seed = new MemDbContext(options))
            {
                await seed.Database.MigrateAsync();
                seed.Installations.Add(new InstallationEntity
                {
                    Id = installationId,
                    Status = InstallationStatuses.Running,
                    ConfigJson = "{}",
                    FrozenConfigJson = "{}",
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
                    UpdatedAtUtc = DateTime.UtcNow
                });

                for (var index = 0; index < legacySteps.Length; index++)
                {
                    var stepName = legacySteps[index];
                    var completed = stepName != InstallStepNames.RunVerificationChecks &&
                        stepName != InstallStepNames.CompleteSetupHandoff;
                    seed.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
                    {
                        Id = Guid.NewGuid(),
                        InstallationId = installationId,
                        StepName = stepName,
                        Sequence = index + 1,
                        Status = completed
                            ? InstallationStepStatuses.Succeeded
                            : InstallationStepStatuses.Pending,
                        Message = completed ? $"Historical success: {stepName}" : null,
                        ErrorMessage = null,
                        AttemptCount = completed ? 2 : 0,
                        StartedAtUtc = completed ? DateTime.UtcNow.AddMinutes(-5) : null,
                        CompletedAtUtc = completed ? DateTime.UtcNow.AddMinutes(-4) : null
                    });
                }

                await seed.SaveChangesAsync();
            }

            await using (var db = new MemDbContext(options))
            {
                await new InstallStepGraphReconciler(db)
                    .ReconcileAsync(installationId, CancellationToken.None);
            }

            await using (var verifyDb = new MemDbContext(options))
            {
                var steps = await verifyDb.InstallationStepExecutions
                    .AsNoTracking()
                    .Where(x => x.InstallationId == installationId)
                    .OrderBy(x => x.Sequence)
                    .ToListAsync();

                Assert.Equal(InstallStepNames.InitialSteps, steps.Select(x => x.StepName).ToArray());

                var installTurn = steps.Single(x => x.StepName == InstallStepNames.InstallSharedPlatformTurn);
                var verifyTurn = steps.Single(x => x.StepName == InstallStepNames.VerifySharedPlatformTurn);
                Assert.Equal(InstallationStepStatuses.Pending, installTurn.Status);
                Assert.Equal(0, installTurn.AttemptCount);
                Assert.Equal(InstallationStepStatuses.Pending, verifyTurn.Status);
                Assert.Equal(0, verifyTurn.AttemptCount);

                var supportTools = steps.Single(x => x.StepName == InstallStepNames.StartSelectedSupportTools);
                Assert.Equal(InstallationStepStatuses.Succeeded, supportTools.Status);
                Assert.Equal(2, supportTools.AttemptCount);
                Assert.Equal($"Historical success: {InstallStepNames.StartSelectedSupportTools}", supportTools.Message);
            }
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
    [Fact]
    public async Task Completed_install_without_shared_Coturn_is_reported_as_repair_required()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-platform-turn-startup-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();
            var installationId = Guid.NewGuid();
            db.Installations.Add(new InstallationEntity
            {
                Id = installationId,
                Status = InstallationStatuses.Succeeded,
                ConfigJson = "{}",
                FrozenConfigJson = "{}",
                CreatedAtUtc = DateTime.UtcNow.AddHours(-1),
                UpdatedAtUtc = DateTime.UtcNow,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-50),
                CompletedAtUtc = DateTime.UtcNow.AddMinutes(-40)
            });
            db.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
            {
                Id = Guid.NewGuid(),
                InstallationId = installationId,
                StepName = InstallStepNames.CompleteSetupHandoff,
                Sequence = 99,
                Status = InstallationStepStatuses.WaitingForUser,
                Message = "Historical setup handoff pending.",
                AttemptCount = 1
            });
            await db.SaveChangesAsync();

            var probe = new FixedDockerProbe(
            [
                Container("mem-postgres", "postgres:16"),
                Container("mem-npm", "jc21/nginx-proxy-manager:latest")
            ]);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Product:Version"] = "0.2.0"
                })
                .Build();
            var service = new SetupStartService(
                probe,
                db,
                configuration,
                NullLogger<SetupStartService>.Instance);

            var result = await service.GetAsync(CancellationToken.None);

            Assert.Equal(SetupStartModes.Repair, result.SetupMode);
            Assert.Equal(SetupStartInstallationStates.RepairRequired, result.InstallationState);
            Assert.Equal(SetupStartRecommendedActions.RunRepairCheck, result.RecommendedAction);
            var coturn = Assert.Single(result.RequiredServices, item => item.Key == "coturn");
            Assert.True(coturn.Required);
            Assert.False(coturn.Installed);
            Assert.Contains(result.Warnings, warning =>
                warning.Code == "required-dependency-not-ready");
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static SetupDockerContainer Container(string name, string image) =>
        new(
            Id: name,
            Name: name,
            Image: image,
            State: "running",
            Status: "Up (healthy)",
            Labels: new Dictionary<string, string>(),
            Ports: []);

    private sealed class FixedDockerProbe(IReadOnlyList<SetupDockerContainer> containers)
        : ISetupDockerRuntimeProbe
    {
        public Task<SetupDockerSystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new SetupDockerSystemInfo(
                ServerVersion: "29.4.2",
                DockerRootDir: "/var/lib/docker",
                OperatingSystem: "Ubuntu 24.04 LTS",
                Architecture: "x86_64",
                MemoryBytes: 8L * 1024 * 1024 * 1024,
                CpuCount: 8,
                ContainerCount: containers.Count,
                ImageCount: 10));

        public Task<IReadOnlyList<SetupDockerContainer>> ListContainersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(containers);

        public Task<IReadOnlyList<SetupDockerVolume>> ListVolumesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SetupDockerVolume>>([]);

        public Task<IReadOnlyList<SetupDockerNetwork>> ListNetworksAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SetupDockerNetwork>>([]);
    }

}
