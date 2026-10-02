using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Shared.Domains;
using Modules.Shared.Domains.Certificates;

namespace Api.IntegrationTests.Domains;

public sealed class DomainActiveCertificateSelectionTests
{
    [Fact]
    public async Task First_certificate_is_active_and_operator_selection_switches_the_domain_pointer_durably()
    {
        await using var fixture = await Fixture.CreateAsync();

        var main = Metadata(
            certificateId: "cert-deltabox",
            domain: "*.deltabox.dev",
            zone: "deltabox.dev",
            createdAtUtc: new DateTimeOffset(2026, 9, 11, 4, 0, 0, TimeSpan.Zero));
        var first = Metadata(
            certificateId: "cert-matrixeasyhost-first",
            domain: "*.matrixeasyhost.com",
            zone: "matrixeasyhost.com",
            createdAtUtc: new DateTimeOffset(2026, 9, 11, 4, 5, 0, TimeSpan.Zero));
        var replacement = Metadata(
            certificateId: "cert-matrixeasyhost-replacement",
            domain: "*.matrixeasyhost.com",
            zone: "matrixeasyhost.com",
            createdAtUtc: new DateTimeOffset(2026, 9, 11, 4, 10, 0, TimeSpan.Zero));

        await fixture.Registry.RegisterCertificateAsync(main, CancellationToken.None);
        await fixture.Registry.RegisterCertificateAsync(first, CancellationToken.None);
        await fixture.Registry.RegisterCertificateAsync(replacement, CancellationToken.None);

        var before = await fixture.Registry.ListCertificatesAsync(CancellationToken.None);
        Assert.True(before.Single(x => x.CertificateId == first.CertificateId).IsInUse);
        Assert.False(before.Single(x => x.CertificateId == replacement.CertificateId).IsInUse);

        var targetDomainId = await fixture.Db.Domains
            .Where(x => x.BaseDomain == "matrixeasyhost.com")
            .Select(x => x.Id)
            .SingleAsync();

        var result = await fixture.Registry.SetDomainActiveCertificateAsync(
            targetDomainId,
            replacement.CertificateId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Succeeded", result.Status);
        Assert.Equal("matrixeasyhost.com", result.BaseDomain);

        fixture.Db.ChangeTracker.Clear();
        var domain = await fixture.Db.Domains
            .Include(x => x.ActiveCertificate)
            .SingleAsync(x => x.BaseDomain == "matrixeasyhost.com");

        Assert.Equal(replacement.CertificateId, domain.ActiveCertificate?.CertificateId);

        var after = await fixture.Registry.ListCertificatesAsync(CancellationToken.None);
        Assert.False(after.Single(x => x.CertificateId == first.CertificateId).IsInUse);
        Assert.True(after.Single(x => x.CertificateId == replacement.CertificateId).IsInUse);

        var firstStored = await fixture.Storage.GetMetadataAsync(first.CertificateId, CancellationToken.None);
        var replacementStored = await fixture.Storage.GetMetadataAsync(replacement.CertificateId, CancellationToken.None);
        Assert.NotNull(firstStored);
        Assert.NotNull(replacementStored);
        Assert.False(firstStored!.IsInUse);
        Assert.True(replacementStored!.IsInUse);
    }

    [Fact]
    public async Task Main_domain_rejects_a_non_main_certificate_as_a_separate_active_selection()
    {
        await using var fixture = await Fixture.CreateAsync();

        var main = Metadata(
            certificateId: "cert-main",
            domain: "*.deltabox.dev",
            zone: "deltabox.dev",
            createdAtUtc: new DateTimeOffset(2026, 9, 11, 5, 0, 0, TimeSpan.Zero));
        var replacement = Metadata(
            certificateId: "cert-main-replacement",
            domain: "*.deltabox.dev",
            zone: "deltabox.dev",
            createdAtUtc: new DateTimeOffset(2026, 9, 11, 5, 5, 0, TimeSpan.Zero));

        await fixture.Registry.RegisterCertificateAsync(main, CancellationToken.None);
        await fixture.Registry.RegisterCertificateAsync(replacement, CancellationToken.None);

        var targetDomainId = await fixture.Db.Domains
            .Where(x => x.BaseDomain == "deltabox.dev")
            .Select(x => x.Id)
            .SingleAsync();

        var result = await fixture.Registry.SetDomainActiveCertificateAsync(
            targetDomainId,
            replacement.CertificateId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("main platform certificate", result.Message, StringComparison.OrdinalIgnoreCase);

        fixture.Db.ChangeTracker.Clear();
        var domain = await fixture.Db.Domains
            .Include(x => x.ActiveCertificate)
            .SingleAsync(x => x.BaseDomain == "deltabox.dev");

        Assert.Equal(main.CertificateId, domain.ActiveCertificate?.CertificateId);
    }

    [Fact]
    public async Task Staging_certificate_cannot_be_selected_as_active_for_a_domain()
    {
        await using var fixture = await Fixture.CreateAsync();

        var production = Metadata(
            certificateId: "cert-matrixeasyhost-production",
            domain: "*.matrixeasyhost.com",
            zone: "matrixeasyhost.com",
            createdAtUtc: new DateTimeOffset(2026, 9, 12, 8, 0, 0, TimeSpan.Zero));
        var staging = Metadata(
            certificateId: "cert-matrixeasyhost-staging",
            domain: "*.matrixeasyhost.com",
            zone: "matrixeasyhost.com",
            createdAtUtc: new DateTimeOffset(2026, 9, 12, 8, 5, 0, TimeSpan.Zero),
            isStaging: true);

        await fixture.Registry.RegisterCertificateAsync(production, CancellationToken.None);
        await fixture.Registry.RegisterCertificateAsync(staging, CancellationToken.None);

        var targetDomainId = await fixture.Db.Domains
            .Where(x => x.BaseDomain == "matrixeasyhost.com")
            .Select(x => x.Id)
            .SingleAsync();

        var result = await fixture.Registry.SetDomainActiveCertificateAsync(
            targetDomainId,
            staging.CertificateId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("staging", result.Message, StringComparison.OrdinalIgnoreCase);

        fixture.Db.ChangeTracker.Clear();
        var domain = await fixture.Db.Domains
            .Include(x => x.ActiveCertificate)
            .SingleAsync(x => x.BaseDomain == "matrixeasyhost.com");

        Assert.Equal(production.CertificateId, domain.ActiveCertificate?.CertificateId);
    }

    private static StoredCertificateMetadata Metadata(
        string certificateId,
        string domain,
        string zone,
        DateTimeOffset createdAtUtc,
        bool isStaging = false) =>
        new()
        {
            CertificateId = certificateId,
            Domain = domain,
            Zone = zone,
            Provider = "desec",
            IsWildcard = true,
            IsStaging = isStaging,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = createdAtUtc.AddDays(90),
            FullchainPath = $"/test/{certificateId}/fullchain.pem",
            PrivateKeyPath = $"/test/{certificateId}/privkey.pem",
            Thumbprint = certificateId,
            Status = "Succeeded"
        };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;

        private Fixture(
            string root,
            MemDbContext db,
            CertificateStorageService storage,
            DomainRegistryService registry)
        {
            _root = root;
            Db = db;
            Storage = storage;
            Registry = registry;
        }

        public MemDbContext Db { get; }
        public CertificateStorageService Storage { get; }
        public DomainRegistryService Registry { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"mem-domain-active-cert-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var databasePath = Path.Combine(root, "registry.db");
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var storage = new CertificateStorageService(
                Options.Create(new CertificateStorageOptions
                {
                    RootPath = Path.Combine(root, "certificates")
                }),
                NullLogger<CertificateStorageService>.Instance);

            var registry = new DomainRegistryService(
                db,
                storage,
                NullLogger<DomainRegistryService>.Instance);

            return new Fixture(root, db, storage, registry);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
