using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Modules.Shared.Domains;
using Modules.Shared.Domains.Certificates;

namespace Api.IntegrationTests.Domains;

public sealed class DomainRegistryCertificateSelectionCoherenceTests
{
    [Fact]
    public async Task Storage_sync_reconciles_a_stale_domain_pointer_to_the_unique_main_platform_certificate()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-cert-coherence-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "registry.db");
        Directory.CreateDirectory(root);

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
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

            var now = new DateTimeOffset(2026, 8, 20, 9, 30, 0, TimeSpan.Zero);
            var domain = new DomainEntity
            {
                Id = Guid.NewGuid(),
                BaseDomain = "deltabox.dev",
                DisplayName = "deltabox.dev",
                Purpose = "platform-main",
                IsMainPlatformDomain = true,
                DnsProvider = "desec",
                DnsZone = "deltabox.dev",
                Status = "Active",
                CreatedAtUtc = now.UtcDateTime,
                UpdatedAtUtc = now.UtcDateTime
            };
            db.Domains.Add(domain);
            await db.SaveChangesAsync();

            var staging = new CertificateEntity
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                CertificateId = "cert-staging",
                CommonName = "*.deltabox.dev",
                Provider = "desec",
                IsWildcard = true,
                IsStaging = true,
                IsMainPlatformCertificate = false,
                IsActive = true,
                Status = "Succeeded",
                CreatedAtUtc = now.AddMinutes(-10).UtcDateTime,
                ExpiresAtUtc = now.AddDays(90).UtcDateTime,
                ImportedToNpm = true
            };
            var production = new CertificateEntity
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                CertificateId = "cert-production",
                CommonName = "*.deltabox.dev",
                Provider = "desec",
                IsWildcard = true,
                IsStaging = false,
                IsMainPlatformCertificate = true,
                IsActive = true,
                Status = "Succeeded",
                CreatedAtUtc = now.UtcDateTime,
                ExpiresAtUtc = now.AddDays(90).UtcDateTime,
                ImportedToNpm = true
            };
            db.Certificates.AddRange(staging, production);
            await db.SaveChangesAsync();

            domain.ActiveCertificateId = staging.Id;
            await db.SaveChangesAsync();

            await storage.SaveMetadataAsync(
                Metadata(staging, now.AddMinutes(-10)),
                CancellationToken.None);
            await storage.SaveMetadataAsync(
                Metadata(production, now),
                CancellationToken.None);

            await registry.SyncStorageAsync(CancellationToken.None);

            db.ChangeTracker.Clear();
            var reconciled = await db.Domains
                .Include(x => x.Certificates)
                .SingleAsync(x => x.Id == domain.Id);

            Assert.True(reconciled.IsMainPlatformDomain);
            Assert.Equal(production.Id, reconciled.ActiveCertificateId);
            Assert.Equal("platform-main", reconciled.Purpose);
            Assert.Single(reconciled.Certificates.Where(x => x.IsMainPlatformCertificate));
            Assert.True(reconciled.Certificates.Single(x => x.Id == production.Id).IsMainPlatformCertificate);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }


    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_C_staging_registration_never_becomes_domain_active()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-cert-staging-selection-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "registry.db");
        Directory.CreateDirectory(root);

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
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

            var now = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
            var domain = new DomainEntity
            {
                Id = Guid.NewGuid(),
                BaseDomain = "matrixeasyhost.com",
                DisplayName = "matrixeasyhost.com",
                Purpose = "chat",
                IsMainPlatformDomain = false,
                DnsProvider = "desec",
                DnsZone = "matrixeasyhost.com",
                Status = "Active",
                CreatedAtUtc = now.UtcDateTime,
                UpdatedAtUtc = now.UtcDateTime
            };
            db.Domains.Add(domain);
            await db.SaveChangesAsync();

            var registered = await registry.RegisterCertificateAsync(
                new StoredCertificateMetadata
                {
                    CertificateId = "cert-staging-only",
                    Domain = "*.matrixeasyhost.com",
                    Zone = "matrixeasyhost.com",
                    Provider = "desec",
                    IsWildcard = true,
                    IsStaging = true,
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddDays(90),
                    FullchainPath = "/test/cert-staging-only/fullchain.pem",
                    PrivateKeyPath = "/test/cert-staging-only/privkey.pem",
                    Thumbprint = "staging-thumbprint",
                    Status = "Valid",
                    IsMainPlatformCertificate = false,
                    IsInUse = false,
                    Purpose = "chat"
                },
                CancellationToken.None);

            var setMain = await registry.SetMainPlatformCertificateAsync(
                "cert-staging-only",
                CancellationToken.None);

            db.ChangeTracker.Clear();
            var persistedDomain = await db.Domains.SingleAsync(item => item.Id == domain.Id);
            var staging = await db.Certificates.SingleAsync(item => item.CertificateId == "cert-staging-only");

            Assert.Null(setMain);
            Assert.Null(persistedDomain.ActiveCertificateId);
            Assert.False(persistedDomain.IsMainPlatformDomain);
            Assert.False(staging.IsMainPlatformCertificate);
            Assert.False(registered.IsInUse);
            Assert.False(registered.IsMainPlatformCertificate);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DOMAINS_WORKSPACE_01G_01F_C_storage_sync_repairs_staging_selection_to_valid_production()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-cert-staging-repair-{Guid.NewGuid():N}");
        var databasePath = Path.Combine(root, "registry.db");
        Directory.CreateDirectory(root);

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
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

            var now = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);
            var domain = new DomainEntity
            {
                Id = Guid.NewGuid(),
                BaseDomain = "matrixeasyhost.com",
                DisplayName = "matrixeasyhost.com",
                Purpose = "chat",
                IsMainPlatformDomain = false,
                DnsProvider = "desec",
                DnsZone = "matrixeasyhost.com",
                Status = "Active",
                CreatedAtUtc = now.UtcDateTime,
                UpdatedAtUtc = now.UtcDateTime
            };
            db.Domains.Add(domain);
            await db.SaveChangesAsync();

            var staging = new CertificateEntity
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                CertificateId = "cert-staging",
                CommonName = "*.matrixeasyhost.com",
                Provider = "desec",
                IsWildcard = true,
                IsStaging = true,
                IsActive = true,
                Status = "Valid",
                CreatedAtUtc = now.AddMinutes(-5).UtcDateTime,
                ExpiresAtUtc = now.AddDays(90).UtcDateTime
            };
            var production = new CertificateEntity
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                CertificateId = "cert-production",
                CommonName = "*.matrixeasyhost.com",
                Provider = "desec",
                IsWildcard = true,
                IsStaging = false,
                IsActive = true,
                Status = "Valid",
                CreatedAtUtc = now.UtcDateTime,
                ExpiresAtUtc = now.AddDays(90).UtcDateTime
            };
            db.Certificates.AddRange(staging, production);
            await db.SaveChangesAsync();

            domain.ActiveCertificateId = staging.Id;
            await db.SaveChangesAsync();

            await storage.SaveMetadataAsync(
                Metadata(staging, now.AddMinutes(-5)) with
                {
                    Zone = "matrixeasyhost.com",
                    Purpose = "chat",
                    IsMainPlatformCertificate = false,
                    IsInUse = true
                },
                CancellationToken.None);
            await storage.SaveMetadataAsync(
                Metadata(production, now) with
                {
                    Zone = "matrixeasyhost.com",
                    Purpose = "chat",
                    IsMainPlatformCertificate = false,
                    IsInUse = false
                },
                CancellationToken.None);

            await registry.SyncStorageAsync(CancellationToken.None);

            db.ChangeTracker.Clear();
            var persistedDomain = await db.Domains.SingleAsync(item => item.Id == domain.Id);
            Assert.Equal(production.Id, persistedDomain.ActiveCertificateId);

            var persistedMetadata = await storage.ListAsync(CancellationToken.None);
            Assert.False(persistedMetadata.Single(item => item.CertificateId == staging.CertificateId).IsInUse);
            Assert.True(persistedMetadata.Single(item => item.CertificateId == production.CertificateId).IsInUse);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static StoredCertificateMetadata Metadata(
        CertificateEntity certificate,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            CertificateId = certificate.CertificateId,
            Domain = certificate.CommonName,
            Zone = "deltabox.dev",
            Provider = certificate.Provider,
            IsWildcard = certificate.IsWildcard,
            IsStaging = certificate.IsStaging,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = certificate.ExpiresAtUtc.HasValue
                ? new DateTimeOffset(DateTime.SpecifyKind(certificate.ExpiresAtUtc.Value, DateTimeKind.Utc))
                : null,
            FullchainPath = $"/test/{certificate.CertificateId}/fullchain.pem",
            PrivateKeyPath = $"/test/{certificate.CertificateId}/privkey.pem",
            Thumbprint = certificate.CertificateId,
            Status = certificate.Status,
            IsMainPlatformCertificate = certificate.IsMainPlatformCertificate,
            IsInUse = certificate.IsActive,
            Purpose = "platform-main"
        };
}
