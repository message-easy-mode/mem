using System.Net;
using System.Text;
using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Integrations.Npm.Services;
using Modules.Setup.InstallRuns;
using Modules.Setup.Secrets;

namespace Api.IntegrationTests.Setup;

public sealed class NpmProtectedTokenProviderTests
{
    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01C_NPM_API_tokens_use_verified_protected_credential()
    {
        var installationId = Guid.NewGuid();
        var databasePath = TempDatabasePath();

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();
            db.Installations.Add(Installation(installationId));
            await db.SaveChangesAsync();

            var store = new RecordingSecretStore();
            store.Seed(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminEmail,
                "admin@deltabox.dev");
            store.Seed(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminPassword,
                "protected-npm-password");
            store.Seed(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminVerifiedAtUtc,
                "2026-08-12T10:00:00.0000000Z");

            var credentialService = new NpmAdminCredentialService(db, store);
            var handler = new RecordingTokenHandler();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var provider = new NpmTokenProvider(
                new NpmApiClient(new HttpClient(handler)),
                new ProtectedNpmApiCredentialProvider(credentialService),
                cache,
                NullLogger<NpmTokenProvider>.Instance);

            var first = await provider.GetTokenAsync(
                "http://npm:81/api",
                CancellationToken.None);
            var second = await provider.GetTokenAsync(
                "http://npm:81/api",
                CancellationToken.None);

            Assert.Equal("protected-token", first);
            Assert.Equal(first, second);
            Assert.Equal(1, handler.LoginCalls);
            Assert.Equal("admin@deltabox.dev", handler.Identity);
            Assert.Equal("protected-npm-password", handler.Secret);
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task STARTUP_NPM_BOOTSTRAP_01C_NPM_API_tokens_fail_closed_until_credential_is_verified()
    {
        var installationId = Guid.NewGuid();
        var databasePath = TempDatabasePath();

        try
        {
            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={databasePath}")
                .Options;

            await using var db = new MemDbContext(options);
            await db.Database.MigrateAsync();
            db.Installations.Add(Installation(installationId));
            await db.SaveChangesAsync();

            var store = new RecordingSecretStore();
            store.Seed(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminEmail,
                "admin@deltabox.dev");
            store.Seed(
                installationId,
                InstallationSecretNames.PlatformCategory,
                InstallationSecretNames.NpmAdminPassword,
                "unverified-password");

            var credentialService = new NpmAdminCredentialService(db, store);
            var handler = new RecordingTokenHandler();
            using var cache = new MemoryCache(new MemoryCacheOptions());
            var provider = new NpmTokenProvider(
                new NpmApiClient(new HttpClient(handler)),
                new ProtectedNpmApiCredentialProvider(credentialService),
                cache,
                NullLogger<NpmTokenProvider>.Instance);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.GetTokenAsync(
                    "http://npm:81/api",
                    CancellationToken.None));

            Assert.Contains("has not been verified", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, handler.LoginCalls);
        }
        finally
        {
            DeleteSqliteArtifacts(databasePath);
        }
    }

    private static InstallationEntity Installation(Guid id) => new()
    {
        Id = id,
        Status = InstallationStatuses.Running,
        ConfigJson = null,
        FrozenConfigJson = null,
        LastError = null,
        CreatedAtUtc = DateTime.UtcNow.AddMinutes(-2),
        UpdatedAtUtc = DateTime.UtcNow.AddMinutes(-1),
        StartedAtUtc = DateTime.UtcNow.AddMinutes(-1),
        CompletedAtUtc = null
    };

    private static string TempDatabasePath() =>
        Path.Combine(
            Path.GetTempPath(),
            $"mem-npm-token-{Guid.NewGuid():N}.db");

    private static void DeleteSqliteArtifacts(string databasePath)
    {
        foreach (var path in new[]
                 {
                     databasePath,
                     databasePath + "-shm",
                     databasePath + "-wal"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class RecordingTokenHandler : HttpMessageHandler
    {
        public int LoginCalls { get; private set; }
        public string? Identity { get; private set; }
        public string? Secret { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post ||
                request.RequestUri?.AbsolutePath != "/api/tokens")
            {
                throw new InvalidOperationException(
                    $"Unexpected request {request.Method} {request.RequestUri}");
            }

            LoginCalls++;
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            Identity = json.RootElement.GetProperty("identity").GetString();
            Secret = json.RootElement.GetProperty("secret").GetString();

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"token\":\"protected-token\"}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class RecordingSecretStore : IInstallationSecretStore
    {
        private readonly Dictionary<(Guid InstallationId, string Category, string Key), string> _values = [];

        public void Seed(
            Guid installationId,
            string category,
            string key,
            string value) =>
            _values[(installationId, category, key)] = value;

        public Task SetProtectedAsync(
            Guid installationId,
            string category,
            string key,
            string secret,
            string? description,
            CancellationToken cancellationToken)
        {
            _values[(installationId, category, key)] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> ResolveProtectedAsync(
            Guid installationId,
            string category,
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _values.TryGetValue(
                    (installationId, category, key),
                    out var value)
                    ? value
                    : null);

        public Task<bool> ExistsAsync(
            Guid installationId,
            string category,
            string key,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                _values.ContainsKey(
                    (installationId, category, key)));

        public Task DeleteAsync(
            Guid installationId,
            string category,
            string key,
            CancellationToken cancellationToken)
        {
            _values.Remove((installationId, category, key));
            return Task.CompletedTask;
        }
    }
}
