using System.Text.Json;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Setup.Domains.Planning;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Modules.Setup.Secrets;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Dns;
using Shared.Diagnostics;

namespace Api.IntegrationTests.Setup;

public sealed class SetupDomainPlanTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CLEANROOM_CORR_06A_fresh_domain_plan_defaults_to_production_acme()
    {
        await using var fixture = await DomainPlanFixture.CreateAsync(
            DnsZoneAccessProbeResultSuccess());

        var setup = await fixture.InstallPlanService.BeginSetupAsync(CancellationToken.None);
        var response = await fixture.DomainPlanService.GetCurrentAsync(CancellationToken.None);

        Assert.False(response.UseStaging);

        var installation = await fixture.Db.Installations
            .AsNoTracking()
            .SingleAsync(x => x.Id == setup.Id);
        var config = JsonSerializer.Deserialize<InstallPlan>(installation.ConfigJson!, JsonOptions);

        Assert.NotNull(config);
        Assert.False(config!.PublicAccess.UseStaging);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01D_domain_validation_persists_only_plan_and_protected_credentials()
    {
        await using var fixture = await DomainPlanFixture.CreateAsync(
            DnsZoneAccessProbeResultSuccess());

        const string token = "desec-token-01d-do-not-persist-in-config";
        var setup = await fixture.InstallPlanService.BeginSetupAsync(CancellationToken.None);

        // Pretend a future Review had frozen this Draft, then edit the Domain plan.
        var installation = await fixture.Db.Installations.SingleAsync(x => x.Id == setup.Id);
        installation.Status = InstallationStatuses.Ready;
        installation.FrozenConfigJson = "{\"reviewed\":true}";
        await fixture.Db.SaveChangesAsync();

        var response = await fixture.DomainPlanService.ValidateAndSaveAsync(
            new SetupDomainPlanRequest(
                BaseDomain: "https://Example.COM/",
                AcmeEmail: "admin@example.com",
                DnsProvider: "desec",
                ProviderToken: token,
                UseStaging: true),
            CancellationToken.None);

        Assert.True(response.Succeeded);
        Assert.True(response.Configured);
        Assert.Equal("Validated", response.Status);
        Assert.Equal("example.com", response.Zone);
        Assert.Equal("*.example.com", response.Domain);
        Assert.True(response.ProviderAccessConfirmed);
        Assert.True(response.ProviderCredentialStored);
        Assert.Contains("No DNS records or certificates have been changed", response.Message, StringComparison.Ordinal);

        var saved = await fixture.Db.Installations
            .AsNoTracking()
            .SingleAsync(x => x.Id == setup.Id);
        var config = JsonSerializer.Deserialize<InstallPlan>(saved.ConfigJson!, JsonOptions);

        Assert.NotNull(config);
        Assert.Equal("example.com", config!.PublicAccess.Zone);
        Assert.Equal("*.example.com", config.PublicAccess.Domain);
        Assert.Equal("admin@example.com", config.PublicAccess.AcmeEmail);
        Assert.Equal("desec", config.PublicAccess.DnsProvider);
        Assert.True(config.PublicAccess.UseStaging);
        Assert.Null(config.PublicAccess.CertificateId);
        Assert.Null(config.PublicAccess.NpmCertificateId);
        Assert.False(config.PublicAccess.CertificateValidated);
        Assert.False(config.PublicAccess.ImportedToNpm);
        Assert.False(config.PublicAccess.ProxyHostVerified);
        Assert.NotNull(config.PublicAccess.Preparation);
        Assert.Equal("Validated", config.PublicAccess.Preparation!.Status);
        Assert.True(config.PublicAccess.Preparation.ProviderAccessConfirmed);
        Assert.True(config.PublicAccess.Preparation.ProviderCredentialStored);

        Assert.Equal(InstallationStatuses.Draft, saved.Status);
        Assert.Null(saved.FrozenConfigJson);
        Assert.DoesNotContain(token, saved.ConfigJson!, StringComparison.Ordinal);

        Assert.Empty(await fixture.Db.Domains.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.Certificates.AsNoTracking().ToListAsync());

        var secretRows = await fixture.Db.InstallationSecrets
            .AsNoTracking()
            .Where(x => x.InstallationId == setup.Id)
            .OrderBy(x => x.Key)
            .ToListAsync();

        Assert.Equal(2, secretRows.Count);
        Assert.All(secretRows, row => Assert.DoesNotContain(token, row.Value, StringComparison.Ordinal));
        Assert.Contains(secretRows, row => row.Key == InstallationSecretNames.DesecProviderToken);
        Assert.Contains(secretRows, row => row.Key == InstallationSecretNames.PostgresPassword);

        var resolvedToken = await fixture.SecretStore.ResolveProtectedAsync(
            setup.Id,
            InstallationSecretNames.DnsCategory,
            InstallationSecretNames.DesecProviderToken,
            CancellationToken.None);
        Assert.Equal(token, resolvedToken);

        var postgresPassword = await fixture.SecretStore.ResolveProtectedAsync(
            setup.Id,
            InstallationSecretNames.PlatformCategory,
            InstallationSecretNames.PostgresPassword,
            CancellationToken.None);
        Assert.NotNull(postgresPassword);
        Assert.Equal(64, postgresPassword!.Length);
        Assert.NotEqual("postgres", postgresPassword);
        Assert.DoesNotContain(postgresPassword, saved.ConfigJson!, StringComparison.Ordinal);

        Assert.Single(fixture.ZoneProbe.Requests);
        Assert.Equal("example.com", fixture.ZoneProbe.Requests[0].Zone);
        Assert.Equal(token, fixture.ZoneProbe.Requests[0].ProviderToken);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01D_failed_read_only_provider_validation_does_not_persist_secret_or_mutation_state()
    {
        await using var fixture = await DomainPlanFixture.CreateAsync(
            new DnsZoneAccessProbeResult(
                Succeeded: false,
                Message: "deSEC rejected the credential.",
                ErrorCode: "DesecUnauthorized",
                Evidence:
                [
                    new CertificateOperationEvidence(
                        "dns.providerAccess",
                        "authorization failed",
                        Status: "Failed")
                ]));

        var setup = await fixture.InstallPlanService.BeginSetupAsync(CancellationToken.None);
        var before = await fixture.Db.Installations.AsNoTracking().SingleAsync(x => x.Id == setup.Id);

        var response = await fixture.DomainPlanService.ValidateAndSaveAsync(
            new SetupDomainPlanRequest(
                BaseDomain: "example.com",
                AcmeEmail: "admin@example.com",
                DnsProvider: "desec",
                ProviderToken: "rejected-secret-token",
                UseStaging: false),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.False(response.Configured);
        Assert.Equal("ValidationFailed", response.Status);
        Assert.Equal("DesecUnauthorized", response.ErrorCode);

        var after = await fixture.Db.Installations.AsNoTracking().SingleAsync(x => x.Id == setup.Id);
        Assert.Equal(before.ConfigJson, after.ConfigJson);
        Assert.Equal(before.FrozenConfigJson, after.FrozenConfigJson);
        Assert.Empty(await fixture.Db.InstallationSecrets.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.Domains.AsNoTracking().ToListAsync());
        Assert.Empty(await fixture.Db.Certificates.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01D_CORR_01_failed_provider_validation_writes_safe_diagnostic_event()
    {
        const string token = "desec-domain-diagnostic-secret-token";
        await using var fixture = await DomainPlanFixture.CreateAsync(
            new DnsZoneAccessProbeResult(
                Succeeded: false,
                Message: "deSEC could not find DNS zone 'wrong.example' in the account accessible with this token.",
                ErrorCode: "DesecZoneNotAccessible",
                Evidence:
                [
                    new CertificateOperationEvidence(
                        "desecResponse",
                        "404 NotFound",
                        Status: "Failed")
                ],
                ProviderStatusCode: 404));

        var setup = await fixture.InstallPlanService.BeginSetupAsync(CancellationToken.None);

        var response = await fixture.DomainPlanService.ValidateAndSaveAsync(
            new SetupDomainPlanRequest(
                BaseDomain: "wrong.example",
                AcmeEmail: "admin@example.com",
                DnsProvider: "desec",
                ProviderToken: token,
                UseStaging: true),
            CancellationToken.None);

        Assert.False(response.Succeeded);
        Assert.Equal("DesecZoneNotAccessible", response.ErrorCode);

        var diagnostic = Assert.Single(fixture.Diagnostics.Requests);
        Assert.Equal("setup.domain.validation.failed", diagnostic.EventCode);
        Assert.Equal("setup", diagnostic.Feature);
        Assert.Equal("domain-validation", diagnostic.Stage);
        Assert.Equal(setup.Id.ToString("D"), diagnostic.Resource?.Id);
        Assert.Equal("desec", diagnostic.Details?["provider"]);
        Assert.Equal("wrong.example", diagnostic.Details?["zone"]);
        Assert.Equal("DesecZoneNotAccessible", diagnostic.Details?["failureClass"]);
        Assert.Equal("404", diagnostic.Details?["httpStatus"]);
        Assert.Equal("none", diagnostic.Details?["externalMutation"]);
        Assert.True(diagnostic.Retryable);
        Assert.False(diagnostic.CreateIncident);

        var detailProjection = diagnostic.Details is null
            ? string.Empty
            : string.Join(";", diagnostic.Details.Select(pair => $"{pair.Key}={pair.Value}"));
        var safeProjection = string.Join(
            "|",
            diagnostic.Message,
            diagnostic.SuggestedAction ?? string.Empty,
            detailProjection);
        Assert.DoesNotContain(token, safeProjection, StringComparison.Ordinal);
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01D_get_current_plan_projects_no_provider_token()
    {
        await using var fixture = await DomainPlanFixture.CreateAsync(DnsZoneAccessProbeResultSuccess());

        const string token = "desec-token-should-never-return-to-browser";
        await fixture.InstallPlanService.BeginSetupAsync(CancellationToken.None);
        await fixture.DomainPlanService.ValidateAndSaveAsync(
            new SetupDomainPlanRequest(
                "example.com",
                "admin@example.com",
                "desec",
                token,
                UseStaging: true),
            CancellationToken.None);

        var response = await fixture.DomainPlanService.GetCurrentAsync(CancellationToken.None);
        var json = JsonSerializer.Serialize(response, JsonOptions);

        Assert.True(response.Configured);
        Assert.True(response.ProviderCredentialStored);
        Assert.DoesNotContain(token, json, StringComparison.Ordinal);
        Assert.DoesNotContain("ProviderToken", json, StringComparison.OrdinalIgnoreCase);
    }

    private static DnsZoneAccessProbeResult DnsZoneAccessProbeResultSuccess() =>
        new(
            Succeeded: true,
            Message: "Read-only zone access confirmed.",
            ErrorCode: null,
            Evidence:
            [
                new CertificateOperationEvidence(
                    "dns.providerAccess",
                    "read-only access confirmed",
                    Status: "Succeeded")
            ]);

    private sealed class DomainPlanFixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly ServiceProvider _dataProtectionServices;

        private DomainPlanFixture(
            string root,
            MemDbContext db,
            ServiceProvider dataProtectionServices,
            RecordingZoneAccessProbe zoneProbe,
            ProtectedInstallationSecretStore secretStore,
            InstallPlanService installPlanService,
            SetupDomainPlanService domainPlanService,
            RecordingDiagnosticWriter diagnostics)
        {
            _root = root;
            Db = db;
            _dataProtectionServices = dataProtectionServices;
            ZoneProbe = zoneProbe;
            SecretStore = secretStore;
            InstallPlanService = installPlanService;
            DomainPlanService = domainPlanService;
            Diagnostics = diagnostics;
        }

        public MemDbContext Db { get; }
        public RecordingZoneAccessProbe ZoneProbe { get; }
        public ProtectedInstallationSecretStore SecretStore { get; }
        public InstallPlanService InstallPlanService { get; }
        public SetupDomainPlanService DomainPlanService { get; }
        public RecordingDiagnosticWriter Diagnostics { get; }

        public static async Task<DomainPlanFixture> CreateAsync(DnsZoneAccessProbeResult probeResult)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"mem-startup-install-rel-01d-domain-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var databasePath = Path.Combine(root, "control-plane.db");
            var keyRingPath = Path.Combine(root, "keys");
            Directory.CreateDirectory(keyRingPath);

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var services = new ServiceCollection();
            services.AddDataProtection()
                .SetApplicationName("mem-startup-install-rel-01d-domain-tests")
                .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
            var dataProtectionServices = services.BuildServiceProvider();

            var zoneProbe = new RecordingZoneAccessProbe(probeResult);
            var secretStore = new ProtectedInstallationSecretStore(
                db,
                dataProtectionServices.GetRequiredService<IDataProtectionProvider>());
            var credentialService = new InstallationCredentialService(secretStore);
            var diagnostics = new RecordingDiagnosticWriter();
            var installPlanService = new InstallPlanService(
                db,
                NullLogger<InstallPlanService>.Instance,
                new RecordingInstallRunCoordinator());
            var domainPlanService = new SetupDomainPlanService(
                db,
                zoneProbe,
                secretStore,
                credentialService,
                NullLogger<SetupDomainPlanService>.Instance,
                diagnostics);

            return new DomainPlanFixture(
                root,
                db,
                dataProtectionServices,
                zoneProbe,
                secretStore,
                installPlanService,
                domainPlanService,
                diagnostics);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _dataProtectionServices.Dispose();

            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
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

    private sealed class RecordingZoneAccessProbe(DnsZoneAccessProbeResult result) : IDnsZoneAccessProbe
    {
        public List<DnsZoneAccessProbeRequest> Requests { get; } = [];

        public Task<DnsZoneAccessProbeResult> ProbeAsync(
            DnsZoneAccessProbeRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(result);
        }
    }
}
