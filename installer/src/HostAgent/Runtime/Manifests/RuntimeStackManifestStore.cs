using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Commands;
using HostAgent.Runtime.ServiceRuntime;
using HostAgent.Runtime.Stacks.Identity;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HostAgent.Runtime.Manifests;

public sealed class RuntimeStackManifestStore
{
    private readonly IConfiguration _configuration;
    private readonly MemDbContext _db;

    public RuntimeStackManifestStore(
        IConfiguration configuration,
        MemDbContext db)
    {
        _configuration = configuration;
        _db = db;
    }

    public async Task SaveAsync(
        string stackSlug,
        CreateChatStackRuntimeResult result,
        CancellationToken ct,
        string? displayName = null,
        string? category = null)
    {
        var now = DateTimeOffset.UtcNow;

        var manifest = RuntimeStackManifest.FromResult(
            stackSlug,
            result,
            now,
            displayName,
            category);

        var directory = GetManifestDirectory();

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            $"{result.StackId:N}.json");

        var json = JsonSerializer.Serialize(
            manifest,
            JsonOptions());

        await File.WriteAllTextAsync(
            path,
            json,
            ct);

        await UpsertDatabaseRowsAsync(
            manifest,
            path,
            now,
            ct);
    }

    public async Task<RuntimeStackManifest?> FindAsync(
        string slugOrId,
        CancellationToken ct)
    {
        var directory = GetManifestDirectory();

        if (!Directory.Exists(directory))
        {
            return null;
        }

        Guid? requestedStackId = null;
        if (Guid.TryParse(slugOrId, out var parsedId))
        {
            requestedStackId = parsedId;
        }
        else
        {
            var normalizedSlug = slugOrId.Trim().ToLowerInvariant();
            var durableCandidates = await _db.RuntimeStacks
                .AsNoTracking()
                .Where(x => x.Slug.ToLower() == normalizedSlug)
                .Select(x => new
                {
                    x.Id,
                    x.Status,
                    x.LastVerifiedStatus,
                    x.Slug
                })
                .ToArrayAsync(ct);

            var activeCandidates = durableCandidates
                .Where(x =>
                    !string.Equals(x.Status, "destroyed", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(x.LastVerifiedStatus, "destroyed", StringComparison.OrdinalIgnoreCase) &&
                    !x.Slug.Contains("--destroyed-", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (activeCandidates.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Runtime stack slug '{slugOrId}' maps to more than one active durable stack identity.");
            }

            requestedStackId = activeCandidates.SingleOrDefault()?.Id;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var json = await File.ReadAllTextAsync(path, ct);

            var manifest = JsonSerializer.Deserialize<RuntimeStackManifest>(
                json,
                JsonOptions());

            if (manifest is null)
            {
                continue;
            }

            if (requestedStackId.HasValue)
            {
                if (manifest.StackId == requestedStackId.Value)
                {
                    return manifest;
                }

                continue;
            }

            if (string.Equals(
                    manifest.Slug,
                    slugOrId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return manifest;
            }

            if (string.Equals(
                    manifest.StackId.ToString("N"),
                    slugOrId.Replace("-", "", StringComparison.Ordinal),
                    StringComparison.OrdinalIgnoreCase))
            {
                return manifest;
            }
        }

        return null;
    }


    public async Task<RuntimeStackManifest?> UpdateIdentityAsync(
        Guid runtimeStackId,
        string? displayName,
        string? category,
        CancellationToken ct)
    {
        var directory = GetManifestDirectory();
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var originalBytes = await File.ReadAllBytesAsync(path, ct);
            var manifest = JsonSerializer.Deserialize<RuntimeStackManifest>(
                originalBytes,
                JsonOptions());

            if (manifest is null || manifest.StackId != runtimeStackId)
            {
                continue;
            }

            var updated = manifest with
            {
                Metadata = RuntimeStackIdentity.WithIdentityMetadata(
                    manifest.Metadata,
                    manifest.Slug,
                    displayName,
                    category)
            };

            var updatedBytes = JsonSerializer.SerializeToUtf8Bytes(
                updated,
                JsonOptions());
            var temporary = path + $".mem-update-{Guid.NewGuid():N}.tmp";
            var mode = TryGetUnixFileMode(path);

            try
            {
                await WriteFileAtomicallyAsync(
                    temporary,
                    path,
                    updatedBytes,
                    mode,
                    ct);

                try
                {
                    await UpdateDatabaseIdentityAsync(
                        updated,
                        path,
                        DateTimeOffset.UtcNow,
                        ct);
                }
                catch
                {
                    await RestoreFileAsync(path, originalBytes, mode);
                    throw;
                }

                return updated;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        return null;
    }

    public async Task<RuntimeStackManifest?> UpdateVerificationAsync(
        Guid runtimeStackId,
        string verificationStatus,
        DateTimeOffset verifiedAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(verificationStatus))
        {
            throw new ArgumentException("Verification status is required.", nameof(verificationStatus));
        }

        var normalizedStatus = verificationStatus.Trim().ToLowerInvariant();
        var directory = GetManifestDirectory();
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var originalBytes = await File.ReadAllBytesAsync(path, ct);
            var manifest = JsonSerializer.Deserialize<RuntimeStackManifest>(
                originalBytes,
                JsonOptions());

            if (manifest is null || manifest.StackId != runtimeStackId)
            {
                continue;
            }

            var updated = manifest with
            {
                LastVerifiedStatus = normalizedStatus,
                LastVerifiedAtUtc = verifiedAtUtc
            };

            var updatedBytes = JsonSerializer.SerializeToUtf8Bytes(
                updated,
                JsonOptions());
            var temporary = path + $".mem-update-{Guid.NewGuid():N}.tmp";
            var mode = TryGetUnixFileMode(path);

            try
            {
                await WriteFileAtomicallyAsync(
                    temporary,
                    path,
                    updatedBytes,
                    mode,
                    ct);

                try
                {
                    await UpdateDatabaseVerificationAsync(
                        updated,
                        path,
                        DateTimeOffset.UtcNow,
                        ct);
                }
                catch
                {
                    await RestoreFileAsync(path, originalBytes, mode);
                    throw;
                }

                return updated;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        return null;
    }

    public async Task<RuntimeStackManifest?> UpdateLogoMetadataAsync(
        Guid runtimeStackId,
        RuntimeStackLogoMetadata? logo,
        CancellationToken ct)
    {
        var directory = GetManifestDirectory();
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var originalBytes = await File.ReadAllBytesAsync(path, ct);
            var manifest = JsonSerializer.Deserialize<RuntimeStackManifest>(
                originalBytes,
                JsonOptions());

            if (manifest is null || manifest.StackId != runtimeStackId)
            {
                continue;
            }

            var updated = manifest with
            {
                Metadata = RuntimeStackIdentity.WithLogoMetadata(
                    manifest.Metadata,
                    logo)
            };

            var updatedBytes = JsonSerializer.SerializeToUtf8Bytes(
                updated,
                JsonOptions());
            var temporary = path + $".mem-update-{Guid.NewGuid():N}.tmp";
            var mode = TryGetUnixFileMode(path);

            try
            {
                await WriteFileAtomicallyAsync(
                    temporary,
                    path,
                    updatedBytes,
                    mode,
                    ct);

                try
                {
                    await UpdateDatabaseIdentityAsync(
                        updated,
                        path,
                        DateTimeOffset.UtcNow,
                        ct);
                }
                catch
                {
                    await RestoreFileAsync(path, originalBytes, mode);
                    throw;
                }

                return updated;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        return null;
    }

    public Task<RuntimeStackManifest?> ReplaceMatrixRuntimeMetadataAsync(
        Guid runtimeStackId,
        IReadOnlyDictionary<string, string?> runtimeMetadata,
        CancellationToken ct) =>
        ReplaceMatrixRuntimeMetadataAsync(
            runtimeStackId,
            runtimeMetadata,
            expectedCurrentMetadata: null,
            ct);

    public async Task<RuntimeStackManifest?> ReplaceMatrixRuntimeMetadataAsync(
        Guid runtimeStackId,
        IReadOnlyDictionary<string, string?> runtimeMetadata,
        IReadOnlyDictionary<string, string?>? expectedCurrentMetadata,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(runtimeMetadata);

        var directory = GetManifestDirectory();
        if (!Directory.Exists(directory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var originalBytes = await File.ReadAllBytesAsync(path, ct);
            var manifest = JsonSerializer.Deserialize<RuntimeStackManifest>(
                originalBytes,
                JsonOptions());

            if (manifest is null || manifest.StackId != runtimeStackId)
            {
                continue;
            }

            if (expectedCurrentMetadata is not null &&
                !MetadataEqual(
                    manifest.Matrix.RuntimeMetadata,
                    expectedCurrentMetadata))
            {
                throw new InvalidOperationException(
                    "The Runtime Stack metadata changed before the atomic update.");
            }

            var updated = manifest with
            {
                Matrix = manifest.Matrix with
                {
                    RuntimeMetadata = runtimeMetadata.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.Ordinal)
                }
            };

            var updatedBytes = JsonSerializer.SerializeToUtf8Bytes(
                updated,
                JsonOptions());
            var temporary = path + $".mem-update-{Guid.NewGuid():N}.tmp";
            var mode = TryGetUnixFileMode(path);

            try
            {
                await WriteFileAtomicallyAsync(
                    temporary,
                    path,
                    updatedBytes,
                    mode,
                    ct);

                try
                {
                    await UpsertDatabaseRowsAsync(
                        updated,
                        path,
                        DateTimeOffset.UtcNow,
                        ct);
                }
                catch
                {
                    await RestoreFileAsync(path, originalBytes, mode);
                    throw;
                }

                return updated;
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        return null;
    }


    private static bool MetadataEqual(
        IReadOnlyDictionary<string, string?> left,
        IReadOnlyDictionary<string, string?> right) =>
        left.Count == right.Count &&
        left.All(pair =>
            right.TryGetValue(pair.Key, out var value) &&
            string.Equals(pair.Value, value, StringComparison.Ordinal));

    private static async Task WriteFileAtomicallyAsync(
        string temporary,
        string destination,
        byte[] bytes,
        UnixFileMode? mode,
        CancellationToken ct)
    {
        await using (var stream = new FileStream(
            temporary,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await stream.WriteAsync(bytes, ct);
            await stream.FlushAsync(ct);
            stream.Flush(flushToDisk: true);
        }

        if (mode is not null)
        {
            File.SetUnixFileMode(temporary, mode.Value);
        }

        File.Move(temporary, destination, overwrite: true);
    }

    private static async Task RestoreFileAsync(
        string destination,
        byte[] bytes,
        UnixFileMode? mode)
    {
        var temporary = destination + $".mem-restore-{Guid.NewGuid():N}.tmp";
        try
        {
            await WriteFileAtomicallyAsync(
                temporary,
                destination,
                bytes,
                mode,
                CancellationToken.None);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static UnixFileMode? TryGetUnixFileMode(string path) =>
        OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()
            ? File.GetUnixFileMode(path)
            : null;

    public async Task<bool> DeleteAsync(
    string slugOrId,
    CancellationToken ct)
    {
        var directory = GetManifestDirectory();

        if (!Directory.Exists(directory))
        {
            return false;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var json = await File.ReadAllTextAsync(path, ct);

            var manifest = JsonSerializer.Deserialize<RuntimeStackManifest>(
                json,
                JsonOptions());

            if (manifest is null)
            {
                continue;
            }

            var matchesSlug = string.Equals(
                manifest.Slug,
                slugOrId,
                StringComparison.OrdinalIgnoreCase);

            var matchesGuid =
                Guid.TryParse(slugOrId, out var requestedId) &&
                manifest.StackId == requestedId;

            var matchesN =
                string.Equals(
                    manifest.StackId.ToString("N"),
                    slugOrId.Replace("-", "", StringComparison.Ordinal),
                    StringComparison.OrdinalIgnoreCase);

            if (!matchesSlug && !matchesGuid && !matchesN)
            {
                continue;
            }

            File.Delete(path);

            // The manifest is authoritative for whether the stack/logo exists. A cosmetic
            // Control Plane asset must never turn an otherwise completed stack destruction
            // into a partial failure merely because stale payload cleanup is blocked.
            DeleteStackAssetDirectoryBestEffort(manifest.StackId);
            return true;
        }

        return false;
    }

    public async Task<IReadOnlyList<RuntimeStackManifest>> ListAsync(
        CancellationToken ct)
    {
        var directory = GetManifestDirectory();

        if (!Directory.Exists(directory))
        {
            return [];
        }

        var results = new List<RuntimeStackManifest>();

        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            var json = await File.ReadAllTextAsync(path, ct);

            var manifest = JsonSerializer.Deserialize<RuntimeStackManifest>(
                json,
                JsonOptions());

            if (manifest is not null)
            {
                results.Add(manifest);
            }
        }

        return results
            .OrderBy(x => x.Slug)
            .ToArray();
    }

    private async Task UpdateDatabaseVerificationAsync(
        RuntimeStackManifest manifest,
        string manifestPath,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await _db.RuntimeStacks
            .Include(x => x.Routes)
            .FirstOrDefaultAsync(x => x.Id == manifest.StackId, ct);

        if (existing is null)
        {
            await UpsertDatabaseRowsAsync(manifest, manifestPath, now, ct);
            return;
        }

        existing.Status = manifest.LastVerifiedStatus;
        existing.LastVerifiedStatus = manifest.LastVerifiedStatus;
        existing.LastVerifiedAtUtc = manifest.LastVerifiedAtUtc.UtcDateTime;
        existing.ManifestPath = manifestPath;
        existing.UpdatedAtUtc = now.UtcDateTime;

        foreach (var route in existing.Routes)
        {
            route.LastVerifiedAtUtc = manifest.LastVerifiedAtUtc.UtcDateTime;
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task UpdateDatabaseIdentityAsync(
        RuntimeStackManifest manifest,
        string manifestPath,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await _db.RuntimeStacks
            .FirstOrDefaultAsync(x => x.Id == manifest.StackId, ct);

        if (existing is null)
        {
            await UpsertDatabaseRowsAsync(manifest, manifestPath, now, ct);
            return;
        }

        var durableMetadata = DeserializeMetadata(existing.MetadataJson);
        durableMetadata = RuntimeStackIdentity.WithIdentityMetadata(
            durableMetadata,
            manifest.Slug,
            RuntimeStackIdentity.ResolveDisplayName(manifest),
            RuntimeStackIdentity.ResolveCategory(manifest))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        durableMetadata = RuntimeStackIdentity.WithLogoMetadata(
            durableMetadata,
            RuntimeStackIdentity.ResolveLogoMetadata(manifest))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        existing.DisplayName = RuntimeStackIdentity.ResolveDisplayName(manifest);
        existing.MetadataJson = SerializeObject(durableMetadata);
        existing.ManifestPath = manifestPath;
        existing.UpdatedAtUtc = now.UtcDateTime;

        await _db.SaveChangesAsync(ct);
    }

    private async Task UpsertDatabaseRowsAsync(
        RuntimeStackManifest manifest,
        string manifestPath,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var existing = await _db.RuntimeStacks
            .Include(x => x.ServiceInstances)
            .Include(x => x.Routes)
            .FirstOrDefaultAsync(x => x.Id == manifest.StackId, ct);

        if (existing is null)
        {
            existing = new RuntimeStackEntity
            {
                Id = manifest.StackId,
                CreatedAtUtc = now.UtcDateTime
            };

            _db.RuntimeStacks.Add(existing);
        }
        else
        {
            _db.RuntimeRoutes.RemoveRange(existing.Routes);
            _db.RuntimeServiceInstances.RemoveRange(existing.ServiceInstances);
        }

        existing.Slug = manifest.Slug;
        existing.DisplayName = RuntimeStackIdentity.ResolveDisplayName(manifest);
        existing.Status = manifest.LastVerifiedStatus;
        existing.LastVerifiedStatus = manifest.LastVerifiedStatus;
        existing.LastVerifiedAtUtc = manifest.LastVerifiedAtUtc.UtcDateTime;
        existing.UpdatedAtUtc = now.UtcDateTime;

        existing.BaseDomain = GetMetadata(
            manifest.Matrix,
            "baseDomain") ?? GetMetadata(
            manifest.Element,
            "baseDomain");

        existing.DomainId = TryGetGuid(
            GetMetadata(manifest.Matrix, "domainId") ??
            GetMetadata(manifest.Element, "domainId"));

        existing.ActiveCertificateId = TryGetGuid(
            GetMetadata(manifest.Matrix, "activeCertificateId") ??
            GetMetadata(manifest.Element, "activeCertificateId"));

        existing.ActiveNpmCertificateId = TryGetInt(
            GetMetadata(manifest.Matrix, "activeNpmCertificateId") ??
            GetMetadata(manifest.Element, "activeNpmCertificateId"));

        existing.RuntimeNetworkName =
            GetMetadata(manifest.Matrix, "runtimeNetworkName") ??
            GetMetadata(manifest.Element, "runtimeNetworkName");

        existing.DataRoot = GetDataRoot();
        existing.ManifestPath = manifestPath;

        existing.MatrixInstanceId = manifest.Matrix.InstanceId;
        existing.ElementInstanceId = manifest.Element?.InstanceId;

        existing.MatrixPublicBaseUrl = manifest.Matrix.PublicBaseUrl;
        existing.ElementPublicBaseUrl = manifest.Element?.PublicBaseUrl;

        existing.LastError = null;
        existing.MetadataJson = SerializeObject(manifest.Metadata);

        var matrixService = CreateServiceInstanceEntity(
            manifest.StackId,
            manifest.Matrix,
            manifest.LastVerifiedStatus,
            now);

        _db.RuntimeServiceInstances.Add(matrixService);

        RuntimeServiceInstanceEntity? elementService = null;

        if (manifest.Element is not null)
        {
            elementService = CreateServiceInstanceEntity(
                manifest.StackId,
                manifest.Element,
                manifest.LastVerifiedStatus,
                now);

            _db.RuntimeServiceInstances.Add(elementService);
        }

        var matrixRoute = CreateRouteEntity(
            manifest.StackId,
            matrixService.Id,
            manifest.Matrix,
            manifest.LastVerifiedAtUtc,
            now);

        if (matrixRoute is not null)
        {
            _db.RuntimeRoutes.Add(matrixRoute);
        }

        if (manifest.Element is not null && elementService is not null)
        {
            var elementRoute = CreateRouteEntity(
                manifest.StackId,
                elementService.Id,
                manifest.Element,
                manifest.LastVerifiedAtUtc,
                now);

            if (elementRoute is not null)
            {
                _db.RuntimeRoutes.Add(elementRoute);
            }
        }

        await _db.SaveChangesAsync(ct);
    }

    private RuntimeServiceInstanceEntity CreateServiceInstanceEntity(
        Guid runtimeStackId,
        RuntimeStackServiceManifest service,
        string stackStatus,
        DateTimeOffset now)
    {
        var image = service.ServiceKey switch
        {
            ServiceKeys.Matrix => GetMetadata(service, "matrixImage"),
            ServiceKeys.ElementWeb => GetMetadata(service, "elementImage"),
            _ => null
        };

        return new RuntimeServiceInstanceEntity
        {
            Id = service.InstanceId,
            RuntimeStackId = runtimeStackId,
            InstanceId = service.InstanceId,
            ServiceKey = service.ServiceKey,
            Role = service.ServiceKey,
            Status = ResolveServiceStatus(service, stackStatus),

            Image = image,
            Version = ExtractImageVersion(image),

            ContainerId = service.ContainerId,
            ContainerName = service.ContainerName,
            NetworkName = GetMetadata(service, "runtimeNetworkName"),

            InternalHost = service.InternalHost,
            InternalBaseUrl = service.InternalBaseUrl,

            PublicHost = service.PublicHost,
            PublicBaseUrl = service.PublicBaseUrl,

            HostPort = service.HostPort,
            ContainerPort = ResolveContainerPort(service.ServiceKey),

            DataPath = service.DataPath,
            ConfigPath = service.ConfigPath,

            BackupInclude = true,

            ServerName = service.ServerName,

            RuntimeMetadataJson = SerializeObject(service.RuntimeMetadata),

            CreatedAtUtc = now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime,
            LastObservedAtUtc = now.UtcDateTime,

            LastError = null
        };
    }

    private RuntimeRouteEntity? CreateRouteEntity(
        Guid runtimeStackId,
        Guid runtimeServiceInstanceId,
        RuntimeStackServiceManifest service,
        DateTimeOffset lastVerifiedAtUtc,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(service.PublicHost) ||
            string.IsNullOrWhiteSpace(service.InternalHost))
        {
            return null;
        }

        var forwardPort = TryGetInt(
            GetMetadata(service, "publicForwardPort")) ??
            ResolveContainerPort(service.ServiceKey);

        if (forwardPort is null)
        {
            return null;
        }

        var sslConfigured = service.NpmCertificateId.HasValue &&
                            service.NpmCertificateId.Value > 0;

        return new RuntimeRouteEntity
        {
            Id = Guid.NewGuid(),

            RuntimeStackId = runtimeStackId,
            RuntimeServiceInstanceId = runtimeServiceInstanceId,

            ServiceKey = service.ServiceKey,

            Provider = "npm",
            RouteKind = ResolveRouteKind(service.ServiceKey),

            IsPublic = true,

            PublicHost = service.PublicHost,
            PublicBaseUrl = service.PublicBaseUrl,

            ForwardScheme = "http",
            ForwardHost = GetMetadata(service, "publicForwardHost") ?? service.InternalHost,
            ForwardPort = forwardPort.Value,

            ProviderRouteId = service.PublicRouteId,

            CertificateId = TryGetGuid(GetMetadata(service, "activeCertificateId")),
            NpmCertificateId = service.NpmCertificateId,

            SslExpected = true,
            SslConfigured = sslConfigured,
            ForceSsl = sslConfigured,
            Http2 = sslConfigured,

            AdvancedConfigHash = null,
            AdvancedConfigApplied = false,

            Status = ResolveRouteStatus(service),

            LastVerifiedAtUtc = lastVerifiedAtUtc.UtcDateTime,
            LastError = null,

            MetadataJson = SerializeObject(new Dictionary<string, string?>
            {
                ["publicRouteReady"] = GetMetadata(service, "publicRouteReady"),
                ["readinessVerified"] = GetMetadata(service, "readinessVerified")
            })
        };
    }

    private string GetManifestDirectory()
    {
        return Path.Combine(
            GetDataRoot(),
            "control-plane",
            "runtime-stacks");
    }

    private string GetStackAssetDirectory(Guid stackId)
    {
        return Path.Combine(
            GetDataRoot(),
            "control-plane",
            "runtime-stack-assets",
            stackId.ToString("N"));
    }

    private void DeleteStackAssetDirectoryBestEffort(Guid stackId)
    {
        var directory = GetStackAssetDirectory(stackId);
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // The manifest has already been removed. Leave an unreferenced cosmetic asset
            // rather than making stack destruction fail after its durable state is gone.
        }
        catch (UnauthorizedAccessException)
        {
            // Same rationale as above. A later bounded maintenance pass can remove the
            // orphan without reviving a stack that has already been destroyed.
        }
    }

    private string GetDataRoot() =>

        global::Modules.Shared.Storage.MemDataRootResolver.Resolve(_configuration);

    private static string ResolveServiceStatus(
        RuntimeStackServiceManifest service,
        string stackStatus)
    {
        if (string.Equals(
                GetMetadata(service, "readinessVerified"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return "verified";
        }

        if (string.Equals(
                GetMetadata(service, "elementStarted"),
                "true",
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                GetMetadata(service, "matrixStarted"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return "running";
        }

        return stackStatus;
    }

    private static string ResolveRouteStatus(
        RuntimeStackServiceManifest service)
    {
        if (string.Equals(
                GetMetadata(service, "publicRouteReady"),
                "true",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                GetMetadata(service, "readinessVerified"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return "verified";
        }

        if (!string.IsNullOrWhiteSpace(service.PublicRouteId))
        {
            return "created";
        }

        return "missing";
    }

    private static string ResolveRouteKind(
        string serviceKey)
    {
        return serviceKey switch
        {
            ServiceKeys.Matrix => "Matrix",
            ServiceKeys.ElementWeb => "ElementWeb",
            _ => serviceKey
        };
    }

    private static int? ResolveContainerPort(
        string serviceKey)
    {
        return serviceKey switch
        {
            ServiceKeys.Matrix => 8008,
            ServiceKeys.ElementWeb => 80,
            _ => null
        };
    }

    private static string? ExtractImageVersion(
        string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return null;
        }

        if (image.Contains('@', StringComparison.Ordinal))
        {
            return image.Split('@', 2)[1];
        }

        var slashIndex = image.LastIndexOf('/');
        var colonIndex = image.LastIndexOf(':');

        return colonIndex > slashIndex
            ? image[(colonIndex + 1)..]
            : null;
    }

    private static string? GetMetadata(
        RuntimeStackServiceManifest? service,
        string key)
    {
        if (service is null)
        {
            return null;
        }

        return service.RuntimeMetadata.TryGetValue(key, out var value)
            ? value
            : null;
    }

    private static Guid? TryGetGuid(
        string? value)
    {
        return Guid.TryParse(value, out var parsed)
            ? parsed
            : null;
    }

    private static int? TryGetInt(
        string? value)
    {
        return int.TryParse(value, out var parsed)
            ? parsed
            : null;
    }


    private static Dictionary<string, string?> DeserializeMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, JsonOptions()) is { } parsed
                ? new Dictionary<string, string?>(parsed, StringComparer.Ordinal)
                : new Dictionary<string, string?>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }
    }

    private static string SerializeObject<T>(T value)
    {
        return JsonSerializer.Serialize(
            value,
            JsonOptions());
    }

    private static JsonSerializerOptions JsonOptions()
    {
        return new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}

public sealed record RuntimeStackManifest(
    string Source,
    Guid StackId,
    string Slug,
    string LastVerifiedStatus,
    DateTimeOffset LastVerifiedAtUtc,
    RuntimeStackServiceManifest Matrix,
    RuntimeStackServiceManifest? Element,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public static RuntimeStackManifest FromResult(
        string stackSlug,
        CreateChatStackRuntimeResult result,
        DateTimeOffset now,
        string? displayName = null,
        string? category = null)
    {
        var metadata = RuntimeStackIdentity.WithIdentityMetadata(
            new Dictionary<string, string?>
            {
                ["message"] = result.Message,
                ["evidenceCount"] = result.Evidence.Count.ToString()
            },
            stackSlug,
            displayName,
            category);

        return new RuntimeStackManifest(
            Source: "control-plane",
            StackId: result.StackId,
            Slug: stackSlug,
            LastVerifiedStatus: result.Status,
            LastVerifiedAtUtc: now,
            Matrix: RuntimeStackServiceManifest.FromRuntimeResult(result.Matrix),
            Element: result.Element is null
                ? null
                : RuntimeStackServiceManifest.FromRuntimeResult(result.Element),
            Warnings: result.Warnings,
            Metadata: metadata);
    }
}

public sealed record RuntimeStackServiceManifest(
    Guid InstanceId,
    string ServiceKey,
    string? ContainerId,
    string? ContainerName,
    int HostPort,
    string? DataPath,
    string? ServerName,
    string? PublicHost,
    string? PublicBaseUrl,
    string? InternalHost,
    string? InternalBaseUrl,
    string? PublicRouteId,
    string? InternalRouteId,
    int? NpmCertificateId,
    IReadOnlyDictionary<string, string?> RuntimeMetadata)
{
    public static RuntimeStackServiceManifest FromRuntimeResult(
        HostAgentServiceRuntimeResult result)
    {
        return new RuntimeStackServiceManifest(
            InstanceId: result.InstanceId,
            ServiceKey: result.ServiceKey,
            ContainerId: result.ContainerId,
            ContainerName: result.ContainerName,
            HostPort: result.HostPort,
            DataPath: result.DataPath,
            ServerName: result.ServerName,
            PublicHost: result.PublicHost,
            PublicBaseUrl: result.PublicBaseUrl,
            InternalHost: result.InternalHost,
            InternalBaseUrl: result.InternalBaseUrl,
            PublicRouteId: result.PublicRouteId,
            InternalRouteId: result.InternalRouteId,
            NpmCertificateId: result.NpmCertificateId,
            RuntimeMetadata: result.RuntimeMetadata);
    }

    public string? ConfigPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DataPath))
            {
                return null;
            }

            return ServiceKey switch
            {
                ServiceKeys.Matrix => Path.Combine(DataPath, "homeserver.yaml"),
                ServiceKeys.ElementWeb => Path.Combine(DataPath, "config.json"),
                _ => null
            };
        }
    }
}