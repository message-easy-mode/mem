using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Certificates.Npm;

namespace Modules.Operator.Domains;

public sealed class OperatorDomainCertificateDeletionService(
    MemDbContext db,
    CertificateStorageService certificateStorage,
    INpmCertificateDeletionService npmCertificateDeletion,
    ILogger<OperatorDomainCertificateDeletionService> logger)
{
    private static readonly HashSet<string> RetiredRuntimeStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "destroyed",
            "destroyed_with_warnings",
            "deleted",
            "removed",
            "retired"
        };

    public async Task<DeleteCertificateResponse> DeleteAsync(
        Guid domainId,
        string certificateId,
        CancellationToken cancellationToken)
    {
        var certificate = await db.Certificates
            .Include(x => x.Domain)
            .FirstOrDefaultAsync(
                x => x.DomainId == domainId &&
                     x.CertificateId == certificateId &&
                     x.Status != "Deleted",
                cancellationToken);

        if (certificate is null)
        {
            return NotFound(certificateId, domainId);
        }

        var domain = certificate.Domain;
        var selectedForDomain = domain.ActiveCertificateId == certificate.Id;

        var metadata = await certificateStorage.GetMetadataAsync(
            certificate.CertificateId,
            cancellationToken);

        if (certificate.IsMainPlatformCertificate ||
            metadata?.IsMainPlatformCertificate == true ||
            (domain.IsMainPlatformDomain && selectedForDomain))
        {
            return Blocked(
                certificate.CertificateId,
                "The main platform certificate cannot be deleted. Move the main platform role first.",
                "Main platform certificate delete was blocked.");
        }

        if (selectedForDomain)
        {
            var runtimeReferences = await db.RuntimeStacks
                .AsNoTracking()
                .Where(stack =>
                    stack.DomainId == domainId ||
                    stack.BaseDomain == domain.BaseDomain)
                .ToListAsync(cancellationToken);

            var liveRuntimeReferences = runtimeReferences
                .Where(stack => !IsRetiredRuntimeStatus(stack.Status))
                .OrderBy(stack => stack.Slug, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (liveRuntimeReferences.Length > 0)
            {
                var slugs = liveRuntimeReferences
                    .Select(stack => stack.Slug)
                    .Where(slug => !string.IsNullOrWhiteSpace(slug))
                    .ToArray();

                return Blocked(
                    certificate.CertificateId,
                    slugs.Length == 0
                        ? "This certificate is active for a Domain that is still used by one or more chat servers. Activate a replacement certificate first."
                        : $"This certificate is active for a Domain still used by chat server(s): {string.Join(", ", slugs)}. Activate a replacement certificate first.",
                    "Live chat-server certificate dependency exists.");
            }
        }

        var warnings = new List<string>();
        var npmDeleted = false;

        if (certificate.NpmCertificateId is > 0)
        {
            var sharedNpmReference = await db.Certificates
                .AsNoTracking()
                .AnyAsync(
                    other =>
                        other.Id != certificate.Id &&
                        other.Status != "Deleted" &&
                        other.NpmCertificateId == certificate.NpmCertificateId,
                    cancellationToken);

            if (sharedNpmReference)
            {
                return Blocked(
                    certificate.CertificateId,
                    $"NPM certificate {certificate.NpmCertificateId.Value} is also referenced by another MEM certificate record and cannot be deleted safely.",
                    "Shared NPM certificate reference exists.");
            }

            var npmResult = await npmCertificateDeletion.DeleteIfUnusedAsync(
                certificate.NpmCertificateId.Value,
                cancellationToken);

            if (!npmResult.Succeeded)
            {
                var consumerDetail = npmResult.ConsumerProxyHostIds.Count > 0
                    ? $" Proxy host(s): {string.Join(", ", npmResult.ConsumerProxyHostIds)}."
                    : string.Empty;

                return new DeleteCertificateResponse(
                    Succeeded: false,
                    Status: npmResult.Status == "Blocked" ? "Blocked" : "Failed",
                    Message: $"{npmResult.Message}{consumerDetail}",
                    CertificateId: certificate.CertificateId,
                    MetadataDeleted: false,
                    FilesDeleted: false,
                    NpmCertificateDeleted: false,
                    Warnings: ["NPM certificate cleanup did not complete."]);
            }

            npmDeleted = string.Equals(
                npmResult.Status,
                "Deleted",
                StringComparison.OrdinalIgnoreCase);

            if (string.Equals(
                    npmResult.Status,
                    "AlreadyAbsent",
                    StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add("The NPM certificate record was already absent.");
            }
        }

        var metadataDeleted = false;
        var filesDeleted = false;

        if (metadata is null)
        {
            warnings.Add("Certificate storage was already absent.");
        }
        else
        {
            var storageResult = await certificateStorage.DeleteAsync(
                certificate.CertificateId,
                deleteFiles: true,
                deleteNpmCertificate: false,
                force: false,
                cancellationToken);

            if (!storageResult.Succeeded)
            {
                logger.LogWarning(
                    "Certificate deletion stopped after storage cleanup failed. DomainId={DomainId} CertificateId={CertificateId}",
                    domainId,
                    certificate.CertificateId);

                return new DeleteCertificateResponse(
                    Succeeded: false,
                    Status: "Failed",
                    Message: "Certificate storage cleanup failed. The certificate registry record was not deleted.",
                    CertificateId: certificate.CertificateId,
                    MetadataDeleted: storageResult.MetadataDeleted,
                    FilesDeleted: storageResult.FilesDeleted,
                    NpmCertificateDeleted: npmDeleted,
                    Warnings: storageResult.Warnings);
            }

            metadataDeleted = storageResult.MetadataDeleted;
            filesDeleted = storageResult.FilesDeleted;
            warnings.AddRange(storageResult.Warnings);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (selectedForDomain)
        {
            domain.ActiveCertificateId = null;
            domain.Status = "Pending";
            domain.UpdatedAtUtc = DateTime.UtcNow;

            // Break Domain.ActiveCertificateId -> Certificate -> Domain before the row delete.
            await db.SaveChangesAsync(cancellationToken);
        }

        db.Certificates.Remove(certificate);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Deleted certificate {CertificateId} from Domain {DomainId}. WasSelected={WasSelected}",
            certificate.CertificateId,
            domainId,
            selectedForDomain);

        return new DeleteCertificateResponse(
            Succeeded: true,
            Status: "Deleted",
            Message: selectedForDomain
                ? "Certificate was deleted and the Domain no longer has an active certificate."
                : "Certificate was deleted from the Domain.",
            CertificateId: certificate.CertificateId,
            MetadataDeleted: metadataDeleted,
            FilesDeleted: filesDeleted,
            NpmCertificateDeleted: npmDeleted,
            Warnings: warnings);
    }

    private static DeleteCertificateResponse NotFound(string certificateId, Guid domainId) =>
        new(
            Succeeded: false,
            Status: "NotFound",
            Message: $"Certificate '{certificateId}' was not found for domain '{domainId}'.",
            CertificateId: certificateId,
            MetadataDeleted: false,
            FilesDeleted: false,
            NpmCertificateDeleted: false,
            Warnings: []);

    private static DeleteCertificateResponse Blocked(
        string certificateId,
        string message,
        string warning) =>
        new(
            Succeeded: false,
            Status: "Blocked",
            Message: message,
            CertificateId: certificateId,
            MetadataDeleted: false,
            FilesDeleted: false,
            NpmCertificateDeleted: false,
            Warnings: [warning]);

    private static bool IsRetiredRuntimeStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status) &&
        RetiredRuntimeStatuses.Contains(status.Trim());
}
