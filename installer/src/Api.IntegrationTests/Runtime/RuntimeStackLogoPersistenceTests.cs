using System.Text.Json;
using HostAgent.Endpoints;
using HostAgent.Runtime.Manifests;
using HostAgent.Runtime.Stacks.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Api.IntegrationTests.Runtime;

public sealed class RuntimeStackLogoPersistenceTests
{
    private const string FirstLogoBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAZUlEQVR42u3QQREAAAQAMDGEFlITcjh7rMAiq+ezECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAgQIECBAgAABAu5boJcyWUwYGNkAAAAASUVORK5CYII=";

    private const string ReplacementLogoBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAGAAAABACAYAAADlNHIOAAAAc0lEQVR42u3RMQEAAAjDMNTtRjauQAZPjhpoajqrv8oEAAAEAIAAABAAAAIAQAAACAAAAQAgAAAEAIAAABAAAAIAQAAACAAAAQAgAAAEAIAAABAAAAIAQAAACAAAAQAgAAAEAIAAABAAAAIAQAAACAAAZQ8VELtJCvCxoAAAAABJRU5ErkJggg==";

    private const string OversizeDimensionsLogoBase64 =
        "iVBORw0KGgoAAAANSUhEUgAACAAAAABACAYAAACOEJc7AAACgklEQVR42u3aOQ0AAAgAMR7/msEGJB1q4OaLrB4AAAAAAAAA4LcQAQAAAAAAAAAMAAAAAAAAAACAAQAAAAAAAAAAMAAAAAAAAAAAAAYAAAAAAAAAADAAAAAAAAAAAAAGAAAAAAAAAADAAAAAAAAAAAAAGAAAAAAAAAAAwAAAAAAAAAAAABgAAAAAAAAAAAADAAAAAAAAAABgAAAAAAAAAAAAAwAAAAAAAAAAYAAAAAAAAAAAAAwAAAAAAAAAAIABAAAAAAAAAAAMAAAAAAAAAACAAQAAAAAAAAAAMAAAAAAAAAAAAAYAAAAAAAAAADAAAAAAAAAAAAAGAAAAAAAAAADAAAAAAAAAAAAAGAAAAAAAAAAAwAAAAAAAAAAAABgAAAAAAAAAAAADAAAAAAAAAABgAAAAAAAAAAAAAwAAAAAAAAAAYAAAAAAAAAAAAAwAAAAAAAAAAIABAAAAAAAAAAAMAAAAAAAAAACAAQAAAAAAAAAAMAAAAAAAAAAAAAYAAAAAAAAAADAAAAAAAAAAAAAGAAAAAAAAAADAAAAAAAAAAAAAGAAAAAAAAAAAwAAgBAAAAAAAAAAYAAAAAAAAAAAAAwAAAAAAAAAAYAAAAAAAAAAAAAwAAAAAAAAAAGAAAAAAAAAAAAAMAAAAAAAAAACAAQAAAAAAAAAAMAAAAAAAAAAAgAEAAAAAAAAAADAAAAAAAAAAAAAGAAAAAAAAAADAAAAAAAAAAAAABgAAAAAAAAAAwAAAAAAAAAAAABgAAAAAAAAAAAADAAAAAAAAAAAYAAAAAAAAAAAAAwAAAAAAAAAAYAAAAAAAAAAAAAwAAAAAAAAAAGAAAAAAAAAAAABuW9KBHpeT5aPbAAAAAElFTkSuQmCC";

    [Fact]
    public async Task Upload_replace_and_remove_logo_preserves_identity_and_uses_bounded_asset_storage()
    {
        var fixture = await CreateFixtureAsync();
        var db = fixture.Database;

        try
        {
            var manifest = await fixture.Store.FindAsync("family-chat", CancellationToken.None);
            Assert.NotNull(manifest);

            var uploaded = await fixture.Logos.UploadAsync(
                manifest!,
                FormFile(Convert.FromBase64String(FirstLogoBase64), "../../escape.png"),
                CancellationToken.None);

            Assert.Equal("Dewar Family Chat", RuntimeStackIdentity.ResolveDisplayName(uploaded));
            Assert.Equal("Family", RuntimeStackIdentity.ResolveCategory(uploaded));
            var firstMetadata = RuntimeStackIdentity.ResolveLogoMetadata(uploaded);
            Assert.NotNull(firstMetadata);
            Assert.Equal(64, firstMetadata!.Width);
            Assert.Equal(64, firstMetadata.Height);
            var firstLogoUrl = RuntimeStackIdentity.ResolveLogoUrl(uploaded);
            Assert.NotNull(firstLogoUrl);
            Assert.Contains(firstMetadata.Sha256, firstLogoUrl);
            Assert.Equal(
                RuntimeStackIdentity.ResolveLogoUrl(uploaded),
                RuntimeStackSummaryResponse.FromManifest(uploaded).LogoUrl);
            Assert.Equal(
                RuntimeStackIdentity.ResolveLogoUrl(uploaded),
                RuntimeStackInspectResponse.FromManifest(uploaded).LogoUrl);

            var firstResolved = fixture.Logos.Resolve(uploaded);
            Assert.NotNull(firstResolved);
            Assert.Equal(RuntimeStackLogoService.ContentType, firstResolved!.ContentType);
            Assert.True(File.Exists(firstResolved.Path));
            Assert.Contains(
                Path.Combine("control-plane", "runtime-stack-assets", fixture.StackId.ToString("N")),
                firstResolved.Path);

            var durable = await db.RuntimeStacks
                .Include(x => x.ServiceInstances)
                .Include(x => x.Routes)
                .SingleAsync(x => x.Id == fixture.StackId);
            Assert.NotNull(durable.MetadataJson);
            Assert.Contains(RuntimeStackIdentity.LogoSha256MetadataKey, durable.MetadataJson!);

            var reconstructed = RuntimeStackManifestDatabaseReconstructor.Build(durable);
            Assert.Equal(firstMetadata, RuntimeStackIdentity.ResolveLogoMetadata(reconstructed));

            var durableMetadata = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                durable.MetadataJson!,
                JsonOptions())!;
            durableMetadata["unrelated.flag"] = "preserve-me";
            durable.MetadataJson = JsonSerializer.Serialize(durableMetadata, JsonOptions());
            await db.SaveChangesAsync();

            var serviceIdsBefore = await db.RuntimeServiceInstances
                .Where(x => x.RuntimeStackId == fixture.StackId)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArrayAsync();

            var renamed = await fixture.Store.UpdateIdentityAsync(
                fixture.StackId,
                "Dewar Family Chat Updated",
                "Community",
                CancellationToken.None);
            Assert.NotNull(renamed);
            Assert.Equal(firstMetadata, RuntimeStackIdentity.ResolveLogoMetadata(renamed!));

            var replaced = await fixture.Logos.UploadAsync(
                renamed!,
                FormFile(Convert.FromBase64String(ReplacementLogoBase64), "replacement.png"),
                CancellationToken.None);

            var replacementMetadata = RuntimeStackIdentity.ResolveLogoMetadata(replaced);
            Assert.NotNull(replacementMetadata);
            Assert.Equal(96, replacementMetadata!.Width);
            Assert.Equal(64, replacementMetadata.Height);
            Assert.NotEqual(firstMetadata.Sha256, replacementMetadata.Sha256);
            Assert.False(File.Exists(firstResolved.Path));

            var replacementResolved = fixture.Logos.Resolve(replaced);
            Assert.NotNull(replacementResolved);
            Assert.True(File.Exists(replacementResolved!.Path));

            var removed = await fixture.Logos.RemoveAsync(replaced, CancellationToken.None);
            Assert.Null(RuntimeStackIdentity.ResolveLogoMetadata(removed));
            Assert.Null(RuntimeStackIdentity.ResolveLogoUrl(removed));
            Assert.Null(fixture.Logos.Resolve(removed));
            Assert.False(Directory.Exists(Path.GetDirectoryName(replacementResolved.Path)!));
            Assert.Equal("Dewar Family Chat Updated", RuntimeStackIdentity.ResolveDisplayName(removed));
            Assert.Equal("Community", RuntimeStackIdentity.ResolveCategory(removed));

            var serviceIdsAfter = await db.RuntimeServiceInstances
                .Where(x => x.RuntimeStackId == fixture.StackId)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToArrayAsync();
            Assert.Equal(serviceIdsBefore, serviceIdsAfter);

            var durableAfter = await db.RuntimeStacks.SingleAsync(x => x.Id == fixture.StackId);
            Assert.NotNull(durableAfter.MetadataJson);
            var durableMetadataAfter = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                durableAfter.MetadataJson!,
                JsonOptions())!;
            Assert.Equal("preserve-me", durableMetadataAfter["unrelated.flag"]);
            Assert.DoesNotContain(RuntimeStackIdentity.LogoSha256MetadataKey, durableMetadataAfter.Keys);
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Logo_validation_accepts_only_bounded_png_payloads()
    {
        var fixture = await CreateFixtureAsync();

        try
        {
            var manifest = await fixture.Store.FindAsync("family-chat", CancellationToken.None);
            Assert.NotNull(manifest);

            var notPng = await Assert.ThrowsAsync<RuntimeStackLogoValidationException>(() =>
                fixture.Logos.UploadAsync(
                    manifest!,
                    FormFile([0xFF, 0xD8, 0xFF, 0xD9], "photo.jpg"),
                    CancellationToken.None));
            Assert.Equal("stack_logo_not_png", notPng.Code);

            var tooWide = await Assert.ThrowsAsync<RuntimeStackLogoValidationException>(() =>
                fixture.Logos.UploadAsync(
                    manifest!,
                    FormFile(Convert.FromBase64String(OversizeDimensionsLogoBase64), "wide.png"),
                    CancellationToken.None));
            Assert.Equal("stack_logo_dimensions_too_large", tooWide.Code);

            var tooLarge = await Assert.ThrowsAsync<RuntimeStackLogoValidationException>(() =>
                fixture.Logos.UploadAsync(
                    manifest!,
                    FormFile(new byte[RuntimeStackLogoService.MaximumBytes + 1], "large.png"),
                    CancellationToken.None));
            Assert.Equal("stack_logo_too_large", tooLarge.Code);

            Assert.Null(RuntimeStackIdentity.ResolveLogoMetadata(manifest!));
            var assetsRoot = Path.Combine(
                fixture.Root,
                "control-plane",
                "runtime-stack-assets",
                fixture.StackId.ToString("N"));
            Assert.False(Directory.Exists(assetsRoot));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Restore_from_backup_requires_exact_metadata_and_rehydrates_managed_logo()
    {
        var fixture = await CreateFixtureAsync();

        try
        {
            var manifest = await fixture.Store.FindAsync("family-chat", CancellationToken.None);
            Assert.NotNull(manifest);

            var uploaded = await fixture.Logos.UploadAsync(
                manifest!,
                FormFile(Convert.FromBase64String(FirstLogoBase64), "family.png"),
                CancellationToken.None);
            var metadata = RuntimeStackIdentity.ResolveLogoMetadata(uploaded);
            Assert.NotNull(metadata);
            var resolved = fixture.Logos.Resolve(uploaded);
            Assert.NotNull(resolved);

            var backupDirectory = Path.Combine(fixture.Root, "backup", "identity");
            Directory.CreateDirectory(backupDirectory);
            var backupPath = Path.Combine(backupDirectory, "logo.png");
            File.Copy(resolved!.Path, backupPath, overwrite: true);

            var removed = await fixture.Logos.RemoveAsync(uploaded, CancellationToken.None);
            Assert.Null(RuntimeStackIdentity.ResolveLogoMetadata(removed));

            var restored = await fixture.Logos.RestoreFromBackupAsync(
                removed,
                backupPath,
                metadata!,
                CancellationToken.None);

            Assert.Equal(metadata, RuntimeStackIdentity.ResolveLogoMetadata(restored));
            var restoredFile = fixture.Logos.Resolve(restored);
            Assert.NotNull(restoredFile);
            Assert.True(File.Exists(restoredFile!.Path));
            Assert.NotEqual(backupPath, restoredFile.Path);
            Assert.Equal(
                await File.ReadAllBytesAsync(backupPath),
                await File.ReadAllBytesAsync(restoredFile.Path));

            var mismatched = metadata! with
            {
                Sha256 = new string('0', 64)
            };

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                fixture.Logos.RestoreFromBackupAsync(
                    restored,
                    backupPath,
                    mismatched,
                    CancellationToken.None));

            Assert.Equal(metadata, RuntimeStackIdentity.ResolveLogoMetadata(
                await fixture.Store.FindAsync("family-chat", CancellationToken.None)));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    [Fact]
    public async Task Manifest_deletion_removes_stack_logo_assets_without_blocking_manifest_removal()
    {
        var fixture = await CreateFixtureAsync();

        try
        {
            var manifest = await fixture.Store.FindAsync("family-chat", CancellationToken.None);
            Assert.NotNull(manifest);
            var uploaded = await fixture.Logos.UploadAsync(
                manifest!,
                FormFile(Convert.FromBase64String(FirstLogoBase64), "family.png"),
                CancellationToken.None);
            var logo = fixture.Logos.Resolve(uploaded);
            Assert.NotNull(logo);

            Assert.True(await fixture.Store.DeleteAsync("family-chat", CancellationToken.None));
            Assert.False(File.Exists(logo!.Path));
            Assert.Null(await fixture.Store.FindAsync("family-chat", CancellationToken.None));
        }
        finally
        {
            fixture.Dispose();
        }
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mem-stack-logo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var manifests = Path.Combine(root, "control-plane", "runtime-stacks");
        Directory.CreateDirectory(manifests);

        var stackId = Guid.NewGuid();
        var metadata = RuntimeStackIdentity.WithIdentityMetadata(
            new Dictionary<string, string?>(),
            "family-chat",
            "Dewar Family Chat",
            "Family");
        var manifest = new RuntimeStackManifest(
            Source: "control-plane",
            StackId: stackId,
            Slug: "family-chat",
            LastVerifiedStatus: "ready",
            LastVerifiedAtUtc: DateTimeOffset.Parse("2026-08-25T00:00:00Z"),
            Matrix: Service("matrix", "matrix.family.test"),
            Element: Service("element-web", "chat.family.test"),
            Warnings: [],
            Metadata: metadata);
        var path = Path.Combine(manifests, $"{stackId:N}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(manifest, JsonOptions()));

        var options = new DbContextOptionsBuilder<MemDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "control-plane.db")}")
            .Options;
        var db = new MemDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["HostAgent:DataRoot"] = root
            })
            .Build();
        var store = new RuntimeStackManifestStore(configuration, db);
        var logos = new RuntimeStackLogoService(configuration, store);
        return new Fixture(root, stackId, db, store, logos);
    }

    private static FormFile FormFile(byte[] bytes, string fileName)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream"
        };
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

    private sealed record Fixture(
        string Root,
        Guid StackId,
        MemDbContext Database,
        RuntimeStackManifestStore Store,
        RuntimeStackLogoService Logos) : IDisposable
    {
        public void Dispose()
        {
            Database.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
