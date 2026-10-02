using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Data.Entities.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Auth.Identity;
using Modules.Setup.HostChecks.Runtime;
using Modules.Setup.InstallRuns;
using Modules.Setup.Start;

namespace Api.IntegrationTests.Setup;

public sealed class LegacyApplicationOperatorSurfaceRetirementTests
{
    [Fact]
    public async Task V011_APP_RETIRE_02_legacy_applications_require_the_separate_migrator()
    {
        await using var fixture = await Fixture.CreateAsync(
            DockerContainer("mem-api", "mem-api:0.1.0", "running"),
            DockerContainer("mem-web", "mem-web:0.1.0", "running"));

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartModes.MigrationRequired, response.SetupMode);
        Assert.Equal(
            SetupStartRecommendedActions.UseMemMigrate,
            response.RecommendedAction);
        Assert.Equal(SetupStartTargets.SetupStart, response.StartupTarget);
        Assert.Equal(
            SetupStartInstallationStates.PartiallyInstalled,
            response.InstallationState);
        Assert.DoesNotContain(
            response.RequiredServices,
            service => service.Key is "mem-api" or "mem-web");

        var warning = Assert.Single(response.Warnings);
        Assert.Equal("legacy-v010-applications-detected", warning.Code);
        Assert.True(warning.Blocking);
        Assert.Contains("MEM Migrate", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("separate server", warning.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0.2.0", response.DetectedInstallation?.TargetVersion);
        Assert.False(response.DetectedInstallation?.UpgradeAvailable ?? true);
    }

    [Fact]
    public async Task V011_APP_RETIRE_02_current_control_plane_does_not_require_legacy_applications()
    {
        await using var fixture = await Fixture.CreateAsync(
            DockerContainer("mem-postgres", "postgres:16", "running"),
            DockerContainer("mem-npm", "jc21/nginx-proxy-manager:2.14.0", "running"),
            DockerContainer("mem-coturn", "sha256:approved-coturn", "running"),
            DockerContainer("mem-api", "mem-api:0.1.0", "running"),
            DockerContainer("mem-web", "mem-web:0.1.0", "running"));

        fixture.Db.Installations.Add(new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "Succeeded",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartModes.AlreadyInstalled, response.SetupMode);
        Assert.Equal(SetupStartInstallationStates.Installed, response.InstallationState);
        Assert.Equal(SetupStartRecommendedActions.OpenDashboard, response.RecommendedAction);
        Assert.Equal(SetupStartTargets.Dashboard, response.StartupTarget);
        Assert.Equal(new[] { "postgres", "npm", "coturn" }, response.RequiredServices.Select(x => x.Key));
        Assert.All(response.RequiredServices, service => Assert.True(service.Running));
        Assert.Contains(
            response.Warnings,
            warning => warning.Code == "legacy-applications-observed" && !warning.Blocking);
    }

    [Fact]
    public async Task STARTUP_01A_completed_installation_targets_dashboard_when_a_required_dependency_is_stopped()
    {
        await using var fixture = await Fixture.CreateAsync(
            DockerContainer("mem-postgres", "postgres:16", "running"),
            DockerContainer("mem-npm", "jc21/nginx-proxy-manager:2.14.0", "exited"),
            DockerContainer("mem-coturn", "sha256:approved-coturn", "running"));

        fixture.Db.Installations.Add(new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "Succeeded",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartModes.Repair, response.SetupMode);
        Assert.Equal(SetupStartInstallationStates.RepairRequired, response.InstallationState);
        Assert.Equal(SetupStartRecommendedActions.RunRepairCheck, response.RecommendedAction);
        Assert.Equal(SetupStartTargets.Dashboard, response.StartupTarget);
        Assert.Contains(response.Warnings, warning => warning.Code == "required-dependency-not-ready");
    }

    [Fact]
    public async Task DEV_CONTEXT_PARITY_01_CORR_01_completed_installation_remains_authoritative_over_a_newer_draft()
    {
        await using var fixture = await Fixture.CreateAsync(
            DockerContainer("mem-postgres", "postgres:16", "running"),
            DockerContainer("mem-npm", "jc21/nginx-proxy-manager:2.14.0", "running"),
            DockerContainer("mem-coturn", "sha256:approved-coturn", "running"));

        var completedAt = DateTime.UtcNow.AddMinutes(-10);
        fixture.Db.Installations.Add(new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "Succeeded",
            CreatedAtUtc = completedAt.AddMinutes(-5),
            UpdatedAtUtc = completedAt,
            StartedAtUtc = completedAt.AddMinutes(-4),
            CompletedAtUtc = completedAt
        });
        fixture.Db.Installations.Add(new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "Draft",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = null,
            CompletedAtUtc = null
        });
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartModes.AlreadyInstalled, response.SetupMode);
        Assert.Equal(SetupStartInstallationStates.Installed, response.InstallationState);
        Assert.Equal(SetupStartRecommendedActions.OpenDashboard, response.RecommendedAction);
        Assert.Equal(SetupStartTargets.Dashboard, response.StartupTarget);
    }

    [Fact]
    public async Task DEV_CONTEXT_PARITY_01_CORR_01_draft_without_a_completed_installation_remains_in_setup()
    {
        await using var fixture = await Fixture.CreateAsync(
            DockerContainer("mem-postgres", "postgres:16", "running"),
            DockerContainer("mem-npm", "jc21/nginx-proxy-manager:2.14.0", "running"));

        var draftId = Guid.NewGuid();
        fixture.Db.Installations.Add(new InstallationEntity
        {
            Id = draftId,
            Status = InstallationStatuses.Draft,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = null,
            CompletedAtUtc = null
        });
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartModes.FreshInstall, response.SetupMode);
        Assert.Equal(SetupStartInstallationStates.PartiallyInstalled, response.InstallationState);
        Assert.Equal(SetupStartRecommendedActions.ContinueSetup, response.RecommendedAction);
        Assert.Equal(SetupStartTargets.SetupStart, response.StartupTarget);
        Assert.Equal(draftId, response.ActiveInstallationId);
        Assert.Contains(response.Warnings, warning => warning.Code == "setup-planning-in-progress");
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01A_ready_plan_reconstructs_first_time_setup_after_restart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var readyId = Guid.NewGuid();
        fixture.Db.Installations.Add(new InstallationEntity
        {
            Id = readyId,
            Status = InstallationStatuses.Ready,
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartModes.FreshInstall, response.SetupMode);
        Assert.Equal(SetupStartRecommendedActions.ContinueSetup, response.RecommendedAction);
        Assert.Equal(SetupStartTargets.SetupStart, response.StartupTarget);
        Assert.Equal(readyId, response.ActiveInstallationId);
    }

    [Fact]
    public async Task STARTUP_01B_CORR_01_established_operator_state_without_installation_history_targets_dashboard()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCompletedPlatformOwnerAsync();

        fixture.Db.RuntimeServices.Add(new RuntimeServiceEntity
        {
            Id = Guid.NewGuid(),
            ServiceName = "seq",
            ContainerName = "mem-seq-dev",
            Image = "datalust/seq:latest",
            ContainerPort = 80,
            PreferredHostPort = 17341,
            SelectedHostPort = 17341,
            Status = "Running",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            LastObservedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartModes.Repair, response.SetupMode);
        Assert.Equal(SetupStartInstallationStates.PartiallyInstalled, response.InstallationState);
        Assert.Equal(SetupStartRecommendedActions.ReviewDiagnostics, response.RecommendedAction);
        Assert.Equal(SetupStartTargets.Dashboard, response.StartupTarget);
        Assert.Equal("0.2.0", response.DetectedInstallation?.Version);
        Assert.Contains(response.Warnings, warning => warning.Code == "installation-history-missing");
    }

    [Fact]
    public async Task STARTUP_01B_CORR_01_completed_owner_without_operator_state_remains_first_time_setup()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddCompletedPlatformOwnerAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartModes.FreshInstall, response.SetupMode);
        Assert.Equal(SetupStartInstallationStates.NotInstalled, response.InstallationState);
        Assert.Equal(SetupStartRecommendedActions.RunSetupCheck, response.RecommendedAction);
        Assert.Equal(SetupStartTargets.SetupStart, response.StartupTarget);
    }

    [Fact]
    public async Task STARTUP_01D_A_failed_installation_reopens_failure_review()
    {
        await using var fixture = await Fixture.CreateAsync(
            DockerContainer("mem-postgres", "postgres:16", "running"),
            DockerContainer("mem-npm", "jc21/nginx-proxy-manager:2.14.0", "running"));

        fixture.Db.Installations.Add(new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "Failed",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartModes.Repair, response.SetupMode);
        Assert.Equal(SetupStartInstallationStates.RepairRequired, response.InstallationState);
        Assert.Equal(SetupStartTargets.ResumeInstallation, response.StartupTarget);
        Assert.NotNull(response.ActiveInstallationId);
        Assert.Equal(SetupActiveInstallationStages.FailureReview, response.ActiveInstallationStage);
    }

    [Fact]
    public async Task STARTUP_01D_B_failed_final_verification_reopens_verification_report()
    {
        await using var fixture = await Fixture.CreateAsync(
            DockerContainer("mem-postgres", "postgres:16", "running"),
            DockerContainer("mem-npm", "jc21/nginx-proxy-manager:2.14.0", "running"));

        var installation = new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "Failed",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            LastError = "Final verification failed."
        };

        fixture.Db.Installations.Add(installation);
        fixture.Db.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
        {
            Id = Guid.NewGuid(),
            InstallationId = installation.Id,
            StepName = InstallStepNames.RunVerificationChecks,
            Sequence = 10,
            Status = "Failed",
            Message = "Final verification failed.",
            ErrorMessage = "NPM readiness verification failed.",
            AttemptCount = 1,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartTargets.ResumeInstallation, response.StartupTarget);
        Assert.Equal(
            SetupStartRecommendedActions.ReviewVerification,
            response.RecommendedAction);
        Assert.Equal(installation.Id, response.ActiveInstallationId);
        Assert.Equal(
            SetupActiveInstallationStages.Verification,
            response.ActiveInstallationStage);
        Assert.Contains(
            response.Warnings,
            warning => warning.Code == "installation-verification-required");
    }

    [Fact]
    public async Task STARTUP_01D_A_running_installation_reopens_authoritative_activity()
    {
        await using var fixture = await Fixture.CreateAsync(
            DockerContainer("mem-postgres", "postgres:16", "running"),
            DockerContainer("mem-npm", "jc21/nginx-proxy-manager:2.14.0", "running"));

        var installation = new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "Running",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            CompletedAtUtc = null
        };
        fixture.Db.Installations.Add(installation);
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartTargets.ResumeInstallation, response.StartupTarget);
        Assert.Equal(SetupStartRecommendedActions.ResumeInstall, response.RecommendedAction);
        Assert.Equal(installation.Id, response.ActiveInstallationId);
        Assert.Equal(SetupActiveInstallationStages.Activity, response.ActiveInstallationStage);
    }

    [Fact]
    public async Task STARTUP_01D_A_waiting_for_user_reopens_authoritative_activity()
    {
        await using var fixture = await Fixture.CreateAsync(
            DockerContainer("mem-postgres", "postgres:16", "running"),
            DockerContainer("mem-npm", "jc21/nginx-proxy-manager:2.14.0", "running"));

        var installation = new InstallationEntity
        {
            Id = Guid.NewGuid(),
            Status = "WaitingForUser",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            CompletedAtUtc = null,
            LastError = "A required operator action is still outstanding."
        };
        fixture.Db.Installations.Add(installation);
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartTargets.ResumeInstallation, response.StartupTarget);
        Assert.Equal(installation.Id, response.ActiveInstallationId);
        Assert.Equal(SetupActiveInstallationStages.Activity, response.ActiveInstallationStage);
    }

    [Fact]
    public async Task STARTUP_01D_C_completed_installation_with_pending_handoff_reopens_finish()
    {
        await using var fixture = await Fixture.CreateAsync(
            DockerContainer("mem-postgres", "postgres:16", "running"),
            DockerContainer("mem-npm", "jc21/nginx-proxy-manager:2.14.0", "running"),
            DockerContainer("mem-coturn", "sha256:approved-coturn", "running"));

        var installationId = Guid.NewGuid();
        fixture.Db.Installations.Add(new InstallationEntity
        {
            Id = installationId,
            Status = "Succeeded",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        fixture.Db.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
        {
            Id = Guid.NewGuid(),
            InstallationId = installationId,
            StepName = InstallStepNames.CompleteSetupHandoff,
            Sequence = 11,
            Status = "WaitingForUser",
            Message = "Finish setup.",
            AttemptCount = 1,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CompletedAtUtc = null
        });
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.SetupStart.GetAsync(CancellationToken.None);

        Assert.Equal(SetupStartTargets.ResumeInstallation, response.StartupTarget);
        Assert.Equal(
            SetupStartRecommendedActions.CompleteHandoff,
            response.RecommendedAction);
        Assert.Equal(installationId, response.ActiveInstallationId);
        Assert.Equal(
            SetupActiveInstallationStages.Handoff,
            response.ActiveInstallationStage);
        Assert.Contains(
            response.Warnings,
            warning => warning.Code == "installation-handoff-required");
    }

    [Fact]
    public async Task STARTUP_01D_C_handoff_acknowledgement_is_durable_and_restores_dashboard_startup()
    {
        await using var fixture = await Fixture.CreateAsync(
            DockerContainer("mem-postgres", "postgres:16", "running"),
            DockerContainer("mem-npm", "jc21/nginx-proxy-manager:2.14.0", "running"),
            DockerContainer("mem-coturn", "sha256:approved-coturn", "running"));

        var installationId = Guid.NewGuid();
        fixture.Db.Installations.Add(new InstallationEntity
        {
            Id = installationId,
            Status = "Succeeded",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        fixture.Db.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
        {
            Id = Guid.NewGuid(),
            InstallationId = installationId,
            StepName = InstallStepNames.RunVerificationChecks,
            Sequence = 10,
            Status = "Succeeded",
            Message = "Verification passed.",
            AttemptCount = 1,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-2),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        fixture.Db.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
        {
            Id = Guid.NewGuid(),
            InstallationId = installationId,
            StepName = InstallStepNames.CompleteSetupHandoff,
            Sequence = 11,
            Status = "WaitingForUser",
            Message = "Finish setup.",
            AttemptCount = 1,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CompletedAtUtc = null
        });
        await fixture.Db.SaveChangesAsync();

        var handoff = new SetupHandoffService(
            fixture.Db,
            NullLogger<SetupHandoffService>.Instance);

        var before = await handoff.GetAsync(
            installationId,
            CancellationToken.None);
        Assert.NotNull(before);
        Assert.True(before.HandoffRequired);
        Assert.False(before.HandoffCompleted);

        var completion = await handoff.CompleteAsync(
            installationId,
            CancellationToken.None);

        Assert.NotNull(completion);
        Assert.True(completion.Completed);
        Assert.Equal("completed", completion.Status);

        fixture.Db.ChangeTracker.Clear();

        var persistedStep = await fixture.Db.InstallationStepExecutions
            .AsNoTracking()
            .SingleAsync(x =>
                x.InstallationId == installationId &&
                x.StepName == InstallStepNames.CompleteSetupHandoff);
        Assert.Equal("Succeeded", persistedStep.Status);
        Assert.NotNull(persistedStep.CompletedAtUtc);

        var startup = await fixture.SetupStart.GetAsync(CancellationToken.None);
        Assert.Equal(SetupStartTargets.Dashboard, startup.StartupTarget);
        Assert.Equal(
            SetupStartRecommendedActions.OpenDashboard,
            startup.RecommendedAction);
    }

    [Fact]
    public async Task V011_APP_RETIRE_02_handoff_points_to_the_operator_dashboard_and_has_no_legacy_route_contract()
    {
        await using var fixture = await Fixture.CreateAsync();
        var installationId = Guid.NewGuid();

        fixture.Db.Installations.Add(new InstallationEntity
        {
            Id = installationId,
            Status = "Succeeded",
            CreatedAtUtc = DateTime.UtcNow.AddMinutes(-5),
            UpdatedAtUtc = DateTime.UtcNow,
            StartedAtUtc = DateTime.UtcNow.AddMinutes(-4),
            CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
        });
        await fixture.Db.SaveChangesAsync();

        var handoff = new SetupHandoffService(
            fixture.Db,
            NullLogger<SetupHandoffService>.Instance);

        var response = await handoff.GetAsync(
            installationId,
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("ready", response.Status);
        Assert.Equal("/dashboard", response.OperatorDashboardPath);
        Assert.Equal("mem-postgres", response.PostgresContainerName);
        Assert.Equal("mem-npm", response.NpmContainerName);
        Assert.Equal("mem-coturn", response.CoturnContainerName);
        Assert.False(response.HandoffRequired);
        Assert.True(response.HandoffCompleted);
        Assert.Contains("operator dashboard", response.Message, StringComparison.OrdinalIgnoreCase);

        var json = JsonSerializer.Serialize(response);
        Assert.DoesNotContain("memWeb", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("memApi", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("admin.", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.", json, StringComparison.OrdinalIgnoreCase);
    }

    private static SetupDockerContainer DockerContainer(
        string name,
        string image,
        string state) =>
        new(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            Image: image,
            State: state,
            Status: state == "running" ? "Up 1 minute" : "Exited (0)",
            Labels: new Dictionary<string, string>());

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private Fixture(
            string databasePath,
            MemDbContext db,
            SetupStartService setupStart)
        {
            _databasePath = databasePath;
            Db = db;
            SetupStart = setupStart;
        }

        public MemDbContext Db { get; }
        public SetupStartService SetupStart { get; }

        public static async Task<Fixture> CreateAsync(params SetupDockerContainer[] dockerRows)
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-v011-app-retire-02-{Guid.NewGuid():N}.db");

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var dockerProbe = new StaticDockerProbe(dockerRows);
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Product:Version"] = "0.2.0"
                })
                .Build();

            var setupStart = new SetupStartService(
                dockerProbe,
                db,
                configuration,
                NullLogger<SetupStartService>.Instance);

            return new Fixture(databasePath, db, setupStart);
        }

        public async Task AddCompletedPlatformOwnerAsync()
        {
            var userId = Guid.NewGuid();
            var roleId = Guid.NewGuid();

            Db.Users.Add(new MemOperator
            {
                Id = userId,
                UserName = "owner",
                NormalizedUserName = "OWNER",
                SecurityStamp = Guid.NewGuid().ToString("N"),
                IsEnabled = true,
                IsBootstrapProvisioning = false,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            Db.Roles.Add(new IdentityRole<Guid>
            {
                Id = roleId,
                Name = MemOperatorRoles.PlatformOwner,
                NormalizedName = MemOperatorRoles.PlatformOwner.ToUpperInvariant()
            });
            Db.UserRoles.Add(new IdentityUserRole<Guid>
            {
                UserId = userId,
                RoleId = roleId
            });

            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();

            foreach (var path in new[]
                     {
                         _databasePath,
                         _databasePath + "-shm",
                         _databasePath + "-wal"
                     })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private sealed class StaticDockerProbe(
        IReadOnlyList<SetupDockerContainer> containers)
        : ISetupDockerRuntimeProbe
    {
        public Task<SetupDockerSystemInfo> GetSystemInfoAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new SetupDockerSystemInfo(
                ServerVersion: "28.0.2",
                DockerRootDir: "/var/lib/docker",
                OperatingSystem: "Ubuntu",
                Architecture: "x86_64",
                MemoryBytes: 8L * 1024 * 1024 * 1024,
                CpuCount: 4,
                ContainerCount: containers.Count,
                ImageCount: 1));
        }

        public Task<IReadOnlyList<SetupDockerContainer>> ListContainersAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(containers);
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
}
