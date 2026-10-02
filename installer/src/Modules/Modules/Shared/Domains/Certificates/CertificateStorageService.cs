using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Modules.Shared.Domains.Certificates;

public sealed class CertificateStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly CertificateStorageOptions _options;
    private readonly ILogger<CertificateStorageService> _logger;

    public CertificateStorageService(
        IOptions<CertificateStorageOptions> options,
        ILogger<CertificateStorageService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StoredCertificateMetadata> StoreAsync(
        CertificateIssueRequest request,
        string fullchainPem,
        string privateKeyPem,
        DateTimeOffset? expiresAtUtc,
        string? thumbprint,
        string status,
        CancellationToken cancellationToken)
    {
        var certificateId = CreateCertificateId(request.StorageName);
        var certificateDirectory = GetCertificateDirectory(certificateId);

        Directory.CreateDirectory(certificateDirectory);

        var fullchainPath = Path.Combine(certificateDirectory, "fullchain.pem");
        var privateKeyPath = Path.Combine(certificateDirectory, "privkey.pem");
        var metadataPath = Path.Combine(certificateDirectory, "metadata.json");

        await File.WriteAllTextAsync(fullchainPath, fullchainPem, cancellationToken);
        await File.WriteAllTextAsync(privateKeyPath, privateKeyPem, cancellationToken);

        var metadata = new StoredCertificateMetadata
        {
            CertificateId = certificateId,
            Domain = request.Domain,
            Zone = request.Zone,
            Provider = request.Provider,
            IsWildcard = request.IsWildcard,
            IsStaging = request.UseStaging,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = expiresAtUtc,
            FullchainPath = fullchainPath,
            PrivateKeyPath = privateKeyPath,
            Thumbprint = thumbprint,
            Status = status,
            Purpose = "test"
        };

        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

        _logger.LogInformation(
            "Stored certificate metadata and PEM files for {CertificateId} at {CertificateDirectory}",
            certificateId,
            certificateDirectory);

        return metadata;
    }

    public async Task<IReadOnlyList<StoredCertificateMetadata>> ListAsync(CancellationToken cancellationToken)
    {
        var root = GetRootPath();
        if (!Directory.Exists(root))
        {
            return [];
        }

        var results = new List<StoredCertificateMetadata>();

        foreach (var metadataPath in Directory.EnumerateFiles(root, "metadata.json", SearchOption.AllDirectories))
        {
            var metadata = await ReadMetadataFileAsync(metadataPath, cancellationToken);
            if (metadata is not null)
            {
                results.Add(metadata);
            }
        }

        return results
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<StoredCertificateMetadata?> GetMetadataAsync(
        string certificateId,
        CancellationToken cancellationToken)
    {
        var metadataPath = GetMetadataPath(certificateId);
        return await ReadMetadataFileAsync(metadataPath, cancellationToken);
    }

    public string GetCertificateDirectory(string certificateId)
    {
        return Path.Combine(GetRootPath(), certificateId);
    }

    public string GetFullchainPath(string certificateId)
    {
        return Path.Combine(GetCertificateDirectory(certificateId), "fullchain.pem");
    }

    public string GetPrivateKeyPath(string certificateId)
    {
        return Path.Combine(GetCertificateDirectory(certificateId), "privkey.pem");
    }


    public async Task SaveMetadataAsync(
        StoredCertificateMetadata metadata,
        CancellationToken cancellationToken)
    {
        var certificateDirectory = GetCertificateDirectory(metadata.CertificateId);
        Directory.CreateDirectory(certificateDirectory);

        var metadataPath = GetMetadataPath(metadata.CertificateId);
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
    }

    public async Task<StoredCertificateMetadata?> SetMainPlatformCertificateAsync(
        string certificateId,
        CancellationToken cancellationToken)
    {
        var certificates = await ListAsync(cancellationToken);
        var selected = certificates.FirstOrDefault(x =>
            string.Equals(x.CertificateId, certificateId, StringComparison.OrdinalIgnoreCase));

        if (selected is null)
        {
            return null;
        }

        foreach (var certificate in certificates)
        {
            var updated = certificate with
            {
                IsMainPlatformCertificate = string.Equals(
                    certificate.CertificateId,
                    certificateId,
                    StringComparison.OrdinalIgnoreCase),
                Purpose = string.Equals(
                    certificate.CertificateId,
                    certificateId,
                    StringComparison.OrdinalIgnoreCase)
                    ? "platform-main"
                    : certificate.Purpose
            };

            await SaveMetadataAsync(updated, cancellationToken);
        }

        return selected with
        {
            IsMainPlatformCertificate = true,
            Purpose = "platform-main"
        };
    }

    public async Task<DeleteCertificateResponse> DeleteAsync(
        string certificateId,
        bool deleteFiles,
        bool deleteNpmCertificate,
        bool force,
        CancellationToken cancellationToken)
    {
        var metadata = await GetMetadataAsync(certificateId, cancellationToken);
        if (metadata is null)
        {
            return new DeleteCertificateResponse(
                Succeeded: false,
                Status: "NotFound",
                Message: $"Certificate '{certificateId}' was not found.",
                CertificateId: certificateId,
                MetadataDeleted: false,
                FilesDeleted: false,
                NpmCertificateDeleted: false,
                Warnings: []);
        }

        if (metadata.IsMainPlatformCertificate && !force)
        {
            return new DeleteCertificateResponse(
                Succeeded: false,
                Status: "Blocked",
                Message: "This certificate is the main platform certificate. Choose another main certificate first, or force delete in development only.",
                CertificateId: certificateId,
                MetadataDeleted: false,
                FilesDeleted: false,
                NpmCertificateDeleted: false,
                Warnings: ["Main platform certificate delete was blocked."]);
        }

        var warnings = new List<string>();
        var metadataDeleted = false;
        var filesDeleted = false;
        var npmDeleted = false;

        var metadataPath = GetMetadataPath(certificateId);
        var certificateDirectory = GetCertificateDirectory(certificateId);

        try
        {
            if (deleteFiles)
            {
                if (Directory.Exists(certificateDirectory))
                {
                    Directory.Delete(certificateDirectory, recursive: true);
                    filesDeleted = true;
                    metadataDeleted = true;
                }
            }
            else if (File.Exists(metadataPath))
            {
                File.Delete(metadataPath);
                metadataDeleted = true;
                warnings.Add("PEM files were left on disk because deleteFiles=false.");
            }

            if (deleteNpmCertificate)
            {
                warnings.Add("NPM certificate deletion is not implemented yet. Remove the NPM certificate manually if required.");
            }

            return new DeleteCertificateResponse(
                Succeeded: true,
                Status: "Deleted",
                Message: deleteFiles
                    ? "Certificate metadata and files were deleted."
                    : "Certificate metadata was deleted. PEM files were left on disk.",
                CertificateId: certificateId,
                MetadataDeleted: metadataDeleted,
                FilesDeleted: filesDeleted,
                NpmCertificateDeleted: npmDeleted,
                Warnings: warnings);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete certificate {CertificateId}", certificateId);

            return new DeleteCertificateResponse(
                Succeeded: false,
                Status: "Failed",
                Message: "Certificate delete failed.",
                CertificateId: certificateId,
                MetadataDeleted: metadataDeleted,
                FilesDeleted: filesDeleted,
                NpmCertificateDeleted: npmDeleted,
                Warnings: [ex.Message]);
        }
    }

    private string GetMetadataPath(string certificateId)
    {
        return Path.Combine(GetCertificateDirectory(certificateId), "metadata.json");
    }

    private string GetRootPath()
    {
        return Path.GetFullPath(_options.RootPath);
    }

    private async Task<StoredCertificateMetadata?> ReadMetadataFileAsync(
        string metadataPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            return JsonSerializer.Deserialize<StoredCertificateMetadata>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read certificate metadata at {MetadataPath}", metadataPath);
            return null;
        }
    }

    private static string CreateCertificateId(string storageName)
    {
        var normalized = NormalizeStorageName(storageName);
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var suffix = Guid.NewGuid().ToString("N")[..8];

        return $"cert_{normalized}_{timestamp}_{suffix}";
    }

    private static string NormalizeStorageName(string storageName)
    {
        if (string.IsNullOrWhiteSpace(storageName))
        {
            return "unnamed";
        }

        var chars = storageName
            .Trim()
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();

        var value = new string(chars).Trim('-');

        if (string.IsNullOrWhiteSpace(value))
        {
            return "unnamed";
        }

        return value.Length <= 64
            ? value
            : value[..64].Trim('-');
    }
}
