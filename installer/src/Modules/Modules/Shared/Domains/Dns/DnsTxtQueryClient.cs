using System.Net;
using DnsClient;
using DnsClient.Protocol;
using Microsoft.Extensions.Logging;

namespace Modules.Shared.Domains.Dns;

public enum DnsTxtQueryStatus
{
    Visible,
    NotVisible,
    Unavailable
}

public sealed record DnsTxtQueryResult(
    string Server,
    DnsTxtQueryStatus Status);

public interface IDnsTxtQueryClient
{
    Task<DnsTxtQueryResult> QueryAsync(
        string server,
        string recordName,
        string expectedValue,
        CancellationToken cancellationToken);
}

public sealed class DnsTxtQueryClient(
    ILogger<DnsTxtQueryClient> logger) : IDnsTxtQueryClient
{
    public async Task<DnsTxtQueryResult> QueryAsync(
        string server,
        string recordName,
        string expectedValue,
        CancellationToken cancellationToken)
    {
        try
        {
            var address = await ResolveAddressAsync(server, cancellationToken);
            if (address is null)
            {
                return new DnsTxtQueryResult(server, DnsTxtQueryStatus.Unavailable);
            }

            var lookup = new LookupClient(
                new LookupClientOptions(new IPEndPoint(address, 53))
                {
                    Timeout = TimeSpan.FromSeconds(5),
                    Retries = 1,
                    UseCache = false
                });
            var query = await lookup.QueryAsync(
                recordName,
                QueryType.TXT,
                cancellationToken: cancellationToken);

            var visible = query.Answers
                .OfType<TxtRecord>()
                .SelectMany(record => record.Text)
                .Contains(expectedValue, StringComparer.Ordinal);

            return new DnsTxtQueryResult(
                server,
                visible ? DnsTxtQueryStatus.Visible : DnsTxtQueryStatus.NotVisible);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(
                ex,
                "DNS TXT observation failed for {RecordName} through {DnsServer}",
                recordName,
                server);

            return new DnsTxtQueryResult(server, DnsTxtQueryStatus.Unavailable);
        }
    }

    private static async Task<IPAddress?> ResolveAddressAsync(
        string server,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(server, out var parsed))
        {
            return parsed;
        }

        var addresses = await System.Net.Dns.GetHostAddressesAsync(
            server,
            cancellationToken);

        return addresses.FirstOrDefault();
    }
}
