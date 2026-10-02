using System.Net;
using Infrastructure.Data.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Contracts;
using Modules.Integrations.Npm.Services;
using Shared.Exceptions;

namespace Modules.Shared.Domains.Certificates.Npm;

public sealed class NpmCertificateImportProbe
{
    private readonly MemDbContext _db;
    private readonly CertificateStorageService _storage;
    private readonly CertificateValidationService _validation;
    private readonly NpmApiClient _npmApiClient;
    private readonly INpmTokenProvider _tokenProvider;
    private readonly NpmApiBaseUrlResolver _baseUrlResolver;
    private readonly NpmProxyHostService _npmProxyHostService;
    private static readonly TimeSpan ImportMutationTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PostRestartReadinessTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PostRestartReadinessPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly NpmCertificateLogSecretBoundaryService _npmCertificateLogSecretBoundary;
    private readonly NpmReadinessService _npmReadinessService;
    private readonly ILogger<NpmCertificateImportProbe> _logger;

    public NpmCertificateImportProbe(
        MemDbContext db,
        CertificateStorageService storage,
        CertificateValidationService validation,
        NpmApiClient npmApiClient,
        INpmTokenProvider tokenProvider,
        NpmApiBaseUrlResolver baseUrlResolver,
        NpmProxyHostService npmProxyHostService,
        NpmCertificateLogSecretBoundaryService npmCertificateLogSecretBoundary,
        NpmReadinessService npmReadinessService,
        ILogger<NpmCertificateImportProbe> logger)
    {
        _db = db;
        _storage = storage;
        _validation = validation;
        _npmApiClient = npmApiClient;
        _tokenProvider = tokenProvider;
        _baseUrlResolver = baseUrlResolver;
        _npmProxyHostService = npmProxyHostService;
        _npmCertificateLogSecretBoundary = npmCertificateLogSecretBoundary;
        _npmReadinessService = npmReadinessService;
        _logger = logger;
    }

    public async Task<NpmCertificateProbeResult> ProbeAsync(
        string certificateId,
        CancellationToken cancellationToken)
    {
        var evidence = new List<CertificateOperationEvidence>
        {
            new("certificateId", certificateId)
        };

        try
        {
            var metadata = await _storage.GetMetadataAsync(
                certificateId,
                cancellationToken);

            if (metadata is null)
            {
                return Failed(
                    "Certificate metadata was not found.",
                    "CertificateMetadataNotFound",
                    null,
                    evidence);
            }

            evidence.Add(new("domain", metadata.Domain));
            evidence.Add(new("provider", metadata.Provider));
            evidence.Add(new("isStaging", metadata.IsStaging.ToString()));
            evidence.Add(new("fullchainPath", metadata.FullchainPath));
            evidence.Add(new("privateKeyPath", "stored, sensitive", Sensitive: true));

            var validation = await _validation.ValidateAsync(
                certificateId,
                cancellationToken);

            evidence.AddRange(PrefixEvidence("validation", validation.Evidence));

            if (!validation.Succeeded)
            {
                return Failed(
                    "Stored certificate is not valid enough to send to NPM.",
                    validation.ErrorCode ?? "CertificateValidationFailed",
                    validation.ErrorDetail,
                    evidence);
            }

            var baseUrl = await ResolveNpmApiBaseUrlAsync(evidence, cancellationToken);

            var token = await _tokenProvider.GetTokenAsync(
                baseUrl,
                cancellationToken);

            evidence.Add(new(
                Key: "npm.auth",
                Value: "token acquired",
                Sensitive: true,
                Status: "Succeeded"));

            var certificates = await _npmApiClient.GetCertificatesAsync(
                baseUrl,
                token,
                cancellationToken);

            evidence.Add(new(
                Key: "npm.certificates.count",
                Value: certificates.Count.ToString(),
                Status: "Succeeded"));

            var matching = certificates.FirstOrDefault(cert =>
                string.Equals(
                    cert.nice_name?.Trim(),
                    metadata.Domain.Trim(),
                    StringComparison.OrdinalIgnoreCase)
                || cert.domain_names?.Any(domain =>
                    string.Equals(
                        domain?.Trim(),
                        metadata.Domain.Trim(),
                        StringComparison.OrdinalIgnoreCase)) == true);

            if (matching is not null)
            {
                evidence.Add(new(
                    Key: "npm.match",
                    Value: $"certificate id {matching.id}",
                    Status: "Succeeded"));

                evidence.Add(new(
                    Key: "npm.match.provider",
                    Value: matching.provider ?? "unknown"));

                evidence.Add(new(
                    Key: "npm.match.niceName",
                    Value: matching.nice_name ?? ""));

                return new NpmCertificateProbeResult(
                    Succeeded: true,
                    Status: "Succeeded",
                    Message: "NPM is reachable and already has a matching certificate.",
                    ErrorCode: null,
                    ErrorDetail: null,
                    MatchingCertificateId: matching.id,
                    Evidence: evidence);
            }

            evidence.Add(new(
                Key: "npm.match",
                Value: "no matching certificate found",
                Status: "Warning"));

            return new NpmCertificateProbeResult(
                Succeeded: true,
                Status: "Warning",
                Message: "NPM is reachable, but no matching certificate was found.",
                ErrorCode: null,
                ErrorDetail: null,
                MatchingCertificateId: null,
                Evidence: evidence);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _tokenProvider.Invalidate();

            evidence.Add(new(
                Key: "npm.auth",
                Value: "NPM rejected the cached token or credentials.",
                Sensitive: true,
                Status: "Failed"));

            return Failed(
                "NPM authentication failed.",
                "NpmAuthenticationFailed",
                ex.Message,
                evidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "NPM certificate probe failed for certificate {CertificateId}",
                certificateId);

            evidence.Add(new(
                Key: "exception",
                Value: ex.Message,
                Status: "Failed"));

            return Failed(
                "NPM certificate probe failed.",
                "NpmCertificateProbeFailed",
                ex.Message,
                evidence);
        }
    }

    public async Task<NpmCertificateProbeResult> ImportAsync(
    string certificateId,
    CancellationToken cancellationToken)
    {
        var evidence = new List<CertificateOperationEvidence>
    {
        new("certificateId", certificateId)
    };

        try
        {
            var metadata = await _storage.GetMetadataAsync(
                certificateId,
                cancellationToken);

            if (metadata is null)
            {
                return Failed(
                    "Certificate metadata was not found.",
                    "CertificateMetadataNotFound",
                    null,
                    evidence);
            }

            evidence.Add(new("domain", metadata.Domain));
            evidence.Add(new("provider", metadata.Provider));
            evidence.Add(new("isStaging", metadata.IsStaging.ToString()));
            evidence.Add(new("fullchainPath", metadata.FullchainPath));
            evidence.Add(new("privateKeyPath", "stored, sensitive", Sensitive: true));

            var validation = await _validation.ValidateAsync(
                certificateId,
                cancellationToken);

            evidence.AddRange(PrefixEvidence("validation", validation.Evidence));

            if (!validation.Succeeded)
            {
                return Failed(
                    "Stored certificate is not valid enough to import into NPM.",
                    validation.ErrorCode ?? "CertificateValidationFailed",
                    validation.ErrorDetail,
                    evidence);
            }

            var secretBoundary = await _npmCertificateLogSecretBoundary.EnsureHardenedAsync(
                cancellationToken);

            evidence.Add(new(
                Key: "npm.secretBoundary",
                Value: secretBoundary.Changed ? "hardened before import" : "already hardened",
                Status: "Succeeded"));

            // Import mutation acceptance boundary. If hardening restarted NPM,
            // that server-owned mutation has already been accepted and may have
            // outlived the browser request. Otherwise, refuse to accept a new
            // certificate mutation after the request has already been cancelled.
            if (!secretBoundary.Restarted)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            using var importLifetime = new CancellationTokenSource(ImportMutationTimeout);
            var importToken = importLifetime.Token;

            if (secretBoundary.Restarted)
            {
                _tokenProvider.Invalidate();

                await WaitForCertificateApiReadyAsync(importToken);

                evidence.Add(new(
                    Key: "npm.readiness.afterHardeningRestart",
                    Value: "certificate API authenticated and reachable",
                    Status: "Succeeded"));
            }

            var baseUrl = await ResolveNpmApiBaseUrlAsync(evidence, importToken);

            var token = await _tokenProvider.GetTokenAsync(
                baseUrl,
                importToken);

            evidence.Add(new(
                Key: "npm.auth",
                Value: "token acquired",
                Sensitive: true,
                Status: "Succeeded"));

            var before = await _npmApiClient.GetCertificatesAsync(
                baseUrl,
                token,
                importToken);

            evidence.Add(new(
                Key: "npm.certificates.beforeCount",
                Value: before.Count.ToString(),
                Status: "Succeeded"));

            var existing = before.FirstOrDefault(cert =>
                string.Equals(
                    cert.nice_name?.Trim(),
                    metadata.Domain.Trim(),
                    StringComparison.OrdinalIgnoreCase)
                || cert.domain_names?.Any(domain =>
                    string.Equals(
                        domain?.Trim(),
                        metadata.Domain.Trim(),
                        StringComparison.OrdinalIgnoreCase)) == true);

            if (existing is not null)
            {
                evidence.Add(new(
                    Key: "npm.import.record",
                    Value: $"matching certificate record exists with id {existing.id}",
                    Status: "Succeeded"));

                await _npmApiClient.UploadCustomCertificateFilesAsync(
                    baseUrl,
                    token,
                    existing.id,
                    metadata.FullchainPath,
                    metadata.PrivateKeyPath,
                    importToken);

                evidence.Add(new(
                    Key: "npm.import.files",
                    Value: $"uploaded certificate files for id {existing.id}",
                    Status: "Succeeded"));

                evidence.Add(new(
                    Key: "npm.import.provider",
                    Value: existing.provider ?? "unknown"));

                evidence.Add(new(
                    Key: "npm.import.niceName",
                    Value: existing.nice_name ?? ""));

                var refreshedConsumers = await _npmProxyHostService.ReapplyCertificateConsumersAsync(
                    existing.id,
                    importToken);

                evidence.Add(new(
                    Key: "npm.import.activeConsumers",
                    Value: refreshedConsumers.Count.ToString(),
                    Status: "Succeeded"));

                if (refreshedConsumers.Count > 0)
                {
                    evidence.Add(new(
                        Key: "npm.import.activation",
                        Value: $"re-applied proxy hosts {string.Join(",", refreshedConsumers.Select(host => host.id))}",
                        Status: "Succeeded"));
                }
                else
                {
                    evidence.Add(new(
                        Key: "npm.import.activation",
                        Value: "no existing proxy hosts reference this certificate id",
                        Status: "Succeeded"));
                }

                await PersistNpmLinkAsync(certificateId, existing.id, importToken);

                var activationMessage = refreshedConsumers.Count > 0
                    ? $" MEM also re-applied {refreshedConsumers.Count} existing proxy host(s) so the running Nginx runtime reloads the replacement certificate material."
                    : " No existing proxy hosts referenced this certificate id, so no runtime activation was required.";

                return new NpmCertificateProbeResult(
                    Succeeded: true,
                    Status: "Succeeded",
                    Message: $"NPM already had a matching certificate record; MEM uploaded replacement certificate files.{activationMessage}",
                    ErrorCode: null,
                    ErrorDetail: null,
                    MatchingCertificateId: existing.id,
                    Evidence: evidence);
            }

            var created = await _npmApiClient.CreateCustomCertificateRecordAsync(
                baseUrl,
                token,
                niceName: metadata.Domain,
                domainNames: [metadata.Domain],
                importToken);

            evidence.Add(new(
                Key: "npm.import.record",
                Value: $"created certificate record id {created.id}",
                Status: "Succeeded"));

            evidence.Add(new(
                Key: "npm.import.provider",
                Value: created.provider ?? "unknown"));

            evidence.Add(new(
                Key: "npm.import.niceName",
                Value: created.nice_name ?? ""));

            await _npmApiClient.UploadCustomCertificateFilesAsync(
                baseUrl,
                token,
                created.id,
                metadata.FullchainPath,
                metadata.PrivateKeyPath,
                importToken);

            evidence.Add(new(
                Key: "npm.import.files",
                Value: $"uploaded certificate files for id {created.id}",
                Status: "Succeeded"));

            evidence.Add(new(
                Key: "npm.import.uploadedProvider",
                Value: created.provider ?? "unknown"));

            evidence.Add(new(
                Key: "npm.import.uploadedNiceName",
                Value: created.nice_name ?? ""));

            var after = await _npmApiClient.GetCertificatesAsync(
                baseUrl,
                token,
                importToken);

            evidence.Add(new(
                Key: "npm.certificates.afterCount",
                Value: after.Count.ToString(),
                Status: "Succeeded"));

            await PersistNpmLinkAsync(certificateId, created.id, importToken);

            return new NpmCertificateProbeResult(
                Succeeded: true,
                Status: "Succeeded",
                Message: "MEM-managed certificate record was created in NPM and certificate files were uploaded.",
                ErrorCode: null,
                ErrorDetail: null,
                MatchingCertificateId: created.id,
                Evidence: evidence);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _tokenProvider.Invalidate();

            evidence.Add(new(
                Key: "npm.auth",
                Value: "NPM rejected the cached token or credentials.",
                Sensitive: true,
                Status: "Failed"));

            return Failed(
                "NPM authentication failed.",
                "NpmAuthenticationFailed",
                ex.Message,
                evidence);
        }
        catch (MemProblemException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "NPM certificate import failed for certificate {CertificateId}",
                certificateId);

            throw new MemProblemException(
                statusCode: 502,
                code: "npm.certificate.import_failed",
                title: "NPM certificate import failed",
                safeDetail: "MEM could not safely complete the certificate import into Nginx Proxy Manager.",
                createIncident: true,
                retryable: true,
                suggestedAction: "Open Diagnostics, confirm NPM readiness, and review the recorded certificate-import failure before retrying.",
                feature: "domains",
                stage: "npm-certificate-import",
                diagnosticDetails: new Dictionary<string, string?>
                {
                    ["certificateId"] = certificateId
                },
                innerException: ex);
        }
    }

    public async Task<NpmCertificateProbeResult> ImportReplacementAsync(
        string certificateId,
        int expectedNpmCertificateId,
        CancellationToken cancellationToken)
    {
        if (expectedNpmCertificateId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedNpmCertificateId));
        }

        var evidence = new List<CertificateOperationEvidence>
        {
            new("certificateId", certificateId),
            new("npm.expectedCertificateId", expectedNpmCertificateId.ToString())
        };

        try
        {
            var metadata = await _storage.GetMetadataAsync(
                certificateId,
                cancellationToken);
            if (metadata is null)
            {
                return Failed(
                    "Certificate metadata was not found.",
                    "CertificateMetadataNotFound",
                    null,
                    evidence);
            }

            evidence.Add(new("domain", metadata.Domain));
            evidence.Add(new("provider", metadata.Provider));
            evidence.Add(new("isStaging", metadata.IsStaging.ToString()));

            if (metadata.IsStaging)
            {
                return Failed(
                    "A staging certificate cannot replace the production NPM certificate.",
                    "StagingCertificateRejected",
                    null,
                    evidence);
            }

            var validation = await _validation.ValidateAsync(
                certificateId,
                cancellationToken);
            evidence.AddRange(PrefixEvidence("validation", validation.Evidence));
            if (!validation.Succeeded)
            {
                return Failed(
                    "Stored certificate is not valid enough to activate in NPM.",
                    validation.ErrorCode ?? "CertificateValidationFailed",
                    validation.ErrorDetail,
                    evidence);
            }

            var secretBoundary = await _npmCertificateLogSecretBoundary.EnsureHardenedAsync(
                cancellationToken);
            evidence.Add(new(
                Key: "npm.secretBoundary",
                Value: secretBoundary.Changed ? "hardened before replacement" : "already hardened",
                Status: "Succeeded"));

            if (!secretBoundary.Restarted)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            using var importLifetime = new CancellationTokenSource(ImportMutationTimeout);
            var importToken = importLifetime.Token;

            if (secretBoundary.Restarted)
            {
                _tokenProvider.Invalidate();
                await WaitForCertificateApiReadyAsync(importToken);
                evidence.Add(new(
                    Key: "npm.readiness.afterHardeningRestart",
                    Value: "certificate API authenticated and reachable",
                    Status: "Succeeded"));
            }

            var baseUrl = await ResolveNpmApiBaseUrlAsync(evidence, importToken);
            var token = await _tokenProvider.GetTokenAsync(baseUrl, importToken);
            evidence.Add(new(
                Key: "npm.auth",
                Value: "token acquired",
                Sensitive: true,
                Status: "Succeeded"));

            var certificates = await _npmApiClient.GetCertificatesAsync(
                baseUrl,
                token,
                importToken);
            var existing = certificates.FirstOrDefault(item => item.id == expectedNpmCertificateId);
            if (existing is null)
            {
                evidence.Add(new(
                    Key: "npm.import.record",
                    Value: $"expected certificate id {expectedNpmCertificateId} was not found",
                    Status: "Failed"));
                return Failed(
                    "The NPM certificate currently used by this Domain no longer exists.",
                    "NpmSourceCertificateMissing",
                    null,
                    evidence);
            }

            var expectedDomain = metadata.Domain.Trim();
            var existingMatchesDomain =
                string.Equals(existing.nice_name?.Trim(), expectedDomain, StringComparison.OrdinalIgnoreCase) ||
                existing.domain_names?.Any(domain => string.Equals(
                    domain?.Trim(),
                    expectedDomain,
                    StringComparison.OrdinalIgnoreCase)) == true;
            if (!existingMatchesDomain)
            {
                evidence.Add(new(
                    Key: "npm.import.record",
                    Value: $"certificate id {expectedNpmCertificateId} belongs to a different domain",
                    Status: "Failed"));
                return Failed(
                    "The expected NPM certificate id does not belong to this Domain.",
                    "NpmCertificateIdentityMismatch",
                    null,
                    evidence);
            }

            evidence.Add(new(
                Key: "npm.import.record",
                Value: $"reusing certificate id {existing.id}",
                Status: "Succeeded"));

            await _npmApiClient.UploadCustomCertificateFilesAsync(
                baseUrl,
                token,
                existing.id,
                metadata.FullchainPath,
                metadata.PrivateKeyPath,
                importToken);
            evidence.Add(new(
                Key: "npm.import.files",
                Value: $"uploaded replacement certificate files for id {existing.id}",
                Status: "Succeeded"));

            var refreshedConsumers = await _npmProxyHostService.ReapplyCertificateConsumersAsync(
                existing.id,
                importToken);
            evidence.Add(new(
                Key: "npm.import.activeConsumers",
                Value: refreshedConsumers.Count.ToString(),
                Status: "Succeeded"));
            evidence.Add(new(
                Key: "npm.import.activation",
                Value: refreshedConsumers.Count > 0
                    ? $"re-applied proxy hosts {string.Join(",", refreshedConsumers.Select(host => host.id))}"
                    : "no existing proxy hosts reference this certificate id",
                Status: "Succeeded"));

            // The renewal activator persists the candidate NPM linkage together with
            // the Domain active-certificate transition. Do not create an intermediate
            // MEM state in which both the old and replacement certificates claim the
            // same live NPM certificate id.

            return new NpmCertificateProbeResult(
                Succeeded: true,
                Status: "Succeeded",
                Message: refreshedConsumers.Count > 0
                    ? $"MEM replaced certificate files in NPM certificate id {existing.id} and re-applied {refreshedConsumers.Count} proxy host(s)."
                    : $"MEM replaced certificate files in NPM certificate id {existing.id}; no proxy hosts required re-application.",
                ErrorCode: null,
                ErrorDetail: null,
                MatchingCertificateId: existing.id,
                Evidence: evidence);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _tokenProvider.Invalidate();
            return Failed(
                "NPM authentication failed.",
                "NpmAuthenticationFailed",
                null,
                evidence);
        }
        catch (MemProblemException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "NPM renewal certificate activation failed for certificate {CertificateId}. NpmCertificateId={NpmCertificateId} FailureType={FailureType}",
                certificateId,
                expectedNpmCertificateId,
                ex.GetType().Name);

            throw new MemProblemException(
                statusCode: 502,
                code: "npm.certificate.renewal_activation_failed",
                title: "NPM certificate activation failed",
                safeDetail: "MEM could not safely activate the renewed certificate in Nginx Proxy Manager.",
                createIncident: false,
                retryable: true,
                suggestedAction: "Open Diagnostics and Domains > Renewal, confirm NPM readiness and allow the server-owned renewal cycle to retry.",
                feature: "domains",
                stage: "npm-renewal-activation",
                diagnosticDetails: new Dictionary<string, string?>
                {
                    ["certificateId"] = certificateId,
                    ["npmCertificateId"] = expectedNpmCertificateId.ToString()
                });
        }
    }

    private async Task WaitForCertificateApiReadyAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + PostRestartReadinessTimeout;
        NpmReadinessResponse? last = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            last = await _npmReadinessService.GetReadinessAsync(cancellationToken);

            if (last.ContainerRunning
                && last.AdminUiReachable
                && last.Initialized
                && last.ApiAuthenticated
                && last.CertificateApiReachable)
            {
                return;
            }

            await Task.Delay(PostRestartReadinessPollInterval, cancellationToken);
        }

        var observed = last is null
            ? "no readiness result was available"
            : $"containerRunning={last.ContainerRunning}; adminUiReachable={last.AdminUiReachable}; initialized={last.Initialized}; apiAuthenticated={last.ApiAuthenticated}; certificateApiReachable={last.CertificateApiReachable}";

        throw new InvalidOperationException(
            $"NPM did not restore full certificate API readiness after the certificate-log hardening restart ({observed}).");
    }

    private async Task PersistNpmLinkAsync(
        string certificateId,
        int npmCertificateId,
        CancellationToken cancellationToken)
    {
        var certificate = await _db.Certificates
            .Include(x => x.Domain)
            .SingleOrDefaultAsync(x => x.CertificateId == certificateId, cancellationToken);

        if (certificate is null)
        {
            _logger.LogWarning(
                "Could not persist NPM certificate linkage because registry certificate {CertificateId} was not found.",
                certificateId);
            return;
        }

        ApplyNpmLink(certificate, npmCertificateId, DateTime.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);
    }

    internal static void ApplyNpmLink(
        CertificateEntity certificate,
        int npmCertificateId,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (npmCertificateId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(npmCertificateId));
        }

        certificate.NpmCertificateId = npmCertificateId;
        certificate.ImportedToNpm = true;
        certificate.LastImportedToNpmAtUtc = nowUtc;
        certificate.LastError = null;
        certificate.IsActive = certificate.Status != "Deleted";

        if (certificate.Domain is not null)
        {
            certificate.Domain.ActiveCertificateId ??= certificate.Id;
            certificate.Domain.Status = "Active";
            certificate.Domain.UpdatedAtUtc = nowUtc;
        }
    }

    public async Task<NpmCertificateTestProxyHostResult> TestProxyHostAsync(
    string certificateId,
    NpmCertificateTestProxyHostRequest request,
    CancellationToken cancellationToken)
    {
        var evidence = new List<CertificateOperationEvidence>
    {
        new("certificateId", certificateId),
        new("proxy.domain", request.Domain),
        new("proxy.forwardHost", request.ForwardHost),
        new("proxy.forwardPort", request.ForwardPort.ToString()),
        new("proxy.forwardScheme", request.ForwardScheme)
    };

        try
        {
            if (string.IsNullOrWhiteSpace(request.Domain))
            {
                return FailedProxyHost(
                    "Proxy host domain is required.",
                    "ProxyHostDomainMissing",
                    null,
                    null,
                    null,
                    evidence);
            }

            if (string.IsNullOrWhiteSpace(request.ForwardHost))
            {
                return FailedProxyHost(
                    "Proxy host forward host is required.",
                    "ProxyHostForwardHostMissing",
                    null,
                    null,
                    null,
                    evidence);
            }

            if (request.ForwardPort is <= 0 or > 65535)
            {
                return FailedProxyHost(
                    "Proxy host forward port is invalid.",
                    "ProxyHostForwardPortInvalid",
                    null,
                    null,
                    null,
                    evidence);
            }

            var metadata = await _storage.GetMetadataAsync(
                certificateId,
                cancellationToken);

            if (metadata is null)
            {
                return FailedProxyHost(
                    "Certificate metadata was not found.",
                    "CertificateMetadataNotFound",
                    null,
                    null,
                    null,
                    evidence);
            }

            evidence.Add(new("cert.domain", metadata.Domain));
            evidence.Add(new("cert.fullchainPath", metadata.FullchainPath));
            evidence.Add(new("cert.privateKeyPath", "stored, sensitive", Sensitive: true));

            var validation = await _validation.ValidateAsync(
                certificateId,
                cancellationToken);

            evidence.AddRange(PrefixEvidence("validation", validation.Evidence));

            if (!validation.Succeeded)
            {
                return FailedProxyHost(
                    "Stored certificate is not valid enough to attach to an NPM proxy host.",
                    validation.ErrorCode ?? "CertificateValidationFailed",
                    validation.ErrorDetail,
                    null,
                    null,
                    evidence);
            }

            var baseUrl = await ResolveNpmApiBaseUrlAsync(evidence, cancellationToken);

            var token = await _tokenProvider.GetTokenAsync(
                baseUrl,
                cancellationToken);

            evidence.Add(new(
                Key: "npm.auth",
                Value: "token acquired",
                Sensitive: true,
                Status: "Succeeded"));

            var importResult = await ImportAsync(
                certificateId,
                cancellationToken);

            evidence.AddRange(PrefixEvidence("npm.importProbe", importResult.Evidence));

            if (!importResult.Succeeded || importResult.MatchingCertificateId is null or <= 0)
            {
                return FailedProxyHost(
                    "NPM certificate import/probe did not return a usable certificate id.",
                    importResult.ErrorCode ?? "NpmCertificateIdMissing",
                    importResult.ErrorDetail,
                    null,
                    null,
                    evidence);
            }

            var npmCertificateId = importResult.MatchingCertificateId.Value;

            evidence.Add(new(
                Key: "npm.certificateId",
                Value: npmCertificateId.ToString(),
                Status: "Succeeded"));

            var existing = await _npmApiClient.FindProxyHostByDomainAsync(
                baseUrl,
                token,
                request.Domain,
                cancellationToken);

            NpmProxyHost proxyHost;

            if (existing is null)
            {
                evidence.Add(new(
                    Key: "npm.proxyHost",
                    Value: "not found; creating",
                    Status: "Running"));

                proxyHost = await _npmApiClient.CreateProxyHostAsync(
                    baseUrl,
                    token,
                    new NpmProxyHostCreate(
                        domain_names: [request.Domain],
                        forward_host: request.ForwardHost,
                        forward_port: request.ForwardPort,
                        forward_scheme: request.ForwardScheme,
                        access_list_id: 0,
                        certificate_id: npmCertificateId,
                        ssl_forced: true,
                        http2_support: true,
                        allow_websocket_upgrade: true,
                        block_exploits: true,
                        caching_enabled: false,
                        enabled: true,
                        advanced_config: "",
                        locations: Array.Empty<object>(),
                        hsts_enabled: false,
                        hsts_subdomains: false,
                        trust_forwarded_proto: false),
                    cancellationToken);

                evidence.Add(new(
                    Key: "npm.proxyHost",
                    Value: $"created id {proxyHost.id}",
                    Status: "Succeeded"));
            }
            else
            {
                evidence.Add(new(
                    Key: "npm.proxyHost",
                    Value: $"found existing id {existing.id}; updating",
                    Status: "Running"));

                proxyHost = await _npmApiClient.UpdateProxyHostAsync(
                    baseUrl,
                    token,
                    existing.id,
                    new NpmProxyHostUpdateRequest(
                        domain_names: [request.Domain],
                        forward_host: request.ForwardHost,
                        forward_port: request.ForwardPort,
                        forward_scheme: request.ForwardScheme,
                        access_list_id: existing.access_list_id ?? 0,
                        certificate_id: npmCertificateId,
                        ssl_forced: true,
                        caching_enabled: false,
                        block_exploits: true,
                        allow_websocket_upgrade: true,
                        http2_support: true,
                        enabled: true,
                        advanced_config: "",
                        locations: Array.Empty<object>(),
                        hsts_enabled: false,
                        hsts_subdomains: false),
                    cancellationToken);

                evidence.Add(new(
                    Key: "npm.proxyHost",
                    Value: $"updated id {proxyHost.id}",
                    Status: "Succeeded"));
            }

            var assignedCertificateId = proxyHost.certificate_id ?? 0;
            var sslForced = proxyHost.ssl_forced ?? false;
            var http2 = proxyHost.http2_support ?? false;

            evidence.Add(new(
                Key: "npm.proxyHost.id",
                Value: proxyHost.id.ToString(),
                Status: "Succeeded"));

            evidence.Add(new(
                Key: "npm.proxyHost.certificateId",
                Value: assignedCertificateId.ToString(),
                Status: assignedCertificateId == npmCertificateId ? "Succeeded" : "Failed"));

            evidence.Add(new(
                Key: "npm.proxyHost.sslForced",
                Value: sslForced.ToString(),
                Status: sslForced ? "Succeeded" : "Warning"));

            evidence.Add(new(
                Key: "npm.proxyHost.http2",
                Value: http2.ToString(),
                Status: http2 ? "Succeeded" : "Warning"));

            var nginxOnline = proxyHost.meta?.nginx_online;
            var nginxError = proxyHost.meta?.nginx_err;

            if (nginxOnline is not null)
            {
                evidence.Add(new(
                    Key: "npm.proxyHost.nginxOnline",
                    Value: nginxOnline.Value.ToString(),
                    Status: nginxOnline.Value ? "Succeeded" : "Failed"));
            }

            if (!string.IsNullOrWhiteSpace(nginxError))
            {
                evidence.Add(new(
                    Key: "npm.proxyHost.nginxError",
                    Value: nginxError,
                    Status: "Failed"));
            }

            if (nginxOnline == false)
            {
                return FailedProxyHost(
                    "NPM accepted the proxy host configuration, but Nginx reported that the generated config is not online.",
                    "NpmNginxConfigOffline",
                    nginxError,
                    npmCertificateId,
                    proxyHost.id,
                    evidence);
            }

            var assignmentStuck = assignedCertificateId == npmCertificateId;

            if (!assignmentStuck)
            {
                return FailedProxyHost(
                    "NPM proxy host was created or updated, but the expected certificate id was not assigned.",
                    "NpmCertificateAssignmentMismatch",
                    $"Expected certificate id {npmCertificateId}, got {assignedCertificateId}.",
                    npmCertificateId,
                    proxyHost.id,
                    evidence);
            }

            return new NpmCertificateTestProxyHostResult(
                Succeeded: true,
                Status: "Succeeded",
                Message: "NPM proxy host successfully consumed the MEM-managed certificate.",
                ErrorCode: null,
                ErrorDetail: null,
                NpmCertificateId: npmCertificateId,
                ProxyHostId: proxyHost.id,
                Evidence: evidence);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            _tokenProvider.Invalidate();

            evidence.Add(new(
                Key: "npm.auth",
                Value: "NPM rejected the cached token or credentials.",
                Sensitive: true,
                Status: "Failed"));

            return FailedProxyHost(
                "NPM authentication failed.",
                "NpmAuthenticationFailed",
                ex.Message,
                null,
                null,
                evidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "NPM proxy host certificate test failed for certificate {CertificateId}",
                certificateId);

            evidence.Add(new(
                Key: "exception",
                Value: ex.Message,
                Status: "Failed"));

            return FailedProxyHost(
                "NPM proxy host certificate test failed.",
                "NpmProxyHostCertificateTestFailed",
                ex.Message,
                null,
                null,
                evidence);
        }
    }

    private static NpmCertificateTestProxyHostResult FailedProxyHost(
        string message,
        string errorCode,
        string? errorDetail,
        int? npmCertificateId,
        int? proxyHostId,
        IReadOnlyList<CertificateOperationEvidence> evidence)
    {
        return new NpmCertificateTestProxyHostResult(
            Succeeded: false,
            Status: "Failed",
            Message: message,
            ErrorCode: errorCode,
            ErrorDetail: errorDetail,
            NpmCertificateId: npmCertificateId,
            ProxyHostId: proxyHostId,
            Evidence: evidence);
    }

    private async Task<string> ResolveNpmApiBaseUrlAsync(
        List<CertificateOperationEvidence> evidence,
        CancellationToken cancellationToken)
    {
        var resolution = await _baseUrlResolver.ResolveAsync(cancellationToken);

        _logger.LogInformation(
            "NPM certificate operation resolved administration authority. Source={Source} RuntimeRecordFound={RuntimeRecordFound}",
            resolution.Source,
            resolution.RuntimeRecordFound);

        evidence.Add(new(
            Key: "npm.baseUrl",
            Value: resolution.BaseUrl,
            Status: "Succeeded"));

        evidence.Add(new(
            Key: "npm.baseUrl.source",
            Value: resolution.Source,
            Status: "Succeeded"));

        if (!string.IsNullOrWhiteSpace(resolution.Warning))
        {
            evidence.Add(new(
                Key: "npm.baseUrl.warning",
                Value: resolution.Warning,
                Status: "Warning"));
        }

        return resolution.BaseUrl;
    }

    private static NpmCertificateProbeResult Failed(
        string message,
        string errorCode,
        string? errorDetail,
        IReadOnlyList<CertificateOperationEvidence> evidence)
    {
        return new NpmCertificateProbeResult(
            Succeeded: false,
            Status: "Failed",
            Message: message,
            ErrorCode: errorCode,
            ErrorDetail: errorDetail,
            MatchingCertificateId: null,
            Evidence: evidence);
    }

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