using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Shared.Domains.Dns;
using Modules.Shared.Domains.Renewal;

namespace Api.IntegrationTests.Domains;

public sealed class DomainCertificateRenewalPolicyTests
{
    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01A_historical_domain_requires_one_time_credential_enrolment()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = await fixture.AddDomainWithActiveProductionCertificateAsync("historical.example");

        var state = await fixture.Service.GetAsync(domain.Id, CancellationToken.None);

        Assert.NotNull(state);
        Assert.False(state!.PolicyConfigured);
        Assert.False(state.CredentialConfigured);
        Assert.True(state.HasActiveProductionCertificate);
        Assert.Equal(DomainRenewalReadinessStatuses.RenewalCredentialRequired, state.ReadinessStatus);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01A_verified_enrolment_sets_defaults_rotation_replaces_secret_and_removal_is_immediately_unready()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = await fixture.AddDomainWithActiveProductionCertificateAsync("renewal.example");
        const string firstToken = "desec-renewal-token-one";
        const string secondToken = "desec-renewal-token-two";

        var enrolled = await fixture.Service.EnrollOrRotateCredentialAsync(
            domain.Id,
            firstToken,
            "ops@renewal.example",
            CancellationToken.None);

        Assert.True(enrolled.Succeeded, enrolled.Message);
        Assert.Equal(firstToken, Assert.Single(fixture.Probe.Tokens));
        Assert.NotNull(enrolled.State);
        Assert.True(enrolled.State!.PolicyConfigured);
        Assert.True(enrolled.State.AutoRenewEnabled);
        Assert.True(enrolled.State.CredentialConfigured);
        Assert.Equal("ops@renewal.example", enrolled.State.AcmeEmail);
        Assert.Equal(DomainCertificateRenewalService.DefaultRenewalWindowDays, enrolled.State.RenewalWindowDays);
        Assert.Equal(DomainCertificateRenewalService.DefaultRetryIntervalHours, enrolled.State.RetryIntervalHours);
        Assert.Equal(DomainRenewalReadinessStatuses.Ready, enrolled.State.ReadinessStatus);

        var stored = await fixture.Db.DomainSecrets.AsNoTracking().SingleAsync();
        Assert.DoesNotContain(firstToken, stored.ProtectedValue, StringComparison.Ordinal);

        var rotated = await fixture.Service.EnrollOrRotateCredentialAsync(
            domain.Id,
            secondToken,
            null,
            CancellationToken.None);
        Assert.True(rotated.Succeeded, rotated.Message);
        Assert.Equal(secondToken, fixture.Probe.Tokens.Last());
        Assert.Equal(
            secondToken,
            await fixture.SecretStore.ResolveProtectedAsync(
                domain.Id,
                DomainRenewalSecretNames.DnsProviderCategory,
                DomainRenewalSecretNames.DesecProviderToken,
                CancellationToken.None));

        var removed = await fixture.Service.RemoveCredentialAsync(domain.Id, CancellationToken.None);
        Assert.NotNull(removed);
        Assert.True(removed!.AutoRenewEnabled);
        Assert.False(removed.CredentialConfigured);
        Assert.Equal(DomainRenewalReadinessStatuses.RenewalCredentialRequired, removed.ReadinessStatus);
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01A_domain_delete_cascades_policy_and_credential()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = await fixture.AddDomainAsync("delete.example");

        await fixture.SecretStore.SetProtectedAsync(
            domain.Id,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            "delete-secret",
            null,
            CancellationToken.None);
        await fixture.Service.UpdatePolicyAsync(
            domain.Id,
            autoRenewEnabled: true,
            acmeEmail: "ops@delete.example",
            CancellationToken.None);

        fixture.Db.Domains.Remove(await fixture.Db.Domains.SingleAsync(x => x.Id == domain.Id));
        await fixture.Db.SaveChangesAsync();

        Assert.False(await fixture.Db.DomainSecrets.AsNoTracking().AnyAsync(x => x.DomainId == domain.Id));
        Assert.False(await fixture.Db.DomainCertificateRenewalPolicies.AsNoTracking().AnyAsync(x => x.DomainId == domain.Id));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly ServiceProvider _services;

        private Fixture(
            string root,
            ServiceProvider services,
            MemDbContext db,
            ProtectedDomainSecretStore secretStore,
            RecordingDnsZoneAccessProbe probe,
            DomainCertificateRenewalService service)
        {
            _root = root;
            _services = services;
            Db = db;
            SecretStore = secretStore;
            Probe = probe;
            Service = service;
        }

        public MemDbContext Db { get; }
        public ProtectedDomainSecretStore SecretStore { get; }
        public RecordingDnsZoneAccessProbe Probe { get; }
        public DomainCertificateRenewalService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"mem-domain-renewal-policy-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var keyRingPath = Path.Combine(root, "keys");
            Directory.CreateDirectory(keyRingPath);

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "control-plane.db")}")
                .Options;
            var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var services = new ServiceCollection();
            services.AddDataProtection()
                .SetApplicationName("mem-domain-renewal-policy-tests")
                .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
            var provider = services.BuildServiceProvider();
            var secretStore = new ProtectedDomainSecretStore(
                db,
                provider.GetRequiredService<IDataProtectionProvider>());
            var probe = new RecordingDnsZoneAccessProbe();
            var service = new DomainCertificateRenewalService(
                db,
                secretStore,
                probe,
                NullLogger<DomainCertificateRenewalService>.Instance);

            return new Fixture(root, provider, db, secretStore, probe, service);
        }

        public async Task<DomainEntity> AddDomainAsync(string baseDomain)
        {
            var now = DateTime.UtcNow;
            var domain = new DomainEntity
            {
                Id = Guid.NewGuid(),
                BaseDomain = baseDomain,
                DisplayName = baseDomain,
                Purpose = "chat",
                IsMainPlatformDomain = false,
                DnsProvider = "desec",
                DnsZone = baseDomain,
                Status = "Active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            Db.Domains.Add(domain);
            await Db.SaveChangesAsync();
            return domain;
        }

        public async Task<DomainEntity> AddDomainWithActiveProductionCertificateAsync(string baseDomain)
        {
            var domain = await AddDomainAsync(baseDomain);
            var certificate = new CertificateEntity
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                CertificateId = $"cert-{Guid.NewGuid():N}",
                CommonName = $"*.{baseDomain}",
                Provider = "letsencrypt",
                IsWildcard = true,
                IsStaging = false,
                IsMainPlatformCertificate = false,
                IsActive = true,
                Status = "Active",
                CreatedAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddDays(60),
                FullchainPath = "/protected/fullchain.pem",
                PrivateKeyPath = "/protected/privkey.pem"
            };
            Db.Certificates.Add(certificate);
            await Db.SaveChangesAsync();
            domain.ActiveCertificateId = certificate.Id;
            domain.UpdatedAtUtc = DateTime.UtcNow;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return domain;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _services.Dispose();
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

    private sealed class RecordingDnsZoneAccessProbe : IDnsZoneAccessProbe
    {
        public List<string> Tokens { get; } = [];

        public Task<DnsZoneAccessProbeResult> ProbeAsync(
            DnsZoneAccessProbeRequest request,
            CancellationToken cancellationToken)
        {
            Tokens.Add(request.ProviderToken);
            return Task.FromResult(new DnsZoneAccessProbeResult(
                Succeeded: true,
                Message: "Provider access confirmed.",
                ErrorCode: null,
                Evidence: []));
        }
    }
}
