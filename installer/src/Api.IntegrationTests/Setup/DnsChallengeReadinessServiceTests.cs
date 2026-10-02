using Microsoft.Extensions.Options;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Dns;

namespace Api.IntegrationTests.Setup;

public sealed class DnsChallengeReadinessServiceTests
{
    [Fact]
    public async Task CORR_04_readiness_uses_deSEC_authoritative_gate_without_named_public_resolver_dependency()
    {
        var authoritative = new SequencedAuthoritativeObserver(
            waitResult: Visible("authoritative"),
            checkResults: [Visible("authoritative")]);
        var service = new DnsChallengeReadinessService(
            authoritative,
            Options.Create(new AcmeCertificateOptions
            {
                DnsVisibilityStabilitySeconds = 0
            }));
        var phases = new List<CertificateIssueProgress>();

        var result = await service.WaitForReadyAsync(
            "_acme-challenge.deltabox.dev",
            "expected-value",
            (progress, _) =>
            {
                phases.Add(progress);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(result.Ready, result.Message);
        Assert.Equal(
            ["certificate.dns-authoritative"],
            phases.Where(x => !x.IsHeartbeat).Select(x => x.PhaseCode).ToArray());
        Assert.DoesNotContain(phases, x => x.PhaseCode == "certificate.dns-public");
    }

    [Fact]
    public async Task CORR_04_readiness_requires_continuous_authoritative_settling_before_ACME()
    {
        var authoritative = new SequencedAuthoritativeObserver(
            waitResult: Visible("authoritative"),
            checkResults: [Visible("settle-1"), Visible("settle-2")]);
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero));
        var service = CreateDeterministicService(
            authoritative,
            new AcmeCertificateOptions
            {
                DnsVisibilityStabilitySeconds = 1,
                DnsVisibilityStabilityTimeoutSeconds = 4,
                DnsVisibilityStabilityPollSeconds = 1
            },
            clock);
        var phases = new List<CertificateIssueProgress>();

        var result = await service.WaitForReadyAsync(
            "_acme-challenge.deltabox.dev",
            "expected-value",
            (progress, _) =>
            {
                phases.Add(progress);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(result.Ready, result.Message);
        Assert.Contains(phases, x =>
            x.PhaseCode == "certificate.dns-stability" && !x.IsHeartbeat);
        Assert.Contains(result.Evidence, x =>
            x.Key == "dnsVisibilityStability" &&
            x.Status == "Succeeded" &&
            x.Value.Contains("1s", StringComparison.Ordinal));
        Assert.Contains(result.Evidence, x =>
            x.Key == "dnsValidationGate" &&
            x.Value.Contains("no named third-party recursive resolver dependency", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CORR_04_readiness_restarts_settling_window_when_authoritative_visibility_drops()
    {
        var authoritative = new SequencedAuthoritativeObserver(
            waitResult: Visible("authoritative"),
            checkResults:
            [
                NotVisible("settle-drop"),
                Visible("settle-recovered-1"),
                Visible("settle-recovered-2")
            ]);
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 8, 28, 10, 0, 0, TimeSpan.Zero));
        var service = CreateDeterministicService(
            authoritative,
            new AcmeCertificateOptions
            {
                DnsVisibilityStabilitySeconds = 1,
                DnsVisibilityStabilityTimeoutSeconds = 5,
                DnsVisibilityStabilityPollSeconds = 1
            },
            clock);
        var heartbeats = new List<CertificateIssueProgress>();

        var result = await service.WaitForReadyAsync(
            "_acme-challenge.deltabox.dev",
            "expected-value",
            (progress, _) =>
            {
                if (progress.IsHeartbeat)
                {
                    heartbeats.Add(progress);
                }

                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.True(result.Ready, result.Message);
        Assert.True(authoritative.CheckCalls >= 3);
        Assert.Contains(heartbeats, x =>
            x.SafeSummary.Contains("restarted the continuous stability window", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CORR_04_readiness_preserves_authoritative_visibility_failure_class()
    {
        var authoritative = new SequencedAuthoritativeObserver(
            waitResult: new DnsChallengeObservationResult(
                Visible: false,
                Message: "Authoritative DNS visibility timed out.",
                Evidence: [],
                ErrorCode: "AuthoritativeDnsVisibilityTimedOut",
                VisibleCount: 1,
                ResponsiveCount: 2,
                UnavailableCount: 0),
            checkResults: []);
        var service = new DnsChallengeReadinessService(
            authoritative,
            Options.Create(new AcmeCertificateOptions()));

        var result = await service.WaitForReadyAsync(
            "_acme-challenge.deltabox.dev",
            "expected-value",
            progressCallback: null,
            CancellationToken.None);

        Assert.False(result.Ready);
        Assert.Equal("AuthoritativeDnsVisibilityTimedOut", result.ErrorCode);
    }

    private static DnsChallengeReadinessService CreateDeterministicService(
        IAuthoritativeDnsChallengeObserver authoritative,
        AcmeCertificateOptions options,
        ManualTimeProvider clock) =>
        new(
            authoritative,
            Options.Create(options),
            clock,
            (delay, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                clock.Advance(delay);
                return Task.CompletedTask;
            });

    private sealed class ManualTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(duration));
            }

            _utcNow = _utcNow.Add(duration);
        }
    }

    private static DnsChallengeObservationResult Visible(string key) =>
        new(
            Visible: true,
            Message: "Visible.",
            Evidence: [new CertificateOperationEvidence(key, "visible", Status: "Succeeded")],
            ErrorCode: null,
            VisibleCount: 2,
            ResponsiveCount: 2,
            UnavailableCount: 0);

    private static DnsChallengeObservationResult NotVisible(string key) =>
        new(
            Visible: false,
            Message: "Not visible.",
            Evidence: [new CertificateOperationEvidence(key, "not visible", Status: "Pending")],
            ErrorCode: null,
            VisibleCount: 1,
            ResponsiveCount: 2,
            UnavailableCount: 0);

    private sealed class SequencedAuthoritativeObserver(
        DnsChallengeObservationResult waitResult,
        IReadOnlyList<DnsChallengeObservationResult> checkResults) : IAuthoritativeDnsChallengeObserver
    {
        private int _checkIndex;

        public int CheckCalls { get; private set; }

        public Task<DnsChallengeObservationResult> CheckDesecTxtAsync(
            string fullRecordName,
            string expectedValue,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckCalls++;

            if (checkResults.Count == 0)
            {
                return Task.FromResult(waitResult);
            }

            var index = Math.Min(_checkIndex, checkResults.Count - 1);
            _checkIndex++;
            return Task.FromResult(checkResults[index]);
        }

        public Task<DnsChallengeObservationResult> WaitForDesecTxtAsync(
            string fullRecordName,
            string expectedValue,
            TimeSpan timeout,
            TimeSpan pollInterval,
            CancellationToken cancellationToken,
            Func<CancellationToken, Task>? heartbeat = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(waitResult);
        }
    }
}
