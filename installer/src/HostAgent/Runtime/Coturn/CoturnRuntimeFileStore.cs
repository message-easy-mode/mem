using System.Text.Json;
using System.Text.Json.Serialization;
using HostAgent.Runtime.Secrets;
using Microsoft.Extensions.Options;

namespace HostAgent.Runtime.Coturn;

public sealed class CoturnRuntimeFileStore
{
    private readonly string _storageRootPath;
    private readonly TimeProvider _timeProvider;

    public CoturnRuntimeFileStore(
        IOptions<CoturnRuntimeOptions> options,
        TimeProvider timeProvider)
    {
        _storageRootPath = NormalizeStorageRoot(
            options.Value.StorageRootPath,
            options.Value.AllowRelativeDevelopmentStorageRoot);
        _timeProvider = timeProvider;
    }

    public string ConfigPath => Path.Combine(_storageRootPath, "turnserver.conf");

    private string SecretPath => Path.Combine(_storageRootPath, "coturn-secret.json");

    public async Task<CoturnRuntimeFiles> ReadAsync(CancellationToken ct)
    {
        // Inspection is deliberately read-only. Host-native development runs
        // under the developer UID and must not be granted read access to the
        // owner-only Coturn secret/configuration merely to make inspection look
        // identical to containerized execution. Distinguish that expected
        // restriction from genuinely missing or invalid protected material.
        var protectedEvidenceAccess = ProbeProtectedEvidenceAccess();
        if (!string.Equals(
                protectedEvidenceAccess,
                CoturnProtectedEvidenceAccess.Available,
                StringComparison.Ordinal))
        {
            return RestrictedOrUnavailableFiles(protectedEvidenceAccess);
        }

        try
        {
            var secret = await ReadSecretAsync(ct);
            var config = await ReadConfigAsync(ct);

            return new CoturnRuntimeFiles(
                SecretValue: secret.SecretValue,
                SecretPresent: !string.IsNullOrWhiteSpace(secret.SecretValue),
                SecretSource: secret.Source,
                ConfigPresent: config is not null,
                Configuration: config,
                ConfigSha256: config is null
                    ? null
                    : CoturnRuntimePolicy.ComputeSha256(config),
                PermissionsApplied: PermissionsAreOwnerOnly(),
                ProtectedEvidenceAccess: CoturnProtectedEvidenceAccess.Available);
        }
        catch (UnauthorizedAccessException)
        {
            return RestrictedOrUnavailableFiles(
                CoturnProtectedEvidenceAccess.Restricted);
        }
        catch (IOException)
        {
            return RestrictedOrUnavailableFiles(
                CoturnProtectedEvidenceAccess.Unavailable);
        }
    }

    public async Task<CoturnRuntimeFiles> EnsureAsync(
        string baseDomain,
        string realm,
        string? externalIp,
        CancellationToken ct)
    {
        Directory.CreateDirectory(_storageRootPath);
        ApplyDirectoryPermissions();

        var secret = await ReadSecretAsync(ct);
        var secretValue = secret.SecretValue;
        var secretSource = secret.Source;

        if (string.IsNullOrWhiteSpace(secretValue))
        {
            secretValue = RuntimeStackSecretService.CreateSecretValue();
            secretSource = "generated-file";

            var secretFile = new CoturnSecretFile(
                SecretValue: secretValue,
                CreatedAtUtc: _timeProvider.GetUtcNow().UtcDateTime,
                Metadata: new Dictionary<string, string?>
                {
                    ["source"] = "coturn-runtime-service",
                    ["baseDomainAtCreation"] = baseDomain
                });

            await WriteAtomicallyAsync(
                SecretPath,
                JsonSerializer.Serialize(secretFile, JsonOptions()),
                ct);
        }

        _ = CoturnRuntimePolicy.NormalizeExternalIp(externalIp);

        var configuration = CoturnRuntimePolicy.RenderConfiguration(
            realm,
            secretValue);

        var existingConfiguration = await ReadConfigAsync(ct);
        if (!string.Equals(
                existingConfiguration,
                configuration,
                StringComparison.Ordinal))
        {
            await WriteAtomicallyAsync(ConfigPath, configuration, ct);
        }

        ApplyExistingPermissions();

        return new CoturnRuntimeFiles(
            SecretValue: secretValue,
            SecretPresent: true,
            SecretSource: secretSource,
            ConfigPresent: true,
            Configuration: configuration,
            ConfigSha256: CoturnRuntimePolicy.ComputeSha256(configuration),
            PermissionsApplied: PermissionsAreOwnerOnly(),
            ProtectedEvidenceAccess: CoturnProtectedEvidenceAccess.Available);
    }

    private async Task<CoturnSecretState> ReadSecretAsync(CancellationToken ct)
    {
        if (!File.Exists(SecretPath))
        {
            return new CoturnSecretState(null, "missing");
        }

        var json = await File.ReadAllTextAsync(SecretPath, ct);
        var file = JsonSerializer.Deserialize<CoturnSecretFile>(json, JsonOptions());
        return string.IsNullOrWhiteSpace(file?.SecretValue)
            ? new CoturnSecretState(null, "invalid-file")
            : new CoturnSecretState(file.SecretValue, "existing-file");
    }

    private async Task<string?> ReadConfigAsync(CancellationToken ct)
    {
        return File.Exists(ConfigPath)
            ? await File.ReadAllTextAsync(ConfigPath, ct)
            : null;
    }

    private string ProbeProtectedEvidenceAccess()
    {
        try
        {
            _ = File.GetAttributes(_storageRootPath);

            // Force one directory enumeration step. On Unix this establishes
            // whether the current process can traverse/read the owner-only
            // directory instead of relying on File.Exists(), which intentionally
            // collapses access-denied and missing into false.
            using var entries = Directory
                .EnumerateFileSystemEntries(_storageRootPath)
                .GetEnumerator();
            _ = entries.MoveNext();

            return CoturnProtectedEvidenceAccess.Available;
        }
        catch (UnauthorizedAccessException)
        {
            return CoturnProtectedEvidenceAccess.Restricted;
        }
        catch (FileNotFoundException)
        {
            // A genuinely missing storage root is inspectable evidence.
            return CoturnProtectedEvidenceAccess.Available;
        }
        catch (DirectoryNotFoundException)
        {
            // A genuinely missing storage root is inspectable evidence.
            return CoturnProtectedEvidenceAccess.Available;
        }
        catch (IOException)
        {
            return CoturnProtectedEvidenceAccess.Unavailable;
        }
    }

    private static CoturnRuntimeFiles RestrictedOrUnavailableFiles(
        string protectedEvidenceAccess) =>
        new(
            SecretValue: null,
            SecretPresent: false,
            SecretSource: protectedEvidenceAccess,
            ConfigPresent: false,
            Configuration: null,
            ConfigSha256: null,
            PermissionsApplied: false,
            ProtectedEvidenceAccess: protectedEvidenceAccess);

    private async Task WriteAtomicallyAsync(
        string path,
        string content,
        CancellationToken ct)
    {
        Directory.CreateDirectory(_storageRootPath);
        ApplyDirectoryPermissions();

        var temporaryPath = Path.Combine(
            _storageRootPath,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.partial");

        try
        {
            await File.WriteAllTextAsync(temporaryPath, content, ct);
            ApplyFilePermissions(temporaryPath);
            File.Move(temporaryPath, path, overwrite: true);
            ApplyFilePermissions(path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private void ApplyExistingPermissions()
    {
        if (!Directory.Exists(_storageRootPath))
        {
            return;
        }

        ApplyDirectoryPermissions();

        if (File.Exists(SecretPath))
        {
            ApplyFilePermissions(SecretPath);
        }

        if (File.Exists(ConfigPath))
        {
            ApplyFilePermissions(ConfigPath);
        }
    }

    private void ApplyDirectoryPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            _storageRootPath,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
    }

    private static void ApplyFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite);
    }

    private bool PermissionsAreOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        if (!Directory.Exists(_storageRootPath) ||
            !File.Exists(SecretPath) ||
            !File.Exists(ConfigPath))
        {
            return false;
        }

        var directoryMode = File.GetUnixFileMode(_storageRootPath);
        var secretMode = File.GetUnixFileMode(SecretPath);
        var configMode = File.GetUnixFileMode(ConfigPath);

        return directoryMode ==
                   (UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute) &&
               secretMode ==
                   (UnixFileMode.UserRead |
                    UnixFileMode.UserWrite) &&
               configMode ==
                   (UnixFileMode.UserRead |
                    UnixFileMode.UserWrite);
    }

    private static string NormalizeStorageRoot(
        string? value,
        bool allowRelativeDevelopmentStorageRoot)
    {
        var path = value?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "Coturn:StorageRootPath must be configured as an absolute host path.");
        }

        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        if (!allowRelativeDevelopmentStorageRoot)
        {
            throw new InvalidOperationException(
                "Coturn:StorageRootPath must be an absolute host path.");
        }

        return Path.GetFullPath(path, ResolveDirectSourceApiProjectRoot());
    }

    private static string ResolveDirectSourceApiProjectRoot()
    {
        foreach (var startingPath in new[]
                 {
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 })
        {
            var current = new DirectoryInfo(startingPath);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Api.csproj")))
                {
                    return current.FullName;
                }

                var siblingApiProject = Path.Combine(
                    current.FullName,
                    "Api",
                    "Api.csproj");
                if (File.Exists(siblingApiProject))
                {
                    return Path.GetDirectoryName(siblingApiProject)!;
                }

                var repositoryApiProject = Path.Combine(
                    current.FullName,
                    "installer",
                    "src",
                    "Api",
                    "Api.csproj");
                if (File.Exists(repositoryApiProject))
                {
                    return Path.GetDirectoryName(repositoryApiProject)!;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException(
            "Coturn:StorageRootPath is repository-relative, but the Direct Source API project root could not be resolved.");
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record CoturnSecretState(
        string? SecretValue,
        string Source);

    private sealed record CoturnSecretFile(
        string SecretValue,
        DateTime CreatedAtUtc,
        IReadOnlyDictionary<string, string?> Metadata);
}

public sealed record CoturnRuntimeFiles(
    string? SecretValue,
    bool SecretPresent,
    string SecretSource,
    bool ConfigPresent,
    string? Configuration,
    string? ConfigSha256,
    bool PermissionsApplied,
    string ProtectedEvidenceAccess);
