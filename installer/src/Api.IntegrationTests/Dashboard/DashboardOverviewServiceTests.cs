using System.Security.Claims;
using System.Text.Json;
using Modules.Operator.Dashboard;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Identity;

namespace Api.IntegrationTests.Dashboard;

/// <summary>
/// SQLite-backed regression coverage for the dashboard's state rules. These
/// tests deliberately construct the composition service with a deterministic
/// runtime probe so Home behaviour is proven without Docker or network access.
/// </summary>
public sealed class DashboardOverviewServiceTests
{
    [Fact]
    public async Task No_stack_with_ready_platform_offers_create_server_and_marks_recovery_not_applicable()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("not_applicable", response.Recovery.State);
        Assert.Equal(3, response.Platform.RequiredServiceCount);
        Assert.Equal(3, response.Platform.RunningRequiredServiceCount);
        Assert.Equal(
            new[] { "postgres", "npm_ingress", "coturn" },
            response.Platform.Services.Select(service => service.Key));
        Assert.Equal("create_first_chat_server", response.Hero.HeadlineCode);
        Assert.Equal("create_chat_server", response.Hero.Action?.Code);
        Assert.Equal("available", response.Onboarding.State);
        Assert.Single(response.Onboarding.NextSteps);
        Assert.Equal("create_chat_server", response.Onboarding.NextSteps[0].Code);
    }

    [Fact]
    public async Task Managed_stack_without_a_valid_available_catalog_entry_needs_a_recovery_point()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();
        await fixture.AddStackAsync("family-chat", "passed");
        await fixture.AddCatalogAsync(
            sourceStackSlug: "family-chat",
            payloadState: "available",
            integrityStatus: "warning");

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("no_recovery_point", response.Recovery.State);
        Assert.Equal(0, response.Recovery.StacksWithValidRecoveryPointCount);
        Assert.Equal("recovery_needed", response.Hero.HeadlineCode);
        Assert.Equal("open_backups", response.Hero.Action?.Code);
    }

    [Fact]
    public async Task Valid_available_catalog_entries_cover_every_managed_stack()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();
        await fixture.AddStackAsync("family-chat", "passed");
        await fixture.AddStackAsync("gaming-chat", "healthy");
        await fixture.AddCatalogAsync("family-chat", "available", "valid", payloadBytes: 12_000);
        await fixture.AddCatalogAsync("gaming-chat", "available", "valid", payloadBytes: 24_000);

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("covered", response.Recovery.State);
        Assert.Equal(2, response.Recovery.StacksWithValidRecoveryPointCount);
        Assert.Equal(2, response.Recovery.ValidCatalogEntryCount);
        Assert.Equal(36_000L, response.Recovery.TotalPayloadBytes.GetValueOrDefault());
        Assert.Equal("platform_ready", response.Hero.HeadlineCode);
    }

    [Fact]
    public async Task Restored_stack_with_a_new_mem_slug_remains_covered_by_preserved_matrix_identity()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();
        await fixture.AddStackAsync(
            "demo-stack-restored-restored",
            "passed",
            matrixServerName: "matrix-demo-stack.deltabox.dev",
            matrixPublicHost: "matrix-demo-stack.deltabox.dev");
        await fixture.AddCatalogAsync(
            "demo-stack-restored",
            "available",
            "valid",
            matrixServerName: "matrix-demo-stack.deltabox.dev",
            matrixHost: "matrix-demo-stack.deltabox.dev");

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("covered", response.Recovery.State);
        Assert.Equal(1, response.Recovery.StacksWithValidRecoveryPointCount);
    }

    [Fact]
    public async Task Known_matrix_identity_mismatch_does_not_fall_back_to_a_reused_matching_slug()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();
        await fixture.AddStackAsync(
            "family-chat",
            "passed",
            matrixServerName: "matrix-new.example.test",
            matrixPublicHost: "matrix-new.example.test");
        await fixture.AddCatalogAsync(
            "family-chat",
            "available",
            "valid",
            matrixServerName: "matrix-old.example.test",
            matrixHost: "matrix-old.example.test");

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("no_recovery_point", response.Recovery.State);
        Assert.Equal(0, response.Recovery.StacksWithValidRecoveryPointCount);
    }

    [Fact]
    public async Task Legacy_catalog_without_matrix_identity_can_still_use_direct_stack_slug_fallback()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();
        await fixture.AddStackAsync("legacy-chat", "passed");
        await fixture.AddCatalogAsync("legacy-chat", "available", "valid");

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("covered", response.Recovery.State);
        Assert.Equal(1, response.Recovery.StacksWithValidRecoveryPointCount);
    }

    [Fact]
    public async Task Main_platform_certificate_wins_over_a_stale_domain_active_certificate_pointer()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddConflictingPublicAccessSelectionAsync();

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("valid", response.PublicAccess.Certificate.State);
        Assert.Equal("ready", response.PublicAccess.State);
        Assert.DoesNotContain(
            response.Notices,
            notice => notice.Code == "public_access_staging_certificate");
    }

    [Fact]
    public async Task Active_restore_attention_remains_separate_from_recovery_coverage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();
        await fixture.AddStackAsync("family-chat", "passed");
        await fixture.AddCatalogAsync("family-chat", "available", "valid");
        await fixture.AddRestoreAttemptAsync(
            restoreSessionId: "restore-needs-review",
            status: "needs-attention",
            errorCount: 1);

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("attention", response.Hero.State);
        Assert.Equal("restore_needs_attention", response.Hero.HeadlineCode);
        Assert.Equal("open_restore_workspace", response.Hero.Action?.Code);
        Assert.Equal("restore-needs-review", response.Hero.Action?.RestoreSessionId);
        Assert.Equal("covered", response.Recovery.State);
        Assert.Equal(1, response.Recovery.AttentionRestoreCount);
    }


    [Fact]
    public async Task Completed_restore_with_historical_errors_does_not_require_current_dashboard_attention()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();
        await fixture.AddStackAsync("family-chat", "passed");
        await fixture.AddCatalogAsync("family-chat", "available", "valid");
        await fixture.AddRestoreAttemptAsync(
            restoreSessionId: "restore-completed-with-private-test-history",
            status: "completed",
            errorCount: 1);

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("covered", response.Recovery.State);
        Assert.Equal(0, response.Recovery.ActiveRestoreCount);
        Assert.Equal(0, response.Recovery.AttentionRestoreCount);
        Assert.Null(response.Recovery.PriorityRestoreSessionId);
        Assert.Equal("platform_ready", response.Hero.HeadlineCode);

        var restoreActivity = Assert.Single(
            response.Activity.Items.Where(item => item.Kind == "restore_attempt"));
        Assert.Equal("success", restoreActivity.Severity);
        Assert.Equal("restore_completed", restoreActivity.TitleCode);
    }

    [Fact]
    public async Task Historical_invalid_catalog_entry_does_not_override_valid_managed_stack_coverage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();
        await fixture.AddStackAsync("family-chat", "passed");
        await fixture.AddCatalogAsync("family-chat", "available", "valid");
        await fixture.AddCatalogAsync("retired-chat", "failed", "invalid");

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("covered", response.Recovery.State);
        Assert.Equal(1, response.Recovery.StacksWithValidRecoveryPointCount);
        Assert.Equal(1, response.Recovery.InvalidCatalogEntryCount);
    }

    [Fact]
    public async Task Destroyed_runtime_stack_rows_are_excluded_from_active_dashboard_inventory_and_recovery_coverage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();
        await fixture.AddStackAsync("live-chat", "passed");
        await fixture.AddStackAsync("old-chat--destroyed-20260709012705", "destroyed");
        await fixture.AddCatalogAsync(
            sourceStackSlug: "old-chat--destroyed-20260709012705",
            payloadState: "available",
            integrityStatus: "valid");

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal(1, response.Stacks.Total);
        Assert.Single(response.Stacks.Items);
        Assert.Equal("live-chat", response.Stacks.Items[0].Slug);
        Assert.Equal(1, response.Recovery.ManagedStackCount);
        Assert.Equal(0, response.Recovery.StacksWithValidRecoveryPointCount);
        Assert.Equal("no_recovery_point", response.Recovery.State);
        Assert.Equal("recovery_needed", response.Hero.HeadlineCode);
    }

    [Theory]
    [InlineData(20, "scheduled", DashboardStates.Renewing)]
    [InlineData(20, "queued", DashboardStates.Renewing)]
    [InlineData(20, "running", DashboardStates.Renewing)]
    [InlineData(20, "awaiting-activation", DashboardStates.Renewing)]
    [InlineData(20, "failed", DashboardStates.Expiring)]
    [InlineData(20, "unready", DashboardStates.Expiring)]
    [InlineData(31, "scheduled", DashboardStates.Valid)]
    [InlineData(-1, "running", DashboardStates.Expired)]
    public void DOMAINS_CERTIFICATE_RENEWAL_01D_dashboard_does_not_treat_a_healthy_renewal_window_as_failure(
        int daysRemaining,
        string renewalState,
        string expected)
    {
        var state = DashboardOverviewService.ResolveCertificateState(
            fixtureNow.AddDays(daysRemaining),
            renewalState,
            fixtureNow);

        Assert.Equal(expected, state);
    }

    [Fact]
    public async Task Staging_certificate_is_active_but_public_access_remains_attention_with_specific_notice_reason()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddStagingPublicAccessAsync();

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("staging", response.PublicAccess.Certificate.State);
        Assert.Equal("attention", response.PublicAccess.State);
        Assert.Contains(
            response.Notices,
            notice => notice.Code == "public_access_staging_certificate");
    }

    [Fact]
    public async Task Protected_service_verification_limit_is_not_reported_as_runtime_degradation()
    {
        await using var fixture = await Fixture.CreateAsync(new FakeRuntimeProbe(
            docker: new DashboardDockerObservation("responsive", fixtureNow),
            host: new DashboardHostObservation(
                "available",
                fixtureNow,
                null,
                "Ubuntu 24.04.2 LTS",
                "x86_64",
                8,
                16L * 1024 * 1024 * 1024,
                "29.4.2",
                7,
                12,
                null,
                "host_filesystem_unavailable"),
            services:
            [
                new DashboardPlatformServiceObservation("postgres", "required", "running", fixtureNow),
                new DashboardPlatformServiceObservation("npm_ingress", "required", "running", fixtureNow),
                new DashboardPlatformServiceObservation("coturn", "required", "verification_limited", fixtureNow)
            ],
            ingress: new DashboardIngressObservation("ready", fixtureNow)));

        await fixture.AddReadyPublicAccessAsync();
        await fixture.AddStackAsync("family-chat", "passed");
        await fixture.AddCatalogAsync("family-chat", "available", "valid");

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("verification_limited", response.Platform.State);
        Assert.Equal(3, response.Platform.RunningRequiredServiceCount);
        Assert.Contains(
            response.Platform.Services,
            service =>
                service.Key == "coturn" &&
                service.State == "verification_limited");
        Assert.Equal("platform_ready", response.Hero.HeadlineCode);
    }

    [Fact]
    public async Task Runtime_probe_unknown_state_is_safe_and_never_carries_raw_failure_text()
    {
        await using var fixture = await Fixture.CreateAsync(new FakeRuntimeProbe(
            docker: new DashboardDockerObservation("unavailable", fixtureNow),
            host: new DashboardHostObservation(
                "unavailable",
                fixtureNow,
                "host_observation_failed",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "host_filesystem_unavailable"),
            services:
            [
                new DashboardPlatformServiceObservation("postgres", "required", "unknown", fixtureNow),
                new DashboardPlatformServiceObservation("npm_ingress", "required", "unknown", fixtureNow),
                new DashboardPlatformServiceObservation("coturn", "required", "unknown", fixtureNow)
            ],
            ingress: new DashboardIngressObservation("unknown", fixtureNow)));

        var response = await fixture.GetOverviewAsync(PlatformOwner());
        var json = JsonSerializer.Serialize(response);

        Assert.Equal("unavailable", response.Platform.State);
        Assert.Equal("platform_unavailable", response.Hero.HeadlineCode);
        Assert.DoesNotContain("docker socket", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("exception", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stack trace", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Activity_is_bounded_newest_first_and_does_not_serialize_raw_persistence_values()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();
        var stackId = await fixture.AddStackAsync("family-chat", "passed");

        for (var index = 0; index < 12; index++)
        {
            await fixture.AddRuntimeOperationAsync(
                stackId,
                operation: "backup-stack",
                status: index == 0 ? "failed" : "completed",
                requestedAtUtc: fixtureNow.AddMinutes(-index),
                lastError: "docker socket /var/run/docker.sock failed with secret-token");
        }

        await fixture.AddCatalogAsync(
            sourceStackSlug: "family-chat",
            payloadState: "available",
            integrityStatus: "valid",
            payloadDirectoryPath: "/srv/mem/private/backup-payload",
            validationId: "validation-should-never-leave-storage");

        var response = await fixture.GetOverviewAsync(PlatformOwner());
        var json = JsonSerializer.Serialize(response);

        Assert.Equal("available", response.Activity.State);
        Assert.True(response.Activity.Items.Count <= 8);
        Assert.True(response.Activity.Items
            .Zip(response.Activity.Items.Skip(1), (newer, older) => newer.OccurredAtUtc >= older.OccurredAtUtc)
            .All(x => x));
        Assert.All(response.Activity.Items, item => Assert.DoesNotContain("secret", item.TitleCode, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("/srv/mem/private", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("validation-should-never-leave-storage", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret-token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker socket", json, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task Host_projection_preserves_safe_docker_host_facts_without_inventing_usage_metrics()
    {
        await using var fixture = await Fixture.CreateAsync();

        var response = await fixture.GetOverviewAsync(PlatformOwner());

        Assert.Equal("available", response.Host.State);
        Assert.Equal("Ubuntu 24.04.2 LTS", response.Host.OperatingSystem);
        Assert.Equal("x86_64", response.Host.Architecture);
        Assert.Equal(8L, response.Host.CpuCount);
        Assert.Equal(16L * 1024 * 1024 * 1024, response.Host.MemoryTotalBytes);
        Assert.Equal("29.4.2", response.Host.DockerServerVersion);
        Assert.Equal(7L, response.Host.ContainerCount);
        Assert.Equal(12L, response.Host.ImageCount);
        Assert.Null(response.Host.Disk);
        Assert.Equal(
            "containerized_host_filesystem_unavailable",
            response.Host.StorageUnavailableReasonCode);

        var json = JsonSerializer.Serialize(response.Host);
        Assert.DoesNotContain("DockerRootDir", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/var/lib/docker", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Auditor_receives_read_only_capabilities_and_no_mutation_onboarding_action()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddReadyPublicAccessAsync();

        var response = await fixture.GetOverviewAsync(Auditor());

        Assert.False(response.Capabilities.CanOperate);
        Assert.False(response.Capabilities.CanManagePlatform);
        Assert.Equal("blocked", response.Onboarding.State);
        Assert.Null(response.Onboarding.NextSteps.Single().Action);
        Assert.Null(response.Hero.Action);
    }

    private static readonly DateTimeOffset fixtureNow = new(2026, 7, 6, 1, 0, 0, TimeSpan.Zero);

    private static ClaimsPrincipal PlatformOwner() => PrincipalFor(MemOperatorRoles.PlatformOwner);

    private static ClaimsPrincipal Auditor() => PrincipalFor(MemOperatorRoles.Auditor);

    private static ClaimsPrincipal PrincipalFor(string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString("D")),
            new Claim(ClaimTypes.Role, role)
        ],
        authenticationType: "test"));

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly FakeTimeProvider _timeProvider;
        private readonly IDashboardRuntimeProbe _runtimeProbe;

        private Fixture(
            string databasePath,
            MemDbContext db,
            FakeTimeProvider timeProvider,
            IDashboardRuntimeProbe runtimeProbe)
        {
            _databasePath = databasePath;
            Db = db;
            _timeProvider = timeProvider;
            _runtimeProbe = runtimeProbe;
        }

        public MemDbContext Db { get; }

        public static async Task<Fixture> CreateAsync(IDashboardRuntimeProbe? runtimeProbe = null)
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"mem-dashboard-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var timeProvider = new FakeTimeProvider(fixtureNow);
            runtimeProbe ??= FakeRuntimeProbe.Ready(timeProvider.GetUtcNow());

            return new Fixture(databasePath, db, timeProvider, runtimeProbe);
        }

        public async Task<DashboardOverviewResponse> GetOverviewAsync(ClaimsPrincipal principal)
        {
            var activityReader = new DashboardActivityReader(Db);
            var service = new DashboardOverviewService(
                Db,
                _runtimeProbe,
                activityReader,
                _timeProvider);

            return await service.GetOverviewAsync(principal, CancellationToken.None);
        }

        public async Task AddReadyPublicAccessAsync()
        {
            var domainId = Guid.NewGuid();
            var certificateId = Guid.NewGuid();

            var domain = new DomainEntity
            {
                Id = domainId,
                BaseDomain = "example.test",
                DisplayName = "Example test",
                Purpose = "platform",
                IsMainPlatformDomain = true,
                DnsProvider = "test",
                Status = "active",
                ActiveCertificateId = null,
                CreatedAtUtc = fixtureNow.UtcDateTime,
                UpdatedAtUtc = fixtureNow.UtcDateTime
            };

            Db.Domains.Add(domain);
            await Db.SaveChangesAsync();

            Db.Certificates.Add(new CertificateEntity
            {
                Id = certificateId,
                DomainId = domainId,
                CertificateId = "cert-example-test",
                CommonName = "*.example.test",
                Provider = "test",
                IsWildcard = true,
                IsStaging = false,
                IsMainPlatformCertificate = true,
                IsActive = true,
                Status = "active",
                CreatedAtUtc = fixtureNow.UtcDateTime,
                ExpiresAtUtc = fixtureNow.AddDays(90).UtcDateTime,
                ImportedToNpm = true
            });
            await Db.SaveChangesAsync();

            domain.ActiveCertificateId = certificateId;
            domain.UpdatedAtUtc = fixtureNow.UtcDateTime;
            await Db.SaveChangesAsync();
        }

        public async Task AddStagingPublicAccessAsync()
        {
            var domainId = Guid.NewGuid();
            var certificateId = Guid.NewGuid();

            var domain = new DomainEntity
            {
                Id = domainId,
                BaseDomain = "example.test",
                DisplayName = "Example test",
                Purpose = "platform",
                IsMainPlatformDomain = true,
                DnsProvider = "test",
                Status = "active",
                ActiveCertificateId = null,
                CreatedAtUtc = fixtureNow.UtcDateTime,
                UpdatedAtUtc = fixtureNow.UtcDateTime
            };

            Db.Domains.Add(domain);
            await Db.SaveChangesAsync();

            Db.Certificates.Add(new CertificateEntity
            {
                Id = certificateId,
                DomainId = domainId,
                CertificateId = "cert-example-test-staging",
                CommonName = "*.example.test",
                Provider = "test",
                IsWildcard = true,
                IsStaging = true,
                IsMainPlatformCertificate = true,
                IsActive = true,
                Status = "active",
                CreatedAtUtc = fixtureNow.UtcDateTime,
                ExpiresAtUtc = fixtureNow.AddDays(90).UtcDateTime,
                ImportedToNpm = true
            });
            await Db.SaveChangesAsync();

            domain.ActiveCertificateId = certificateId;
            domain.UpdatedAtUtc = fixtureNow.UtcDateTime;
            await Db.SaveChangesAsync();
        }

        public async Task AddConflictingPublicAccessSelectionAsync()
        {
            var domainId = Guid.NewGuid();
            var stagingId = Guid.NewGuid();
            var productionId = Guid.NewGuid();

            var domain = new DomainEntity
            {
                Id = domainId,
                BaseDomain = "deltabox.dev",
                DisplayName = "deltabox.dev",
                Purpose = "platform-main",
                IsMainPlatformDomain = true,
                DnsProvider = "desec",
                DnsZone = "deltabox.dev",
                Status = "Active",
                CreatedAtUtc = fixtureNow.UtcDateTime,
                UpdatedAtUtc = fixtureNow.UtcDateTime
            };

            Db.Domains.Add(domain);
            await Db.SaveChangesAsync();

            Db.Certificates.AddRange(
                new CertificateEntity
                {
                    Id = stagingId,
                    DomainId = domainId,
                    CertificateId = "cert-staging",
                    CommonName = "*.deltabox.dev",
                    Provider = "desec",
                    IsWildcard = true,
                    IsStaging = true,
                    IsMainPlatformCertificate = false,
                    IsActive = true,
                    Status = "Succeeded",
                    CreatedAtUtc = fixtureNow.AddMinutes(-10).UtcDateTime,
                    ExpiresAtUtc = fixtureNow.AddDays(90).UtcDateTime,
                    ImportedToNpm = true
                },
                new CertificateEntity
                {
                    Id = productionId,
                    DomainId = domainId,
                    CertificateId = "cert-production",
                    CommonName = "*.deltabox.dev",
                    Provider = "desec",
                    IsWildcard = true,
                    IsStaging = false,
                    IsMainPlatformCertificate = true,
                    IsActive = true,
                    Status = "Succeeded",
                    CreatedAtUtc = fixtureNow.UtcDateTime,
                    ExpiresAtUtc = fixtureNow.AddDays(90).UtcDateTime,
                    ImportedToNpm = true
                });
            await Db.SaveChangesAsync();

            // Deliberately preserve a stale legacy pointer to prove Dashboard follows
            // the authoritative main-platform certificate instead.
            domain.ActiveCertificateId = stagingId;
            await Db.SaveChangesAsync();
        }

        public async Task<Guid> AddStackAsync(
            string slug,
            string lastVerifiedStatus,
            string? matrixServerName = null,
            string? matrixPublicHost = null)
        {
            var id = Guid.NewGuid();
            var matrixInstanceId = Guid.NewGuid();
            var matrixHost = matrixPublicHost ?? $"matrix-{slug}.example.test";

            Db.RuntimeStacks.Add(new RuntimeStackEntity
            {
                Id = id,
                Slug = slug,
                DisplayName = slug,
                Status = lastVerifiedStatus,
                LastVerifiedStatus = lastVerifiedStatus,
                LastVerifiedAtUtc = fixtureNow.UtcDateTime,
                CreatedAtUtc = fixtureNow.UtcDateTime,
                UpdatedAtUtc = fixtureNow.UtcDateTime,
                MatrixInstanceId = matrixInstanceId,
                MatrixPublicBaseUrl = $"https://{matrixHost}",
                ElementPublicBaseUrl = $"https://chat-{slug}.example.test"
            });
            await Db.SaveChangesAsync();

            if (matrixServerName is not null || matrixPublicHost is not null)
            {
                Db.RuntimeServiceInstances.Add(new RuntimeServiceInstanceEntity
                {
                    Id = Guid.NewGuid(),
                    RuntimeStackId = id,
                    InstanceId = matrixInstanceId,
                    ServiceKey = "matrix",
                    Status = "running",
                    ServerName = matrixServerName,
                    PublicHost = matrixPublicHost,
                    PublicBaseUrl = $"https://{matrixHost}",
                    CreatedAtUtc = fixtureNow.UtcDateTime,
                    UpdatedAtUtc = fixtureNow.UtcDateTime
                });
                await Db.SaveChangesAsync();
            }

            return id;
        }

        public async Task AddCatalogAsync(
            string sourceStackSlug,
            string payloadState,
            string integrityStatus,
            long? payloadBytes = 4096,
            string? payloadDirectoryPath = null,
            string? validationId = null,
            string? matrixServerName = null,
            string? matrixHost = null)
        {
            Db.BackupCatalogEntries.Add(new BackupCatalogEntryEntity
            {
                Id = Guid.NewGuid(),
                CatalogEntryId = $"bkp_{Guid.NewGuid():N}",
                OriginKind = "local-captured",
                DisplayName = "Safe dashboard test backup",
                PayloadState = payloadState,
                PayloadStorageKind = "local-backup-directory",
                PayloadDirectoryPath = payloadDirectoryPath ?? "/test/catalog/payload",
                SourceStackSlug = sourceStackSlug,
                SourceBackupId = "safe-backup-id",
                ValidationId = validationId,
                MatrixServerName = matrixServerName,
                MatrixHost = matrixHost,
                IntegrityStatus = integrityStatus,
                IntegritySummary = "Private test-only persistence detail.",
                WarningCount = integrityStatus == "warning" ? 1 : 0,
                PayloadBytes = payloadBytes,
                CreatedAtUtc = fixtureNow.UtcDateTime,
                CapturedAtUtc = fixtureNow.UtcDateTime
            });
            await Db.SaveChangesAsync();
        }

        public async Task AddRestoreAttemptAsync(
            string restoreSessionId,
            string status,
            int errorCount)
        {
            Db.RestoreAttempts.Add(new RestoreAttemptEntity
            {
                Id = Guid.NewGuid(),
                RestoreSessionId = restoreSessionId,
                SourceKind = "backup-catalog",
                SourceKey = $"backup-catalog:{Guid.NewGuid():N}",
                ActiveSourceKey = status == "completed" ? null : $"backup-catalog:{Guid.NewGuid():N}",
                SourceCatalogEntryIdSnapshot = "bkp_dashboard",
                SourceDisplayNameSnapshot = "Dashboard backup",
                SourceOriginKindSnapshot = "local-captured",
                Status = status,
                CurrentStage = status,
                CreatedAtUtc = fixtureNow.UtcDateTime,
                UpdatedAtUtc = fixtureNow.UtcDateTime,
                LastEventAtUtc = fixtureNow.UtcDateTime,
                ErrorCount = errorCount,
                WarningCount = 0,
                SessionDirectoryPath = "/test/restore/should-not-be-projected",
                LastErrorSummary = "private restore error detail"
            });
            await Db.SaveChangesAsync();
        }

        public async Task AddRuntimeOperationAsync(
            Guid runtimeStackId,
            string operation,
            string status,
            DateTimeOffset requestedAtUtc,
            string lastError)
        {
            Db.RuntimeOperations.Add(new RuntimeOperationEntity
            {
                Id = Guid.NewGuid(),
                RuntimeStackId = runtimeStackId,
                Operation = operation,
                Status = status,
                RequestedAtUtc = requestedAtUtc.UtcDateTime,
                StartedAtUtc = requestedAtUtc.UtcDateTime,
                CompletedAtUtc = requestedAtUtc.UtcDateTime,
                CurrentStep = "private-current-step",
                AttemptCount = 1,
                LastError = lastError,
                InputJson = "{\"secret\":\"hidden\"}",
                ResultJson = "{\"private\":\"hidden\"}",
                EvidenceJson = "{\"path\":\"/private\"}"
            });
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();

            foreach (var path in new[] { _databasePath, _databasePath + "-shm", _databasePath + "-wal" })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    private sealed class FakeRuntimeProbe : IDashboardRuntimeProbe
    {
        private readonly DashboardDockerObservation _docker;
        private readonly DashboardHostObservation _host;
        private readonly IReadOnlyList<DashboardPlatformServiceObservation> _services;
        private readonly DashboardIngressObservation _ingress;

        public FakeRuntimeProbe(
            DashboardDockerObservation docker,
            DashboardHostObservation host,
            IReadOnlyList<DashboardPlatformServiceObservation> services,
            DashboardIngressObservation ingress)
        {
            _docker = docker;
            _host = host;
            _services = services;
            _ingress = ingress;
        }

        public static FakeRuntimeProbe Ready(DateTimeOffset observedAtUtc) => new(
            new DashboardDockerObservation("responsive", observedAtUtc),
            new DashboardHostObservation(
                "available",
                observedAtUtc,
                null,
                "Ubuntu 24.04.2 LTS",
                "x86_64",
                8,
                16L * 1024 * 1024 * 1024,
                "29.4.2",
                7,
                12,
                null,
                "containerized_host_filesystem_unavailable"),
            [
                new DashboardPlatformServiceObservation("postgres", "required", "running", observedAtUtc),
                new DashboardPlatformServiceObservation("npm_ingress", "required", "running", observedAtUtc),
                new DashboardPlatformServiceObservation("coturn", "required", "running", observedAtUtc)
            ],
            new DashboardIngressObservation("ready", observedAtUtc));

        public Task<DashboardDockerObservation> ObserveDockerAsync(CancellationToken ct) =>
            Task.FromResult(_docker);

        public Task<DashboardHostObservation> ObserveHostAsync(CancellationToken ct) =>
            Task.FromResult(_host);

        public Task<IReadOnlyList<DashboardPlatformServiceObservation>> ObservePlatformServicesAsync(CancellationToken ct) =>
            Task.FromResult(_services);

        public Task<DashboardIngressObservation> ObserveIngressAsync(CancellationToken ct) =>
            Task.FromResult(_ingress);
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
