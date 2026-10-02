using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.Domains.Certificates;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Modules.Setup.Secrets;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Certificates.Npm;
using Modules.Shared.Domains.Dns;
using Modules.Shared.Domains.Renewal;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Setup;

public sealed class InstallPlatformCertificateProvisionerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task STARTUP_INSTALL_REL_01E_reviewed_pipeline_resolves_protected_token_and_persists_runtime_outcome()
    {
        await using var fixture = await CertificateFixture.CreateAsync();
        var fake = new RecordingPlatformCertificateService(fixture.Db)
        {
            IssueResultFactory = async request =>
            {
                await AddCertificateAsync(
                    fixture.Db,
                    "cert-01e-success",
                    request.Zone,
                    importedToNpm: true,
                    npmCertificateId: 71);

                return SucceededIssue("cert-01e-success");
            }
        };
        var diagnostics = new RecordingDiagnosticWriter();
        var provisioner = new InstallPlatformCertificateProvisioner(
            fixture.Db,
            fake,
            fixture.SecretStore,
            fixture.RenewalService,
            NullLogger<InstallPlatformCertificateProvisioner>.Instance,
            diagnostics);

        var frozenBefore = await fixture.Db.Installations
            .AsNoTracking()
            .Where(x => x.Id == fixture.InstallationId)
            .Select(x => x.FrozenConfigJson)
            .SingleAsync();

        var result = await provisioner.EnsureAsync(
            Context(fixture.InstallationId, frozenBefore),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var request = Assert.Single(fake.IssueRequests);
        Assert.Equal(CertificateFixture.DesecToken, request.ProviderToken);
        Assert.Equal("deltabox.dev", request.Zone);
        Assert.Equal("*.deltabox.dev", request.Domain);
        Assert.True(request.UseStaging);
        Assert.Equal(0, fake.ImportCalls);

        var installation = await fixture.Db.Installations
            .AsNoTracking()
            .SingleAsync(x => x.Id == fixture.InstallationId);
        Assert.Equal(frozenBefore, installation.FrozenConfigJson);
        Assert.DoesNotContain(CertificateFixture.DesecToken, installation.ConfigJson!, StringComparison.Ordinal);
        Assert.DoesNotContain(CertificateFixture.DesecToken, installation.FrozenConfigJson!, StringComparison.Ordinal);

        var runtime = JsonSerializer.Deserialize<InstallPlan>(installation.ConfigJson!, JsonOptions)!;
        Assert.Equal("cert-01e-success", runtime.PublicAccess.CertificateId);
        Assert.Equal(71, runtime.PublicAccess.NpmCertificateId);
        Assert.True(runtime.PublicAccess.CertificateValidated);
        Assert.True(runtime.PublicAccess.ImportedToNpm);

        Assert.False(await fixture.SecretStore.ExistsAsync(
            fixture.InstallationId,
            InstallationSecretNames.DnsCategory,
            InstallationSecretNames.DesecProviderToken,
            CancellationToken.None));

        var domain = await fixture.Db.Domains.AsNoTracking().SingleAsync();
        Assert.False(await fixture.DomainSecretStore.ExistsAsync(
            domain.Id,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            CancellationToken.None));
        Assert.False(await fixture.Db.DomainCertificateRenewalPolicies
            .AsNoTracking()
            .AnyAsync(x => x.DomainId == domain.Id));

        Assert.Contains(diagnostics.Requests, x => x.EventCode == "installation.certificate.pipeline.started");
        Assert.Contains(diagnostics.Requests, x => x.EventCode == "installation.certificate.pipeline.succeeded");
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01E_provider_permission_failure_waits_for_domain_repair_and_keeps_credential()
    {
        await using var fixture = await CertificateFixture.CreateAsync();
        var fake = new RecordingPlatformCertificateService(fixture.Db)
        {
            IssueResultFactory = request => Task.FromResult(new CertificateOperationResult(
                Succeeded: false,
                Status: "Failed",
                Message: "deSEC denied access to this DNS zone.",
                ErrorCode: "DesecAccessDenied",
                ErrorDetail: "Check token permissions.",
                Evidence: []))
        };
        var diagnostics = new RecordingDiagnosticWriter();
        var provisioner = new InstallPlatformCertificateProvisioner(
            fixture.Db,
            fake,
            fixture.SecretStore,
            fixture.RenewalService,
            NullLogger<InstallPlatformCertificateProvisioner>.Instance,
            diagnostics);
        var frozen = await fixture.FrozenAsync();

        var result = await provisioner.EnsureAsync(
            Context(fixture.InstallationId, frozen),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(InstallationStepStatuses.WaitingForUser, result.StepStatus);
        Assert.Contains("DesecAccessDenied", result.ErrorMessage, StringComparison.Ordinal);
        Assert.True(await fixture.SecretStore.ExistsAsync(
            fixture.InstallationId,
            InstallationSecretNames.DnsCategory,
            InstallationSecretNames.DesecProviderToken,
            CancellationToken.None));

        var waiting = Assert.Single(diagnostics.Requests.Where(
            x => x.EventCode == "installation.certificate.pipeline.action_required"));
        Assert.Equal("DesecAccessDenied", waiting.Details!["failureClass"]);
        Assert.Equal("post-review-installation", waiting.Details!["mutationBoundary"]);
        Assert.Contains(CertificateFixture.DesecToken, waiting.ExactSecrets!);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01E_CORR_05_NPM_import_failure_explains_reusable_certificate_and_failed_phase()
    {
        await using var fixture = await CertificateFixture.CreateAsync();
        await AddCertificateAsync(
            fixture.Db,
            "cert-01e-npm-failed",
            "deltabox.dev",
            importedToNpm: false,
            npmCertificateId: null);
        await fixture.SetRuntimeCertificateAsync("cert-01e-npm-failed", null);

        var fake = new RecordingPlatformCertificateService(fixture.Db)
        {
            ImportResultFactory = certificateId => Task.FromResult(new NpmCertificateProbeResult(
                Succeeded: false,
                Status: "Failed",
                Message: "NPM import failed.",
                ErrorCode: "NpmCertificateImportFailed",
                ErrorDetail: "NPM API authority was unavailable.",
                MatchingCertificateId: null,
                Evidence: []))
        };
        var diagnostics = new RecordingDiagnosticWriter();
        var provisioner = new InstallPlatformCertificateProvisioner(
            fixture.Db,
            fake,
            fixture.SecretStore,
            fixture.RenewalService,
            NullLogger<InstallPlatformCertificateProvisioner>.Instance,
            diagnostics);

        var result = await provisioner.EnsureAsync(
            Context(fixture.InstallationId, await fixture.FrozenAsync()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Wildcard certificate is ready, but NPM import failed.", result.Message);
        Assert.Contains("will be reused", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new certificate request is not required", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.IssueRequests);
        Assert.Equal(1, fake.ImportCalls);

        var failure = Assert.Single(diagnostics.Requests.Where(
            x => x.EventCode == "installation.certificate.pipeline.failed"));
        Assert.Equal("npm-import", failure.Details!["failedPhase"]);
        Assert.Equal("true", failure.Details!["certificateReusable"]);
        Assert.Equal("cert-01e-npm-failed", failure.Details!["certificateId"]);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01E_retry_reuses_durable_certificate_and_only_retries_npm_import()
    {
        await using var fixture = await CertificateFixture.CreateAsync();
        await AddCertificateAsync(
            fixture.Db,
            "cert-01e-partial",
            "deltabox.dev",
            importedToNpm: false,
            npmCertificateId: null);
        await fixture.SetRuntimeCertificateAsync("cert-01e-partial", null);

        var fake = new RecordingPlatformCertificateService(fixture.Db)
        {
            ImportResultFactory = async certificateId =>
            {
                var certificate = await fixture.Db.Certificates.SingleAsync(x => x.CertificateId == certificateId);
                certificate.ImportedToNpm = true;
                certificate.NpmCertificateId = 88;
                certificate.LastImportedToNpmAtUtc = DateTime.UtcNow;
                await fixture.Db.SaveChangesAsync();

                return new NpmCertificateProbeResult(
                    Succeeded: true,
                    Status: "Succeeded",
                    Message: "Imported.",
                    ErrorCode: null,
                    ErrorDetail: null,
                    MatchingCertificateId: 88,
                    Evidence: []);
            }
        };
        var diagnostics = new RecordingDiagnosticWriter();
        var provisioner = new InstallPlatformCertificateProvisioner(
            fixture.Db,
            fake,
            fixture.SecretStore,
            fixture.RenewalService,
            NullLogger<InstallPlatformCertificateProvisioner>.Instance,
            diagnostics);

        var result = await provisioner.EnsureAsync(
            Context(fixture.InstallationId, await fixture.FrozenAsync()),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Empty(fake.IssueRequests);
        Assert.Equal(1, fake.ImportCalls);

        var installation = await fixture.Db.Installations.AsNoTracking().SingleAsync(x => x.Id == fixture.InstallationId);
        var runtime = JsonSerializer.Deserialize<InstallPlan>(installation.ConfigJson!, JsonOptions)!;
        Assert.Equal("cert-01e-partial", runtime.PublicAccess.CertificateId);
        Assert.Equal(88, runtime.PublicAccess.NpmCertificateId);
        Assert.True(runtime.PublicAccess.ImportedToNpm);

        Assert.False(await fixture.SecretStore.ExistsAsync(
            fixture.InstallationId,
            InstallationSecretNames.DnsCategory,
            InstallationSecretNames.DesecProviderToken,
            CancellationToken.None));
    }


    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01A_production_setup_transfers_verified_credential_before_deleting_temporary_copy()
    {
        await using var fixture = await CertificateFixture.CreateAsync(useStaging: false);
        var fake = new RecordingPlatformCertificateService(fixture.Db)
        {
            IssueResultFactory = async request =>
            {
                await AddCertificateAsync(
                    fixture.Db,
                    "cert-renewal-production",
                    request.Zone,
                    importedToNpm: true,
                    npmCertificateId: 91,
                    isStaging: false);

                return SucceededIssue("cert-renewal-production");
            }
        };
        var provisioner = new InstallPlatformCertificateProvisioner(
            fixture.Db,
            fake,
            fixture.SecretStore,
            fixture.RenewalService,
            NullLogger<InstallPlatformCertificateProvisioner>.Instance,
            new RecordingDiagnosticWriter());

        var result = await provisioner.EnsureAsync(
            Context(fixture.InstallationId, await fixture.FrozenAsync()),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.False(await fixture.SecretStore.ExistsAsync(
            fixture.InstallationId,
            InstallationSecretNames.DnsCategory,
            InstallationSecretNames.DesecProviderToken,
            CancellationToken.None));

        var domain = await fixture.Db.Domains.AsNoTracking().SingleAsync();
        Assert.True(await fixture.DomainSecretStore.ExistsAsync(
            domain.Id,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            CancellationToken.None));
        Assert.Equal(
            CertificateFixture.DesecToken,
            await fixture.DomainSecretStore.ResolveProtectedAsync(
                domain.Id,
                DomainRenewalSecretNames.DnsProviderCategory,
                DomainRenewalSecretNames.DesecProviderToken,
                CancellationToken.None));

        var stored = await fixture.Db.DomainSecrets
            .AsNoTracking()
            .SingleAsync(x => x.DomainId == domain.Id);
        Assert.DoesNotContain(CertificateFixture.DesecToken, stored.ProtectedValue, StringComparison.Ordinal);

        var policy = await fixture.Db.DomainCertificateRenewalPolicies
            .AsNoTracking()
            .SingleAsync(x => x.DomainId == domain.Id);
        Assert.True(policy.AutoRenewEnabled);
        Assert.Equal("admin@deltabox.dev", policy.AcmeEmail);
        Assert.Equal(DomainCertificateRenewalService.DefaultRenewalWindowDays, policy.RenewalWindowDays);
        Assert.Equal(DomainCertificateRenewalService.DefaultRetryIntervalHours, policy.RetryIntervalHours);

        var state = await fixture.RenewalService.GetAsync(domain.Id, CancellationToken.None);
        Assert.NotNull(state);
        Assert.True(state!.CredentialConfigured);
        Assert.True(state.AutoRenewEnabled);
        Assert.Equal(DomainRenewalReadinessStatuses.Ready, state.ReadinessStatus);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01A_failed_domain_credential_persistence_keeps_temporary_setup_secret_and_waits()
    {
        await using var fixture = await CertificateFixture.CreateAsync(useStaging: false);
        var fake = new RecordingPlatformCertificateService(fixture.Db)
        {
            IssueResultFactory = async request =>
            {
                await AddCertificateAsync(
                    fixture.Db,
                    "cert-renewal-persist-failure",
                    request.Zone,
                    importedToNpm: true,
                    npmCertificateId: 92,
                    isStaging: false);

                return SucceededIssue("cert-renewal-persist-failure");
            }
        };
        var failingRenewalService = fixture.CreateRenewalService(new ThrowingDomainSecretStore());
        var provisioner = new InstallPlatformCertificateProvisioner(
            fixture.Db,
            fake,
            fixture.SecretStore,
            failingRenewalService,
            NullLogger<InstallPlatformCertificateProvisioner>.Instance,
            new RecordingDiagnosticWriter());

        var result = await provisioner.EnsureAsync(
            Context(fixture.InstallationId, await fixture.FrozenAsync()),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(InstallationStepStatuses.WaitingForUser, result.StepStatus);
        Assert.Contains("RenewalCredentialPersistenceFailed", result.ErrorMessage ?? string.Empty, StringComparison.Ordinal);
        Assert.True(await fixture.SecretStore.ExistsAsync(
            fixture.InstallationId,
            InstallationSecretNames.DnsCategory,
            InstallationSecretNames.DesecProviderToken,
            CancellationToken.None));
        Assert.False(await fixture.Db.DomainCertificateRenewalPolicies.AsNoTracking().AnyAsync());
    }

    private static InstallStepContext Context(Guid installationId, string? frozenJson) =>
        new(
            installationId,
            Guid.NewGuid(),
            InstallStepNames.IssueAndImportPlatformCertificate,
            Sequence: 8,
            ConfigJson: frozenJson);

    private static CertificateOperationResult SucceededIssue(string certificateId) =>
        new(
            Succeeded: true,
            Status: "Succeeded",
            Message: "Issued.",
            ErrorCode: null,
            ErrorDetail: null,
            Evidence:
            [
                new CertificateOperationEvidence(
                    Key: "certificateId",
                    Value: certificateId,
                    Status: "Succeeded")
            ]);

    private static async Task AddCertificateAsync(
        MemDbContext db,
        string certificateId,
        string zone,
        bool importedToNpm,
        int? npmCertificateId,
        bool isStaging = true)
    {
        var domain = await db.Domains.FirstOrDefaultAsync(x => x.BaseDomain == zone);
        if (domain is null)
        {
            domain = new DomainEntity
            {
                Id = Guid.NewGuid(),
                BaseDomain = zone,
                DisplayName = zone,
                Purpose = "Platform",
                IsMainPlatformDomain = true,
                DnsProvider = "desec",
                DnsZone = zone,
                Status = "Active",
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            db.Domains.Add(domain);
            await db.SaveChangesAsync();
        }

        var certificate = new CertificateEntity
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            CertificateId = certificateId,
            CommonName = $"*.{zone}",
            Provider = "letsencrypt",
            IsWildcard = true,
            IsStaging = isStaging,
            IsMainPlatformCertificate = true,
            IsActive = true,
            Status = "Active",
            CreatedAtUtc = DateTime.UtcNow,
            NpmCertificateId = npmCertificateId,
            ImportedToNpm = importedToNpm,
            LastValidatedAtUtc = DateTime.UtcNow,
            LastImportedToNpmAtUtc = importedToNpm ? DateTime.UtcNow : null
        };
        db.Certificates.Add(certificate);
        await db.SaveChangesAsync();

        domain.ActiveCertificateId = certificate.Id;
        domain.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private sealed class RecordingPlatformCertificateService(MemDbContext db) : ISetupPlatformCertificateService
    {
        public List<CertificateIssueRequest> IssueRequests { get; } = [];
        public int ImportCalls { get; private set; }
        public Func<CertificateIssueRequest, Task<CertificateOperationResult>>? IssueResultFactory { get; init; }
        public Func<string, Task<NpmCertificateProbeResult>>? ImportResultFactory { get; init; }

        public async Task<CertificateOperationResult> IssuePlatformCertificateAsync(
            CertificateIssueRequest request,
            CancellationToken cancellationToken,
            CertificateIssueProgressCallback? progressCallback = null)
        {
            IssueRequests.Add(request);
            if (progressCallback is not null)
            {
                await progressCallback(
                    new CertificateIssueProgress(
                        "certificate.dns-stability",
                        "Allowing deSEC authoritative DNS visibility to settle before ACME validation."),
                    cancellationToken);
            }

            if (IssueResultFactory is not null)
            {
                return await IssueResultFactory(request);
            }

            return new CertificateOperationResult(false, "Failed", "Not configured.", "FakeNotConfigured", null, []);
        }

        public async Task<NpmCertificateProbeResult> EnsureNpmImportAsync(
            string certificateId,
            CancellationToken cancellationToken)
        {
            ImportCalls++;
            if (ImportResultFactory is not null)
            {
                return await ImportResultFactory(certificateId);
            }

            var certificate = await db.Certificates.AsNoTracking().SingleAsync(x => x.CertificateId == certificateId, cancellationToken);
            return new NpmCertificateProbeResult(
                certificate.ImportedToNpm,
                certificate.ImportedToNpm ? "Succeeded" : "Failed",
                certificate.ImportedToNpm ? "Already imported." : "Not imported.",
                certificate.ImportedToNpm ? null : "NpmImportNotConfigured",
                null,
                certificate.NpmCertificateId,
                []);
        }
    }

    private sealed class CertificateFixture : IAsyncDisposable
    {
        public const string DesecToken = "desec-01e-certificate-secret";
        private readonly string _root;
        private readonly ServiceProvider _dataProtectionServices;

        private CertificateFixture(
            string root,
            MemDbContext db,
            ServiceProvider dataProtectionServices,
            Guid installationId,
            ProtectedInstallationSecretStore secretStore,
            ProtectedDomainSecretStore domainSecretStore,
            DomainCertificateRenewalService renewalService)
        {
            _root = root;
            Db = db;
            _dataProtectionServices = dataProtectionServices;
            InstallationId = installationId;
            SecretStore = secretStore;
            DomainSecretStore = domainSecretStore;
            RenewalService = renewalService;
        }

        public MemDbContext Db { get; }
        public Guid InstallationId { get; }
        public ProtectedInstallationSecretStore SecretStore { get; }
        public ProtectedDomainSecretStore DomainSecretStore { get; }
        public DomainCertificateRenewalService RenewalService { get; }

        public DomainCertificateRenewalService CreateRenewalService(IDomainSecretStore secretStore) =>
            new(
                Db,
                secretStore,
                new AlwaysSuccessfulDnsZoneAccessProbe(),
                NullLogger<DomainCertificateRenewalService>.Instance);

        public static async Task<CertificateFixture> CreateAsync(bool useStaging = true)
        {
            var root = Path.Combine(Path.GetTempPath(), $"mem-startup-install-rel-01e-cert-{Guid.NewGuid():N}");
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
            var intent = InstallPlanFactory.CreateDefault() with
            {
                Preflight = new PreflightSetupConfig(
                    "01e-cert-preflight",
                    DateTimeOffset.UtcNow,
                    "Succeeded",
                    9, 0, 0, 2, 2, 0, 0,
                    [],
                    ["host.filesystem", "host.ports"]),
                PublicAccess = InstallPlanFactory.CreateDefault().PublicAccess with
                {
                    Domain = "*.deltabox.dev",
                    Zone = "deltabox.dev",
                    AcmeEmail = "admin@deltabox.dev",
                    DnsProvider = "desec",
                    UseStaging = useStaging,
                    Preparation = new DomainPreparationSetupConfig(
                        "Validated",
                        DateTime.UtcNow,
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
            var reviewedJson = JsonSerializer.Serialize(reviewed, JsonOptions);

            db.Installations.Add(new InstallationEntity
            {
                Id = installationId,
                Status = InstallationStatuses.Running,
                ConfigJson = reviewedJson,
                FrozenConfigJson = reviewedJson,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                StartedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            var services = new ServiceCollection();
            services.AddDataProtection()
                .SetApplicationName("mem-startup-install-rel-01e-cert-tests")
                .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
            var dataProtectionServices = services.BuildServiceProvider();
            var secretStore = new ProtectedInstallationSecretStore(
                db,
                dataProtectionServices.GetRequiredService<IDataProtectionProvider>());
            await secretStore.SetProtectedAsync(
                installationId,
                InstallationSecretNames.DnsCategory,
                InstallationSecretNames.DesecProviderToken,
                DesecToken,
                "deSEC token",
                CancellationToken.None);

            var domainSecretStore = new ProtectedDomainSecretStore(
                db,
                dataProtectionServices.GetRequiredService<IDataProtectionProvider>());
            var renewalService = new DomainCertificateRenewalService(
                db,
                domainSecretStore,
                new AlwaysSuccessfulDnsZoneAccessProbe(),
                NullLogger<DomainCertificateRenewalService>.Instance);

            return new CertificateFixture(
                root,
                db,
                dataProtectionServices,
                installationId,
                secretStore,
                domainSecretStore,
                renewalService);
        }

        public Task<string?> FrozenAsync() =>
            Db.Installations.AsNoTracking()
                .Where(x => x.Id == InstallationId)
                .Select(x => x.FrozenConfigJson)
                .SingleAsync();

        public async Task SetRuntimeCertificateAsync(string certificateId, int? npmCertificateId)
        {
            var installation = await Db.Installations.SingleAsync(x => x.Id == InstallationId);
            var plan = JsonSerializer.Deserialize<InstallPlan>(installation.ConfigJson!, JsonOptions)!;
            installation.ConfigJson = JsonSerializer.Serialize(
                plan with
                {
                    PublicAccess = plan.PublicAccess with
                    {
                        CertificateId = certificateId,
                        NpmCertificateId = npmCertificateId,
                        CertificateValidated = true,
                        ImportedToNpm = npmCertificateId is > 0,
                        LastVerifiedAtUtc = DateTime.UtcNow
                    }
                },
                JsonOptions);
            await Db.SaveChangesAsync();
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


    private sealed class AlwaysSuccessfulDnsZoneAccessProbe : IDnsZoneAccessProbe
    {
        public Task<DnsZoneAccessProbeResult> ProbeAsync(
            DnsZoneAccessProbeRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new DnsZoneAccessProbeResult(
                Succeeded: true,
                Message: "deSEC credential verified.",
                ErrorCode: null,
                Evidence: []));
    }

    private sealed class ThrowingDomainSecretStore : IDomainSecretStore
    {
        public Task SetProtectedAsync(
            Guid domainId,
            string category,
            string key,
            string secret,
            string? description,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated protected Domain credential persistence failure.");

        public Task<string?> ResolveProtectedAsync(
            Guid domainId,
            string category,
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        public Task<bool> ExistsAsync(
            Guid domainId,
            string category,
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task DeleteAsync(
            Guid domainId,
            string category,
            string key,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
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
