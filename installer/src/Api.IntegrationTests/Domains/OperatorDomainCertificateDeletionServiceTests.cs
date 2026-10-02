using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Operator.Domains;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Certificates.Npm;

namespace Api.IntegrationTests.Domains;

public sealed class OperatorDomainCertificateDeletionServiceTests
{
    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_rejects_a_certificate_owned_by_another_domain()
    {
        await using var fixture = await Fixture.CreateAsync();
        var alpha = Domain("alpha.example", isMain: false);
        var bravo = Domain("bravo.example", isMain: false);
        fixture.Db.Domains.AddRange(alpha, bravo);
        await fixture.Db.SaveChangesAsync();

        var certificate = Certificate(alpha, "cert-alpha", isStaging: true);
        fixture.Db.Certificates.Add(certificate);
        await fixture.Db.SaveChangesAsync();
        await fixture.SaveMetadataAsync(certificate);

        var result = await fixture.Service.DeleteAsync(
            bravo.Id,
            certificate.CertificateId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("NotFound", result.Status);
        Assert.True(await fixture.Db.Certificates.AnyAsync(x => x.Id == certificate.Id));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_main_platform_certificate_remains_protected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = Domain("deltabox.dev", isMain: true);
        fixture.Db.Domains.Add(domain);
        await fixture.Db.SaveChangesAsync();

        var certificate = Certificate(domain, "cert-main", isStaging: false, isMain: true);
        fixture.Db.Certificates.Add(certificate);
        await fixture.Db.SaveChangesAsync();
        domain.ActiveCertificateId = certificate.Id;
        await fixture.Db.SaveChangesAsync();
        await fixture.SaveMetadataAsync(certificate);

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            certificate.CertificateId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("main platform", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await fixture.Db.Certificates.AnyAsync(x => x.Id == certificate.Id));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_staging_certificate_delete_removes_storage_and_registry_row()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
        await fixture.Db.SaveChangesAsync();

        var certificate = Certificate(domain, "cert-staging", isStaging: true);
        fixture.Db.Certificates.Add(certificate);
        await fixture.Db.SaveChangesAsync();
        await fixture.SaveMetadataAsync(certificate);

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            certificate.CertificateId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Deleted", result.Status);
        fixture.Db.ChangeTracker.Clear();
        Assert.False(await fixture.Db.Certificates.AnyAsync(x => x.Id == certificate.Id));
        Assert.Null(await fixture.Storage.GetMetadataAsync(certificate.CertificateId, CancellationToken.None));
        Assert.Empty(fixture.Npm.DeletedCertificateIds);
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_selected_certificate_on_unused_non_main_domain_can_be_deleted_deliberately()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
        await fixture.Db.SaveChangesAsync();

        var certificate = Certificate(domain, "cert-production", isStaging: false, npmCertificateId: 42);
        fixture.Db.Certificates.Add(certificate);
        await fixture.Db.SaveChangesAsync();
        domain.ActiveCertificateId = certificate.Id;
        await fixture.Db.SaveChangesAsync();
        await fixture.SaveMetadataAsync(certificate, isInUse: true);

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            certificate.CertificateId,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Contains("no longer has an active certificate", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { 42 }, fixture.Npm.DeletedCertificateIds);

        fixture.Db.ChangeTracker.Clear();
        var persistedDomain = await fixture.Db.Domains.SingleAsync(x => x.Id == domain.Id);
        Assert.Null(persistedDomain.ActiveCertificateId);
        Assert.Equal("Pending", persistedDomain.Status);
        Assert.False(await fixture.Db.Certificates.AnyAsync(x => x.Id == certificate.Id));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_selected_certificate_for_live_chat_server_is_blocked_until_replaced()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
        await fixture.Db.SaveChangesAsync();

        var certificate = Certificate(domain, "cert-production", isStaging: false);
        fixture.Db.Certificates.Add(certificate);
        fixture.Db.RuntimeStacks.Add(new RuntimeStackEntity
        {
            Id = Guid.NewGuid(),
            Slug = "tester",
            DisplayName = "Tester",
            Status = "Running",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            BaseDomain = domain.BaseDomain,
            DomainId = domain.Id,
            MatrixInstanceId = Guid.NewGuid()
        });
        await fixture.Db.SaveChangesAsync();
        domain.ActiveCertificateId = certificate.Id;
        await fixture.Db.SaveChangesAsync();
        await fixture.SaveMetadataAsync(certificate, isInUse: true);

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            certificate.CertificateId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("tester", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await fixture.Db.Certificates.AnyAsync(x => x.Id == certificate.Id));
        Assert.NotNull(await fixture.Storage.GetMetadataAsync(certificate.CertificateId, CancellationToken.None));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_npm_proxy_consumer_blocks_certificate_delete_without_local_mutation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
        await fixture.Db.SaveChangesAsync();

        var certificate = Certificate(domain, "cert-production", isStaging: false, npmCertificateId: 42);
        fixture.Db.Certificates.Add(certificate);
        await fixture.Db.SaveChangesAsync();
        await fixture.SaveMetadataAsync(certificate);

        fixture.Npm.ResultFactory = id => new NpmCertificateDeletionResult(
            Succeeded: false,
            Status: "Blocked",
            Message: "The NPM certificate is still referenced by one or more proxy hosts.",
            CertificateId: id,
            ConsumerProxyHostIds: [7, 9]);

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            certificate.CertificateId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("7", result.Message, StringComparison.Ordinal);
        Assert.Contains("9", result.Message, StringComparison.Ordinal);
        Assert.True(await fixture.Db.Certificates.AnyAsync(x => x.Id == certificate.Id));
        Assert.NotNull(await fixture.Storage.GetMetadataAsync(certificate.CertificateId, CancellationToken.None));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_npm_cleanup_failure_leaves_certificate_registry_and_storage_intact()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
        await fixture.Db.SaveChangesAsync();

        var certificate = Certificate(domain, "cert-production", isStaging: false, npmCertificateId: 42);
        fixture.Db.Certificates.Add(certificate);
        await fixture.Db.SaveChangesAsync();
        await fixture.SaveMetadataAsync(certificate);

        fixture.Npm.ResultFactory = id => new NpmCertificateDeletionResult(
            Succeeded: false,
            Status: "Failed",
            Message: "NPM certificate cleanup failed.",
            CertificateId: id,
            ConsumerProxyHostIds: []);

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            certificate.CertificateId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Failed", result.Status);
        Assert.True(await fixture.Db.Certificates.AnyAsync(x => x.Id == certificate.Id));
        Assert.NotNull(await fixture.Storage.GetMetadataAsync(certificate.CertificateId, CancellationToken.None));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_shared_npm_certificate_reference_is_blocked()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
        await fixture.Db.SaveChangesAsync();

        var first = Certificate(domain, "cert-first", isStaging: false, npmCertificateId: 42);
        var second = Certificate(domain, "cert-second", isStaging: false, npmCertificateId: 42);
        fixture.Db.Certificates.AddRange(first, second);
        await fixture.Db.SaveChangesAsync();
        await fixture.SaveMetadataAsync(first);
        await fixture.SaveMetadataAsync(second);

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            first.CertificateId,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("also referenced", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fixture.Npm.DeletedCertificateIds);
    }

    private static DomainEntity Domain(string baseDomain, bool isMain) =>
        new()
        {
            Id = Guid.NewGuid(),
            BaseDomain = baseDomain,
            DisplayName = baseDomain,
            Purpose = isMain ? "platform-main" : "chat",
            IsMainPlatformDomain = isMain,
            DnsProvider = "desec",
            DnsZone = baseDomain,
            Status = "Active",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static CertificateEntity Certificate(
        DomainEntity domain,
        string certificateId,
        bool isStaging,
        bool isMain = false,
        int? npmCertificateId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            Domain = domain,
            CertificateId = certificateId,
            CommonName = $"*.{domain.BaseDomain}",
            Provider = "desec",
            IsWildcard = true,
            IsStaging = isStaging,
            IsMainPlatformCertificate = isMain,
            IsActive = !isStaging,
            Status = "Succeeded",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(90),
            NpmCertificateId = npmCertificateId,
            ImportedToNpm = npmCertificateId is not null
        };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;

        private Fixture(
            string root,
            MemDbContext db,
            CertificateStorageService storage,
            FakeNpmCertificateDeletionService npm,
            OperatorDomainCertificateDeletionService service)
        {
            _root = root;
            Db = db;
            Storage = storage;
            Npm = npm;
            Service = service;
        }

        public MemDbContext Db { get; }
        public CertificateStorageService Storage { get; }
        public FakeNpmCertificateDeletionService Npm { get; }
        public OperatorDomainCertificateDeletionService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"mem-certificate-delete-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "registry.db")}")
                .Options;

            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var storage = new CertificateStorageService(
                Options.Create(new CertificateStorageOptions
                {
                    RootPath = Path.Combine(root, "certificates")
                }),
                NullLogger<CertificateStorageService>.Instance);

            var npm = new FakeNpmCertificateDeletionService();
            var service = new OperatorDomainCertificateDeletionService(
                db,
                storage,
                npm,
                NullLogger<OperatorDomainCertificateDeletionService>.Instance);

            return new Fixture(root, db, storage, npm, service);
        }

        public async Task SaveMetadataAsync(
            CertificateEntity certificate,
            bool isInUse = false)
        {
            await Storage.SaveMetadataAsync(
                new StoredCertificateMetadata
                {
                    CertificateId = certificate.CertificateId,
                    Domain = certificate.CommonName,
                    Zone = certificate.Domain.BaseDomain,
                    Provider = certificate.Provider,
                    IsWildcard = certificate.IsWildcard,
                    IsStaging = certificate.IsStaging,
                    CreatedAtUtc = new DateTimeOffset(DateTime.SpecifyKind(certificate.CreatedAtUtc, DateTimeKind.Utc)),
                    ExpiresAtUtc = certificate.ExpiresAtUtc.HasValue
                        ? new DateTimeOffset(DateTime.SpecifyKind(certificate.ExpiresAtUtc.Value, DateTimeKind.Utc))
                        : null,
                    FullchainPath = Storage.GetFullchainPath(certificate.CertificateId),
                    PrivateKeyPath = Storage.GetPrivateKeyPath(certificate.CertificateId),
                    Thumbprint = certificate.CertificateId,
                    Status = certificate.Status,
                    IsMainPlatformCertificate = certificate.IsMainPlatformCertificate,
                    IsInUse = isInUse,
                    Purpose = "test"
                },
                CancellationToken.None);
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

    private sealed class FakeNpmCertificateDeletionService : INpmCertificateDeletionService
    {
        public List<int> DeletedCertificateIds { get; } = [];

        public Func<int, NpmCertificateDeletionResult> ResultFactory { get; set; } =
            id => new NpmCertificateDeletionResult(
                Succeeded: true,
                Status: "Deleted",
                Message: "Deleted.",
                CertificateId: id,
                ConsumerProxyHostIds: []);

        public Task<NpmCertificateDeletionResult> DeleteIfUnusedAsync(
            int certificateId,
            CancellationToken cancellationToken)
        {
            DeletedCertificateIds.Add(certificateId);
            return Task.FromResult(ResultFactory(certificateId));
        }
    }
}
