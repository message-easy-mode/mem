using System.Net;
using Microsoft.Extensions.Logging;
using Modules.Integrations.Npm.Services;

namespace Modules.Shared.Domains.Certificates.Npm;

public interface INpmCertificateDeletionService
{
    Task<NpmCertificateDeletionResult> DeleteIfUnusedAsync(
        int certificateId,
        CancellationToken cancellationToken);
}

public sealed record NpmCertificateDeletionResult(
    bool Succeeded,
    string Status,
    string Message,
    int CertificateId,
    IReadOnlyList<int> ConsumerProxyHostIds);

public sealed class NpmCertificateDeletionService(
    NpmProxyHostService proxyHostService,
    NpmApiClient npmApiClient,
    INpmTokenProvider tokenProvider,
    NpmApiBaseUrlResolver baseUrlResolver,
    ILogger<NpmCertificateDeletionService> logger)
    : INpmCertificateDeletionService
{
    public async Task<NpmCertificateDeletionResult> DeleteIfUnusedAsync(
        int certificateId,
        CancellationToken cancellationToken)
    {
        if (certificateId <= 0)
        {
            return new NpmCertificateDeletionResult(
                Succeeded: true,
                Status: "NotApplicable",
                Message: "No NPM certificate record is linked.",
                CertificateId: certificateId,
                ConsumerProxyHostIds: []);
        }

        try
        {
            var consumers = (await proxyHostService.ListAsync(cancellationToken))
                .Where(host => (host.certificate_id ?? 0) == certificateId)
                .OrderBy(host => host.id)
                .Select(host => host.id)
                .ToArray();

            if (consumers.Length > 0)
            {
                return new NpmCertificateDeletionResult(
                    Succeeded: false,
                    Status: "Blocked",
                    Message: "The NPM certificate is still referenced by one or more proxy hosts.",
                    CertificateId: certificateId,
                    ConsumerProxyHostIds: consumers);
            }

            var resolution = await baseUrlResolver.ResolveAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(resolution.Warning))
            {
                logger.LogDebug(
                    "NPM base URL resolved from {Source}. Warning={Warning}",
                    resolution.Source,
                    resolution.Warning);
            }

            await ExecuteWithTokenRetryAsync(
                async token =>
                {
                    await npmApiClient.DeleteCertificateAsync(
                        resolution.BaseUrl,
                        token,
                        certificateId,
                        cancellationToken);
                    return true;
                },
                resolution.BaseUrl,
                cancellationToken);

            logger.LogInformation(
                "Deleted unused NPM certificate record {CertificateId}.",
                certificateId);

            return new NpmCertificateDeletionResult(
                Succeeded: true,
                Status: "Deleted",
                Message: "NPM certificate record was deleted.",
                CertificateId: certificateId,
                ConsumerProxyHostIds: []);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogInformation(
                "NPM certificate record {CertificateId} was already absent.",
                certificateId);

            return new NpmCertificateDeletionResult(
                Succeeded: true,
                Status: "AlreadyAbsent",
                Message: "NPM certificate record was already absent.",
                CertificateId: certificateId,
                ConsumerProxyHostIds: []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to inspect or delete NPM certificate record {CertificateId}.",
                certificateId);

            return new NpmCertificateDeletionResult(
                Succeeded: false,
                Status: "Failed",
                Message: "NPM certificate cleanup failed.",
                CertificateId: certificateId,
                ConsumerProxyHostIds: []);
        }
    }

    private async Task<T> ExecuteWithTokenRetryAsync<T>(
        Func<string, Task<T>> action,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetTokenAsync(baseUrl, cancellationToken);

        try
        {
            return await action(token);
        }
        catch (HttpRequestException ex) when (
            ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            logger.LogWarning(
                ex,
                "NPM returned unauthorized/forbidden during certificate deletion; retrying with refreshed token.");

            tokenProvider.Invalidate();

            var refreshedToken = await tokenProvider.GetTokenAsync(
                baseUrl,
                cancellationToken);

            return await action(refreshedToken);
        }
    }
}
