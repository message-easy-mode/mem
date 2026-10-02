using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Docker;
using Infrastructure.Docker.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.HostChecks.Runtime;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Modules.Setup.Secrets;

namespace Api.IntegrationTests.Setup;

public sealed class LegacyApplicationInstallerRetirementTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void V011_APP_RETIRE_01_default_plan_disables_legacy_mem_applications()
    {
        var plan = InstallPlanFactory.CreateDefault();

        Assert.Equal("Message Easy Mode", plan.General.InstallName);
        Assert.False(plan.Platform.MemApi.Enabled);
        Assert.False(plan.Platform.MemWeb.Enabled);
    }

    [Fact]
    public void V011_APP_RETIRE_01_new_installations_do_not_schedule_legacy_application_or_route_steps()
    {
        var steps = InstallStepNames.InitialSteps;

        Assert.DoesNotContain(InstallStepNames.StartMemApi, steps);
        Assert.DoesNotContain(InstallStepNames.WaitForMemApiHealth, steps);
        Assert.DoesNotContain(InstallStepNames.StartMemWeb, steps);
        Assert.DoesNotContain(InstallStepNames.ConfigurePlatformIngressRoutes, steps);
        Assert.DoesNotContain(InstallStepNames.VerifyPlatformRoutes, steps);

        Assert.Contains(InstallStepNames.StartPostgres, steps);
        Assert.Contains(InstallStepNames.StartNpmIngress, steps);
        Assert.Contains(InstallStepNames.RunVerificationChecks, steps);
        Assert.Equal(InstallStepNames.CompleteSetupHandoff, steps[^1]);
    }

    [Fact]
    public async Task STARTUP_01D_C_handoff_step_waits_for_explicit_operator_acknowledgement()
    {
        var runner = new RecordingCommandRunner();
        var executor = CreateExecutor(runner);

        var result = await executor.ExecuteAsync(
            Context(
                InstallStepNames.CompleteSetupHandoff,
                JsonSerializer.Serialize(InstallPlanFactory.CreateDefault(), JsonOptions)),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("WaitingForUser", result.StepStatus);
        Assert.Contains("handoff", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task STARTUP_01D_C_runner_marks_platform_succeeded_while_handoff_waits_for_acknowledgement()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-startup-01d-c-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var installation = new InstallationEntity
            {
                Id = Guid.NewGuid(),
                Status = "Running",
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2),
                UpdatedAtUtc = DateTime.UtcNow,
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-1)
            };
            db.Installations.Add(installation);
            db.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
            {
                Id = Guid.NewGuid(),
                InstallationId = installation.Id,
                StepName = InstallStepNames.CompleteSetupHandoff,
                Sequence = 11,
                Status = "Pending",
                AttemptCount = 0
            });
            await db.SaveChangesAsync();

            var executor = CreateExecutor(new RecordingCommandRunner());
            var runner = new InstallRunner(
                db,
                executor,
                NullLogger<InstallRunner>.Instance);

            await runner.RunAsync(installation.Id, CancellationToken.None);

            db.ChangeTracker.Clear();

            var persistedInstallation = await db.Installations
                .AsNoTracking()
                .SingleAsync(x => x.Id == installation.Id);
            var handoff = await db.InstallationStepExecutions
                .AsNoTracking()
                .SingleAsync(x =>
                    x.InstallationId == installation.Id &&
                    x.StepName == InstallStepNames.CompleteSetupHandoff);

            Assert.Equal("Succeeded", persistedInstallation.Status);
            Assert.NotNull(persistedInstallation.CompletedAtUtc);
            Assert.Equal("WaitingForUser", handoff.Status);
            Assert.Null(handoff.CompletedAtUtc);
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task V011_APP_RETIRE_01_platform_updates_cannot_reenable_legacy_applications()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"mem-v011-app-retire-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var service = new InstallPlanService(
                db,
                NullLogger<InstallPlanService>.Instance,
                runCoordinator: new RecordingInstallRunCoordinator());

            var created = await service.CreateAsync(CancellationToken.None);
            var current = Deserialize(created.ConfigJson);

            var requestedPlatform = current.Platform with
            {
                MemApi = current.Platform.MemApi with { Enabled = true },
                MemWeb = current.Platform.MemWeb with { Enabled = true }
            };

            var updated = await service.UpdatePlatformAsync(
                created.Id,
                requestedPlatform,
                CancellationToken.None);

            Assert.NotNull(updated);
            var normalized = Deserialize(updated.ConfigJson);
            Assert.False(normalized.Platform.MemApi.Enabled);
            Assert.False(normalized.Platform.MemWeb.Enabled);
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task V011_APP_RETIRE_01_persisted_legacy_steps_complete_as_safe_no_ops()
    {
        var runner = new RecordingCommandRunner();
        var executor = CreateExecutor(runner);

        foreach (var stepName in InstallStepNames.RetiredLegacyApplicationSteps)
        {
            var result = await executor.ExecuteAsync(
                new InstallStepContext(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    stepName,
                    Sequence: 1,
                    ConfigJson: null),
                CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Contains("retired", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("skipped", result.Message, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task V011_APP_RETIRE_01_disabled_legacy_applications_do_not_fail_plan_validation()
    {
        var runner = new RecordingCommandRunner();
        var dockerProbe = new RecordingDockerRuntimeProbe();
        var executor = CreateExecutor(runner, dockerProbe);
        var plan = ReviewedPlan();

        var result = await executor.ExecuteAsync(
            Context(
                InstallStepNames.ValidateInstallPlan,
                JsonSerializer.Serialize(plan, JsonOptions)),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Empty(runner.Calls);
        Assert.Equal(1, dockerProbe.SystemInfoCalls);
        Assert.Equal(1, dockerProbe.ListContainersCalls);
    }

    [Fact]
    public async Task V011_APP_RETIRE_01_old_enabled_flags_do_not_create_the_legacy_mem_logs_volume()
    {
        var runner = new RecordingCommandRunner();
        var dockerHost = new RecordingDockerHost();
        var executor = CreateExecutor(runner, dockerHost: dockerHost);
        var plan = InstallPlanFactory.CreateDefault();
        var legacyPlan = plan with
        {
            Platform = plan.Platform with
            {
                MemApi = plan.Platform.MemApi with { Enabled = true },
                MemWeb = plan.Platform.MemWeb with { Enabled = true }
            }
        };

        var result = await executor.ExecuteAsync(
            Context(
                InstallStepNames.CreatePersistentVolumes,
                JsonSerializer.Serialize(legacyPlan, JsonOptions)),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Contains(legacyPlan.Platform.Postgres.VolumeName, dockerHost.EnsuredVolumes);
        Assert.DoesNotContain(
            dockerHost.EnsuredVolumes,
            volume => string.Equals(volume, "mem_logs", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(runner.Calls);
    }

    private static InstallStepContext Context(string stepName, string configJson) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            stepName,
            Sequence: 1,
            ConfigJson: configJson);

    private static InstallPlan ReviewedPlan()
    {
        var intent = InstallPlanFactory.CreateDefault() with
        {
            Preflight = new PreflightSetupConfig(
                RunId: "legacy-retirement-test",
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

        return intent with
        {
            Review = new ReviewSetupConfig(
                DateTime.UtcNow,
                SetupReviewPlanFingerprint.Compute(intent))
        };
    }

    private static InstallPlan Deserialize(string? json) =>
        JsonSerializer.Deserialize<InstallPlan>(json!, JsonOptions)
        ?? throw new InvalidOperationException("Installation config did not deserialize.");

    private static InstallStepExecutor CreateExecutor(
        ICommandRunner runner,
        ISetupDockerRuntimeProbe? dockerRuntimeProbe = null,
        IDockerHost? dockerHost = null) =>
        new(
            runner,
            dockerHost: dockerHost ?? new RecordingDockerHost(),
            dockerRuntimeProbe: dockerRuntimeProbe ?? new RecordingDockerRuntimeProbe(),
            npmAdminProbe: null!,
            db: null!,
            certificateStorage: null!,
            certificateValidation: null!,
            npmCertificateProbe: null!,
            npmReadiness: null!,
            npmProxyHostService: null!,
            npmOptions: null!,
            memCliHostCommandInstaller: null!,
            approvedPostgresRuntimeProvider: null!,
            portainerRuntimeService: null!,
            installationSecretStore: new AlwaysAvailableInstallationSecretStore(),
            platformCertificateProvisioner: null!,
            npmInitialAdminBootstrap: null!,
            logger: NullLogger<InstallStepExecutor>.Instance);

    private sealed class AlwaysAvailableInstallationSecretStore : IInstallationSecretStore
    {
        public Task SetProtectedAsync(Guid installationId, string category, string key, string secret, string? description, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> ResolveProtectedAsync(Guid installationId, string category, string key, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("test-secret");

        public Task<bool> ExistsAsync(Guid installationId, string category, string key, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task DeleteAsync(Guid installationId, string category, string key, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static void DeleteSqliteArtifacts(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-shm", databasePath + "-wal" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }


    private sealed class RecordingDockerHost : IDockerHost
    {
        public List<string> EnsuredVolumes { get; } = [];

        public Task<bool> PingAsync(CancellationToken ct) => Task.FromResult(true);

        public Task PullImageAsync(string image, CancellationToken ct) => Task.CompletedTask;

        public Task<bool> ImageExistsAsync(string image, CancellationToken ct) => Task.FromResult(true);

        public Task<IReadOnlyList<DockerContainerSummary>> ListContainersAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);

        public Task<IReadOnlyList<DockerContainerSummary>> ListByPrefixAsync(
            string namePrefix,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);

        public Task<IReadOnlyList<DockerContainerSummary>> ListByLabelAsync(
            string labelKey,
            string labelValue,
            CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DockerContainerSummary>>([]);

        public Task<DockerContainerInspection?> InspectByNameAsync(
            string containerName,
            CancellationToken ct) =>
            Task.FromResult<DockerContainerInspection?>(null);

        public Task EnsureNetworkAsync(string networkName, CancellationToken ct) => Task.CompletedTask;

        public Task EnsureVolumeAsync(string volumeName, CancellationToken ct)
        {
            EnsuredVolumes.Add(volumeName);
            return Task.CompletedTask;
        }

        public Task ConnectContainerToNetworkAsync(
            string containerIdOrName,
            string networkName,
            CancellationToken ct) =>
            Task.CompletedTask;

        public Task<string> CreateContainerAsync(DockerContainerSpec spec, CancellationToken ct) =>
            Task.FromResult("test-container");

        public Task CopyFileToContainerAsync(
            string containerIdOrName,
            string destinationDirectory,
            string fileName,
            ReadOnlyMemory<byte> content,
            UnixFileMode mode,
            CancellationToken ct) =>
            Task.CompletedTask;

        public Task<DockerExecResult> ExecAsync(
            string containerIdOrName,
            IReadOnlyList<string> command,
            TimeSpan timeout,
            CancellationToken ct) =>
            Task.FromResult(new DockerExecResult(0, string.Empty, string.Empty, false));

        public Task StartContainerAsync(string containerId, CancellationToken ct) => Task.CompletedTask;

        public Task StopContainerAsync(string containerId, CancellationToken ct) => Task.CompletedTask;

        public Task RemoveContainerAsync(
            string containerId,
            bool force,
            bool removeVolumes,
            CancellationToken ct) =>
            Task.CompletedTask;

        public Task<string> GetLogsAsync(string containerId, int tail, CancellationToken ct) =>
            Task.FromResult(string.Empty);
    }

    private sealed class RecordingDockerRuntimeProbe : ISetupDockerRuntimeProbe
    {
        public int SystemInfoCalls { get; private set; }
        public int ListContainersCalls { get; private set; }

        public Task<SetupDockerSystemInfo> GetSystemInfoAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SystemInfoCalls++;

            return Task.FromResult(new SetupDockerSystemInfo(
                ServerVersion: "29.4.2",
                DockerRootDir: "/var/lib/docker",
                OperatingSystem: "Ubuntu 24.04.2 LTS",
                Architecture: "x86_64",
                MemoryBytes: 8L * 1024 * 1024 * 1024,
                CpuCount: 8,
                ContainerCount: 0,
                ImageCount: 0));
        }

        public Task<IReadOnlyList<SetupDockerContainer>> ListContainersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ListContainersCalls++;
            return Task.FromResult<IReadOnlyList<SetupDockerContainer>>([]);
        }

        public Task<IReadOnlyList<SetupDockerVolume>> ListVolumesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SetupDockerVolume>>([]);
        }

        public Task<IReadOnlyList<SetupDockerNetwork>> ListNetworksAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<SetupDockerNetwork>>([]);
        }
    }

    private sealed class RecordingCommandRunner : ICommandRunner
    {
        public List<CommandCall> Calls { get; } = [];

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new CommandCall(fileName, arguments.ToArray()));

            return Task.FromResult(new CommandResult(
                ExitCode: 0,
                StandardOutput: "ok",
                StandardError: ""));
        }
    }

    private sealed record CommandCall(
        string FileName,
        IReadOnlyList<string> Arguments);
}
