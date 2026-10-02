using Microsoft.Extensions.Options;
using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Dns;

public sealed class PublicDnsChallengeObserver(
    IDnsTxtQueryClient queryClient,
    IOptions<AcmeCertificateOptions> options) : IPublicDnsChallengeObserver
{
    public async Task<DnsChallengeObservationResult> CheckTxtAsync(
        string fullRecordName,
        string expectedValue,
        CancellationToken cancellationToken)
    {
        var resolvers = ResolveResolvers();
        if (resolvers.Count == 0)
        {
            return new DnsChallengeObservationResult(
                Visible: false,
                Message: "No supplemental public DNS resolvers are configured.",
                Evidence:
                [
                    new CertificateOperationEvidence(
                        Key: "publicDnsObservation",
                        Value: "not configured",
                        Status: "Skipped")
                ],
                ErrorCode: "PublicDnsObservationNotConfigured");
        }

        var requiredVisible = Math.Clamp(
            options.Value.PublicDnsMinimumVisibleResolvers,
            1,
            resolvers.Count);

        var evidence = new List<CertificateOperationEvidence>();
        var visibleCount = 0;
        var responsiveCount = 0;
        var unavailableCount = 0;

        foreach (var resolver in resolvers)
        {
            var result = await queryClient.QueryAsync(
                resolver,
                fullRecordName,
                expectedValue,
                cancellationToken);

            switch (result.Status)
            {
                case DnsTxtQueryStatus.Visible:
                    visibleCount++;
                    responsiveCount++;
                    break;
                case DnsTxtQueryStatus.NotVisible:
                    responsiveCount++;
                    break;
                case DnsTxtQueryStatus.Unavailable:
                    unavailableCount++;
                    break;
            }

            evidence.Add(new CertificateOperationEvidence(
                Key: $"publicDns.{resolver}",
                Value: result.Status switch
                {
                    DnsTxtQueryStatus.Visible => "TXT record visible",
                    DnsTxtQueryStatus.NotVisible => "TXT record not visible",
                    _ => "resolver unavailable"
                },
                Sensitive: false,
                Status: result.Status switch
                {
                    DnsTxtQueryStatus.Visible => "Succeeded",
                    DnsTxtQueryStatus.NotVisible => "Pending",
                    _ => "Unavailable"
                }));
        }

        var visible = visibleCount >= requiredVisible;
        var message = visible
            ? $"TXT challenge record is visible through {visibleCount} independent public DNS resolver(s)."
            : responsiveCount == 0
                ? "Independent public DNS resolvers are currently unavailable."
                : $"TXT challenge record is visible through {visibleCount} of {requiredVisible} required independent public DNS resolver(s).";

        return new DnsChallengeObservationResult(
            Visible: visible,
            Message: message,
            Evidence: evidence,
            ErrorCode: responsiveCount == 0 ? "PublicDnsResolversUnavailable" : null,
            VisibleCount: visibleCount,
            ResponsiveCount: responsiveCount,
            UnavailableCount: unavailableCount);
    }

    public async Task<DnsChallengeObservationResult> WaitForTxtAsync(
        string fullRecordName,
        string expectedValue,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? heartbeat = null)
    {
        var startedAt = DateTimeOffset.UtcNow;
        DnsChallengeObservationResult? last = null;
        var anyResponsive = false;

        while (DateTimeOffset.UtcNow - startedAt < timeout)
        {
            last = await CheckTxtAsync(
                fullRecordName,
                expectedValue,
                cancellationToken);

            anyResponsive |= last.ResponsiveCount > 0;

            if (string.Equals(
                    last.ErrorCode,
                    "PublicDnsObservationNotConfigured",
                    StringComparison.Ordinal))
            {
                return last;
            }

            if (last.Visible)
            {
                var evidence = last.Evidence
                    .Append(new CertificateOperationEvidence(
                        Key: "publicDnsVisibility",
                        Value: $"visible after {(DateTimeOffset.UtcNow - startedAt).TotalSeconds:N0}s",
                        Status: "Succeeded"))
                    .ToList();

                return last with
                {
                    Message = "TXT challenge record became visible through independent public DNS resolvers.",
                    Evidence = evidence,
                    ErrorCode = null
                };
            }

            if (heartbeat is not null)
            {
                await heartbeat(cancellationToken);
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        var final = last ?? await CheckTxtAsync(
            fullRecordName,
            expectedValue,
            cancellationToken);

        anyResponsive |= final.ResponsiveCount > 0;

        var errorCode = anyResponsive
            ? "PublicDnsVisibilityTimedOut"
            : "PublicDnsResolversUnavailable";

        return final with
        {
            Visible = false,
            Message = anyResponsive
                ? "TXT challenge record did not become visible through enough independent public DNS resolvers before timeout."
                : "Independent public DNS resolvers remained unavailable until the visibility timeout expired.",
            ErrorCode = errorCode,
            Evidence = final.Evidence
                .Append(new CertificateOperationEvidence(
                    Key: "publicDnsVisibility",
                    Value: $"timed out after {timeout.TotalSeconds:N0}s",
                    Status: "Failed"))
                .ToList()
        };
    }

    private IReadOnlyList<string> ResolveResolvers()
    {
        var configured = options.Value.PublicDnsResolvers
            ?.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return configured is { Length: > 0 }
            ? configured
            : [];
    }
}
