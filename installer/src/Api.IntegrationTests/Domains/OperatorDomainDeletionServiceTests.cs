using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Operator.Domains;

namespace Api.IntegrationTests.Domains;

public sealed class OperatorDomainDeletionServiceTests
{
    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_main_domain_delete_remains_blocked()
    {
        await using var fixture = await Fixture.CreateAsync();

        var domain = Domain("deltabox.dev", isMain: true);
        fixture.Db.Domains.Add(domain);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            force: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("main platform domain", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await fixture.Db.Domains.AnyAsync(x => x.Id == domain.Id));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_domain_delete_requires_certificate_cleanup_first()
    {
        await using var fixture = await Fixture.CreateAsync();

        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
        await fixture.Db.SaveChangesAsync();

        fixture.Db.Certificates.Add(Certificate(domain, "cert-matrix-production", status: "Succeeded"));
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            force: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("certificates first", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await fixture.Db.Domains.AnyAsync(x => x.Id == domain.Id));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_live_stack_dependency_blocks_domain_delete_after_certificates_are_gone()
    {
        await using var fixture = await Fixture.CreateAsync();

        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
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

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            force: false,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("tester", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await fixture.Db.Domains.AnyAsync(x => x.Id == domain.Id));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_unused_certificate_free_domain_delete_cascades_renewal_state()
    {
        await using var fixture = await Fixture.CreateAsync();

        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
        fixture.Db.DomainSecrets.Add(new DomainSecretEntity
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            Category = "certificate-renewal",
            Key = "desec-token",
            ProtectedValue = "protected-value",
            Description = "test",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        fixture.Db.DomainCertificateRenewalPolicies.Add(new DomainCertificateRenewalPolicyEntity
        {
            DomainId = domain.Id,
            AutoRenewEnabled = true,
            AcmeEmail = "ops@matrixeasyhost.com",
            RenewalWindowDays = 30,
            RetryIntervalHours = 24,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            force: false,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal("Deleted", result.Status);

        fixture.Db.ChangeTracker.Clear();
        Assert.False(await fixture.Db.Domains.AnyAsync(x => x.Id == domain.Id));
        Assert.False(await fixture.Db.DomainSecrets.AnyAsync(x => x.DomainId == domain.Id));
        Assert.False(await fixture.Db.DomainCertificateRenewalPolicies.AnyAsync(x => x.DomainId == domain.Id));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_retired_stack_history_does_not_block_domain_delete()
    {
        await using var fixture = await Fixture.CreateAsync();

        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
        fixture.Db.RuntimeStacks.Add(new RuntimeStackEntity
        {
            Id = Guid.NewGuid(),
            Slug = "retired-tester",
            DisplayName = "Retired tester",
            Status = "destroyed",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            BaseDomain = domain.BaseDomain,
            DomainId = domain.Id,
            MatrixInstanceId = Guid.NewGuid()
        });
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            force: false,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.False(await fixture.Db.Domains.AnyAsync(x => x.Id == domain.Id));

        fixture.Db.ChangeTracker.Clear();
        var retired = await fixture.Db.RuntimeStacks.SingleAsync(x => x.Slug == "retired-tester");
        Assert.Null(retired.DomainId);
        Assert.Equal(domain.BaseDomain, retired.BaseDomain);
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_soft_deleted_compatibility_rows_do_not_strand_domain_deletion()
    {
        await using var fixture = await Fixture.CreateAsync();

        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
        await fixture.Db.SaveChangesAsync();

        var retired = Certificate(domain, "cert-retired", status: "Deleted");
        fixture.Db.Certificates.Add(retired);
        await fixture.Db.SaveChangesAsync();
        domain.ActiveCertificateId = retired.Id;
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            force: false,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        fixture.Db.ChangeTracker.Clear();
        Assert.False(await fixture.Db.Certificates.AnyAsync(x => x.Id == retired.Id));
        Assert.False(await fixture.Db.Domains.AnyAsync(x => x.Id == domain.Id));
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_force_delete_is_refused()
    {
        await using var fixture = await Fixture.CreateAsync();

        var domain = Domain("matrixeasyhost.com", isMain: false);
        fixture.Db.Domains.Add(domain);
        await fixture.Db.SaveChangesAsync();

        var result = await fixture.Service.DeleteAsync(
            domain.Id,
            force: true,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Blocked", result.Status);
        Assert.Contains("Force deletion", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(await fixture.Db.Domains.AnyAsync(x => x.Id == domain.Id));
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
        string status) =>
        new()
        {
            Id = Guid.NewGuid(),
            DomainId = domain.Id,
            Domain = domain,
            CertificateId = certificateId,
            CommonName = $"*.{domain.BaseDomain}",
            Provider = "desec",
            IsWildcard = true,
            IsStaging = false,
            IsMainPlatformCertificate = false,
            IsActive = true,
            Status = status,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(90)
        };

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;

        private Fixture(string root, MemDbContext db, OperatorDomainDeletionService service)
        {
            _root = root;
            Db = db;
            Service = service;
        }

        public MemDbContext Db { get; }
        public OperatorDomainDeletionService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"mem-domain-delete-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "registry.db")}")
                .Options;

            var db = new MemDbContext(options);
            await db.Database.MigrateAsync();

            var service = new OperatorDomainDeletionService(
                db,
                NullLogger<OperatorDomainDeletionService>.Instance);

            return new Fixture(root, db, service);
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
