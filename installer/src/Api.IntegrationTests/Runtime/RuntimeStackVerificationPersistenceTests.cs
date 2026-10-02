using System.Text.Json;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Stacks.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackVerificationPersistenceTests
{
    [Fact]
    public async Task Verification_update_refreshes_manifest_and_database_without_recreating_services()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-stack-verification-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var manifests = Path.Combine(root, "control-plane", "runtime-stacks");
            Directory.CreateDirectory(manifests);

            var stackId = Guid.NewGuid();
            var metadata = RuntimeStackIdentity.WithIdentityMetadata(
                new Dictionary<string, string?> { ["unrelated.flag"] = "preserve-me" },
                "family-chat",
                "Dewar Family Chat",
                "Family");
            var initial = new RuntimeStackManifest(
                Source: "control-plane",
                StackId: stackId,
                Slug: "family-chat",
                LastVerifiedStatus: "ready",
                LastVerifiedAtUtc: DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
                Matrix: Service("matrix", "matrix.family.test"),
                Element: Service("element-web", "chat.family.test"),
                Warnings: [],
                Metadata: metadata);

            var path = Path.Combine(manifests, $"{stackId:N}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(initial, JsonOptions()));

            var options = new DbContextOptionsBuilder<MemDbContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "control-plane.db")}")
                .Options;
            await using var db = new MemDbContext(options);
            await db.Database.EnsureCreatedAsync();

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HostAgent:DataRoot"] = root
                })
                .Build();
            var store = new RuntimeStackManifestStore(configuration, db);

            var firstVerifiedAt = DateTimeOffset.Parse("2026-08-25T09:00:00Z");
            var first = await store.UpdateVerificationAsync(
                stackId,
                "passed",
                firstVerifiedAt,
                CancellationToken.None);

            Assert.NotNull(first);
            Assert.Equal("passed", first!.LastVerifiedStatus);
            Assert.Equal(firstVerifiedAt, first.LastVerifiedAtUtc);
            Assert.Equal("Dewar Family Chat", RuntimeStackIdentity.ResolveDisplayName(first));
            Assert.Equal("Family", RuntimeStackIdentity.ResolveCategory(first));
            Assert.Equal("preserve-me", first.Metadata["unrelated.flag"]);

            var serviceIdsBefore = await db.RuntimeServiceInstances
                .Where(x => x.RuntimeStackId == stackId)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArrayAsync();
            Assert.NotEmpty(serviceIdsBefore);

            var secondVerifiedAt = firstVerifiedAt.AddMinutes(10);
            var second = await store.UpdateVerificationAsync(
                stackId,
                "failed",
                secondVerifiedAt,
                CancellationToken.None);

            Assert.NotNull(second);
            Assert.Equal("failed", second!.LastVerifiedStatus);
            Assert.Equal(secondVerifiedAt, second.LastVerifiedAtUtc);

            var serviceIdsAfter = await db.RuntimeServiceInstances
                .Where(x => x.RuntimeStackId == stackId)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArrayAsync();
            Assert.Equal(serviceIdsBefore, serviceIdsAfter);

            var durable = await db.RuntimeStacks
                .Include(x => x.Routes)
                .SingleAsync(x => x.Id == stackId);
            Assert.Equal("failed", durable.LastVerifiedStatus);
            Assert.Equal(secondVerifiedAt.UtcDateTime, durable.LastVerifiedAtUtc!.Value);
            Assert.All(durable.Routes, route => Assert.Equal(secondVerifiedAt.UtcDateTime, route.LastVerifiedAtUtc!.Value));
            Assert.Equal("Dewar Family Chat", durable.DisplayName);
            Assert.Contains("preserve-me", durable.MetadataJson ?? string.Empty);

            var persistedManifest = JsonSerializer.Deserialize<RuntimeStackManifest>(
                await File.ReadAllTextAsync(path),
                JsonOptions());
            Assert.NotNull(persistedManifest);
            Assert.Equal("failed", persistedManifest!.LastVerifiedStatus);
            Assert.Equal(secondVerifiedAt, persistedManifest.LastVerifiedAtUtc);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static RuntimeStackServiceManifest Service(string serviceKey, string host) =>
        new(
            InstanceId: Guid.NewGuid(),
            ServiceKey: serviceKey,
            ContainerId: null,
            ContainerName: null,
            HostPort: 0,
            DataPath: null,
            ServerName: host,
            PublicHost: host,
            PublicBaseUrl: $"https://{host}",
            InternalHost: null,
            InternalBaseUrl: null,
            PublicRouteId: null,
            InternalRouteId: null,
            NpmCertificateId: null,
            RuntimeMetadata: new Dictionary<string, string?>());

    private static JsonSerializerOptions JsonOptions() =>
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
}
