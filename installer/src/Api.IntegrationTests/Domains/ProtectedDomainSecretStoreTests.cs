using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Shared.Domains.Renewal;

namespace Api.IntegrationTests.Domains;

public sealed class ProtectedDomainSecretStoreTests
{
    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01A_domain_secret_round_trips_as_ciphertext_and_rotates_in_place()
    {
        await using var fixture = await Fixture.CreateAsync();
        var domain = await fixture.AddDomainAsync("renewal.example");
        const string firstToken = "desec-domain-renewal-secret-one";
        const string secondToken = "desec-domain-renewal-secret-two";

        await fixture.Store.SetProtectedAsync(
            domain.Id,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            firstToken,
            "deSEC renewal token",
            CancellationToken.None);

        var firstRow = await fixture.Db.DomainSecrets.AsNoTracking().SingleAsync();
        Assert.NotEqual(firstToken, firstRow.ProtectedValue);
        Assert.DoesNotContain(firstToken, firstRow.ProtectedValue, StringComparison.Ordinal);
        Assert.Equal(
            firstToken,
            await fixture.Store.ResolveProtectedAsync(
                domain.Id,
                DomainRenewalSecretNames.DnsProviderCategory,
                DomainRenewalSecretNames.DesecProviderToken,
                CancellationToken.None));

        var originalId = firstRow.Id;
        await fixture.Store.SetProtectedAsync(
            domain.Id,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            secondToken,
            "rotated deSEC renewal token",
            CancellationToken.None);

        var rotated = await fixture.Db.DomainSecrets.AsNoTracking().SingleAsync();
        Assert.Equal(originalId, rotated.Id);
        Assert.NotEqual(firstRow.ProtectedValue, rotated.ProtectedValue);
        Assert.DoesNotContain(secondToken, rotated.ProtectedValue, StringComparison.Ordinal);
        Assert.Equal(
            secondToken,
            await fixture.Store.ResolveProtectedAsync(
                domain.Id,
                DomainRenewalSecretNames.DnsProviderCategory,
                DomainRenewalSecretNames.DesecProviderToken,
                CancellationToken.None));
    }

    [Fact]
    public async Task DOMAINS_CERTIFICATE_RENEWAL_01A_domain_and_purpose_binding_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.AddDomainAsync("alpha.example");
        var second = await fixture.AddDomainAsync("bravo.example");
        const string token = "desec-purpose-bound-secret";

        await fixture.Store.SetProtectedAsync(
            first.Id,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            token,
            null,
            CancellationToken.None);

        var original = await fixture.Db.DomainSecrets.AsNoTracking().SingleAsync();

        fixture.Db.DomainSecrets.Add(new DomainSecretEntity
        {
            Id = Guid.NewGuid(),
            DomainId = second.Id,
            Category = original.Category,
            Key = original.Key,
            ProtectedValue = original.ProtectedValue,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var wrongDomain = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.ResolveProtectedAsync(
                second.Id,
                DomainRenewalSecretNames.DnsProviderCategory,
                DomainRenewalSecretNames.DesecProviderToken,
                CancellationToken.None));
        Assert.DoesNotContain(token, wrongDomain.Message, StringComparison.Ordinal);

        const string wrongKey = "desec.provider-token.wrong-purpose";
        fixture.Db.DomainSecrets.Add(new DomainSecretEntity
        {
            Id = Guid.NewGuid(),
            DomainId = first.Id,
            Category = original.Category,
            Key = wrongKey,
            ProtectedValue = original.ProtectedValue,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var wrongPurpose = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.ResolveProtectedAsync(
                first.Id,
                DomainRenewalSecretNames.DnsProviderCategory,
                wrongKey,
                CancellationToken.None));
        Assert.DoesNotContain(token, wrongPurpose.Message, StringComparison.Ordinal);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root;
        private readonly ServiceProvider _services;

        private Fixture(string root, ServiceProvider services, MemDbContext db, ProtectedDomainSecretStore store)
        {
            _root = root;
            _services = services;
            Db = db;
            Store = store;
        }

        public MemDbContext Db { get; }
        public ProtectedDomainSecretStore Store { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"mem-domain-secret-{Guid.NewGuid():N}");
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
                .SetApplicationName("mem-domain-secret-tests")
                .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
            var provider = services.BuildServiceProvider();
            var store = new ProtectedDomainSecretStore(
                db,
                provider.GetRequiredService<IDataProtectionProvider>());

            return new Fixture(root, provider, db, store);
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
}
