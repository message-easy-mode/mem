using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Modules.Operator.Domains;

public sealed class OperatorDomainDeletionService(
    MemDbContext db,
    ILogger<OperatorDomainDeletionService> logger)
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

    public async Task<DeleteOperatorDomainResponse> DeleteAsync(
        Guid domainId,
        bool force,
        CancellationToken cancellationToken)
    {
        var domain = await db.Domains
            .Include(x => x.Certificates)
            .FirstOrDefaultAsync(x => x.Id == domainId, cancellationToken);

        if (domain is null)
        {
            return new DeleteOperatorDomainResponse(
                false,
                "NotFound",
                $"Domain '{domainId}' was not found.",
                domainId,
                []);
        }

        if (force)
        {
            return new DeleteOperatorDomainResponse(
                false,
                "Blocked",
                "Force deletion is not supported for Domains. Remove live dependencies and retry the normal delete.",
                domainId,
                ["Unsafe Domain force-delete was refused."]);
        }

        if (domain.IsMainPlatformDomain)
        {
            return new DeleteOperatorDomainResponse(
                false,
                "Blocked",
                "The main platform domain cannot be deleted. Choose another main platform domain first.",
                domainId,
                ["Main platform domain delete was blocked."]);
        }

        var remainingCertificates = domain.Certificates
            .Where(certificate => !string.Equals(
                certificate.Status,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(certificate => certificate.CreatedAtUtc)
            .ToArray();

        if (remainingCertificates.Length > 0)
        {
            return new DeleteOperatorDomainResponse(
                false,
                "Blocked",
                "Delete this Domain's certificates first, then retry Domain deletion.",
                domainId,
                [$"{remainingCertificates.Length} certificate record(s) still belong to this Domain."]);
        }

        var runtimeReferences = await db.RuntimeStacks
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

            return new DeleteOperatorDomainResponse(
                false,
                "Blocked",
                slugs.Length == 0
                    ? "This domain is still used by one or more chat servers."
                    : $"This domain is still used by chat server(s): {string.Join(", ", slugs)}.",
                domainId,
                ["Chat-server Domain dependency exists."]);
        }

        var retiredCertificateRows = domain.Certificates
            .Where(certificate => string.Equals(
                certificate.Status,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        domain.ActiveCertificateId = null;

        foreach (var retiredStack in runtimeReferences.Where(
                     stack => IsRetiredRuntimeStatus(stack.Status) &&
                              stack.DomainId == domainId))
        {
            retiredStack.DomainId = null;
            retiredStack.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (retiredCertificateRows.Length > 0)
        {
            // Clear any stale ActiveCertificateId reference before physically removing old
            // soft-deleted compatibility rows.
            await db.SaveChangesAsync(cancellationToken);
            db.Certificates.RemoveRange(retiredCertificateRows);
        }

        db.Domains.Remove(domain);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Deleted Domain {DomainId} ({BaseDomain}) after certificate lifecycle cleanup was completed separately.",
            domainId,
            domain.BaseDomain);

        return new DeleteOperatorDomainResponse(
            true,
            "Deleted",
            "Domain was deleted from the MEM registry.",
            domainId,
            []);
    }

    private static bool IsRetiredRuntimeStatus(string? status) =>
        !string.IsNullOrWhiteSpace(status) &&
        RetiredRuntimeStatuses.Contains(status.Trim());
}
