using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Shared.Domains.Dns;

namespace Api.IntegrationTests.Setup;

public sealed class DesecDnsChallengeProviderMutationTests
{
    [Fact]
    public async Task CORR_03_existing_TXT_RRset_is_replaced_directly_without_an_empty_record_window()
    {
        var handler = new SequentialHttpHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"subname\":\"_acme-challenge\",\"type\":\"TXT\",\"ttl\":3600,\"records\":[\"\\\"old-value\\\"\"]}",
                    Encoding.UTF8,
                    "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"subname\":\"_acme-challenge\",\"type\":\"TXT\",\"ttl\":3600,\"records\":[\"\\\"new-value\\\"\"]}",
                    Encoding.UTF8,
                    "application/json")
            });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://desec.io/api/v1/")
        };
        var provider = new DesecDnsChallengeProvider(
            client,
            NullLogger<DesecDnsChallengeProvider>.Instance);

        var result = await provider.UpsertTxtChallengeAsync(
            new DnsChallengeRequest(
                Zone: "deltabox.dev",
                RecordName: "_acme-challenge",
                TxtValue: "new-value",
                ProviderToken: "test-token"),
            CancellationToken.None);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
        Assert.DoesNotContain("\"records\":[]", handler.Requests[1].Body ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("new-value", handler.Requests[1].Body ?? string.Empty, StringComparison.Ordinal);
    }

    private sealed class SequentialHttpHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                body));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No fake deSEC response remains.");
            }

            return _responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Uri,
        string? Body);
}
