using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Services;
using Modules.Shared.Domains;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Certificates.Npm;

namespace Modules.Setup.Domains.Certificates;

public sealed class SetupPlatformCertificateService : ISetupPlatformCertificateService
{
    private readonly CertificateService _certificateService;
    private readonly CertificateStorageService _storage;
    private readonly DomainRegistryService _registry;
    private readonly NpmCertificateImportProbe _npmCertificateImport;
    private readonly MemDbContext _db;
    private readonly ILogger<SetupPlatformCertificateService> _logger;

    public SetupPlatformCertificateService(
        CertificateService certificateService,
        CertificateStorageService storage,
        DomainRegistryService registry,
        NpmCertificateImportProbe npmCertificateImport,
        MemDbContext db,
        ILogger<SetupPlatformCertificateService> logger)
    {
        _certificateService = certificateService;
        _storage = storage;
        _registry = registry;
        _npmCertificateImport = npmCertificateImport;
        _db = db;
        _logger = logger;
    }

    public async Task<CertificateOperationResult> IssuePlatformCertificateAsync(
        CertificateIssueRequest request,
        CancellationToken cancellationToken,
        CertificateIssueProgressCallback? progressCallback = null)
    {
        var issue = await _certificateService.IssueAsync(
            request,
            cancellationToken,
            progressCallback);

        if (!issue.Succeeded)
        {
            return issue;
        }

        var certificateId = GetEvidenceValue(issue, "certificateId");

        if (string.IsNullOrWhiteSpace(certificateId))
        {
            return issue with
            {
                Succeeded = false,
                Status = "Failed",
                Message = "Certificate was issued, but the generated certificate id could not be resolved.",
                ErrorCode = "CertificateIdMissing",
                ErrorDetail = "The certificate issue result did not include certificateId evidence."
            };
        }

        var metadata = await _storage.GetMetadataAsync(
            certificateId,
            cancellationToken);

        if (metadata is null)
        {
            return issue with
            {
                Succeeded = false,
                Status = "Failed",
                Message = "Certificate was issued, but metadata could not be loaded.",
                ErrorCode = "CertificateMetadataMissing",
                ErrorDetail = $"Certificate metadata was not found for '{certificateId}'."
            };
        }

        await ReportProgressAsync(
            progressCallback,
            "certificate.registry",
            "Registering the issued TLS certificate as a managed platform certificate.",
            cancellationToken);

        var registered = await _registry.RegisterCertificateAsync(
            metadata,
            cancellationToken);

        var main = await _registry.SetMainPlatformCertificateAsync(
            certificateId,
            cancellationToken);

        if (main is null)
        {
            return issue with
            {
                Succeeded = false,
                Status = "Failed",
                Message = "Certificate was issued, but could not be set as the main platform certificate.",
                ErrorCode = "SetMainPlatformCertificateFailed",
                ErrorDetail = $"Certificate '{certificateId}' could not be set as the main platform certificate."
            };
        }

        var evidence = issue.Evidence
            .Concat([
                new CertificateOperationEvidence(
                    Key: "registry.registered",
                    Value: registered.CertificateId,
                    Status: "Succeeded"),
                new CertificateOperationEvidence(
                    Key: "registry.mainPlatformCertificate",
                    Value: certificateId,
                    Status: "Succeeded")
            ])
            .ToList();

        try
        {
            await ReportProgressAsync(
                progressCallback,
                "certificate.npm-import",
                "Importing the managed TLS certificate into Nginx Proxy Manager.",
                cancellationToken);

            var import = await _npmCertificateImport.ImportAsync(
                certificateId,
                cancellationToken);

            evidence.AddRange(PrefixEvidence("npm.import", import.Evidence));

            if (import.Succeeded && import.MatchingCertificateId is > 0)
            {
                var npmCertificateId = import.MatchingCertificateId.Value;

                await PersistNpmImportAsync(
                    certificateId,
                    npmCertificateId,
                    cancellationToken);

                evidence.Add(new CertificateOperationEvidence(
                    Key: "npm.certificateId",
                    Value: npmCertificateId.ToString(),
                    Status: "Succeeded"));

                return issue with
                {
                    Succeeded = true,
                    Status = "Succeeded",
                    Message = "Platform certificate issued, registered, selected as main, and imported into NPM.",
                    Evidence = evidence
                };
            }

            evidence.Add(new CertificateOperationEvidence(
                Key: "npm.import.deferred",
                Value: import.Message,
                Status: "Warning"));

            return issue with
            {
                Succeeded = true,
                Status = "Warning",
                Message = "Platform certificate issued and selected as main. NPM import was not completed and will be retried during installation.",
                ErrorCode = import.ErrorCode,
                ErrorDetail = import.ErrorDetail,
                Evidence = evidence
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Platform certificate was issued and selected as main, but NPM import failed. CertificateId={CertificateId}",
                certificateId);

            evidence.Add(new CertificateOperationEvidence(
                Key: "npm.import.exception",
                Value: ex.Message,
                Status: "Warning"));

            return issue with
            {
                Succeeded = true,
                Status = "Warning",
                Message = "Platform certificate issued and selected as main. NPM import will be retried during installation.",
                ErrorCode = "NpmImportDeferred",
                ErrorDetail = ex.Message,
                Evidence = evidence
            };
        }
    }


    public async Task<NpmCertificateProbeResult> EnsureNpmImportAsync(
        string certificateId,
        CancellationToken cancellationToken)
    {
        var import = await _npmCertificateImport.ImportAsync(
            certificateId,
            cancellationToken);

        if (import.Succeeded && import.MatchingCertificateId is > 0)
        {
            await PersistNpmImportAsync(
                certificateId,
                import.MatchingCertificateId.Value,
                cancellationToken);
        }

        return import;
    }

    private async Task PersistNpmImportAsync(
        string certificateId,
        int npmCertificateId,
        CancellationToken cancellationToken)
    {
        var certificate = await _db.Certificates
            .FirstOrDefaultAsync(
                x => x.CertificateId == certificateId,
                cancellationToken);

        if (certificate is null)
        {
            _logger.LogWarning(
                "Could not persist NPM certificate id because certificate was not found in DB. CertificateId={CertificateId} NpmCertificateId={NpmCertificateId}",
                certificateId,
                npmCertificateId);

            return;
        }

        certificate.NpmCertificateId = npmCertificateId;
        certificate.ImportedToNpm = true;
        certificate.LastImportedToNpmAtUtc = DateTime.UtcNow;
        certificate.LastError = null;

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static string? GetEvidenceValue(
        CertificateOperationResult result,
        string key)
    {
        return result.Evidence
            .FirstOrDefault(x => string.Equals(
                x.Key,
                key,
                StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }

    private static Task ReportProgressAsync(
        CertificateIssueProgressCallback? callback,
        string phaseCode,
        string safeSummary,
        CancellationToken cancellationToken) =>
        callback is null
            ? Task.CompletedTask
            : callback(
                new CertificateIssueProgress(phaseCode, safeSummary),
                cancellationToken);

    private static IReadOnlyList<CertificateOperationEvidence> PrefixEvidence(
        string prefix,
        IReadOnlyList<CertificateOperationEvidence> evidence)
    {
        return evidence
            .Select(item => item with
            {
                Key = $"{prefix}.{item.Key}"
            })
            .ToList();
    }

}
