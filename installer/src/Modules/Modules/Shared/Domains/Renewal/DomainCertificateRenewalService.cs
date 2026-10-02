using System.Net.Mail;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Shared.Domains.Dns;

namespace Modules.Shared.Domains.Renewal;

public sealed class DomainCertificateRenewalService
{
    public const int DefaultRenewalWindowDays = 30;
    public const int DefaultRetryIntervalHours = 24;

    private readonly MemDbContext _db;
    private readonly IDomainSecretStore _secretStore;
    private readonly IDnsZoneAccessProbe _dnsZoneAccessProbe;
    private readonly ILogger<DomainCertificateRenewalService> _logger;

    public DomainCertificateRenewalService(
        MemDbContext db,
        IDomainSecretStore secretStore,
        IDnsZoneAccessProbe dnsZoneAccessProbe,
        ILogger<DomainCertificateRenewalService> logger)
    {
        _db = db;
        _secretStore = secretStore;
        _dnsZoneAccessProbe = dnsZoneAccessProbe;
        _logger = logger;
    }

    public async Task<IReadOnlyList<DomainCertificateRenewalState>> ListAsync(
        CancellationToken cancellationToken)
    {
        var domainIds = await _db.Domains
            .AsNoTracking()
            .OrderByDescending(x => x.IsMainPlatformDomain)
            .ThenBy(x => x.BaseDomain)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var results = new List<DomainCertificateRenewalState>(domainIds.Count);
        foreach (var domainId in domainIds)
        {
            var state = await GetAsync(domainId, cancellationToken);
            if (state is not null)
            {
                results.Add(state);
            }
        }

        return results;
    }

    public async Task<DomainCertificateRenewalState?> GetAsync(
        Guid domainId,
        CancellationToken cancellationToken)
    {
        var domain = await _db.Domains
            .AsNoTracking()
            .Include(x => x.CertificateRenewalPolicy)
            .Include(x => x.ActiveCertificate)
            .FirstOrDefaultAsync(x => x.Id == domainId, cancellationToken);

        if (domain is null)
        {
            return null;
        }

        var credential = await _db.DomainSecrets
            .AsNoTracking()
            .Where(x =>
                x.DomainId == domainId &&
                x.Category == DomainRenewalSecretNames.DnsProviderCategory &&
                x.Key == DomainRenewalSecretNames.DesecProviderToken)
            .Select(x => new { x.UpdatedAtUtc })
            .SingleOrDefaultAsync(cancellationToken);

        return ToState(domain, credential is not null, credential?.UpdatedAtUtc);
    }

    public async Task<DomainRenewalCredentialMutationResult> EnrollOrRotateCredentialAsync(
        Guid domainId,
        string providerToken,
        string? acmeEmail,
        CancellationToken cancellationToken)
    {
        var domain = await _db.Domains
            .AsNoTracking()
            .Include(x => x.CertificateRenewalPolicy)
            .FirstOrDefaultAsync(x => x.Id == domainId, cancellationToken);

        if (domain is null)
        {
            return new(false, "NotFound", $"Domain '{domainId}' was not found.", null);
        }

        if (!string.Equals(domain.DnsProvider, "desec", StringComparison.OrdinalIgnoreCase))
        {
            return new(false, "UnsupportedDnsProvider", "Automatic renewal credential enrollment currently supports deSEC only.", await GetAsync(domainId, cancellationToken));
        }

        var token = providerToken?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(false, "DesecTokenMissing", "deSEC renewal credential is required.", await GetAsync(domainId, cancellationToken));
        }

        var email = NormalizeEmail(acmeEmail ?? domain.CertificateRenewalPolicy?.AcmeEmail);
        if (email is null)
        {
            return new(false, "AcmeEmailInvalid", "A valid ACME contact email is required before automatic renewal can be configured.", await GetAsync(domainId, cancellationToken));
        }

        var zone = string.IsNullOrWhiteSpace(domain.DnsZone) ? domain.BaseDomain : domain.DnsZone!;
        var probe = await _dnsZoneAccessProbe.ProbeAsync(
            new DnsZoneAccessProbeRequest(zone, token),
            cancellationToken);

        if (!probe.Succeeded)
        {
            return new(
                false,
                probe.ErrorCode ?? "DnsProviderCredentialRejected",
                probe.Message,
                await GetAsync(domainId, cancellationToken));
        }

        await ConfigureFromSuccessfulProductionIssuanceAsync(
            domainId,
            email,
            token,
            cancellationToken);

        return new(
            true,
            "Configured",
            "The deSEC renewal credential was verified and stored for automatic certificate renewal.",
            await GetAsync(domainId, cancellationToken));
    }

    public async Task<DomainCertificateRenewalState?> UpdatePolicyAsync(
        Guid domainId,
        bool autoRenewEnabled,
        string? acmeEmail,
        CancellationToken cancellationToken)
    {
        var domainExists = await _db.Domains
            .AsNoTracking()
            .AnyAsync(x => x.Id == domainId, cancellationToken);

        if (!domainExists)
        {
            return null;
        }

        var email = NormalizeEmail(acmeEmail);
        if (autoRenewEnabled && email is null)
        {
            throw new ArgumentException("A valid ACME contact email is required when automatic renewal is enabled.", nameof(acmeEmail));
        }

        var now = DateTime.UtcNow;
        var policy = await _db.DomainCertificateRenewalPolicies
            .FirstOrDefaultAsync(x => x.DomainId == domainId, cancellationToken);

        if (policy is null)
        {
            policy = new DomainCertificateRenewalPolicyEntity
            {
                DomainId = domainId,
                AutoRenewEnabled = autoRenewEnabled,
                AcmeEmail = email,
                RenewalWindowDays = DefaultRenewalWindowDays,
                RetryIntervalHours = DefaultRetryIntervalHours,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _db.DomainCertificateRenewalPolicies.Add(policy);
        }
        else
        {
            policy.AutoRenewEnabled = autoRenewEnabled;
            policy.AcmeEmail = email;
            policy.RenewalWindowDays = DefaultRenewalWindowDays;
            policy.RetryIntervalHours = DefaultRetryIntervalHours;
            policy.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return await GetAsync(domainId, cancellationToken);
    }

    public async Task<DomainCertificateRenewalState?> RemoveCredentialAsync(
        Guid domainId,
        CancellationToken cancellationToken)
    {
        if (!await _db.Domains.AsNoTracking().AnyAsync(x => x.Id == domainId, cancellationToken))
        {
            return null;
        }

        await _secretStore.DeleteAsync(
            domainId,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            cancellationToken);

        return await GetAsync(domainId, cancellationToken);
    }

    public async Task ConfigureFromSuccessfulProductionIssuanceAsync(
        Guid domainId,
        string acmeEmail,
        string providerToken,
        CancellationToken cancellationToken)
    {
        var domain = await _db.Domains
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == domainId, cancellationToken)
            ?? throw new InvalidOperationException($"Domain '{domainId}' was not found while configuring automatic renewal.");

        if (!string.Equals(domain.DnsProvider, "desec", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Automatic renewal configuration currently supports deSEC only.");
        }

        var email = NormalizeEmail(acmeEmail)
            ?? throw new InvalidOperationException("A valid ACME contact email is required for automatic renewal.");
        var token = providerToken?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("A deSEC provider credential is required for automatic renewal.");
        }

        await _secretStore.SetProtectedAsync(
            domainId,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            token,
            "deSEC DNS credential for unattended production certificate renewal",
            cancellationToken);

        // Verify that the current key ring can resolve exactly what was persisted before
        // any caller is allowed to delete a temporary Setup-scoped copy.
        var verified = await _secretStore.ResolveProtectedAsync(
            domainId,
            DomainRenewalSecretNames.DnsProviderCategory,
            DomainRenewalSecretNames.DesecProviderToken,
            cancellationToken);

        if (!string.Equals(verified, token, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The protected Domain renewal credential could not be verified after persistence.");
        }

        var now = DateTime.UtcNow;
        var policy = await _db.DomainCertificateRenewalPolicies
            .FirstOrDefaultAsync(x => x.DomainId == domainId, cancellationToken);

        if (policy is null)
        {
            policy = new DomainCertificateRenewalPolicyEntity
            {
                DomainId = domainId,
                AutoRenewEnabled = true,
                AcmeEmail = email,
                RenewalWindowDays = DefaultRenewalWindowDays,
                RetryIntervalHours = DefaultRetryIntervalHours,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _db.DomainCertificateRenewalPolicies.Add(policy);
        }
        else
        {
            policy.AutoRenewEnabled = true;
            policy.AcmeEmail = email;
            policy.RenewalWindowDays = DefaultRenewalWindowDays;
            policy.RetryIntervalHours = DefaultRetryIntervalHours;
            policy.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Configured protected automatic certificate renewal authority for domain {DomainId}. Provider={Provider} RenewalWindowDays={RenewalWindowDays} RetryIntervalHours={RetryIntervalHours}",
            domainId,
            domain.DnsProvider,
            DefaultRenewalWindowDays,
            DefaultRetryIntervalHours);
    }

    private static DomainCertificateRenewalState ToState(
        DomainEntity domain,
        bool credentialConfigured,
        DateTime? credentialUpdatedAtUtc)
    {
        var policy = domain.CertificateRenewalPolicy;
        var active = domain.ActiveCertificate;
        var hasActiveProductionCertificate = active is not null &&
            active.Status != "Deleted" &&
            active.IsActive &&
            !active.IsStaging;

        var autoRenewEnabled = policy?.AutoRenewEnabled ?? false;
        var acmeEmail = policy?.AcmeEmail;
        var readiness = ResolveReadiness(
            domain.DnsProvider,
            policy is not null,
            autoRenewEnabled,
            acmeEmail,
            credentialConfigured,
            hasActiveProductionCertificate);

        return new DomainCertificateRenewalState(
            domain.Id,
            domain.BaseDomain,
            domain.DnsProvider,
            domain.DnsZone,
            PolicyConfigured: policy is not null,
            AutoRenewEnabled: autoRenewEnabled,
            AcmeEmail: acmeEmail,
            RenewalWindowDays: policy?.RenewalWindowDays ?? DefaultRenewalWindowDays,
            RetryIntervalHours: policy?.RetryIntervalHours ?? DefaultRetryIntervalHours,
            CredentialConfigured: credentialConfigured,
            CredentialUpdatedAtUtc: credentialUpdatedAtUtc,
            HasActiveProductionCertificate: hasActiveProductionCertificate,
            ActiveCertificateId: hasActiveProductionCertificate ? active!.CertificateId : null,
            ActiveCertificateExpiresAtUtc: hasActiveProductionCertificate ? active!.ExpiresAtUtc : null,
            ReadinessStatus: readiness.Status,
            ReadinessMessage: readiness.Message,
            PolicyUpdatedAtUtc: policy?.UpdatedAtUtc);
    }

    private static (string Status, string Message) ResolveReadiness(
        string provider,
        bool policyConfigured,
        bool autoRenewEnabled,
        string? acmeEmail,
        bool credentialConfigured,
        bool hasActiveProductionCertificate)
    {
        if (!string.Equals(provider, "desec", StringComparison.OrdinalIgnoreCase))
        {
            return (DomainRenewalReadinessStatuses.UnsupportedProvider, "Automatic renewal currently supports deSEC-managed Domains only.");
        }

        if (!policyConfigured)
        {
            return (DomainRenewalReadinessStatuses.RenewalCredentialRequired, "Renewal credential required. Enrol the deSEC credential and ACME contact once to enable unattended renewal.");
        }

        if (!autoRenewEnabled)
        {
            return (DomainRenewalReadinessStatuses.Disabled, "Automatic renewal is disabled for this Domain. The production certificate must be renewed manually before expiry unless automatic renewal is re-enabled.");
        }

        if (NormalizeEmail(acmeEmail) is null)
        {
            return (DomainRenewalReadinessStatuses.AcmeEmailRequired, "A valid ACME contact email is required before automatic renewal is ready.");
        }

        if (!credentialConfigured)
        {
            return (DomainRenewalReadinessStatuses.RenewalCredentialRequired, "Renewal credential required. The deSEC token is not stored for this Domain.");
        }

        if (!hasActiveProductionCertificate)
        {
            return (DomainRenewalReadinessStatuses.ProductionCertificateRequired, "The renewal foundation is configured, but this Domain does not yet have an active production certificate.");
        }

        return (DomainRenewalReadinessStatuses.Ready, "Automatic renewal is configured. When the active production certificate enters its renewal window, MEM can issue a validated replacement candidate without changing public traffic until activation succeeds.");
    }

    private static string? NormalizeEmail(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 320)
        {
            return null;
        }

        return MailAddress.TryCreate(candidate, out var parsed) &&
               string.Equals(parsed.Address, candidate, StringComparison.OrdinalIgnoreCase)
            ? parsed.Address
            : null;
    }
}
