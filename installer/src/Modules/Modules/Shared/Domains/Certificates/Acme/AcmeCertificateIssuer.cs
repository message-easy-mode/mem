using System.Security.Cryptography.X509Certificates;
using System.Text;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Modules.Shared.Domains.Dns;

namespace Modules.Shared.Domains.Certificates.Acme;

public sealed class AcmeCertificateIssuer
{
    private readonly IDnsChallengeProvider _dnsProvider;
    private readonly DnsChallengeReadinessService _dnsReadiness;
    private readonly IOptions<AcmeCertificateOptions> _options;
    private readonly ILogger<AcmeCertificateIssuer> _logger;

    public AcmeCertificateIssuer(
        IDnsChallengeProvider dnsProvider,
        DnsChallengeReadinessService dnsReadiness,
        IOptions<AcmeCertificateOptions> options,
        ILogger<AcmeCertificateIssuer> logger)
    {
        _dnsProvider = dnsProvider;
        _dnsReadiness = dnsReadiness;
        _options = options;
        _logger = logger;
    }

    public async Task<AcmeIssueResult> IssueAsync(
        CertificateIssueRequest request,
        CancellationToken cancellationToken,
        CertificateIssueProgressCallback? progressCallback = null)
    {
        var evidence = new List<CertificateOperationEvidence>
        {
            new("domain", request.Domain),
            new("zone", request.Zone),
            new("provider", request.Provider),
            new("isStaging", request.UseStaging.ToString()),
        };
        DnsChallengeRequest? challengeCleanupRequest = null;

        try
        {
            if (!string.Equals(request.Provider, "desec", StringComparison.OrdinalIgnoreCase))
            {
                return AcmeIssueResult.Failed(
                    message: "Only deSEC is supported by the current certificate workflow.",
                    errorCode: "UnsupportedDnsProvider",
                    errorDetail: $"Provider '{request.Provider}' is not supported.",
                    evidence: evidence);
            }

            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return AcmeIssueResult.Failed(
                    message: "An email address is required to create the ACME account.",
                    errorCode: "AcmeEmailMissing",
                    errorDetail: null,
                    evidence: evidence);
            }

            if (string.IsNullOrWhiteSpace(request.ProviderToken))
            {
                return AcmeIssueResult.Failed(
                    message: "A deSEC provider token is required.",
                    errorCode: "DesecTokenMissing",
                    errorDetail: null,
                    evidence: evidence);
            }

            var acmeDirectoryUrl = ResolveAcmeDirectoryUrl(request.UseStaging);

            evidence.Add(new CertificateOperationEvidence(
                Key: "acmeDirectoryUrl",
                Value: acmeDirectoryUrl,
                Status: request.UseStaging ? "Staging" : "Production"));

            _logger.LogInformation(
                "Starting ACME certificate issue for {Domain} using {AcmeDirectoryUrl}",
                request.Domain,
                acmeDirectoryUrl);

            var acmeContext = new AcmeContext(new Uri(acmeDirectoryUrl));

            await ReportProgressAsync(
                progressCallback,
                "certificate.acme-account",
                "Preparing the Let's Encrypt account for the certificate request.",
                false,
                cancellationToken);

            _ = await RunAcmeRequestAsync(
                () => acmeContext.NewAccount(
                    contact: [$"mailto:{request.Email.Trim()}"],
                    termsOfServiceAgreed: true),
                "AcmeAccountRequestTimedOut",
                "The Let's Encrypt account request timed out.",
                cancellationToken);

            evidence.Add(new CertificateOperationEvidence(
                Key: "acmeAccount",
                Value: $"registered for {request.Email.Trim()}",
                Status: "Succeeded"));

            var domainToRequest = request.Domain.Trim().TrimEnd('.').ToLowerInvariant();
            var maxValidationAttempts = Math.Clamp(
                _options.Value.AcmeDnsValidationMaxAttempts,
                1,
                2);

            for (var validationAttempt = 1; validationAttempt <= maxValidationAttempts; validationAttempt++)
            {
                evidence.Add(new CertificateOperationEvidence(
                    Key: "acmeDnsValidationAttempt",
                    Value: $"{validationAttempt} of {maxValidationAttempts}",
                    Status: "Running"));

                await ReportProgressAsync(
                    progressCallback,
                    "certificate.acme-order",
                    validationAttempt == 1
                        ? "Creating the Let's Encrypt certificate order."
                        : "Creating one fresh Let's Encrypt certificate order after a transient secondary DNS validation miss.",
                    false,
                    cancellationToken);

                var orderContext = await RunAcmeRequestAsync(
                    () => acmeContext.NewOrder([domainToRequest]),
                    "AcmeOrderRequestTimedOut",
                    "The Let's Encrypt certificate order request timed out.",
                    cancellationToken);

                evidence.Add(new CertificateOperationEvidence(
                    Key: "acmeOrder",
                    Value: $"created for DNS validation attempt {validationAttempt}",
                    Status: "Succeeded"));

                var authorizations = await RunAcmeRequestAsync(
                    () => orderContext.Authorizations(),
                    "AcmeAuthorizationRequestTimedOut",
                    "The Let's Encrypt authorization request timed out.",
                    cancellationToken);
                var authorizationContext = authorizations.FirstOrDefault();

                if (authorizationContext is null)
                {
                    return AcmeIssueResult.Failed(
                        message: "ACME did not return an authorization for this order.",
                        errorCode: "AcmeAuthorizationMissing",
                        errorDetail: null,
                        evidence: evidence);
                }

                var challenges = await RunAcmeRequestAsync(
                    () => authorizationContext.Challenges(),
                    "AcmeChallengeRequestTimedOut",
                    "The Let's Encrypt DNS challenge request timed out.",
                    cancellationToken);
                var dnsChallenge = challenges.FirstOrDefault(challenge => challenge.Type == ChallengeTypes.Dns01);

                if (dnsChallenge is null)
                {
                    return AcmeIssueResult.Failed(
                        message: "ACME did not return a DNS-01 challenge for this authorization.",
                        errorCode: "AcmeDnsChallengeMissing",
                        errorDetail: null,
                        evidence: evidence);
                }

                var txtValue = acmeContext.AccountKey.DnsTxt(dnsChallenge.Token);
                var recordName = DnsChallengeNameBuilder.BuildSubname(
                    request.Domain,
                    request.Zone);
                var fullRecordName = DnsChallengeNameBuilder.BuildFullRecordName(
                    recordName,
                    request.Zone);

                evidence.Add(new CertificateOperationEvidence(
                    Key: "dnsChallengeRecord",
                    Value: fullRecordName,
                    Status: "Pending"));
                evidence.Add(new CertificateOperationEvidence(
                    Key: "dnsChallengeValue",
                    Value: "created, masked",
                    Sensitive: true,
                    Status: "Pending"));

                var challengeRequest = new DnsChallengeRequest(
                    Zone: request.Zone,
                    RecordName: recordName,
                    TxtValue: txtValue,
                    ProviderToken: request.ProviderToken);

                await ReportProgressAsync(
                    progressCallback,
                    "certificate.dns-publish",
                    validationAttempt == 1
                        ? "Publishing the DNS-01 challenge through deSEC."
                        : "Updating the existing DNS-01 challenge RRset for the automatic ACME recovery attempt.",
                    false,
                    cancellationToken);

                // Replace/create the RRset directly. Do not clear it first: clearing creates
                // an avoidable no-record window that can be observed or negatively cached.
                // On automatic retry the previous challenge stays present until this update,
                // so the recovery path never deliberately creates a no-TXT gap.
                var upsertResult = await _dnsProvider.UpsertTxtChallengeAsync(
                    challengeRequest,
                    cancellationToken);

                evidence.AddRange(PrefixEvidence(
                    $"dns.upsert.attempt{validationAttempt}",
                    upsertResult.Evidence));

                if (!upsertResult.Succeeded)
                {
                    return AcmeIssueResult.Failed(
                        message: upsertResult.Message,
                        errorCode: upsertResult.ErrorCode ?? "DnsChallengeUpsertFailed",
                        errorDetail: upsertResult.Message,
                        evidence: evidence);
                }

                // Keep the currently published RRset alive until the entire bounded ACME
                // sequence completes. The outer finally block performs best-effort cleanup.
                challengeCleanupRequest = challengeRequest;

                var readiness = await _dnsReadiness.WaitForReadyAsync(
                    fullRecordName,
                    txtValue,
                    progressCallback,
                    cancellationToken);

                evidence.AddRange(PrefixEvidence(
                    $"dns.readiness.attempt{validationAttempt}",
                    readiness.Evidence));

                if (!readiness.Ready)
                {
                    return AcmeIssueResult.Failed(
                        message: readiness.Message,
                        errorCode: readiness.ErrorCode ?? "DnsChallengeNotReady",
                        errorDetail: readiness.Message,
                        evidence: evidence);
                }

                var acmeValidationPhaseCode = validationAttempt == 1
                    ? "certificate.acme-validation"
                    : "certificate.acme-validation-retry";

                await ReportProgressAsync(
                    progressCallback,
                    acmeValidationPhaseCode,
                    validationAttempt == 1
                        ? "Let's Encrypt is validating the DNS challenge."
                        : "Let's Encrypt is validating the DNS challenge again after MEM's bounded DNS recovery window.",
                    false,
                    cancellationToken);

                evidence.Add(new CertificateOperationEvidence(
                    Key: "acmeChallengeValidation",
                    Value: $"requested; attempt {validationAttempt}",
                    Status: "Running"));

                _ = await RunAcmeRequestAsync(
                    () => dnsChallenge.Validate(),
                    "AcmeChallengeStartTimedOut",
                    "Starting Let's Encrypt DNS challenge validation timed out.",
                    cancellationToken);

                var challengeResource = await WaitForChallengeAsync(
                    dnsChallenge,
                    evidence,
                    progressCallback,
                    cancellationToken);

                if (challengeResource.Status != ChallengeStatus.Valid)
                {
                    var detail = challengeResource.Error?.Detail
                        ?? $"Challenge finished with status '{challengeResource.Status}'.";
                    var recoverableSecondaryDnsMiss = IsRecoverableSecondaryDnsValidationMiss(detail);

                    evidence.Add(new CertificateOperationEvidence(
                        Key: "acmeChallengeValidation",
                        Value: $"invalid; attempt {validationAttempt}",
                        Status: "Failed"));

                    if (recoverableSecondaryDnsMiss && validationAttempt < maxValidationAttempts)
                    {
                        await ReportProgressAsync(
                            progressCallback,
                            acmeValidationPhaseCode,
                            "Let's Encrypt secondary validation did not observe the TXT record. MEM is recovering automatically without requiring operator action.",
                            false,
                            cancellationToken,
                            CertificateIssueProgressTransition.Recovering);

                        evidence.Add(new CertificateOperationEvidence(
                            Key: "acmeSecondaryDnsRecovery",
                            Value: "secondary DNS validation missed the TXT record; one fresh order will be attempted after the full authoritative settling window is re-established",
                            Status: "Running"));

                        _logger.LogWarning(
                            "Let's Encrypt secondary DNS validation missed the TXT record for {Domain}. MEM will perform one bounded fresh-order recovery attempt without clearing the challenge RRset. Attempt={Attempt} MaxAttempts={MaxAttempts}",
                            request.Domain,
                            validationAttempt,
                            maxValidationAttempts);

                        await ReportProgressAsync(
                            progressCallback,
                            "certificate.acme-dns-recovery",
                            "Let's Encrypt secondary validation did not see the TXT record. MEM is keeping the challenge name populated and will perform one fresh-order retry after re-establishing the full authoritative DNS settling window.",
                            false,
                            cancellationToken);

                        continue;
                    }

                    var errorCode = recoverableSecondaryDnsMiss
                        ? "AcmeSecondaryDnsValidationNotConverged"
                        : "AcmeChallengeValidationFailed";

                    if (recoverableSecondaryDnsMiss)
                    {
                        evidence.Add(new CertificateOperationEvidence(
                            Key: "acmeSecondaryDnsRecovery",
                            Value: "bounded fresh-order recovery exhausted without secondary DNS convergence",
                            Status: "Failed"));
                    }

                    return AcmeIssueResult.Failed(
                        message: recoverableSecondaryDnsMiss
                            ? "Let's Encrypt secondary DNS validation still could not observe the challenge after MEM's bounded recovery attempt."
                            : "ACME DNS-01 challenge validation failed.",
                        errorCode: errorCode,
                        errorDetail: detail,
                        evidence: evidence);
                }

                evidence.Add(new CertificateOperationEvidence(
                    Key: "acmeChallengeValidation",
                    Value: $"valid; attempt {validationAttempt}",
                    Status: "Succeeded"));

                if (validationAttempt > 1)
                {
                    await ReportProgressAsync(
                        progressCallback,
                        "certificate.acme-validation",
                        "The earlier Let's Encrypt secondary DNS miss recovered automatically after MEM's bounded convergence window.",
                        false,
                        cancellationToken,
                        CertificateIssueProgressTransition.Recovered);

                    evidence.Add(new CertificateOperationEvidence(
                        Key: "acmeSecondaryDnsRecovery",
                        Value: $"recovered automatically on fresh-order attempt {validationAttempt}",
                        Status: "Succeeded"));
                }

                var privateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);
                var csrInfo = new CsrInfo
                {
                    CommonName = domainToRequest
                };

                await ReportProgressAsync(
                    progressCallback,
                    "certificate.acme-finalize",
                    "Finalizing the Let's Encrypt certificate order.",
                    false,
                    cancellationToken);

                _ = await RunAcmeRequestAsync(
                    () => orderContext.Finalize(csrInfo, privateKey),
                    "AcmeOrderFinalizeRequestTimedOut",
                    "Finalizing the Let's Encrypt certificate order timed out.",
                    cancellationToken);

                evidence.Add(new CertificateOperationEvidence(
                    Key: "acmeOrderFinalize",
                    Value: "requested",
                    Status: "Running"));

                var order = await WaitForOrderAsync(
                    orderContext,
                    evidence,
                    progressCallback,
                    cancellationToken);

                if (order.Status != OrderStatus.Valid)
                {
                    return AcmeIssueResult.Failed(
                        message: "ACME order did not become valid.",
                        errorCode: "AcmeOrderInvalid",
                        errorDetail: $"Order finished with status '{order.Status}'.",
                        evidence: evidence);
                }

                await ReportProgressAsync(
                    progressCallback,
                    "certificate.download",
                    "Downloading the issued TLS certificate from Let's Encrypt.",
                    false,
                    cancellationToken);

                var certificateChain = await RunAcmeRequestAsync(
                    () => orderContext.Download(),
                    "AcmeCertificateDownloadTimedOut",
                    "Downloading the issued TLS certificate timed out.",
                    cancellationToken);

                var certificatePem = ExportCertificateChainPem(certificateChain);
                var privateKeyPem = privateKey.ToPem();
                var certificateMetadata = ExtractCertificateMetadata(certificatePem);

                evidence.Add(new CertificateOperationEvidence(
                    Key: "certificateDownloaded",
                    Value: "fullchain.pem ready",
                    Status: "Succeeded"));
                evidence.Add(new CertificateOperationEvidence(
                    Key: "privateKey",
                    Value: "privkey.pem ready, sensitive",
                    Sensitive: true,
                    Status: "Succeeded"));

                if (certificateMetadata.ExpiresAtUtc is not null)
                {
                    evidence.Add(new CertificateOperationEvidence(
                        Key: "certificateExpiresAtUtc",
                        Value: certificateMetadata.ExpiresAtUtc.Value.ToString("O"),
                        Status: "Succeeded"));
                }

                if (!string.IsNullOrWhiteSpace(certificateMetadata.Thumbprint))
                {
                    evidence.Add(new CertificateOperationEvidence(
                        Key: "certificateThumbprint",
                        Value: certificateMetadata.Thumbprint,
                        Status: "Succeeded"));
                }

                return new AcmeIssueResult(
                    Succeeded: true,
                    Status: "Succeeded",
                    Message: "ACME certificate issued successfully.",
                    ErrorCode: null,
                    ErrorDetail: null,
                    CertificatePem: certificatePem,
                    PrivateKeyPem: privateKeyPem,
                    ExpiresAtUtc: certificateMetadata.ExpiresAtUtc,
                    Thumbprint: certificateMetadata.Thumbprint,
                    Evidence: evidence);
            }

            return AcmeIssueResult.Failed(
                message: "ACME DNS-01 challenge validation did not complete within the bounded retry policy.",
                errorCode: "AcmeDnsValidationAttemptsExhausted",
                errorDetail: null,
                evidence: evidence);
        }
        catch (AcmeOperationTimeoutException ex)
        {
            _logger.LogWarning(
                "Bounded ACME operation timed out for {Domain}. FailureClass={FailureClass}",
                request.Domain,
                ex.ErrorCode);

            evidence.Add(new CertificateOperationEvidence(
                Key: "acmeTimeout",
                Value: ex.SafeMessage,
                Status: "Failed"));

            return AcmeIssueResult.Failed(
                message: ex.SafeMessage,
                errorCode: ex.ErrorCode,
                errorDetail: ex.SafeMessage,
                evidence: evidence);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AcmeRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "ACME request failed while issuing certificate for {Domain}",
                request.Domain);

            evidence.Add(new CertificateOperationEvidence(
                Key: "acmeError",
                Value: ex.Error?.Detail ?? ex.Message,
                Status: "Failed"));

            return AcmeIssueResult.Failed(
                message: "ACME request failed.",
                errorCode: "AcmeRequestFailed",
                errorDetail: ex.Error?.Detail ?? ex.Message,
                evidence: evidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Certificate issue failed for {Domain}",
                request.Domain);

            evidence.Add(new CertificateOperationEvidence(
                Key: "exception",
                Value: ex.Message,
                Status: "Failed"));

            return AcmeIssueResult.Failed(
                message: "Certificate issue failed.",
                errorCode: "CertificateIssueException",
                errorDetail: ex.Message,
                evidence: evidence);
        }
        finally
        {
            if (challengeCleanupRequest is not null)
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    var cleanup = await _dnsProvider.DeleteTxtChallengeAsync(
                        challengeCleanupRequest,
                        cleanupTimeout.Token);

                    if (cleanup.Succeeded)
                    {
                        _logger.LogInformation(
                            "Best-effort ACME DNS challenge cleanup completed for {Domain} in zone {Zone}",
                            request.Domain,
                            request.Zone);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Best-effort ACME DNS challenge cleanup did not complete for {Domain} in zone {Zone}. FailureClass={FailureClass}",
                            request.Domain,
                            request.Zone,
                            cleanup.ErrorCode ?? "DnsChallengeCleanupFailed");
                    }
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(
                        cleanupException,
                        "Best-effort ACME DNS challenge cleanup failed for {Domain} in zone {Zone}",
                        request.Domain,
                        request.Zone);
                }
            }
        }
    }

    private async Task<Challenge> WaitForChallengeAsync(
        IChallengeContext challengeContext,
        List<CertificateOperationEvidence> evidence,
        CertificateIssueProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(
            Math.Max(15, _options.Value.AcmeChallengeValidationTimeoutSeconds));
        var poll = TimeSpan.FromSeconds(Math.Max(1, _options.Value.AcmePollSeconds));
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var challenge = await RunAcmeRequestAsync(
                () => challengeContext.Resource(),
                "AcmeChallengeStatusRequestTimedOut",
                "Reading Let's Encrypt DNS challenge status timed out.",
                cancellationToken);

            if (challenge.Status is ChallengeStatus.Valid or ChallengeStatus.Invalid)
            {
                return challenge;
            }

            evidence.Add(new CertificateOperationEvidence(
                Key: "acmeChallengeStatus",
                Value: challenge.Status.ToString(),
                Status: "Running"));

            await ReportProgressAsync(
                progressCallback,
                "certificate.acme-validation",
                "Let's Encrypt DNS challenge validation is still in progress.",
                true,
                cancellationToken);

            await Task.Delay(poll, cancellationToken);
        }

        throw new AcmeOperationTimeoutException(
            "AcmeChallengeValidationTimedOut",
            $"Let's Encrypt DNS challenge validation did not finish within {timeout.TotalSeconds:N0} seconds.");
    }

    private async Task<Order> WaitForOrderAsync(
        IOrderContext orderContext,
        List<CertificateOperationEvidence> evidence,
        CertificateIssueProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(
            Math.Max(15, _options.Value.AcmeOrderFinalizationTimeoutSeconds));
        var poll = TimeSpan.FromSeconds(Math.Max(1, _options.Value.AcmePollSeconds));
        var startedAt = DateTimeOffset.UtcNow;

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var order = await RunAcmeRequestAsync(
                () => orderContext.Resource(),
                "AcmeOrderStatusRequestTimedOut",
                "Reading Let's Encrypt certificate order status timed out.",
                cancellationToken);

            if (order.Status is OrderStatus.Valid or OrderStatus.Invalid)
            {
                return order;
            }

            evidence.Add(new CertificateOperationEvidence(
                Key: "acmeOrderStatus",
                Value: order.Status.ToString(),
                Status: "Running"));

            await ReportProgressAsync(
                progressCallback,
                "certificate.acme-finalize",
                "Let's Encrypt certificate order finalization is still in progress.",
                true,
                cancellationToken);

            await Task.Delay(poll, cancellationToken);
        }

        throw new AcmeOperationTimeoutException(
            "AcmeOrderFinalizationTimedOut",
            $"Let's Encrypt certificate order finalization did not finish within {timeout.TotalSeconds:N0} seconds.");
    }

    private async Task<T> RunAcmeRequestAsync<T>(
        Func<Task<T>> action,
        string timeoutErrorCode,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(5, _options.Value.AcmeRequestTimeoutSeconds));
        try
        {
            return await action().WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new AcmeOperationTimeoutException(timeoutErrorCode, timeoutMessage);
        }
    }

    private string ResolveAcmeDirectoryUrl(bool useStaging)
    {
        if (useStaging)
        {
            return !string.IsNullOrWhiteSpace(_options.Value.StagingDirectoryUrl)
                ? _options.Value.StagingDirectoryUrl
                : "https://acme-staging-v02.api.letsencrypt.org/directory";
        }

        return !string.IsNullOrWhiteSpace(_options.Value.ProductionDirectoryUrl)
            ? _options.Value.ProductionDirectoryUrl
            : "https://acme-v02.api.letsencrypt.org/directory";
    }

    internal static bool IsRecoverableSecondaryDnsValidationMiss(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            return false;
        }

        var normalized = detail.Trim();
        if (!normalized.Contains("secondary validation", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return normalized.Contains("no TXT record", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("incorrect TXT record", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("NXDOMAIN", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("DNS problem", StringComparison.OrdinalIgnoreCase);
    }

    private static Task ReportProgressAsync(
        CertificateIssueProgressCallback? callback,
        string phaseCode,
        string safeSummary,
        bool isHeartbeat,
        CancellationToken cancellationToken,
        CertificateIssueProgressTransition transition = CertificateIssueProgressTransition.None) =>
        callback is null
            ? Task.CompletedTask
            : callback(
                new CertificateIssueProgress(phaseCode, safeSummary, isHeartbeat, transition),
                cancellationToken);

    private static IReadOnlyList<CertificateOperationEvidence> PrefixEvidence(
        string prefix,
        IReadOnlyList<CertificateOperationEvidence> evidence) =>
        evidence.Select(item => item with { Key = $"{prefix}.{item.Key}" }).ToList();

    private static string ExportCertificateChainPem(CertificateChain certificateChain)
    {
        using var writer = new StringWriter();
        writer.WriteLine(certificateChain.Certificate.ToPem());
        foreach (var issuer in certificateChain.Issuers)
        {
            writer.WriteLine(issuer.ToPem());
        }

        return writer.ToString();
    }

    private static CertificateMetadata ExtractCertificateMetadata(string certificatePem)
    {
        try
        {
            var certBytes = Encoding.ASCII.GetBytes(certificatePem);
            var certificate = X509Certificate2.CreateFromPem(Encoding.ASCII.GetString(certBytes));

            return new CertificateMetadata(
                ExpiresAtUtc: certificate.NotAfter.ToUniversalTime(),
                Thumbprint: certificate.Thumbprint);
        }
        catch
        {
            return new CertificateMetadata(
                ExpiresAtUtc: null,
                Thumbprint: null);
        }
    }

    private sealed class AcmeOperationTimeoutException(
        string errorCode,
        string safeMessage) : Exception(safeMessage)
    {
        public string ErrorCode { get; } = errorCode;
        public string SafeMessage { get; } = safeMessage;
    }

    private sealed record CertificateMetadata(
        DateTimeOffset? ExpiresAtUtc,
        string? Thumbprint);
}
