using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Shared.Domains.Certificates.Npm;

namespace HostAgent.Runtime.Migrations.ProductionAdoption;

/// <summary>
/// Resolves one active MEM-managed certificate that covers every planned migration host and
/// reconciles its live NPM certificate identity before any route mutation is allowed.
/// </summary>
public sealed class MigrationProductionCertificateResolver(
    MemDbContext db,
    NpmCertificateImportProbe npmCertificateProbe,
    ILogger<MigrationProductionCertificateResolver> logger)
{
    public async Task<MigrationProductionCertificateResolution> ResolveAsync(
        IReadOnlyCollection<string> publicHosts,
        CancellationToken ct)
    {
        var hosts = publicHosts
            .Select(NormalizeHost)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (hosts.Length == 0)
        {
            return MigrationProductionCertificateResolution.Blocked(
                "The adoption plan does not contain a public hostname that can be matched to a certificate.");
        }

        var certificates = await db.Certificates
            .Include(x => x.Domain)
            .Where(x => x.IsActive && x.Status != "Deleted")
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var certificate = SelectCertificate(certificates, hosts, now);
        if (certificate is null)
        {
            return MigrationProductionCertificateResolution.Blocked(
                $"No active, unexpired MEM-managed certificate covers all planned migration hosts: {string.Join(", ", hosts)}.");
        }

        var probe = await npmCertificateProbe.ProbeAsync(certificate.CertificateId, ct);
        if (!probe.Succeeded || probe.MatchingCertificateId is not > 0)
        {
            var detail = string.IsNullOrWhiteSpace(probe.Message)
                ? "The certificate is not visible in Nginx Proxy Manager."
                : probe.Message;
            return MigrationProductionCertificateResolution.Blocked(
                $"Active certificate '{certificate.CommonName}' is not ready in Nginx Proxy Manager. {detail} Use Certificate tools to import it, then prepare a fresh route snapshot.");
        }

        var npmCertificateId = probe.MatchingCertificateId.Value;
        var changed = false;
        if (certificate.NpmCertificateId != npmCertificateId)
        {
            certificate.NpmCertificateId = npmCertificateId;
            changed = true;
        }
        if (!certificate.ImportedToNpm)
        {
            certificate.ImportedToNpm = true;
            changed = true;
        }
        if (certificate.Domain.ActiveCertificateId != certificate.Id)
        {
            certificate.Domain.ActiveCertificateId = certificate.Id;
            changed = true;
        }
        if (!string.Equals(certificate.Domain.Status, "Active", StringComparison.OrdinalIgnoreCase))
        {
            certificate.Domain.Status = "Active";
            changed = true;
        }
        if (changed)
        {
            certificate.Domain.UpdatedAtUtc = now;
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Reconciled migration certificate {CertificateId} to NPM certificate {NpmCertificateId} for domain {BaseDomain}.",
                certificate.CertificateId,
                npmCertificateId,
                certificate.Domain.BaseDomain);
        }

        return new MigrationProductionCertificateResolution(
            Ready: true,
            CertificateRecordId: certificate.CertificateId,
            CertificateName: certificate.CommonName,
            NpmCertificateId: npmCertificateId,
            BaseDomain: certificate.Domain.BaseDomain,
            Blockers: []);
    }

    internal static CertificateEntity? SelectCertificate(
        IEnumerable<CertificateEntity> certificates,
        IReadOnlyCollection<string> publicHosts,
        DateTime nowUtc)
    {
        var hosts = publicHosts
            .Select(NormalizeHost)
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return certificates
            .Where(x => IsCertificateUsable(x, nowUtc))
            .Where(x => hosts.All(host => CertificateCoversHost(x, host)))
            .OrderByDescending(x => x.Domain.ActiveCertificateId == x.Id)
            .ThenByDescending(x => x.Domain.BaseDomain?.Length ?? 0)
            .ThenByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
    }

    internal static bool IsCertificateUsable(CertificateEntity certificate, DateTime nowUtc) =>
        certificate.IsActive &&
        certificate.Status != "Deleted" &&
        certificate.ExpiresAtUtc is not null &&
        certificate.ExpiresAtUtc > nowUtc &&
        (string.Equals(certificate.Status, "Valid", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(certificate.Status, "Succeeded", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(certificate.Status, "Active", StringComparison.OrdinalIgnoreCase));

    internal static bool CertificateCoversHost(CertificateEntity certificate, string publicHost)
    {
        var host = NormalizeHost(publicHost);
        var commonName = NormalizeHost(certificate.CommonName);
        if (host.Length == 0 || commonName.Length == 0)
        {
            return false;
        }

        if (string.Equals(host, commonName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!certificate.IsWildcard || !commonName.StartsWith("*.", StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = commonName[2..];
        if (!host.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hostLabels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var suffixLabels = suffix.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return hostLabels.Length == suffixLabels.Length + 1;
    }

    private static string NormalizeHost(string? value) =>
        value?.Trim().TrimEnd('.').ToLowerInvariant() ?? string.Empty;
}

public sealed record MigrationProductionCertificateResolution(
    bool Ready,
    string? CertificateRecordId,
    string? CertificateName,
    int? NpmCertificateId,
    string? BaseDomain,
    IReadOnlyList<string> Blockers)
{
    public static MigrationProductionCertificateResolution Blocked(string blocker) =>
        new(false, null, null, null, null, [blocker]);
}
