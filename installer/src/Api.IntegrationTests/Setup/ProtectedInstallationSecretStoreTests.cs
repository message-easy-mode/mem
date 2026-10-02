using System.Security.Cryptography;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Setup.InstallRuns;
using Modules.Setup.Secrets;

namespace Api.IntegrationTests.Setup;

public sealed class ProtectedInstallationSecretStoreTests
{
    [Fact]
    public async Task STARTUP_INSTALL_REL_01D_installation_secrets_are_ciphertext_and_survive_key_ring_restart()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-startup-install-rel-01d-secret-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "control-plane.db");
        var keyRingPath = Path.Combine(root, "keys");
        Directory.CreateDirectory(keyRingPath);

        const string secret = "desec-token-01d-super-secret-value";
        var installationId = Guid.NewGuid();

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using (var db = new MemDbContext(options))
            {
                await db.Database.MigrateAsync();
                db.Installations.Add(NewDraft(installationId));
                await db.SaveChangesAsync();

                using var provider = BuildDataProtectionProvider(keyRingPath);
                var store = new ProtectedInstallationSecretStore(
                    db,
                    provider.GetRequiredService<IDataProtectionProvider>());

                await store.SetProtectedAsync(
                    installationId,
                    InstallationSecretNames.DnsCategory,
                    InstallationSecretNames.DesecProviderToken,
                    secret,
                    "deSEC token",
                    CancellationToken.None);

                var row = await db.InstallationSecrets.AsNoTracking().SingleAsync();
                Assert.NotEqual(secret, row.Value);
                Assert.DoesNotContain(secret, row.Value, StringComparison.Ordinal);
            }

            // Recreate both the EF context and Data Protection service provider to prove
            // the persisted key ring, rather than in-memory protector state, owns recovery.
            await using (var db = new MemDbContext(options))
            {
                using var provider = BuildDataProtectionProvider(keyRingPath);
                var store = new ProtectedInstallationSecretStore(
                    db,
                    provider.GetRequiredService<IDataProtectionProvider>());

                var resolved = await store.ResolveProtectedAsync(
                    installationId,
                    InstallationSecretNames.DnsCategory,
                    InstallationSecretNames.DesecProviderToken,
                    CancellationToken.None);

                Assert.Equal(secret, resolved);
            }
        }
        finally
        {
            DeleteDirectoryBestEffort(root);
        }
    }

    [Fact]
    public async Task STARTUP_INSTALL_REL_01D_protected_secret_is_bound_to_installation_and_purpose()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-startup-install-rel-01d-secret-purpose-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "control-plane.db");
        var keyRingPath = Path.Combine(root, "keys");
        Directory.CreateDirectory(keyRingPath);

        var firstInstallationId = Guid.NewGuid();
        var secondInstallationId = Guid.NewGuid();

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();
            db.Installations.AddRange(
                NewDraft(firstInstallationId),
                NewDraft(secondInstallationId));
            await db.SaveChangesAsync();

            using var provider = BuildDataProtectionProvider(keyRingPath);
            var store = new ProtectedInstallationSecretStore(
                db,
                provider.GetRequiredService<IDataProtectionProvider>());

            await store.SetProtectedAsync(
                firstInstallationId,
                InstallationSecretNames.DnsCategory,
                InstallationSecretNames.DesecProviderToken,
                "token-bound-to-first-installation",
                null,
                CancellationToken.None);

            var protectedValue = await db.InstallationSecrets
                .AsNoTracking()
                .Where(x => x.InstallationId == firstInstallationId)
                .Select(x => x.Value)
                .SingleAsync();

            db.InstallationSecrets.Add(new InstallationSecretEntity
            {
                Id = Guid.NewGuid(),
                InstallationId = secondInstallationId,
                Category = InstallationSecretNames.DnsCategory,
                Key = InstallationSecretNames.DesecProviderToken,
                Value = protectedValue,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.ResolveProtectedAsync(
                    secondInstallationId,
                    InstallationSecretNames.DnsCategory,
                    InstallationSecretNames.DesecProviderToken,
                    CancellationToken.None));

            var wrongPurposeProtector = provider
                .GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(
                    "MEM.InstallationSecret.v1",
                    InstallationSecretNames.PlatformCategory,
                    InstallationSecretNames.DesecProviderToken,
                    firstInstallationId.ToString("D"));

            Assert.Throws<CryptographicException>(() =>
                wrongPurposeProtector.Unprotect(protectedValue));
        }
        finally
        {
            DeleteDirectoryBestEffort(root);
        }
    }

    private static InstallationEntity NewDraft(Guid installationId) =>
        new()
        {
            Id = installationId,
            Status = InstallationStatuses.Draft,
            ConfigJson = null,
            FrozenConfigJson = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

    private static ServiceProvider BuildDataProtectionProvider(string keyRingPath)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("mem-startup-install-rel-01d-tests")
            .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        return services.BuildServiceProvider();
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Test cleanup only.
        }
    }
}
