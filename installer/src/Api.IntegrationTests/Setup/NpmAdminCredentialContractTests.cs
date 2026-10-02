using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Setup.InstallRuns;
using Modules.Setup.Secrets;

namespace Api.IntegrationTests.Setup;

public sealed class NpmAdminCredentialContractTests
{
    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01A_npm_admin_credential_is_protected_restart_safe_and_browser_safe()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-startup-npm-bootstrap-01a-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "control-plane.db");
        var keyRingPath = Path.Combine(root, "keys");
        Directory.CreateDirectory(keyRingPath);

        const string email = "npm-owner@deltabox.dev";
        const string password = "npm-01a-protected-password-value";
        const string configJson = "{\"publicAccess\":{\"zone\":\"deltabox.dev\"}}";
        const string frozenConfigJson = "{\"frozen\":true}";
        var installationId = Guid.NewGuid();

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using (var db = new MemDbContext(options))
            {
                await db.Database.MigrateAsync();
                db.Installations.Add(new InstallationEntity
                {
                    Id = installationId,
                    Status = InstallationStatuses.Failed,
                    ConfigJson = configJson,
                    FrozenConfigJson = frozenConfigJson,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                });
                await db.SaveChangesAsync();

                using var provider = BuildDataProtectionProvider(keyRingPath);
                var store = new ProtectedInstallationSecretStore(
                    db,
                    provider.GetRequiredService<IDataProtectionProvider>());
                var service = new NpmAdminCredentialService(db, store);

                var stored = await service.StoreCurrentAsync(
                    new NpmAdminCredentialRequest(email, password),
                    CancellationToken.None);

                Assert.True(stored.CredentialStored);
                Assert.Equal("Stored", stored.Status);
                Assert.Equal(email, stored.AdministratorEmail);
                Assert.Null(stored.VerifiedAtUtc);

                var browserJson = JsonSerializer.Serialize(stored);
                Assert.DoesNotContain(password, browserJson, StringComparison.Ordinal);
                Assert.DoesNotContain("Password", browserJson, StringComparison.OrdinalIgnoreCase);

                var rows = await db.InstallationSecrets
                    .AsNoTracking()
                    .Where(x => x.InstallationId == installationId)
                    .ToListAsync();

                Assert.Equal(2, rows.Count);
                Assert.All(rows, row => Assert.DoesNotContain(password, row.Value, StringComparison.Ordinal));
                Assert.All(rows, row => Assert.DoesNotContain(email, row.Value, StringComparison.OrdinalIgnoreCase));
                Assert.Contains(rows, row => row.Key == InstallationSecretNames.NpmAdminEmail);
                Assert.Contains(rows, row => row.Key == InstallationSecretNames.NpmAdminPassword);

                var installation = await db.Installations.AsNoTracking().SingleAsync();
                Assert.Equal(configJson, installation.ConfigJson);
                Assert.Equal(frozenConfigJson, installation.FrozenConfigJson);
                Assert.DoesNotContain(password, installation.ConfigJson!, StringComparison.Ordinal);
                Assert.DoesNotContain(password, installation.FrozenConfigJson!, StringComparison.Ordinal);
            }

            // Recreate EF and Data Protection to prove the credential is recovered
            // through the persisted Control Plane key ring rather than in-memory state.
            await using (var db = new MemDbContext(options))
            {
                using var provider = BuildDataProtectionProvider(keyRingPath);
                var store = new ProtectedInstallationSecretStore(
                    db,
                    provider.GetRequiredService<IDataProtectionProvider>());
                var service = new NpmAdminCredentialService(db, store);

                var resolved = await service.ResolveAsync(installationId, CancellationToken.None);
                Assert.NotNull(resolved);
                Assert.Equal(email, resolved!.Email);
                Assert.Equal(password, resolved.Password);
                Assert.Null(resolved.VerifiedAtUtc);

                var verifiedAt = new DateTime(2026, 8, 12, 8, 30, 0, DateTimeKind.Utc);
                await service.MarkVerifiedAsync(installationId, verifiedAt, CancellationToken.None);

                var projection = await service.GetForInstallationAsync(installationId, CancellationToken.None);
                Assert.True(projection.CredentialStored);
                Assert.Equal("Verified", projection.Status);
                Assert.Equal(verifiedAt, projection.VerifiedAtUtc);
            }
        }
        finally
        {
            DeleteDirectoryBestEffort(root);
        }
    }

    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01A_replacing_npm_credential_clears_prior_verification_without_touching_frozen_plan()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"mem-startup-npm-bootstrap-01a-replace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "control-plane.db");
        var keyRingPath = Path.Combine(root, "keys");
        Directory.CreateDirectory(keyRingPath);

        var installationId = Guid.NewGuid();
        const string frozen = "{\"review\":\"frozen\"}";

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;
            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();
            db.Installations.Add(new InstallationEntity
            {
                Id = installationId,
                Status = InstallationStatuses.Ready,
                ConfigJson = frozen,
                FrozenConfigJson = frozen,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            using var provider = BuildDataProtectionProvider(keyRingPath);
            var store = new ProtectedInstallationSecretStore(
                db,
                provider.GetRequiredService<IDataProtectionProvider>());
            var service = new NpmAdminCredentialService(db, store);

            await service.StoreCurrentAsync(
                new NpmAdminCredentialRequest("first@deltabox.dev", "first-password"),
                CancellationToken.None);
            await service.MarkVerifiedAsync(
                installationId,
                DateTime.UtcNow,
                CancellationToken.None);

            var verified = await service.GetForInstallationAsync(installationId, CancellationToken.None);
            Assert.NotNull(verified.VerifiedAtUtc);

            var replacement = await service.StoreCurrentAsync(
                new NpmAdminCredentialRequest("second@deltabox.dev", "second-password"),
                CancellationToken.None);

            Assert.True(replacement.CredentialStored);
            Assert.Equal("second@deltabox.dev", replacement.AdministratorEmail);
            Assert.Null(replacement.VerifiedAtUtc);

            var installation = await db.Installations.AsNoTracking().SingleAsync();
            Assert.Equal(InstallationStatuses.Ready, installation.Status);
            Assert.Equal(frozen, installation.ConfigJson);
            Assert.Equal(frozen, installation.FrozenConfigJson);
        }
        finally
        {
            DeleteDirectoryBestEffort(root);
        }
    }

    private static ServiceProvider BuildDataProtectionProvider(string keyRingPath)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("mem-startup-npm-bootstrap-01a-tests")
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
