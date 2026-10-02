// HostAgent/Runtime/Storage/RuntimeStackStorageService.cs

using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Runtime.Storage;

public sealed class RuntimeStackStorageService
{
    private readonly MemDbContext _db;

    public RuntimeStackStorageService(
        MemDbContext db)
    {
        _db = db;
    }

    public async Task<RuntimeStackStorageResponse> InspectAsync(
        string slugOrId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(slugOrId))
        {
            throw new InvalidOperationException("Stack slug or id is required.");
        }

        var stack = await ResolveStackAsync(slugOrId, ct);
        var stackId = stack.Id;

        var matrixService = await _db.RuntimeServiceInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.RuntimeStackId == stackId &&
                x.ServiceKey == "matrix",
                ct);

        if (matrixService is null)
        {
            throw new InvalidOperationException(
                $"Matrix runtime service was not found for stack '{stack.Slug}'.");
        }

        var matrixDataPath = matrixService.DataPath;

        if (string.IsNullOrWhiteSpace(matrixDataPath))
        {
            throw new InvalidOperationException(
                $"Matrix data path is missing for stack '{stack.Slug}'.");
        }

        var homeserverYamlPath = Path.Combine(matrixDataPath, "homeserver.yaml");

        var signingKeyPath = !string.IsNullOrWhiteSpace(matrixService.ServerName)
            ? Path.Combine(matrixDataPath, $"{matrixService.ServerName}.signing.key")
            : Path.Combine(matrixDataPath, "signing.key");

        var fallbackSigningKeyPath = Path.Combine(matrixDataPath, "signing.key");

        if (!File.Exists(signingKeyPath) && File.Exists(fallbackSigningKeyPath))
        {
            signingKeyPath = fallbackSigningKeyPath;
        }

        var mediaStorePath = Path.Combine(matrixDataPath, "media_store");

        var sections = new List<RuntimeStackStorageSectionResponse>
        {
            InspectSection(
                key: "media_store",
                displayName: "All Matrix media",
                path: mediaStorePath),

            InspectSection(
                key: "local_content",
                displayName: "Local uploads",
                path: Path.Combine(mediaStorePath, "local_content")),

            InspectSection(
                key: "remote_content",
                displayName: "Remote media cache",
                path: Path.Combine(mediaStorePath, "remote_content")),

            InspectSection(
                key: "thumbnails",
                displayName: "Generated thumbnails",
                path: Path.Combine(mediaStorePath, "thumbnails")),

            InspectSection(
                key: "url_cache",
                displayName: "URL preview cache",
                path: Path.Combine(mediaStorePath, "url_cache"))
        };

        var total = sections.First(x => x.Key == "media_store");

        var elementService = await _db.RuntimeServiceInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.RuntimeStackId == stackId &&
                x.ServiceKey == "element-web",
                ct);

        RuntimeStackElementStorageResponse? element = null;

        if (elementService is not null)
        {
            var elementConfigPath = !string.IsNullOrWhiteSpace(elementService.DataPath)
                ? Path.Combine(elementService.DataPath, "config.json")
                : null;

            element = new RuntimeStackElementStorageResponse(
                DataPath: elementService.DataPath,
                ConfigPath: File.Exists(elementConfigPath) ? elementConfigPath : null,
                ConfigBytes: GetFileSize(elementConfigPath));
        }

        return new RuntimeStackStorageResponse(
            Source: "control-plane",
            Status: "ok",
            RuntimeStackId: stack.Id,
            Slug: stack.Slug,
            Matrix: new RuntimeStackMatrixStorageResponse(
                DataPath: matrixDataPath,
                HomeserverYamlPath: File.Exists(homeserverYamlPath) ? homeserverYamlPath : null,
                HomeserverYamlBytes: GetFileSize(homeserverYamlPath),
                SigningKeyPath: File.Exists(signingKeyPath) ? signingKeyPath : null,
                SigningKeyBytes: GetFileSize(signingKeyPath),
                MediaStorePath: mediaStorePath,
                MediaStoreExists: Directory.Exists(mediaStorePath),
                TotalBytes: total.Bytes,
                TotalFiles: total.Files,
                Sections: sections),
            Element: element,
            Detail: null);
    }

    private async Task<RuntimeStackEntity> ResolveStackAsync(
        string slugOrId,
        CancellationToken ct)
    {
        var normalized = slugOrId.Trim();

        if (Guid.TryParse(normalized, out var id))
        {
            var byId = await _db.RuntimeStacks
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id, ct);

            if (byId is not null)
            {
                return byId;
            }
        }

        var bySlug = await _db.RuntimeStacks
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == normalized, ct);

        if (bySlug is not null)
        {
            return bySlug;
        }

        throw new InvalidOperationException(
            $"Runtime stack '{slugOrId}' was not found.");
    }

    private static RuntimeStackStorageSectionResponse InspectSection(
        string key,
        string displayName,
        string path)
    {
        if (!Directory.Exists(path))
        {
            return new RuntimeStackStorageSectionResponse(
                Key: key,
                DisplayName: displayName,
                Path: path,
                Exists: false,
                Bytes: 0,
                Files: 0);
        }

        var stats = GetDirectoryStats(path);

        return new RuntimeStackStorageSectionResponse(
            Key: key,
            DisplayName: displayName,
            Path: path,
            Exists: true,
            Bytes: stats.Bytes,
            Files: stats.Files);
    }

    private static (long Bytes, long Files) GetDirectoryStats(
        string path)
    {
        long bytes = 0;
        long files = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(
                path,
                "*",
                SearchOption.AllDirectories))
            {
                try
                {
                    var info = new FileInfo(file);

                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }

                    bytes += info.Length;
                    files++;
                }
                catch
                {
                    // Ignore individual unreadable files for v1.
                }
            }
        }
        catch
        {
            // Ignore unreadable directory for v1.
        }

        return (bytes, files);
    }

    private static long GetFileSize(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return 0;
        }

        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }
}