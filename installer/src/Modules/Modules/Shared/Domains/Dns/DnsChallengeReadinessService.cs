using Microsoft.Extensions.Options;
using Modules.Shared.Domains.Certificates;

namespace Modules.Shared.Domains.Dns;

public sealed class DnsChallengeReadinessService
{
    private readonly IAuthoritativeDnsChallengeObserver _authoritativeObserver;
    private readonly IOptions<AcmeCertificateOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    public DnsChallengeReadinessService(
        IAuthoritativeDnsChallengeObserver authoritativeObserver,
        IOptions<AcmeCertificateOptions> options)
        : this(
            authoritativeObserver,
            options,
            TimeProvider.System,
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken))
    {
    }

    internal DnsChallengeReadinessService(
        IAuthoritativeDnsChallengeObserver authoritativeObserver,
        IOptions<AcmeCertificateOptions> options,
        TimeProvider timeProvider,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        _authoritativeObserver = authoritativeObserver;
        _options = options;
        _timeProvider = timeProvider;
        _delayAsync = delayAsync;
    }
    public async Task<DnsChallengeReadinessResult> WaitForReadyAsync(
        string fullRecordName,
        string expectedValue,
        CertificateIssueProgressCallback? progressCallback,
        CancellationToken cancellationToken)
    {
        var evidence = new List<CertificateOperationEvidence>();
        var poll = TimeSpan.FromSeconds(Math.Max(1, _options.Value.DnsPropagationPollSeconds));
        var authoritativeTimeout = TimeSpan.FromSeconds(
            Math.Max(15, _options.Value.DnsPropagationTimeoutSeconds));

        await ReportProgressAsync(
            progressCallback,
            "certificate.dns-authoritative",
            "Waiting for the DNS challenge on all deSEC authoritative DNS servers.",
            isHeartbeat: false,
            cancellationToken);

        var authoritative = await _authoritativeObserver.WaitForDesecTxtAsync(
            fullRecordName,
            expectedValue,
            authoritativeTimeout,
            poll,
            cancellationToken,
            heartbeat: token => ReportProgressAsync(
                progressCallback,
                "certificate.dns-authoritative",
                "Still waiting for the DNS challenge on all deSEC authoritative DNS servers.",
                isHeartbeat: true,
                token));

        evidence.AddRange(PrefixEvidence("authoritative", authoritative.Evidence));

        if (!authoritative.Visible)
        {
            return new DnsChallengeReadinessResult(
                Ready: false,
                Message: authoritative.Message,
                ErrorCode: authoritative.ErrorCode ?? "AuthoritativeDnsVisibilityTimedOut",
                Evidence: evidence);
        }

        var stabilityWindow = TimeSpan.FromSeconds(
            Math.Max(0, _options.Value.DnsVisibilityStabilitySeconds));

        if (stabilityWindow == TimeSpan.Zero)
        {
            evidence.Add(new CertificateOperationEvidence(
                Key: "dnsVisibilityStability",
                Value: "disabled by configuration",
                Status: "Skipped"));

            return new DnsChallengeReadinessResult(
                Ready: true,
                Message: "The DNS challenge is visible on all deSEC authoritative DNS servers and is ready for ACME validation.",
                ErrorCode: null,
                Evidence: evidence);
        }

        await ReportProgressAsync(
            progressCallback,
            "certificate.dns-stability",
            $"Keeping the DNS challenge continuously visible on deSEC authoritative DNS for {stabilityWindow.TotalSeconds:N0} seconds before Let's Encrypt validation.",
            isHeartbeat: false,
            cancellationToken);

        var stabilityTimeout = TimeSpan.FromSeconds(Math.Max(
            _options.Value.DnsVisibilityStabilitySeconds,
            _options.Value.DnsVisibilityStabilityTimeoutSeconds));
        var stabilityPoll = TimeSpan.FromSeconds(
            Math.Max(1, _options.Value.DnsVisibilityStabilityPollSeconds));
        var startedAt = _timeProvider.GetUtcNow();
        DateTimeOffset? stableSince = startedAt;
        DnsChallengeObservationResult? lastAuthoritative = authoritative;

        while (_timeProvider.GetUtcNow() - startedAt < stabilityTimeout)
        {
            lastAuthoritative = await _authoritativeObserver.CheckDesecTxtAsync(
                fullRecordName,
                expectedValue,
                cancellationToken);

            if (lastAuthoritative.Visible)
            {
                stableSince ??= _timeProvider.GetUtcNow();
                var stableFor = _timeProvider.GetUtcNow() - stableSince.Value;
                if (stableFor >= stabilityWindow)
                {
                    evidence.AddRange(PrefixEvidence(
                        "stability.authoritative",
                        lastAuthoritative.Evidence));
                    evidence.Add(new CertificateOperationEvidence(
                        Key: "dnsVisibilityStability",
                        Value: $"authoritative TXT remained continuously visible for at least {stabilityWindow.TotalSeconds:N0}s",
                        Status: "Succeeded"));
                    evidence.Add(new CertificateOperationEvidence(
                        Key: "dnsValidationGate",
                        Value: "deSEC authoritative visibility plus bounded settling window; no named third-party recursive resolver dependency",
                        Status: "Succeeded"));

                    return new DnsChallengeReadinessResult(
                        Ready: true,
                        Message: "The DNS challenge remained continuously visible on deSEC authoritative DNS and is ready for ACME validation.",
                        ErrorCode: null,
                        Evidence: evidence);
                }
            }
            else
            {
                stableSince = null;
            }

            await ReportProgressAsync(
                progressCallback,
                "certificate.dns-stability",
                stableSince is null
                    ? "Authoritative DNS visibility changed while settling; MEM restarted the continuous stability window before Let's Encrypt validation."
                    : "The DNS challenge remains visible on deSEC authoritative DNS; MEM is continuing the bounded settling window before Let's Encrypt validation.",
                isHeartbeat: true,
                cancellationToken);

            await _delayAsync(stabilityPoll, cancellationToken);
        }

        if (lastAuthoritative is not null)
        {
            evidence.AddRange(PrefixEvidence(
                "stability.authoritative",
                lastAuthoritative.Evidence));
        }

        evidence.Add(new CertificateOperationEvidence(
            Key: "dnsVisibilityStability",
            Value: $"authoritative TXT did not remain continuously visible for {stabilityWindow.TotalSeconds:N0}s within {stabilityTimeout.TotalSeconds:N0}s",
            Status: "Failed"));

        return new DnsChallengeReadinessResult(
            Ready: false,
            Message: "The DNS challenge did not remain continuously visible on deSEC authoritative DNS long enough to start ACME validation safely.",
            ErrorCode: "DnsChallengeVisibilityNotStable",
            Evidence: evidence);
    }

    private static Task ReportProgressAsync(
        CertificateIssueProgressCallback? callback,
        string phaseCode,
        string safeSummary,
        bool isHeartbeat,
        CancellationToken cancellationToken) =>
        callback is null
            ? Task.CompletedTask
            : callback(
                new CertificateIssueProgress(phaseCode, safeSummary, isHeartbeat),
                cancellationToken);

    private static IReadOnlyList<CertificateOperationEvidence> PrefixEvidence(
        string prefix,
        IReadOnlyList<CertificateOperationEvidence> evidence) =>
        evidence.Select(item => item with { Key = $"{prefix}.{item.Key}" }).ToList();
}
