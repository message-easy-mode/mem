using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;

namespace Api.IntegrationTests.Setup;

public sealed class NpmInstallResumeIntegrationTests
{
    [Theory]
    [InlineData("NpmAdminCredentialRequired", "CredentialRequired")]
    [InlineData("NpmCredentialsRejected", "CredentialsRejected")]
    public async Task STARTUP_NPM_BOOTSTRAP_01C_credential_attention_pauses_step_8_as_WaitingForUser(
        string errorCode,
        string status)
    {
        var bootstrap = new StubBootstrapService(new NpmInitialAdminBootstrapResult(
            Succeeded: false,
            Status: status,
            Message: "Synthetic credential attention.",
            ErrorCode: errorCode,
            InitialLoginVerified: false,
            Recreated: false,
            FinalLoginVerified: false,
            BootstrapEnvironmentRemoved: true));

        var executor = new InstallStepExecutor(
            commandRunner: null!,
            dockerHost: null!,
            dockerRuntimeProbe: null!,
            npmAdminProbe: null!,
            db: null!,
            certificateStorage: null!,
            certificateValidation: null!,
            npmCertificateProbe: null!,
            npmReadiness: null!,
            npmProxyHostService: null!,
            npmOptions: Options.Create(new NpmApiOptions()),
            memCliHostCommandInstaller: null!,
            approvedPostgresRuntimeProvider: null!,
            portainerRuntimeService: null!,
            installationSecretStore: null!,
            platformCertificateProvisioner: null!,
            npmInitialAdminBootstrap: bootstrap,
            logger: NullLogger<InstallStepExecutor>.Instance);

        var plan = InstallPlanFactory.CreateDefault();
        var result = await executor.ExecuteAsync(
            new InstallStepContext(
                InstallationId: Guid.NewGuid(),
                StepId: Guid.NewGuid(),
                StepName: InstallStepNames.IssueAndImportPlatformCertificate,
                Sequence: 8,
                ConfigJson: JsonSerializer.Serialize(
                    plan,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(InstallationStepStatuses.WaitingForUser, result.StepStatus);
        Assert.StartsWith($"{errorCode}:", result.ErrorMessage);

        Assert.NotNull(bootstrap.Target);
        Assert.Equal("mem-npm", bootstrap.Target!.ContainerName);
        Assert.Equal(NpmRuntimeRelease.ApprovedImage, bootstrap.Target.Image);
        Assert.Equal("mem_npm_data", bootstrap.Target.DataVolumeName);
        Assert.Equal("mem_npm_letsencrypt", bootstrap.Target.LetsEncryptVolumeName);
        Assert.Equal("mem-gateway", bootstrap.Target.NetworkName);
    }

    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01C_retry_preserves_steps_1_through_7_and_requeues_failed_step_8()
    {
        var databasePath = TempDatabasePath();

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var installationId = Guid.NewGuid();
            var reviewed = ReviewedPlanJson();

            await using (var seed = new MemDbContext(options))
            {
                await seed.Database.MigrateAsync();

                seed.Installations.Add(new InstallationEntity
                {
                    Id = installationId,
                    Status = InstallationStatuses.Failed,
                    ConfigJson = reviewed,
                    FrozenConfigJson = reviewed,
                    LastError = "NpmCertificateImportFailed: historical fixture",
                    CreatedAtUtc = DateTime.UtcNow.AddMinutes(-10),
                    UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
                    StartedAtUtc = DateTime.UtcNow.AddMinutes(-8),
                    CompletedAtUtc = DateTime.UtcNow.AddMinutes(-1)
                });

                for (var index = 0; index < InstallStepNames.InitialSteps.Count; index++)
                {
                    var sequence = index + 1;
                    var succeeded = sequence <= 7;
                    var failed = sequence == 8;

                    seed.InstallationStepExecutions.Add(new InstallationStepExecutionEntity
                    {
                        Id = Guid.NewGuid(),
                        InstallationId = installationId,
                        StepName = InstallStepNames.InitialSteps[index],
                        Sequence = sequence,
                        Status = succeeded
                            ? InstallationStepStatuses.Succeeded
                            : failed
                                ? InstallationStepStatuses.Failed
                                : InstallationStepStatuses.Pending,
                        Message = succeeded ? "Historical success." : failed ? "Historical step-8 failure." : null,
                        ErrorMessage = failed ? "NpmCertificateImportFailed: historical fixture" : null,
                        AttemptCount = succeeded ? 1 : failed ? 2 : 0,
                        StartedAtUtc = succeeded || failed ? DateTime.UtcNow.AddMinutes(-5) : null,
                        CompletedAtUtc = succeeded || failed ? DateTime.UtcNow.AddMinutes(-2) : null
                    });
                }

                await seed.SaveChangesAsync();
            }

            var coordinator = new RecordingInstallRunCoordinator();

            await using (var db = new MemDbContext(options))
            {
                var service = new InstallPlanService(
                    db,
                    NullLogger<InstallPlanService>.Instance,
                    coordinator);

                var run = await service.RunAsync(
                    installationId,
                    CancellationToken.None);

                Assert.NotNull(run);
                Assert.True(run!.Accepted);
                Assert.Equal(InstallationStatuses.Running, run.Status);
            }

            await using (var verify = new MemDbContext(options))
            {
                var installation = await verify.Installations
                    .AsNoTracking()
                    .SingleAsync(x => x.Id == installationId);
                var steps = await verify.InstallationStepExecutions
                    .AsNoTracking()
                    .Where(x => x.InstallationId == installationId)
                    .OrderBy(x => x.Sequence)
                    .ToListAsync();

                Assert.Equal(InstallationStatuses.Running, installation.Status);
                Assert.Null(installation.LastError);
                Assert.Null(installation.CompletedAtUtc);

                Assert.All(steps.Take(7), step =>
                {
                    Assert.Equal(InstallationStepStatuses.Succeeded, step.Status);
                    Assert.Equal(1, step.AttemptCount);
                    Assert.Equal("Historical success.", step.Message);
                });

                Assert.Equal(InstallationStepStatuses.Pending, steps[7].Status);
                Assert.Equal(2, steps[7].AttemptCount);
                Assert.Null(steps[7].Message);
                Assert.Null(steps[7].ErrorMessage);
                Assert.Null(steps[7].StartedAtUtc);
                Assert.Null(steps[7].CompletedAtUtc);

                Assert.All(steps.Skip(8), step =>
                    Assert.Equal(InstallationStepStatuses.Pending, step.Status));
            }

            Assert.Equal(1, coordinator.AcceptedQueueCount);
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    [Fact]
    public void STARTUP_NPM_BOOTSTRAP_01C_future_step_7_and_historical_step_8_both_require_bootstrap()
    {
        var root = FindRepositoryRoot();
        var step7 = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Setup", "InstallRuns",
            "InstallStepExecutor.NpmIngress.cs"));
        var step8 = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Setup", "InstallRuns",
            "InstallStepExecutor.PlatformPublicAccess.cs"));

        Assert.Contains("EnsureNpmAdministratorReadyAsync", step7, StringComparison.Ordinal);
        Assert.Contains("EnsureNpmAdministratorReadyAsync", step8, StringComparison.Ordinal);
    }

    private static string ReviewedPlanJson()
    {
        var intent = InstallPlanFactory.CreateDefault() with
        {
            Preflight = new PreflightSetupConfig(
                RunId: "npm-resume-01c",
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
                Domain = "*.deltabox.dev",
                Zone = "deltabox.dev",
                AcmeEmail = "admin@deltabox.dev",
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

        return JsonSerializer.Serialize(
            reviewed,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"mem-npm-resume-{Guid.NewGuid():N}.db");

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

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(
                    current.FullName,
                    "installer",
                    "src",
                    "MemInstaller.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate repository root containing installer/src/MemInstaller.sln.");
    }

    private sealed class StubBootstrapService(
        NpmInitialAdminBootstrapResult result) : INpmInitialAdminBootstrapService
    {
        public NpmInitialAdminBootstrapTarget? Target { get; private set; }

        public Task<NpmInitialAdminBootstrapResult> BootstrapAsync(
            Guid installationId,
            NpmInitialAdminBootstrapTarget target,
            CancellationToken cancellationToken)
        {
            Target = target;
            return Task.FromResult(result);
        }
    }
}
