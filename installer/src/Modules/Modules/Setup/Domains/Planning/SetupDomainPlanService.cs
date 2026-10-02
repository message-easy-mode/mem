using System.Net.Mail;
using System.Text.Json;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Setup.InstallPlans;
using Modules.Setup.InstallRuns;
using Modules.Setup.Secrets;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Dns;
using Shared.Diagnostics;

namespace Modules.Setup.Domains.Planning;

public sealed class SetupDomainPlanService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly MemDbContext _db;
    private readonly IDnsZoneAccessProbe _dnsZoneAccessProbe;
    private readonly IInstallationSecretStore _secretStore;
    private readonly InstallationCredentialService _credentialService;
    private readonly ILogger<SetupDomainPlanService> _logger;
    private readonly IMemDiagnosticEventWriter? _diagnostics;

    public SetupDomainPlanService(
        MemDbContext db,
        IDnsZoneAccessProbe dnsZoneAccessProbe,
        IInstallationSecretStore secretStore,
        InstallationCredentialService credentialService,
        ILogger<SetupDomainPlanService> logger,
        IMemDiagnosticEventWriter? diagnostics = null)
    {
        _db = db;
        _dnsZoneAccessProbe = dnsZoneAccessProbe;
        _secretStore = secretStore;
        _credentialService = credentialService;
        _logger = logger;
        _diagnostics = diagnostics;
    }

    public async Task<SetupDomainPlanResponse> GetCurrentAsync(
        CancellationToken cancellationToken)
    {
        var installation = await CurrentEditableInstallationAsync(cancellationToken);
        if (installation is null)
        {
            return Empty(
                status: "SetupAuthorityMissing",
                message: "No editable first-time Setup authority is available.",
                errorCode: "SetupAuthorityMissing");
        }

        var config = DeserializeConfig(installation.ConfigJson);
        var publicAccess = config.PublicAccess;
        var preparation = publicAccess.Preparation;

        if (preparation is null ||
            !string.Equals(preparation.Status, "Validated", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(publicAccess.Zone))
        {
            return Empty(
                installation.Id,
                status: "NotConfigured",
                message: "Domain planning has not been validated yet.");
        }

        var credentialStored = await _secretStore.ExistsAsync(
            installation.Id,
            InstallationSecretNames.DnsCategory,
            InstallationSecretNames.DesecProviderToken,
            cancellationToken);

        return new SetupDomainPlanResponse(
            InstallationId: installation.Id,
            Configured: credentialStored && preparation.ProviderAccessConfirmed,
            Succeeded: credentialStored && preparation.ProviderAccessConfirmed,
            Status: credentialStored ? "Validated" : "CredentialRequired",
            Message: credentialStored
                ? "Domain plan is validated. No DNS records or certificates have been changed yet."
                : "The domain plan is present, but the DNS credential must be entered again before installation.",
            ErrorCode: credentialStored ? null : "DnsProviderCredentialMissing",
            ErrorDetail: null,
            Domain: publicAccess.Domain,
            Zone: publicAccess.Zone,
            AcmeEmail: publicAccess.AcmeEmail,
            DnsProvider: publicAccess.DnsProvider,
            UseStaging: publicAccess.UseStaging,
            ProviderAccessConfirmed: preparation.ProviderAccessConfirmed,
            ProviderCredentialStored: credentialStored,
            ValidatedAtUtc: preparation.ValidatedAtUtc,
            Evidence:
            [
                new CertificateOperationEvidence(
                    "dns.providerAccess",
                    preparation.ProviderAccessConfirmed ? "read-only access confirmed" : "not confirmed",
                    Status: preparation.ProviderAccessConfirmed ? "Succeeded" : "Failed"),
                new CertificateOperationEvidence(
                    "dns.providerCredential",
                    credentialStored ? "stored protected server-side" : "credential unavailable",
                    Sensitive: true,
                    Status: credentialStored ? "Succeeded" : "Failed")
            ]);
    }

    public async Task<SetupDomainPlanResponse> ValidateAndSaveAsync(
        SetupDomainPlanRequest request,
        CancellationToken cancellationToken)
    {
        var installation = await CurrentEditableInstallationAsync(cancellationToken);
        if (installation is null)
        {
            return Empty(
                status: "SetupAuthorityMissing",
                message: "No editable first-time Setup authority is available.",
                errorCode: "SetupAuthorityMissing");
        }

        var zone = NormalizeBaseDomain(request.BaseDomain);
        if (!LooksLikePublicDnsName(zone))
        {
            return Failed(
                installation.Id,
                "DomainInvalid",
                "Enter a valid public base domain such as example.com.",
                zone,
                request);
        }

        var email = request.AcmeEmail?.Trim() ?? string.Empty;
        if (!LooksLikeEmailAddress(email))
        {
            return Failed(
                installation.Id,
                "AcmeEmailInvalid",
                "Enter a valid email address for the future ACME account.",
                zone,
                request,
                email);
        }

        if (!string.Equals(request.DnsProvider, "desec", StringComparison.OrdinalIgnoreCase))
        {
            return Failed(
                installation.Id,
                "UnsupportedDnsProvider",
                "Only deSEC is supported by first-time Setup in this release.",
                zone,
                request,
                email);
        }

        var providerToken = request.ProviderToken?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(providerToken))
        {
            return Failed(
                installation.Id,
                "DesecTokenMissing",
                "A deSEC provider token is required.",
                zone,
                request,
                email);
        }

        var probe = await _dnsZoneAccessProbe.ProbeAsync(
            new DnsZoneAccessProbeRequest(zone, providerToken),
            cancellationToken);

        if (!probe.Succeeded)
        {
            _logger.LogWarning(
                "Domain plan provider validation failed. InstallationId={InstallationId} Provider={Provider} Zone={Zone} FailureClass={FailureClass} HttpStatus={HttpStatus}",
                installation.Id,
                "desec",
                zone,
                probe.ErrorCode ?? "Unknown",
                probe.ProviderStatusCode);

            await RecordDomainValidationAsync(
                installation.Id,
                zone,
                providerToken,
                probe,
                succeeded: false);

            return new SetupDomainPlanResponse(
                installation.Id,
                Configured: false,
                Succeeded: false,
                Status: "ValidationFailed",
                Message: probe.Message,
                ErrorCode: probe.ErrorCode,
                ErrorDetail: null,
                Domain: $"*.{zone}",
                Zone: zone,
                AcmeEmail: email,
                DnsProvider: "desec",
                UseStaging: request.UseStaging,
                ProviderAccessConfirmed: false,
                ProviderCredentialStored: false,
                ValidatedAtUtc: null,
                Evidence: probe.Evidence);
        }

        var validatedAtUtc = DateTime.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _secretStore.SetProtectedAsync(
                installation.Id,
                InstallationSecretNames.DnsCategory,
                InstallationSecretNames.DesecProviderToken,
                providerToken,
                "Temporary deSEC credential retained for reviewed certificate issuance and retry.",
                cancellationToken);

            await _credentialService.EnsurePostgresPasswordAsync(
                installation.Id,
                cancellationToken);

            var config = DeserializeConfig(installation.ConfigJson);
            var publicAccess = config.PublicAccess with
            {
                Domain = $"*.{zone}",
                Zone = zone,
                AcmeEmail = email,
                DnsProvider = "desec",
                UseStaging = request.UseStaging,
                CertificateId = null,
                NpmCertificateId = null,
                ProxyHostDomain = null,
                ForwardHost = null,
                ForwardPort = null,
                ForwardScheme = "http",
                CertificateValidated = false,
                ImportedToNpm = false,
                ProxyHostVerified = false,
                LastVerifiedAtUtc = null,
                Preparation = new DomainPreparationSetupConfig(
                    Status: "Validated",
                    ValidatedAtUtc: validatedAtUtc,
                    ProviderAccessConfirmed: true,
                    ProviderCredentialStored: true)
            };

            installation.ConfigJson = JsonSerializer.Serialize(
                config with
                {
                    PublicAccess = publicAccess,
                    Review = null
                },
                JsonOptions);

            // Any Domain edit after Review or a certificate-stage failure invalidates
            // the previously frozen consent. Already-succeeded installation steps
            // remain durable and can be reused after the plan is reviewed again.
            installation.FrozenConfigJson = null;
            if (installation.Status is InstallationStatuses.Ready or
                InstallationStatuses.Failed or
                InstallationStatuses.WaitingForUser)
            {
                installation.Status = InstallationStatuses.Draft;
            }

            installation.LastError = null;
            installation.UpdatedAtUtc = validatedAtUtc;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);

            _logger.LogError(
                ex,
                "Failed to persist protected Domain plan for installation {InstallationId} and zone {Zone}",
                installation.Id,
                zone);

            return new SetupDomainPlanResponse(
                installation.Id,
                Configured: false,
                Succeeded: false,
                Status: "PersistenceFailed",
                Message: "MEM validated the DNS provider but could not safely persist the protected installation plan.",
                ErrorCode: "DomainPlanPersistenceFailed",
                ErrorDetail: null,
                Domain: $"*.{zone}",
                Zone: zone,
                AcmeEmail: email,
                DnsProvider: "desec",
                UseStaging: request.UseStaging,
                ProviderAccessConfirmed: true,
                ProviderCredentialStored: false,
                ValidatedAtUtc: null,
                Evidence: probe.Evidence);
        }

        _logger.LogInformation(
            "Validated read-only DNS access and persisted Domain plan for installation {InstallationId}. Zone={Zone} Provider={Provider} UseStaging={UseStaging} HttpStatus={HttpStatus}",
            installation.Id,
            zone,
            "desec",
            request.UseStaging,
            probe.ProviderStatusCode);

        await RecordDomainValidationAsync(
            installation.Id,
            zone,
            providerToken,
            probe,
            succeeded: true);

        var evidence = probe.Evidence
            .Concat(
            [
                new CertificateOperationEvidence(
                    "dns.providerCredential",
                    "stored protected server-side",
                    Sensitive: true,
                    Status: "Succeeded"),
                new CertificateOperationEvidence(
                    "externalMutation",
                    "none; DNS and ACME mutation is deferred until after Review",
                    Status: "Skipped")
            ])
            .ToArray();

        return new SetupDomainPlanResponse(
            installation.Id,
            Configured: true,
            Succeeded: true,
            Status: "Validated",
            Message: "Domain plan validated. No DNS records or certificates have been changed yet.",
            ErrorCode: null,
            ErrorDetail: null,
            Domain: $"*.{zone}",
            Zone: zone,
            AcmeEmail: email,
            DnsProvider: "desec",
            UseStaging: request.UseStaging,
            ProviderAccessConfirmed: true,
            ProviderCredentialStored: true,
            ValidatedAtUtc: validatedAtUtc,
            Evidence: evidence);
    }

    private Task<MemDiagnosticWriteResult?> RecordDomainValidationAsync(
        Guid installationId,
        string zone,
        string providerToken,
        DnsZoneAccessProbeResult probe,
        bool succeeded)
    {
        var details = new Dictionary<string, string?>
        {
            ["provider"] = "desec",
            ["zone"] = zone,
            ["operation"] = "read-only-zone-access",
            ["failureClass"] = probe.ErrorCode,
            ["httpStatus"] = probe.ProviderStatusCode?.ToString(),
            ["externalMutation"] = "none"
        };

        return _diagnostics.TryWriteWorkflowEventAsync(new MemDiagnosticWriteRequest(
            Severity: succeeded
                ? MemDiagnosticSeverities.Information
                : MemDiagnosticSeverities.Warning,
            EventCode: succeeded
                ? "setup.domain.validation.succeeded"
                : "setup.domain.validation.failed",
            Source: "api.setup-domain-plan",
            Feature: "setup",
            Stage: "domain-validation",
            Message: succeeded
                ? "Read-only DNS provider validation succeeded."
                : "Read-only DNS provider validation failed.",
            CreateIncident: false,
            Resource: new MemDiagnosticResource(
                Kind: "installation",
                Id: installationId.ToString("D"),
                DisplayName: "MEM installation",
                WorkspacePath: "/setup/domain"),
            Observed: new Dictionary<string, string?>
            {
                ["result"] = succeeded ? "succeeded" : "failed"
            },
            Details: details,
            SuggestedAction: succeeded
                ? null
                : "Check the base domain and deSEC token, then retry Domain plan validation.",
            Retryable: !succeeded,
            ExactSecrets: string.IsNullOrWhiteSpace(providerToken)
                ? null
                : [providerToken]));
    }

    private async Task<InstallationEntity?> CurrentEditableInstallationAsync(
        CancellationToken cancellationToken)
    {
        var installation = await _db.Installations
            .OrderByDescending(x => x.UpdatedAtUtc)
            .FirstOrDefaultAsync(
                x => x.Status == InstallationStatuses.Draft ||
                     x.Status == InstallationStatuses.Ready ||
                     x.Status == InstallationStatuses.Failed ||
                     x.Status == InstallationStatuses.WaitingForUser,
                cancellationToken);

        if (installation is null ||
            installation.Status is InstallationStatuses.Draft or InstallationStatuses.Ready)
        {
            return installation;
        }

        var certificateStepNeedsRepair = await _db.InstallationStepExecutions
            .AsNoTracking()
            .AnyAsync(
                x => x.InstallationId == installation.Id &&
                     (x.StepName == InstallStepNames.IssueAndImportPlatformCertificate ||
                      x.StepName == InstallStepNames.ResolvePlatformDomainAndCertificate) &&
                     (x.Status == InstallationStepStatuses.Failed ||
                      x.Status == InstallationStepStatuses.WaitingForUser),
                cancellationToken);

        return certificateStepNeedsRepair ? installation : null;
    }

    private static InstallPlan DeserializeConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return InstallPlanFactory.CreateDefault();
        }

        return JsonSerializer.Deserialize<InstallPlan>(json, JsonOptions)
            ?? InstallPlanFactory.CreateDefault();
    }

    private static string NormalizeBaseDomain(string? value) =>
        (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("*.", string.Empty, StringComparison.Ordinal)
            .Trim('/')
            .Trim('.');

    private static bool LooksLikePublicDnsName(string value) =>
        value.Length is > 3 and <= 253 &&
        value.Contains('.', StringComparison.Ordinal) &&
        Uri.CheckHostName(value) == UriHostNameType.Dns;

    private static bool LooksLikeEmailAddress(string value)
    {
        try
        {
            var address = new MailAddress(value);
            return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static SetupDomainPlanResponse Failed(
        Guid installationId,
        string errorCode,
        string message,
        string zone,
        SetupDomainPlanRequest request,
        string? email = null) =>
        new(
            installationId,
            Configured: false,
            Succeeded: false,
            Status: "ValidationFailed",
            Message: message,
            ErrorCode: errorCode,
            ErrorDetail: null,
            Domain: string.IsNullOrWhiteSpace(zone) ? string.Empty : $"*.{zone}",
            Zone: zone,
            AcmeEmail: email ?? request.AcmeEmail?.Trim() ?? string.Empty,
            DnsProvider: "desec",
            UseStaging: request.UseStaging,
            ProviderAccessConfirmed: false,
            ProviderCredentialStored: false,
            ValidatedAtUtc: null,
            Evidence: []);

    private static SetupDomainPlanResponse Empty(
        string status,
        string message,
        string? errorCode = null) =>
        Empty(null, status, message, errorCode);

    private static SetupDomainPlanResponse Empty(
        Guid? installationId,
        string status,
        string message,
        string? errorCode = null) =>
        new(
            installationId,
            Configured: false,
            Succeeded: false,
            Status: status,
            Message: message,
            ErrorCode: errorCode,
            ErrorDetail: null,
            Domain: string.Empty,
            Zone: string.Empty,
            AcmeEmail: string.Empty,
            DnsProvider: "desec",
            UseStaging: false,
            ProviderAccessConfirmed: false,
            ProviderCredentialStored: false,
            ValidatedAtUtc: null,
            Evidence: []);
}
