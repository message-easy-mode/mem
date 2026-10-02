using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains;

public sealed class DomainRegistryService
{
    private readonly MemDbContext _db;
    private readonly CertificateStorageService _storage;
    private readonly ILogger<DomainRegistryService> _logger;

    public DomainRegistryService(
        MemDbContext db,
        CertificateStorageService storage,
        ILogger<DomainRegistryService> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    public async Task<StoredCertificateMetadata> RegisterCertificateAsync(
    StoredCertificateMetadata metadata,
    CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var baseDomain = NormalizeBaseDomain(metadata.Zone, metadata.Domain);

        var hasMainDomain = await _db.Domains
            .AnyAsync(x => x.IsMainPlatformDomain, cancellationToken);

        var domain = await _db.Domains
            .Include(x => x.Certificates)
            .FirstOrDefaultAsync(x => x.BaseDomain == baseDomain, cancellationToken);

        if (domain is null)
        {
            var isFirstDomain = !hasMainDomain;

            domain = new DomainEntity
            {
                Id = Guid.NewGuid(),
                BaseDomain = baseDomain,
                DisplayName = baseDomain,
                Purpose = isFirstDomain ? "platform-main" : (metadata.Purpose ?? "test"),
                IsMainPlatformDomain = isFirstDomain,
                DnsProvider = string.IsNullOrWhiteSpace(metadata.Provider) ? "unknown" : metadata.Provider,
                DnsZone = metadata.Zone,
                Status = "Active",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            _db.Domains.Add(domain);

            // Save the domain first so the certificate can safely reference it.
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            domain.DnsProvider = string.IsNullOrWhiteSpace(metadata.Provider)
                ? domain.DnsProvider
                : metadata.Provider;

            domain.DnsZone = string.IsNullOrWhiteSpace(metadata.Zone)
                ? domain.DnsZone
                : metadata.Zone;

            domain.Status = string.IsNullOrWhiteSpace(domain.Status)
                ? "Active"
                : domain.Status;

            domain.UpdatedAtUtc = now;

            await _db.SaveChangesAsync(cancellationToken);
        }

        var certificate = await _db.Certificates
            .FirstOrDefaultAsync(x => x.CertificateId == metadata.CertificateId, cancellationToken);

        if (certificate is null)
        {
            certificate = new CertificateEntity
            {
                Id = Guid.NewGuid(),
                DomainId = domain.Id,
                CertificateId = metadata.CertificateId,
                CommonName = metadata.Domain,
                Provider = string.IsNullOrWhiteSpace(metadata.Provider) ? "unknown" : metadata.Provider,
                IsWildcard = metadata.IsWildcard,
                IsStaging = metadata.IsStaging,
                IsActive = true,
                Status = NormalizeCertificateStatus(metadata.Status),
                CreatedAtUtc = metadata.CreatedAtUtc.UtcDateTime,
                ExpiresAtUtc = metadata.ExpiresAtUtc?.UtcDateTime,
                FullchainPath = metadata.FullchainPath,
                PrivateKeyPath = metadata.PrivateKeyPath,
                Thumbprint = metadata.Thumbprint
            };

            _db.Certificates.Add(certificate);

            // Save the certificate before making it the active certificate on the domain.
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            certificate.DomainId = domain.Id;
            certificate.CommonName = metadata.Domain;
            certificate.Provider = string.IsNullOrWhiteSpace(metadata.Provider)
                ? certificate.Provider
                : metadata.Provider;
            certificate.IsWildcard = metadata.IsWildcard;
            certificate.IsStaging = metadata.IsStaging;
            certificate.IsActive = certificate.Status != "Deleted";
            certificate.Status = NormalizeCertificateStatus(metadata.Status);
            certificate.ExpiresAtUtc = metadata.ExpiresAtUtc?.UtcDateTime;
            certificate.FullchainPath = metadata.FullchainPath;
            certificate.PrivateKeyPath = metadata.PrivateKeyPath;
            certificate.Thumbprint = metadata.Thumbprint;

            await _db.SaveChangesAsync(cancellationToken);
        }

        if (certificate.IsStaging && certificate.IsMainPlatformCertificate)
        {
            certificate.IsMainPlatformCertificate = false;
        }

        var activeCertificate = domain.ActiveCertificateId is Guid activeCertificateId
            ? domain.Certificates.FirstOrDefault(item => item.Id == activeCertificateId)
            : null;

        if (activeCertificate is not null &&
            (activeCertificate.IsStaging ||
             string.Equals(activeCertificate.Status, "Deleted", StringComparison.OrdinalIgnoreCase)))
        {
            domain.ActiveCertificateId = null;
            activeCertificate = null;
        }

        var hasMainCertificate = await _db.Certificates
            .AnyAsync(
                x => x.IsMainPlatformCertificate &&
                     !x.IsStaging &&
                     x.Status != "Deleted",
                cancellationToken);

        if (domain.ActiveCertificateId is null &&
            CertificateEligibleForDomainUse(certificate, DateTime.UtcNow))
        {
            domain.ActiveCertificateId = certificate.Id;
            activeCertificate = certificate;
        }

        if (domain.IsMainPlatformDomain &&
            !hasMainCertificate &&
            CertificateEligibleForDomainUse(certificate, DateTime.UtcNow))
        {
            certificate.IsMainPlatformCertificate = true;
            certificate.IsActive = true;
            domain.ActiveCertificateId = certificate.Id;
            activeCertificate = certificate;
        }

        domain.Status = ResolveDomainStatus(domain, activeCertificate, DateTime.UtcNow);
        domain.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        var enriched = metadata with
        {
            IsMainPlatformCertificate = certificate.IsMainPlatformCertificate,
            IsInUse = domain.ActiveCertificateId == certificate.Id,
            Purpose = domain.Purpose
        };

        await _storage.SaveMetadataAsync(enriched, cancellationToken);

        _logger.LogInformation(
            "Registered certificate {CertificateId} under domain {BaseDomain}. MainDomain={IsMainDomain}. MainCert={IsMainCert}.",
            certificate.CertificateId,
            domain.BaseDomain,
            domain.IsMainPlatformDomain,
            certificate.IsMainPlatformCertificate);

        return enriched;
    }

    public async Task SyncStorageAsync(CancellationToken cancellationToken)
    {
        var metadata = await _storage.ListAsync(cancellationToken);

        _logger.LogInformation(
            "Domain registry sync found {Count} certificate metadata item(s).",
            metadata.Count);

        foreach (var item in metadata)
        {
            _logger.LogInformation(
                "Syncing certificate {CertificateId} for {Domain} / zone {Zone}.",
                item.CertificateId,
                item.Domain,
                item.Zone);

            await RegisterCertificateAsync(item, cancellationToken);
        }

        await ReconcileMainPlatformCertificateSelectionAsync(cancellationToken);
    }

    private async Task ReconcileMainPlatformCertificateSelectionAsync(
        CancellationToken cancellationToken)
    {
        var mainCertificates = await _db.Certificates
            .Include(x => x.Domain)
            .Where(x => x.IsMainPlatformCertificate && !x.IsStaging && x.Status != "Deleted")
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (mainCertificates.Count == 0)
        {
            return;
        }

        if (mainCertificates.Count > 1)
        {
            _logger.LogWarning(
                "Domain registry contains {Count} certificates marked as the main platform certificate; automatic selection reconciliation was skipped.",
                mainCertificates.Count);
            return;
        }

        var selected = mainCertificates[0];
        var domains = await _db.Domains.ToListAsync(cancellationToken);
        var changed = false;
        var now = DateTime.UtcNow;

        foreach (var domain in domains)
        {
            var domainChanged = false;
            var shouldBeMainDomain = domain.Id == selected.DomainId;
            if (domain.IsMainPlatformDomain != shouldBeMainDomain)
            {
                domain.IsMainPlatformDomain = shouldBeMainDomain;
                domainChanged = true;
            }

            if (shouldBeMainDomain)
            {
                if (domain.ActiveCertificateId != selected.Id)
                {
                    domain.ActiveCertificateId = selected.Id;
                    domainChanged = true;
                }

                if (!string.Equals(domain.Purpose, "platform-main", StringComparison.Ordinal))
                {
                    domain.Purpose = "platform-main";
                    domainChanged = true;
                }

                if (!string.Equals(domain.Status, "Active", StringComparison.OrdinalIgnoreCase))
                {
                    domain.Status = "Active";
                    domainChanged = true;
                }
            }
            else if (string.Equals(domain.Purpose, "platform-main", StringComparison.Ordinal))
            {
                domain.Purpose = "unknown";
                domainChanged = true;
            }

            if (domainChanged)
            {
                domain.UpdatedAtUtc = now;
                changed = true;
            }
        }

        if (!selected.IsActive)
        {
            selected.IsActive = true;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reconciled main platform certificate {CertificateId} as the selected certificate for domain {BaseDomain}.",
            selected.CertificateId,
            selected.Domain.BaseDomain);
    }

    public async Task<IReadOnlyList<StoredCertificateMetadata>> ListCertificatesAsync(
        CancellationToken cancellationToken)
    {
        await SyncStorageAsync(cancellationToken);

        var metadata = await _storage.ListAsync(cancellationToken);
        var certificateIds = metadata.Select(x => x.CertificateId).ToArray();

        var registryItems = await _db.Certificates
            .Include(x => x.Domain)
            .Where(x => certificateIds.Contains(x.CertificateId))
            .ToListAsync(cancellationToken);

        var registry = registryItems.ToDictionary(
            x => x.CertificateId,
            x => x,
            StringComparer.OrdinalIgnoreCase);

        return metadata
            .Select(item =>
            {
                if (!registry.TryGetValue(item.CertificateId, out var certificate))
                {
                    return item;
                }

                return item with
                {
                    IsMainPlatformCertificate = certificate.IsMainPlatformCertificate,
                    IsInUse = certificate.Domain.ActiveCertificateId == certificate.Id,
                    Purpose = certificate.Domain.Purpose
                };
            })
            .OrderByDescending(x => x.IsMainPlatformCertificate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToList();
    }

    public async Task<StoredCertificateMetadata?> SetMainPlatformCertificateAsync(
        string certificateId,
        CancellationToken cancellationToken)
    {
        await SyncStorageAsync(cancellationToken);

        var certificate = await _db.Certificates
            .Include(x => x.Domain)
            .FirstOrDefaultAsync(x => x.CertificateId == certificateId, cancellationToken);

        if (certificate is null)
        {
            var metadata = await _storage.GetMetadataAsync(certificateId, cancellationToken);
            if (metadata is null)
            {
                return null;
            }

            await RegisterCertificateAsync(metadata, cancellationToken);

            certificate = await _db.Certificates
                .Include(x => x.Domain)
                .FirstOrDefaultAsync(x => x.CertificateId == certificateId, cancellationToken);

            if (certificate is null)
            {
                return null;
            }
        }

        if (!CertificateEligibleForDomainUse(certificate, DateTime.UtcNow))
        {
            return null;
        }

        var domains = await _db.Domains.ToListAsync(cancellationToken);
        var certificates = await _db.Certificates.ToListAsync(cancellationToken);

        foreach (var domain in domains)
        {
            domain.IsMainPlatformDomain = false;
            if (domain.Purpose == "platform-main")
            {
                domain.Purpose = "unknown";
            }
            domain.UpdatedAtUtc = DateTime.UtcNow;
        }

        foreach (var cert in certificates)
        {
            cert.IsMainPlatformCertificate = false;
        }

        certificate.Domain.IsMainPlatformDomain = true;
        certificate.Domain.Purpose = "platform-main";
        certificate.Domain.Status = "Active";
        certificate.Domain.ActiveCertificateId = certificate.Id;
        certificate.Domain.UpdatedAtUtc = DateTime.UtcNow;

        certificate.IsMainPlatformCertificate = true;
        certificate.IsActive = true;
        certificate.Status = certificate.Status == "Deleted" ? "Valid" : certificate.Status;

        await _db.SaveChangesAsync(cancellationToken);

        var registryUsageRows = await _db.Certificates
            .AsNoTracking()
            .Include(x => x.Domain)
            .Select(x => new
            {
                x.CertificateId,
                IsInUse = x.Domain.ActiveCertificateId == x.Id
            })
            .ToListAsync(cancellationToken);

        var registryUsage = registryUsageRows.ToDictionary(
            x => x.CertificateId,
            x => x.IsInUse,
            StringComparer.OrdinalIgnoreCase);

        var allMetadata = await _storage.ListAsync(cancellationToken);
        foreach (var item in allMetadata)
        {
            var updated = item with
            {
                IsMainPlatformCertificate = string.Equals(item.CertificateId, certificateId, StringComparison.OrdinalIgnoreCase),
                IsInUse = registryUsage.TryGetValue(item.CertificateId, out var isInUse)
                    ? isInUse
                    : item.IsInUse,
                Purpose = string.Equals(item.CertificateId, certificateId, StringComparison.OrdinalIgnoreCase)
                    ? "platform-main"
                    : item.Purpose
            };

            await _storage.SaveMetadataAsync(updated, cancellationToken);
        }

        var selectedMetadata = await _storage.GetMetadataAsync(certificateId, cancellationToken);
        return selectedMetadata is null
            ? null
            : selectedMetadata with
            {
                IsMainPlatformCertificate = true,
                IsInUse = true,
                Purpose = "platform-main"
            };
    }

    public Task<DomainActiveCertificateSelectionResult> SetDomainActiveCertificateAsync(
        Guid domainId,
        string certificateId,
        CancellationToken cancellationToken) =>
        SetDomainActiveCertificateCoreAsync(domainId, certificateId, cancellationToken);

    private async Task<DomainActiveCertificateSelectionResult> SetDomainActiveCertificateCoreAsync(
        Guid expectedDomainId,
        string certificateId,
        CancellationToken cancellationToken)
    {
        await SyncStorageAsync(cancellationToken);

        var certificate = await _db.Certificates
            .Include(x => x.Domain)
            .FirstOrDefaultAsync(x => x.CertificateId == certificateId, cancellationToken);

        if (certificate is null || certificate.DomainId != expectedDomainId)
        {
            return new DomainActiveCertificateSelectionResult(
                Succeeded: false,
                Status: "NotFound",
                Message: $"Certificate '{certificateId}' was not found for domain '{expectedDomainId}'.",
                CertificateId: certificateId,
                BaseDomain: null);
        }

        var now = DateTime.UtcNow;
        var validStatus =
            string.Equals(certificate.Status, "Valid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(certificate.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(certificate.Status, "Active", StringComparison.OrdinalIgnoreCase);

        if (!certificate.IsActive ||
            !validStatus ||
            certificate.ExpiresAtUtc is null ||
            certificate.ExpiresAtUtc <= now)
        {
            return new DomainActiveCertificateSelectionResult(
                Succeeded: false,
                Status: "Blocked",
                Message: "This certificate is not currently valid for domain use.",
                CertificateId: certificate.CertificateId,
                BaseDomain: certificate.Domain.BaseDomain);
        }

        if (certificate.IsStaging)
        {
            return new DomainActiveCertificateSelectionResult(
                Succeeded: false,
                Status: "Blocked",
                Message: "Let's Encrypt staging certificates are for ACME/DNS workflow testing only and cannot be made active for a Domain.",
                CertificateId: certificate.CertificateId,
                BaseDomain: certificate.Domain.BaseDomain);
        }

        if (certificate.Domain.IsMainPlatformDomain && !certificate.IsMainPlatformCertificate)
        {
            return new DomainActiveCertificateSelectionResult(
                Succeeded: false,
                Status: "Blocked",
                Message: "The main MEM domain must use the main platform certificate. Set this certificate as the main platform certificate instead.",
                CertificateId: certificate.CertificateId,
                BaseDomain: certificate.Domain.BaseDomain);
        }

        var alreadySelected = certificate.Domain.ActiveCertificateId == certificate.Id;

        certificate.Domain.ActiveCertificateId = certificate.Id;
        certificate.Domain.Status = ResolveDomainStatus(certificate.Domain, certificate, now);
        certificate.Domain.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(cancellationToken);
        await PersistDomainCertificateUsageAsync(certificate.DomainId, cancellationToken);

        _logger.LogInformation(
            "Selected certificate {CertificateId} as the active certificate for domain {BaseDomain}.",
            certificate.CertificateId,
            certificate.Domain.BaseDomain);

        return new DomainActiveCertificateSelectionResult(
            Succeeded: true,
            Status: alreadySelected ? "AlreadyActive" : "Succeeded",
            Message: alreadySelected
                ? $"Certificate '{certificate.CertificateId}' is already active for {certificate.Domain.BaseDomain}."
                : $"Certificate '{certificate.CertificateId}' is now active for {certificate.Domain.BaseDomain}.",
            CertificateId: certificate.CertificateId,
            BaseDomain: certificate.Domain.BaseDomain);
    }

    private async Task PersistDomainCertificateUsageAsync(
        Guid domainId,
        CancellationToken cancellationToken)
    {
        var domain = await _db.Domains
            .AsNoTracking()
            .Where(x => x.Id == domainId)
            .Select(x => new { x.ActiveCertificateId })
            .SingleAsync(cancellationToken);

        var certificateRows = await _db.Certificates
            .AsNoTracking()
            .Where(x => x.DomainId == domainId)
            .Select(x => new { x.CertificateId, x.Id })
            .ToListAsync(cancellationToken);

        var certificateIds = certificateRows.ToDictionary(
            x => x.CertificateId,
            x => x.Id,
            StringComparer.OrdinalIgnoreCase);

        var metadata = await _storage.ListAsync(cancellationToken);
        foreach (var item in metadata)
        {
            if (!certificateIds.TryGetValue(item.CertificateId, out var entityId))
            {
                continue;
            }

            var shouldBeInUse = domain.ActiveCertificateId == entityId;
            if (item.IsInUse == shouldBeInUse)
            {
                continue;
            }

            await _storage.SaveMetadataAsync(
                item with { IsInUse = shouldBeInUse },
                cancellationToken);
        }
    }

    internal static string ResolveDomainStatus(
        DomainEntity domain,
        CertificateEntity? activeCertificate,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(domain);

        if (activeCertificate is not null &&
            CertificateEligibleForDomainUse(activeCertificate, nowUtc))
        {
            return "Active";
        }

        return string.IsNullOrWhiteSpace(domain.Status)
            ? "Pending"
            : domain.Status;
    }

    private static bool CertificateEligibleForDomainUse(
        CertificateEntity certificate,
        DateTime nowUtc)
    {
        var validStatus =
            string.Equals(certificate.Status, "Valid", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(certificate.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(certificate.Status, "Active", StringComparison.OrdinalIgnoreCase);

        return !certificate.IsStaging &&
               certificate.IsActive &&
               !string.Equals(certificate.Status, "Deleted", StringComparison.OrdinalIgnoreCase) &&
               certificate.ExpiresAtUtc is not null &&
               certificate.ExpiresAtUtc > nowUtc &&
               validStatus;
    }

    private static string NormalizeBaseDomain(string zone, string domain)
    {
        if (!string.IsNullOrWhiteSpace(zone))
        {
            return zone.Trim().Trim('.').ToLowerInvariant();
        }

        return domain
            .Trim()
            .ToLowerInvariant()
            .Replace("https://", string.Empty)
            .Replace("http://", string.Empty)
            .TrimStart('*')
            .TrimStart('.')
            .TrimEnd('/');
    }

    private static string NormalizeCertificateStatus(string status)
    {
        return string.Equals(status, "Succeeded", StringComparison.OrdinalIgnoreCase)
            ? "Valid"
            : string.IsNullOrWhiteSpace(status)
                ? "Unknown"
                : status;
    }
}