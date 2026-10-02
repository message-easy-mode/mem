using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;

namespace Modules.Shared.Domains.Certificates;

public sealed class CertificateValidationService
{
    private readonly CertificateStorageService _storage;
    private readonly ILogger<CertificateValidationService> _logger;

    public CertificateValidationService(
        CertificateStorageService storage,
        ILogger<CertificateValidationService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public async Task<CertificateOperationResult> ValidateAsync(
        string certificateId,
        CancellationToken cancellationToken)
    {
        var metadata = await _storage.GetMetadataAsync(certificateId, cancellationToken);

        if (metadata is null)
        {
            return new CertificateOperationResult(
                Succeeded: false,
                Status: "Failed",
                Message: "Certificate metadata was not found.",
                ErrorCode: "CertificateMetadataNotFound",
                ErrorDetail: null,
                Evidence:
                [
                    new("certificateId", certificateId)
                ]);
        }

        var fullchainPath = _storage.GetFullchainPath(certificateId);
        var privateKeyPath = _storage.GetPrivateKeyPath(certificateId);

        var evidence = new List<CertificateOperationEvidence>
        {
            new("certificateId", certificateId),
            new("domain", metadata.Domain),
            new("fullchainPath", fullchainPath),
            new("privateKeyPath", "stored, sensitive", Sensitive: true)
        };

        if (!File.Exists(fullchainPath))
        {
            evidence.Add(new("fullchain.pem", "missing", Status: "Failed"));

            return Failed(
                "fullchain.pem was not found.",
                "FullchainMissing",
                evidence);
        }

        evidence.Add(new("fullchain.pem", "exists", Status: "Succeeded"));

        if (!File.Exists(privateKeyPath))
        {
            evidence.Add(new("privkey.pem", "missing", Sensitive: true, Status: "Failed"));

            return Failed(
                "privkey.pem was not found.",
                "PrivateKeyMissing",
                evidence);
        }

        evidence.Add(new("privkey.pem", "exists", Sensitive: true, Status: "Succeeded"));

        try
        {
            var fullchainPem = await File.ReadAllTextAsync(fullchainPath, cancellationToken);
            var privateKeyPem = await File.ReadAllTextAsync(privateKeyPath, cancellationToken);

            using var certificate = X509Certificate2.CreateFromPem(fullchainPem);

            evidence.Add(new("subject", certificate.Subject));
            evidence.Add(new("thumbprint", certificate.Thumbprint ?? "unknown"));
            evidence.Add(new("notBeforeUtc", certificate.NotBefore.ToUniversalTime().ToString("O")));
            evidence.Add(new("notAfterUtc", certificate.NotAfter.ToUniversalTime().ToString("O")));

            if (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
            {
                evidence.Add(new("expiry", "expired", Status: "Failed"));

                return Failed(
                    "Certificate is expired.",
                    "CertificateExpired",
                    evidence);
            }

            evidence.Add(new("expiry", "valid", Status: "Succeeded"));

            var nameMatches = CertificateNameMatchesDomain(certificate, metadata.Domain);

            evidence.Add(new CertificateOperationEvidence(
                Key: "nameMatch",
                Value: nameMatches ? "certificate matches requested domain" : "certificate does not match requested domain",
                Status: nameMatches ? "Succeeded" : "Failed"));

            if (!nameMatches)
            {
                return Failed(
                    "Certificate subject/SAN does not match the requested domain.",
                    "CertificateNameMismatch",
                    evidence);
            }

            var keyMatches = PrivateKeyMatchesCertificate(certificate, privateKeyPem);

            evidence.Add(new CertificateOperationEvidence(
                Key: "privateKeyMatch",
                Value: keyMatches ? "private key matches certificate" : "private key does not match certificate",
                Sensitive: true,
                Status: keyMatches ? "Succeeded" : "Failed"));

            if (!keyMatches)
            {
                return Failed(
                    "Private key does not match the certificate.",
                    "PrivateKeyMismatch",
                    evidence);
            }

            return new CertificateOperationResult(
                Succeeded: true,
                Status: "Succeeded",
                Message: "Certificate files exist, parse correctly, match the requested domain, are not expired, and the private key matches.",
                ErrorCode: null,
                ErrorDetail: null,
                Evidence: evidence);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to validate certificate {CertificateId}",
                certificateId);

            evidence.Add(new("parse", "failed", Status: "Warning"));

            return new CertificateOperationResult(
                Succeeded: false,
                Status: "Warning",
                Message: "Certificate files exist, but fullchain.pem could not be parsed as a real certificate.",
                ErrorCode: "CertificateParseFailed",
                ErrorDetail: ex.Message,
                Evidence: evidence);
        }
    }

    private static CertificateOperationResult Failed(
        string message,
        string errorCode,
        IReadOnlyList<CertificateOperationEvidence> evidence)
    {
        return new CertificateOperationResult(
            Succeeded: false,
            Status: "Failed",
            Message: message,
            ErrorCode: errorCode,
            ErrorDetail: message,
            Evidence: evidence);
    }

    private static bool CertificateNameMatchesDomain(
        X509Certificate2 certificate,
        string requestedDomain)
    {
        var normalizedRequested = NormalizeDomain(requestedDomain);

        var names = GetSubjectAlternativeNames(certificate)
            .Select(NormalizeDomain)
            .ToList();

        if (names.Count == 0)
        {
            var commonName = GetCommonName(certificate.Subject);
            if (!string.IsNullOrWhiteSpace(commonName))
            {
                names.Add(NormalizeDomain(commonName));
            }
        }

        return names.Any(name => DomainPatternMatches(name, normalizedRequested));
    }

    private static bool DomainPatternMatches(string pattern, string requestedDomain)
    {
        if (string.Equals(pattern, requestedDomain, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!pattern.StartsWith("*."))
        {
            return false;
        }

        var wildcardBase = pattern[2..];

        if (!requestedDomain.EndsWith("." + wildcardBase, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var leftPart = requestedDomain[..^(wildcardBase.Length + 1)];

        return !string.IsNullOrWhiteSpace(leftPart) &&
               !leftPart.Contains('.', StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetSubjectAlternativeNames(X509Certificate2 certificate)
    {
        var results = new List<string>();

        foreach (var extension in certificate.Extensions)
        {
            if (extension.Oid?.Value != "2.5.29.17")
            {
                continue;
            }

            var formatted = extension.Format(multiLine: true);

            var lines = formatted
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var line in lines)
            {
                const string dnsPrefix = "DNS Name=";

                if (line.StartsWith(dnsPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(line[dnsPrefix.Length..].Trim());
                }
            }
        }

        return results;
    }

    private static string? GetCommonName(string subject)
    {
        var parts = subject.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
            {
                return part[3..].Trim();
            }
        }

        return null;
    }

    private static string NormalizeDomain(string domain)
    {
        return domain
            .Trim()
            .TrimEnd('.')
            .ToLowerInvariant();
    }

    private static bool PrivateKeyMatchesCertificate(
        X509Certificate2 certificate,
        string privateKeyPem)
    {
        if (TryRsaPrivateKeyMatches(certificate, privateKeyPem, out var rsaResult))
        {
            return rsaResult;
        }

        if (TryEcdsaPrivateKeyMatches(certificate, privateKeyPem, out var ecdsaResult))
        {
            return ecdsaResult;
        }

        return false;
    }

    private static bool TryRsaPrivateKeyMatches(
        X509Certificate2 certificate,
        string privateKeyPem,
        out bool matches)
    {
        matches = false;

        using var certPublicKey = certificate.GetRSAPublicKey();

        if (certPublicKey is null)
        {
            return false;
        }

        try
        {
            using var privateKey = RSA.Create();
            privateKey.ImportFromPem(privateKeyPem);

            var certParams = certPublicKey.ExportParameters(includePrivateParameters: false);
            var privateParams = privateKey.ExportParameters(includePrivateParameters: false);

            matches = certParams.Modulus.SequenceEqual(privateParams.Modulus) &&
                      certParams.Exponent.SequenceEqual(privateParams.Exponent);

            return true;
        }
        catch
        {
            return true;
        }
    }

    private static bool TryEcdsaPrivateKeyMatches(
        X509Certificate2 certificate,
        string privateKeyPem,
        out bool matches)
    {
        matches = false;

        using var certPublicKey = certificate.GetECDsaPublicKey();

        if (certPublicKey is null)
        {
            return false;
        }

        try
        {
            using var privateKey = ECDsa.Create();
            privateKey.ImportFromPem(privateKeyPem);

            var certParams = certPublicKey.ExportParameters(includePrivateParameters: false);
            var privateParams = privateKey.ExportParameters(includePrivateParameters: false);

            matches = ByteArraysEqual(certParams.Q.X, privateParams.Q.X) &&
                      ByteArraysEqual(certParams.Q.Y, privateParams.Q.Y);

            return true;
        }
        catch
        {
            return true;
        }
    }

    private static bool ByteArraysEqual(byte[]? left, byte[]? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.SequenceEqual(right);
    }
}