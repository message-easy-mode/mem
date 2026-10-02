using System.Text.Json;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Stacks.Identity;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackIdentityPersistenceTests
{
    [Fact]
    public async Task Update_identity_rewrites_manifest_and_durable_display_name_without_schema_change()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-stack-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var manifests = Path.Combine(root, "control-plane", "runtime-stacks");
            Directory.CreateDirectory(manifests);

            var stackId = Guid.NewGuid();
            var manifest = new RuntimeStackManifest(
                Source: "control-plane",
                StackId: stackId,
                Slug: "family-chat",
                LastVerifiedStatus: "ready",
                LastVerifiedAtUtc: DateTimeOffset.Parse("2026-08-24T00:00:00Z"),
                Matrix: Service("matrix", "matrix.family.test"),
                Element: Service("element-web", "chat.family.test"),
                Warnings: [],
                Metadata: new Dictionary<string, string?>());

            var path = Path.Combine(manifests, $"{stackId:N}.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, JsonOptions()));

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

            var updated = await store.UpdateIdentityAsync(
                stackId,
                "Dewar Family Chat",
                "Family",
                CancellationToken.None);

            Assert.NotNull(updated);
            Assert.Equal("Dewar Family Chat", RuntimeStackIdentity.ResolveDisplayName(updated!));
            Assert.Equal("Family", RuntimeStackIdentity.ResolveCategory(updated));

            var persistedManifest = JsonSerializer.Deserialize<RuntimeStackManifest>(
                await File.ReadAllTextAsync(path),
                JsonOptions());
            Assert.NotNull(persistedManifest);
            Assert.Equal("Dewar Family Chat", RuntimeStackIdentity.ResolveDisplayName(persistedManifest!));
            Assert.Equal("Family", RuntimeStackIdentity.ResolveCategory(persistedManifest));

            var durable = await db.RuntimeStacks.SingleAsync(x => x.Id == stackId);
            Assert.Equal("Dewar Family Chat", durable.DisplayName);
            Assert.NotNull(durable.MetadataJson);
            Assert.Contains(RuntimeStackIdentity.DisplayNameMetadataKey, durable.MetadataJson!);
            Assert.Contains(RuntimeStackIdentity.CategoryMetadataKey, durable.MetadataJson!);

            var durableMetadata = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                durable.MetadataJson!,
                JsonOptions())!;
            durableMetadata["unrelated.flag"] = "preserve-me";
            durable.MetadataJson = JsonSerializer.Serialize(durableMetadata, JsonOptions());
            await db.SaveChangesAsync();

            var serviceIdsBefore = await db.RuntimeServiceInstances
                .Where(x => x.RuntimeStackId == stackId)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArrayAsync();

            var secondUpdate = await store.UpdateIdentityAsync(
                stackId,
                "Dewar Family Chat Updated",
                "Community",
                CancellationToken.None);

            Assert.NotNull(secondUpdate);
            Assert.Equal("Dewar Family Chat Updated", RuntimeStackIdentity.ResolveDisplayName(secondUpdate!));
            Assert.Equal("Community", RuntimeStackIdentity.ResolveCategory(secondUpdate));

            var serviceIdsAfter = await db.RuntimeServiceInstances
                .Where(x => x.RuntimeStackId == stackId)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArrayAsync();
            Assert.Equal(serviceIdsBefore, serviceIdsAfter);

            var durableAfter = await db.RuntimeStacks.SingleAsync(x => x.Id == stackId);
            Assert.NotNull(durableAfter.MetadataJson);
            var durableMetadataAfter = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                durableAfter.MetadataJson!,
                JsonOptions())!;
            Assert.Equal("preserve-me", durableMetadataAfter["unrelated.flag"]);
            Assert.Equal("Dewar Family Chat Updated", durableMetadataAfter[RuntimeStackIdentity.DisplayNameMetadataKey]);
            Assert.Equal("Community", durableMetadataAfter[RuntimeStackIdentity.CategoryMetadataKey]);
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
    public void Identity_normalization_falls_back_to_slug_and_rejects_oversize_values()
    {
        Assert.Equal("technical-slug", RuntimeStackIdentity.NormalizeDisplayName("  ", "technical-slug"));
        Assert.Null(RuntimeStackIdentity.NormalizeCategory("   "));

        Assert.Throws<InvalidOperationException>(() =>
            RuntimeStackIdentity.NormalizeDisplayName(
                new string('x', RuntimeStackIdentity.MaximumDisplayNameLength + 1),
                "technical-slug"));
        Assert.Throws<InvalidOperationException>(() =>
            RuntimeStackIdentity.NormalizeCategory(
                new string('x', RuntimeStackIdentity.MaximumCategoryLength + 1)));
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
