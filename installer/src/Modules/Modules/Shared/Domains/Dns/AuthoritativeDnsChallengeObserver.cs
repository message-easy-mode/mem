using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Dns;

public sealed class AuthoritativeDnsChallengeObserver(
    IDnsTxtQueryClient queryClient) : IAuthoritativeDnsChallengeObserver
{
    private static readonly string[] DesecAuthoritativeServers =
    [
        "ns1.desec.io",
        "ns2.desec.org"
    ];

    public async Task<DnsChallengeObservationResult> CheckDesecTxtAsync(
        string fullRecordName,
        string expectedValue,
        CancellationToken cancellationToken)
    {
        var evidence = new List<CertificateOperationEvidence>();
        var visibleCount = 0;
        var responsiveCount = 0;
        var unavailableCount = 0;

        foreach (var server in DesecAuthoritativeServers)
        {
            var result = await queryClient.QueryAsync(
                server,
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
                Key: $"authoritativeDns.{server}",
                Value: result.Status switch
                {
                    DnsTxtQueryStatus.Visible => "TXT record visible",
                    DnsTxtQueryStatus.NotVisible => "TXT record not visible",
                    _ => "authoritative DNS server unavailable"
                },
                Sensitive: false,
                Status: result.Status switch
                {
                    DnsTxtQueryStatus.Visible => "Succeeded",
                    DnsTxtQueryStatus.NotVisible => "Pending",
                    _ => "Unavailable"
                }));
        }

        var visible = visibleCount == DesecAuthoritativeServers.Length;
        return new DnsChallengeObservationResult(
            Visible: visible,
            Message: visible
                ? "TXT challenge record is visible on all deSEC authoritative DNS servers."
                : responsiveCount == 0
                    ? "deSEC authoritative DNS servers are currently unavailable."
                    : "TXT challenge record is not yet visible on all deSEC authoritative DNS servers.",
            Evidence: evidence,
            ErrorCode: responsiveCount == 0 ? "AuthoritativeDnsServersUnavailable" : null,
            VisibleCount: visibleCount,
            ResponsiveCount: responsiveCount,
            UnavailableCount: unavailableCount);
    }

    public async Task<DnsChallengeObservationResult> WaitForDesecTxtAsync(
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
            last = await CheckDesecTxtAsync(
                fullRecordName,
                expectedValue,
                cancellationToken);

            anyResponsive |= last.ResponsiveCount > 0;

            if (last.Visible)
            {
                return last with
                {
                    Message = "TXT challenge record became visible on authoritative DNS.",
                    ErrorCode = null,
                    Evidence = last.Evidence
                        .Append(new CertificateOperationEvidence(
                            Key: "dnsPropagation",
                            Value: $"visible after {(DateTimeOffset.UtcNow - startedAt).TotalSeconds:N0}s",
                            Status: "Succeeded"))
                        .ToList()
                };
            }

            if (heartbeat is not null)
            {
                await heartbeat(cancellationToken);
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        var final = last ?? await CheckDesecTxtAsync(
            fullRecordName,
            expectedValue,
            cancellationToken);

        anyResponsive |= final.ResponsiveCount > 0;

        var errorCode = anyResponsive
            ? "AuthoritativeDnsVisibilityTimedOut"
            : "AuthoritativeDnsServersUnavailable";

        return final with
        {
            Visible = false,
            Message = anyResponsive
                ? "TXT challenge record was not visible on all authoritative DNS servers before timeout."
                : "deSEC authoritative DNS servers remained unavailable until the visibility timeout expired.",
            ErrorCode = errorCode,
            Evidence = final.Evidence
                .Append(new CertificateOperationEvidence(
                    Key: "dnsPropagation",
                    Value: $"timed out after {timeout.TotalSeconds:N0}s",
                    Status: "Failed"))
                .ToList()
        };
    }
}
