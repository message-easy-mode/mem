using Microsoft.Extensions.Options;
using Modules.Shared.Domains.Certificates;
using Modules.Shared.Domains.Dns;

namespace Api.IntegrationTests.Setup;

public sealed class DnsChallengeObserverTests
{
    [Fact]
    public async Task CORR_04_authoritative_observer_requires_all_deSEC_servers_and_distinguishes_unavailable()
    {
        var query = new RecordingDnsTxtQueryClient(new Dictionary<string, DnsTxtQueryStatus>
        {
            ["ns1.desec.io"] = DnsTxtQueryStatus.Visible,
            ["ns2.desec.org"] = DnsTxtQueryStatus.Unavailable
        });
        var observer = new AuthoritativeDnsChallengeObserver(query);

        var result = await observer.CheckDesecTxtAsync(
            "_acme-challenge.deltabox.dev",
            "expected-value",
            CancellationToken.None);

        Assert.False(result.Visible);
        Assert.Equal(1, result.VisibleCount);
        Assert.Equal(1, result.ResponsiveCount);
        Assert.Equal(1, result.UnavailableCount);
        Assert.Contains(result.Evidence, x =>
            x.Key == "authoritativeDns.ns1.desec.io" && x.Status == "Succeeded");
        Assert.Contains(result.Evidence, x =>
            x.Key == "authoritativeDns.ns2.desec.org" && x.Status == "Unavailable");
    }

    [Fact]
    public async Task CORR_04_supplemental_public_observer_is_disabled_by_default()
    {
        var query = new RecordingDnsTxtQueryClient(new Dictionary<string, DnsTxtQueryStatus>());
        var observer = new PublicDnsChallengeObserver(
            query,
            Options.Create(new AcmeCertificateOptions()));

        var result = await observer.CheckTxtAsync(
            "_acme-challenge.deltabox.dev",
            "expected-value",
            CancellationToken.None);

        Assert.False(result.Visible);
        Assert.Equal("PublicDnsObservationNotConfigured", result.ErrorCode);
        Assert.Equal(0, query.QueryCalls);
        Assert.Contains(result.Evidence, x =>
            x.Key == "publicDnsObservation" && x.Status == "Skipped");
    }

    [Fact]
    public async Task CORR_04_supplemental_public_observer_only_queries_explicitly_configured_resolvers()
    {
        var query = new RecordingDnsTxtQueryClient(new Dictionary<string, DnsTxtQueryStatus>
        {
            ["resolver-a"] = DnsTxtQueryStatus.Visible,
            ["resolver-b"] = DnsTxtQueryStatus.Unavailable,
            ["resolver-c"] = DnsTxtQueryStatus.Visible
        });
        var options = Options.Create(new AcmeCertificateOptions
        {
            PublicDnsResolvers = ["resolver-a", "resolver-b", "resolver-c"],
            PublicDnsMinimumVisibleResolvers = 2
        });
        var observer = new PublicDnsChallengeObserver(query, options);

        var result = await observer.CheckTxtAsync(
            "_acme-challenge.deltabox.dev",
            "expected-value",
            CancellationToken.None);

        Assert.True(result.Visible);
        Assert.Equal(2, result.VisibleCount);
        Assert.Equal(2, result.ResponsiveCount);
        Assert.Equal(1, result.UnavailableCount);
        Assert.Equal(3, query.QueryCalls);
        Assert.Null(result.ErrorCode);
    }

    private sealed class RecordingDnsTxtQueryClient(
        IReadOnlyDictionary<string, DnsTxtQueryStatus> results) : IDnsTxtQueryClient
    {
        public int QueryCalls { get; private set; }

        public Task<DnsTxtQueryResult> QueryAsync(
            string server,
            string recordName,
            string expectedValue,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QueryCalls++;
            var status = results.TryGetValue(server, out var configured)
                ? configured
                : DnsTxtQueryStatus.Unavailable;

            return Task.FromResult(new DnsTxtQueryResult(server, status));
        }
    }
}
