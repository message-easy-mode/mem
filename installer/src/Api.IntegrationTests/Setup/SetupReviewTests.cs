using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Modules.Setup.Review;
using Modules.Setup.Secrets;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Setup;

public sealed class SetupReviewTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task STARTUP_INSTALL_REL_01E_review_freezes_exact_server_plan_without_external_mutation_or_secrets()
    {
        await using var fixture = await ReviewFixture.CreateAsync(readyForReview: true);

        var before = await fixture.Service.GetCurrentAsync(CancellationToken.None);
        Assert.True(before.CanAccept);
        Assert.False(before.ReviewAccepted);
        Assert.Null(before.PlanSha256);
        Assert.True(before.Domain.ProviderCredentialStored);
        Assert.True(before.NpmAdministrator.CredentialStored);
        Assert.Equal(ReviewFixture.NpmAdminEmail, before.NpmAdministrator.AdministratorEmail);
        Assert.Equal("mem-coturn", before.Platform.CoturnContainerName);
        Assert.Equal("turn.deltabox.dev", before.Platform.CoturnPublicHost);
        Assert.Equal(3478, before.Platform.CoturnTurnPort);
        Assert.Equal("49160-49200/udp", before.Platform.CoturnRelayPortRange);
        Assert.Contains(before.PlannedActions, action =>
            action.Contains("attach the containerized Control Plane", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(before.PlannedActions, action =>
            action.Contains("shared platform TURN", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(before.WillNotChange, item =>
            item.Contains("does not create a separate Coturn container for each stack", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(before.WillNotChange, item =>
            item.Contains("into one Coturn container per stack", StringComparison.OrdinalIgnoreCase));

        var accepted = await fixture.Service.AcceptAsync(CancellationToken.None);

        Assert.True(accepted.ReviewAccepted);
        Assert.False(accepted.CanAccept);
        Assert.Equal(InstallationStatuses.Ready, accepted.InstallationStatus);
        Assert.NotNull(accepted.PlanSha256);
        Assert.NotNull(accepted.ReviewedAtUtc);
        Assert.Contains(
            accepted.PlannedActions,
            action => action.Contains("DNS-01", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            accepted.WillNotChange,
            item => item.Contains("DNS records before", StringComparison.OrdinalIgnoreCase));

        var installation = await fixture.Db.Installations
            .AsNoTracking()
            .SingleAsync(x => x.Id == fixture.InstallationId);

        Assert.Equal(InstallationStatuses.Ready, installation.Status);
        Assert.Equal(installation.ConfigJson, installation.FrozenConfigJson);
        Assert.DoesNotContain(ReviewFixture.DesecToken, installation.ConfigJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(ReviewFixture.PostgresPassword, installation.ConfigJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(ReviewFixture.NpmAdminPassword, installation.ConfigJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(ReviewFixture.DesecToken, installation.FrozenConfigJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(ReviewFixture.PostgresPassword, installation.FrozenConfigJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(ReviewFixture.NpmAdminPassword, installation.FrozenConfigJson!, StringComparison.Ordinal);

        var frozen = JsonSerializer.Deserialize<InstallPlan>(installation.FrozenConfigJson!, JsonOptions);
        Assert.NotNull(frozen);
        Assert.True(SetupReviewPlanFingerprint.Matches(frozen!));
        Assert.Equal(accepted.PlanSha256, frozen!.Review!.PlanSha256);

        Assert.Empty(await fixture.Db.Domains.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.Certificates.AsNoTracking().ToListAsync());

        var diagnostic = Assert.Single(fixture.Diagnostics.Requests);
        Assert.Equal("setup.review.accepted", diagnostic.EventCode);
        Assert.Equal("none", diagnostic.Details!["externalMutation"]);
        Assert.Equal(fixture.InstallationId.ToString("D"), diagnostic.Resource!.Id);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01E_review_blocks_when_preflight_domain_or_protected_credentials_are_missing()
    {
        await using var fixture = await ReviewFixture.CreateAsync(readyForReview: false);

        var review = await fixture.Service.GetCurrentAsync(CancellationToken.None);

        Assert.False(review.CanAccept);
        Assert.False(review.ReviewAccepted);
        Assert.NotEmpty(review.Blockers);
        Assert.Contains(review.Blockers, blocker => blocker.Contains("Server Checks", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(review.Blockers, blocker => blocker.Contains("Domain", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(review.Blockers, blocker => blocker.Contains("DNS credential", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(review.Blockers, blocker => blocker.Contains("Nginx Proxy Manager", StringComparison.OrdinalIgnoreCase));

        var accepted = await fixture.Service.AcceptAsync(CancellationToken.None);
        Assert.False(accepted.ReviewAccepted);
        Assert.Equal("SetupReviewBlocked", accepted.ErrorCode);

        var installation = await fixture.Db.Installations.AsNoTracking().SingleAsync();
        Assert.Equal(InstallationStatuses.Draft, installation.Status);
        Assert.Null(installation.FrozenConfigJson);
        Assert.Empty(fixture.Diagnostics.Requests);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01E_run_rejects_unreviewed_plan_and_starts_only_the_frozen_reviewed_plan()
    {
        await using var fixture = await ReviewFixture.CreateAsync(readyForReview: true);
        var coordinator = new RecordingInstallRunCoordinator();
        var planService = new InstallPlanService(
            fixture.Db,
            NullLogger<InstallPlanService>.Instance,
            coordinator);

        var before = await planService.RunAsync(fixture.InstallationId, CancellationToken.None);
        Assert.NotNull(before);
        Assert.False(before!.Accepted);
        Assert.Equal(InstallationStatuses.Draft, before.Status);
        Assert.Equal(0, coordinator.QueueCalls);
        Assert.Empty(await fixture.Db.InstallationStepExecutions.AsNoTracking().ToListAsync());

        var review = await fixture.Service.AcceptAsync(CancellationToken.None);
        Assert.True(review.ReviewAccepted);

        var frozenBeforeRun = await fixture.Db.Installations
            .AsNoTracking()
            .Where(x => x.Id == fixture.InstallationId)
            .Select(x => x.FrozenConfigJson)
            .SingleAsync();

        var started = await planService.RunAsync(fixture.InstallationId, CancellationToken.None);
        Assert.NotNull(started);
        Assert.True(started!.Accepted);
        Assert.Equal(InstallationStatuses.Running, started.Status);
        Assert.Equal(1, coordinator.AcceptedQueueCount);

        var running = await fixture.Db.Installations.AsNoTracking().SingleAsync(x => x.Id == fixture.InstallationId);
        Assert.Equal(frozenBeforeRun, running.FrozenConfigJson);

        var steps = await fixture.Db.InstallationStepExecutions
            .AsNoTracking()
            .Where(x => x.InstallationId == fixture.InstallationId)
            .OrderBy(x => x.Sequence)
            .ToListAsync();

        Assert.Equal(InstallStepNames.InitialSteps.Count, steps.Count);
        Assert.Contains(steps, step => step.StepName == InstallStepNames.IssueAndImportPlatformCertificate);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01E_tampered_frozen_plan_is_rejected_before_mutation()
    {
        await using var fixture = await ReviewFixture.CreateAsync(readyForReview: true);
        var accepted = await fixture.Service.AcceptAsync(CancellationToken.None);
        Assert.True(accepted.ReviewAccepted);

        var installation = await fixture.Db.Installations.SingleAsync(x => x.Id == fixture.InstallationId);
        var frozen = JsonSerializer.Deserialize<InstallPlan>(installation.FrozenConfigJson!, JsonOptions)!;
        installation.FrozenConfigJson = JsonSerializer.Serialize(
            frozen with
            {
                PublicAccess = frozen.PublicAccess with { Zone = "tampered.example" }
            },
            JsonOptions);
        await fixture.Db.SaveChangesAsync();

        var coordinator = new RecordingInstallRunCoordinator();
        var planService = new InstallPlanService(
            fixture.Db,
            NullLogger<InstallPlanService>.Instance,
            coordinator);

        var result = await planService.RunAsync(fixture.InstallationId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result!.Accepted);
        Assert.Equal(0, coordinator.QueueCalls);
        Assert.Empty(await fixture.Db.InstallationStepExecutions.AsNoTracking().ToListAsync());
    }

    private sealed class ReviewFixture : IAsyncDisposable
    {
        public const string DesecToken = "desec-01e-review-secret";
        public const string PostgresPassword = "postgres-01e-review-secret";
        public const string NpmAdminEmail = "npm-admin@deltabox.dev";
        public const string NpmAdminPassword = "npm-admin-01a-review-secret";

        private readonly string _root;
        private readonly ServiceProvider _dataProtectionServices;

        private ReviewFixture(
            string root,
            MemDbContext db,
            ServiceProvider dataProtectionServices,
            Guid installationId,
            ProtectedInstallationSecretStore secretStore,
            SetupReviewService service,
            RecordingDiagnosticWriter diagnostics)
        {
            _root = root;
            Db = db;
            _dataProtectionServices = dataProtectionServices;
            InstallationId = installationId;
            SecretStore = secretStore;
            Service = service;
            Diagnostics = diagnostics;
        }

        public MemDbContext Db { get; }
        public Guid InstallationId { get; }
        public ProtectedInstallationSecretStore SecretStore { get; }
        public SetupReviewService Service { get; }
        public RecordingDiagnosticWriter Diagnostics { get; }

        public static async Task<ReviewFixture> CreateAsync(bool readyForReview)
        {
            var root = Path.Combine(Path.GetTempPath(), $"mem-startup-install-rel-01e-review-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var databasePath = Path.Combine(root, "control-plane.db");
            var keyRingPath = Path.Combine(root, "keys");
            Directory.CreateDirectory(keyRingPath);

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var installationId = Guid.NewGuid();
            var plan = readyForReview ? ReadyIntent() : InstallPlanFactory.CreateDefault();
            db.Installations.Add(new InstallationEntity
            {
                Id = installationId,
                Status = InstallationStatuses.Draft,
                ConfigJson = JsonSerializer.Serialize(plan, JsonOptions),
                FrozenConfigJson = null,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var dataProtectionServices = BuildDataProtectionProvider(keyRingPath);
            var secretStore = new ProtectedInstallationSecretStore(
                db,
                dataProtectionServices.GetRequiredService<IDataProtectionProvider>());

            if (readyForReview)
            {
                await secretStore.SetProtectedAsync(
                    installationId,
                    InstallationSecretNames.DnsCategory,
                    InstallationSecretNames.DesecProviderToken,
                    DesecToken,
                    "deSEC token",
                    CancellationToken.None);
                await secretStore.SetProtectedAsync(
                    installationId,
                    InstallationSecretNames.PlatformCategory,
                    InstallationSecretNames.PostgresPassword,
                    PostgresPassword,
                    "Postgres password",
                    CancellationToken.None);
                await secretStore.SetProtectedAsync(
                    installationId,
                    InstallationSecretNames.PlatformCategory,
                    InstallationSecretNames.NpmAdminEmail,
                    NpmAdminEmail,
                    "NPM administrator email",
                    CancellationToken.None);
                await secretStore.SetProtectedAsync(
                    installationId,
                    InstallationSecretNames.PlatformCategory,
                    InstallationSecretNames.NpmAdminPassword,
                    NpmAdminPassword,
                    "NPM administrator password",
                    CancellationToken.None);
            }

            var diagnostics = new RecordingDiagnosticWriter();
            var npmAdminCredentialService = new NpmAdminCredentialService(db, secretStore);
            var service = new SetupReviewService(
                db,
                secretStore,
                npmAdminCredentialService,
                NullLogger<SetupReviewService>.Instance,
                diagnostics);

            return new ReviewFixture(root, db, dataProtectionServices, installationId, secretStore, service, diagnostics);
        }

        private static InstallPlan ReadyIntent()
        {
            var plan = InstallPlanFactory.CreateDefault();
            return plan with
            {
                Preflight = new PreflightSetupConfig(
                    RunId: "01e-preflight",
                    CompletedAtUtc: DateTimeOffset.UtcNow,
                    RunStatus: "Succeeded",
                    Passed: 9,
                    Warnings: 0,
                    Failed: 0,
                    Skipped: 2,
                    Unavailable: 2,
                    Unknown: 0,
                    BlockingIssueCount: 0,
                    WarningCheckKeys: [],
                    UnavailableCheckKeys: ["host.filesystem", "host.ports"]),
                PublicAccess = plan.PublicAccess with
                {
                    Domain = "*.deltabox.dev",
                    Zone = "deltabox.dev",
                    AcmeEmail = "admin@deltabox.dev",
                    DnsProvider = "desec",
                    UseStaging = true,
                    Preparation = new DomainPreparationSetupConfig(
                        Status: "Validated",
                        ValidatedAtUtc: DateTime.UtcNow,
                        ProviderAccessConfirmed: true,
                        ProviderCredentialStored: true)
                }
            };
        }

        private static ServiceProvider BuildDataProtectionProvider(string keyRingPath)
        {
            var services = new ServiceCollection();
            services.AddDataProtection()
                .SetApplicationName("mem-startup-install-rel-01e-review-tests")
                .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
            return services.BuildServiceProvider();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _dataProtectionServices.Dispose();
            try
            {
                if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
            }
            catch
            {
                // Test cleanup only.
            }
        }
    }

    private sealed class RecordingDiagnosticWriter : IMemDiagnosticEventWriter
    {
        public List<MemDiagnosticWriteRequest> Requests { get; } = [];

        public Task<MemDiagnosticWriteResult> WriteAsync(
            MemDiagnosticWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new MemDiagnosticWriteResult(
                Stored: true,
                EventId: $"evt_{Guid.NewGuid():N}",
                IncidentId: null,
                WarningCode: null));
        }
    }
}
