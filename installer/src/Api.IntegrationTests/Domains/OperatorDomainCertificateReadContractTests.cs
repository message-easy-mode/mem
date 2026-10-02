using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Operator.Domains;
using Modules.Shared.Domains;
using Modules.Shared.Domains.Certificates;

namespace Api.IntegrationTests.Domains;

public sealed class OperatorDomainCertificateReadContractTests
{
    [Fact]
    public async Task DOMAINS_WORKSPACE_01B_cross_domain_inventory_is_safe_and_identifies_the_owning_domain()
    {
        await using var fixture = await Fixture.CreateAsync();

        var mainDomain = Domain("deltabox.dev", isMain: true, purpose: "platform-main");
        var ordinaryDomain = Domain("matrixeasyhost.com", isMain: false, purpose: "chat");
        fixture.Db.Domains.AddRange(mainDomain, ordinaryDomain);
        await fixture.Db.SaveChangesAsync();

        var mainCertificate = Certificate(
            mainDomain,
            "cert-deltabox-production",
            "*.deltabox.dev",
            isMain: true,
            createdAtUtc: new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc));
        var oldCertificate = Certificate(
            ordinaryDomain,
            "cert-matrix-old",
            "*.matrixeasyhost.com",
            isMain: false,
            createdAtUtc: new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc));
        var activeCertificate = Certificate(
            ordinaryDomain,
            "cert-matrix-current",
            "*.matrixeasyhost.com",
            isMain: false,
            createdAtUtc: new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc));

        fixture.Db.Certificates.AddRange(mainCertificate, oldCertificate, activeCertificate);
        await fixture.Db.SaveChangesAsync();

        mainDomain.ActiveCertificateId = mainCertificate.Id;
        ordinaryDomain.ActiveCertificateId = activeCertificate.Id;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var inventory = await OperatorDomainsEndpoints.ListCertificateReadModelsAsync(
            fixture.Db,
            domainId: null,
            CancellationToken.None);

        Assert.Equal(3, inventory.Count);

        var current = inventory.Single(x => x.CertificateId == activeCertificate.CertificateId);
        Assert.Equal(ordinaryDomain.Id, current.DomainId);
        Assert.Equal("matrixeasyhost.com", current.DomainBaseDomain);
        Assert.Equal("matrixeasyhost.com", current.DomainDisplayName);
        Assert.False(current.DomainIsMainPlatformDomain);
        Assert.Equal(activeCertificate.Id, current.DomainActiveCertificateEntityId);
        Assert.Equal("desec", current.DomainDnsProvider);
        Assert.Equal("matrixeasyhost.com", current.DomainDnsZone);
        Assert.Equal("*.matrixeasyhost.com", current.Domain);
        Assert.Equal("matrixeasyhost.com", current.Zone);
        Assert.True(current.IsInUse);
        Assert.False(current.IsMainPlatformCertificate);

        var previous = inventory.Single(x => x.CertificateId == oldCertificate.CertificateId);
        Assert.False(previous.IsInUse);

        var main = inventory.Single(x => x.CertificateId == mainCertificate.CertificateId);
        Assert.True(main.DomainIsMainPlatformDomain);
        Assert.True(main.IsMainPlatformCertificate);
        Assert.True(main.IsInUse);

        var json = JsonSerializer.Serialize(current, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("fullchainPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("privateKeyPath", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/protected/certificates", json, StringComparison.Ordinal);
        Assert.Null(typeof(OperatorCertificateReadResponse).GetProperty("FullchainPath"));
        Assert.Null(typeof(OperatorCertificateReadResponse).GetProperty("PrivateKeyPath"));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01B_domain_owned_detail_returns_only_the_matching_owner()
    {
        await using var fixture = await Fixture.CreateAsync();

        var firstDomain = Domain("alpha.example", isMain: false, purpose: "chat");
        var secondDomain = Domain("bravo.example", isMain: false, purpose: "chat");
        fixture.Db.Domains.AddRange(firstDomain, secondDomain);
        await fixture.Db.SaveChangesAsync();

        var certificate = Certificate(
            firstDomain,
            "cert-alpha",
            "*.alpha.example",
            isMain: false,
            createdAtUtc: new DateTime(2026, 9, 3, 8, 0, 0, DateTimeKind.Utc));
        fixture.Db.Certificates.Add(certificate);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var owned = await OperatorDomainsEndpoints.GetCertificateReadModelAsync(
            fixture.Db,
            firstDomain.Id,
            certificate.CertificateId,
            CancellationToken.None);
        var mismatched = await OperatorDomainsEndpoints.GetCertificateReadModelAsync(
            fixture.Db,
            secondDomain.Id,
            certificate.CertificateId,
            CancellationToken.None);

        Assert.NotNull(owned);
        Assert.Equal(firstDomain.Id, owned!.DomainId);
        Assert.Null(mismatched);
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01B_domain_scoped_active_selection_rejects_a_foreign_certificate()
    {
        await using var fixture = await Fixture.CreateAsync();

        var firstDomain = Domain("alpha.example", isMain: false, purpose: "chat");
        var secondDomain = Domain("bravo.example", isMain: false, purpose: "chat");
        fixture.Db.Domains.AddRange(firstDomain, secondDomain);
        await fixture.Db.SaveChangesAsync();

        var firstCertificate = Certificate(
            firstDomain,
            "cert-alpha",
            "*.alpha.example",
            isMain: false,
            createdAtUtc: new DateTime(2026, 9, 4, 8, 0, 0, DateTimeKind.Utc));
        var secondCertificate = Certificate(
            secondDomain,
            "cert-bravo",
            "*.bravo.example",
            isMain: false,
            createdAtUtc: new DateTime(2026, 9, 4, 9, 0, 0, DateTimeKind.Utc));
        fixture.Db.Certificates.AddRange(firstCertificate, secondCertificate);
        await fixture.Db.SaveChangesAsync();

        firstDomain.ActiveCertificateId = firstCertificate.Id;
        secondDomain.ActiveCertificateId = secondCertificate.Id;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Registry.SetDomainActiveCertificateAsync(
            firstDomain.Id,
            secondCertificate.CertificateId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("NotFound", result.Status);
        Assert.Null(result.BaseDomain);

        fixture.Db.ChangeTracker.Clear();
        var persistedFirst = await fixture.Db.Domains.SingleAsync(x => x.Id == firstDomain.Id);
        var persistedSecond = await fixture.Db.Domains.SingleAsync(x => x.Id == secondDomain.Id);
        Assert.Equal(firstCertificate.Id, persistedFirst.ActiveCertificateId);
        Assert.Equal(secondCertificate.Id, persistedSecond.ActiveCertificateId);
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01B_domain_scoped_active_selection_preserves_the_main_platform_guard()
    {
        await using var fixture = await Fixture.CreateAsync();

        var mainDomain = Domain("deltabox.dev", isMain: true, purpose: "platform-main");
        fixture.Db.Domains.Add(mainDomain);
        await fixture.Db.SaveChangesAsync();

        var mainCertificate = Certificate(
            mainDomain,
            "cert-main",
            "*.deltabox.dev",
            isMain: true,
            createdAtUtc: new DateTime(2026, 9, 5, 8, 0, 0, DateTimeKind.Utc));
        var replacement = Certificate(
            mainDomain,
            "cert-main-replacement",
            "*.deltabox.dev",
            isMain: false,
            createdAtUtc: new DateTime(2026, 9, 5, 9, 0, 0, DateTimeKind.Utc));
        fixture.Db.Certificates.AddRange(mainCertificate, replacement);
        await fixture.Db.SaveChangesAsync();

        mainDomain.ActiveCertificateId = mainCertificate.Id;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Registry.SetDomainActiveCertificateAsync(
            mainDomain.Id,
            replacement.CertificateId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("main platform certificate", result.Message, StringComparison.OrdinalIgnoreCase);

        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.Domains.SingleAsync(x => x.Id == mainDomain.Id);
        Assert.Equal(mainCertificate.Id, persisted.ActiveCertificateId);
    }

    [Fact]
    public void DOMAINS_WORKSPACE_01G_routes_keep_global_inventory_and_retire_superseded_global_mutations()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "installer", "src", "Modules", "Modules", "Operator", "Domains", "OperatorDomainsEndpoints.cs"));

        Assert.Contains("group.MapGet(\"/certificates\"", source, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/certificates/{certificateId}\"", source, StringComparison.Ordinal);

        Assert.DoesNotContain("group.MapPost(\"/certificates\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("group.MapPost(\"/certificates/{certificateId}/set-active\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("group.MapPost(\"/certificates/{certificateId}/set-main\"", source, StringComparison.Ordinal);

        Assert.DoesNotContain("group.MapDelete(\"/certificates/{certificateId}\"", source, StringComparison.Ordinal);

        Assert.Contains("group.MapGet(\"/{domainId:guid}/certificates\"", source, StringComparison.Ordinal);
        Assert.Contains("group.MapGet(\"/{domainId:guid}/certificates/{certificateId}\"", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/{domainId:guid}/certificates/{certificateId}/set-active\"", source, StringComparison.Ordinal);
        Assert.Contains("group.MapDelete(\"/{domainId:guid}/certificates/{certificateId}\"", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/{domainId:guid}/certificates/issuance\"", source, StringComparison.Ordinal);
        Assert.Contains("group.MapPost(\"/{domainId:guid}/set-main\"", source, StringComparison.Ordinal);
    }

    private static DomainEntity Domain(string baseDomain, bool isMain, string purpose)
    {
        var now = new DateTime(2026, 9, 1, 7, 0, 0, DateTimeKind.Utc);
        return new DomainEntity
        {
            Id = Guid.NewGuid(),
            BaseDomain = baseDomain,
            DisplayName = baseDomain,
            Purpose = purpose,
            IsMainPlatformDomain = isMain,
            DnsProvider = "desec",
            DnsZone = baseDomain,
            Status = "Active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private static CertificateEntity Certificate(
        DomainEntity domain,
        string certificateId,
        string commonName,
        bool isMain,
        DateTime createdAtUtc) =>
        new()
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            Domain = domain,
            CertificateId = certificateId,
            CommonName = commonName,
            Provider = "desec",
            IsWildcard = true,
            IsStaging = false,
            IsMainPlatformCertificate = isMain,
            IsActive = true,
            Status = "Valid",
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            FullchainPath = $"/protected/certificates/{certificateId}/fullchain.pem",
            PrivateKeyPath = $"/protected/certificates/{certificateId}/privkey.pem",
            Thumbprint = $"thumb-{certificateId}",
            ImportedToNpm = true,
            NpmCertificateId = 42
        };

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "installer", "src", "MemInstaller.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate repository root containing installer/src/MemInstaller.sln.");
    }

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
            var root = Path.Combine(Path.GetTempPath(), $"mem-domains-workspace-01b-{Guid.NewGuid():N}");
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
