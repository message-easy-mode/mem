using Infrastructure.Data.Entities;
using Modules.Shared.Domains;
using Modules.Shared.Domains.Certificates.Npm;

namespace Api.IntegrationTests.Migrations;

public sealed class CertificateRegistryReadinessTests
{
    [Fact]
    public void Valid_active_certificate_promotes_pending_domain_to_active()
    {
        var now = DateTime.UtcNow;
        var domain = Domain("Pending");
        var certificate = Certificate(domain, now.AddDays(30));

        var status = DomainRegistryService.ResolveDomainStatus(domain, certificate, now);

        Assert.Equal("Active", status);
    }

    [Fact]
    public void Npm_import_link_is_persisted_and_clears_pending_domain_state()
    {
        var now = DateTime.UtcNow;
        var domain = Domain("Pending");
        var certificate = Certificate(domain, now.AddDays(30));

        NpmCertificateImportProbe.ApplyNpmLink(certificate, 3, now);

        Assert.Equal(3, certificate.NpmCertificateId);
        Assert.True(certificate.ImportedToNpm);
        Assert.Equal(now, certificate.LastImportedToNpmAtUtc);
        Assert.Equal("Active", domain.Status);
        Assert.Equal(certificate.Id, domain.ActiveCertificateId);
    }

    private static DomainEntity Domain(string status) => new()
    {
        Id = Guid.NewGuid(),
        BaseDomain = "matrixeasyhost.com",
        DisplayName = "matrixeasyhost.com",
        Purpose = "stack",
        DnsProvider = "desec",
        Status = status,
        CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
        UpdatedAtUtc = DateTime.UtcNow.AddDays(-1),
    };

    private static CertificateEntity Certificate(DomainEntity domain, DateTime expiresAtUtc)
    {
        var certificate = new CertificateEntity
        {
            Id = Guid.NewGuid(),
            Domain = domain,
            DomainId = domain.Id,
            CertificateId = "cert-matrixeasyhost",
            CommonName = "*.matrixeasyhost.com",
            Provider = "desec",
            IsWildcard = true,
            IsActive = true,
            Status = "Valid",
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = expiresAtUtc,
        };
        domain.Certificates.Add(certificate);
        return certificate;
    }
}
