using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostAgent.Platform;

public sealed record PlatformDomainResolution(
    Guid DomainId,
    string BaseDomain,
    string DisplayName,
    Guid? ActiveCertificateId,
    int? ActiveNpmCertificateId);

public sealed class PlatformDomainResolver
{
    private readonly MemDbContext _db;

    public PlatformDomainResolver(MemDbContext db)
    {
        _db = db;
    }

    public async Task<PlatformDomainResolution> ResolveMainPlatformDomainAsync(
        string? requestedDomainId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedDomainId))
        {
            if (!Guid.TryParse(requestedDomainId, out var domainId))
            {
                throw new InvalidOperationException(
                    $"Requested domain id '{requestedDomainId}' is not a valid GUID.");
            }

            var requested = await _db.Domains
                .AsNoTracking()
                .Where(x => x.Id == domainId && x.Status == "Active")
                .Select(x => new
                {
                    x.Id,
                    x.BaseDomain,
                    x.DisplayName,
                    x.ActiveCertificateId
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (requested is null)
            {
                throw new InvalidOperationException(
                    $"Requested active domain '{requestedDomainId}' was not found.");
            }

            var requestedNpmCertificateId = await ResolveActiveNpmCertificateIdAsync(
                requested.Id,
                requested.ActiveCertificateId,
                cancellationToken);

            return new PlatformDomainResolution(
                DomainId: requested.Id,
                BaseDomain: requested.BaseDomain,
                DisplayName: requested.DisplayName,
                ActiveCertificateId: requested.ActiveCertificateId,
                ActiveNpmCertificateId: requestedNpmCertificateId);
        }

        var main = await _db.Domains
            .AsNoTracking()
            .Where(x => x.IsMainPlatformDomain && x.Status == "Active")
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.BaseDomain,
                x.DisplayName,
                x.ActiveCertificateId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (main is null)
        {
            throw new InvalidOperationException(
                "No active main platform domain is configured. Return to the Domain setup step and configure a platform domain first.");
        }

        var mainNpmCertificateId = await ResolveActiveNpmCertificateIdAsync(
            main.Id,
            main.ActiveCertificateId,
            cancellationToken);

        return new PlatformDomainResolution(
            DomainId: main.Id,
            BaseDomain: main.BaseDomain,
            DisplayName: main.DisplayName,
            ActiveCertificateId: main.ActiveCertificateId,
            ActiveNpmCertificateId: mainNpmCertificateId);
    }

    private async Task<int?> ResolveActiveNpmCertificateIdAsync(
        Guid domainId,
        Guid? activeCertificateId,
        CancellationToken cancellationToken)
    {
        if (activeCertificateId is not null)
        {
            var activeCertificateNpmId = await _db.Certificates
                .AsNoTracking()
                .Where(x =>
                    x.Id == activeCertificateId.Value &&
                    x.DomainId == domainId &&
                    x.ImportedToNpm &&
                    x.NpmCertificateId != null)
                .Select(x => x.NpmCertificateId)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeCertificateNpmId is not null)
            {
                return activeCertificateNpmId;
            }
        }

        return await _db.Certificates
            .AsNoTracking()
            .Where(x =>
                x.DomainId == domainId &&
                x.IsActive &&
                x.ImportedToNpm &&
                x.NpmCertificateId != null)
            .OrderByDescending(x => x.IsMainPlatformCertificate)
            .ThenByDescending(x => x.LastImportedToNpmAtUtc)
            .ThenByDescending(x => x.CreatedAtUtc)
            .Select(x => x.NpmCertificateId)
            .FirstOrDefaultAsync(cancellationToken);
    }
}