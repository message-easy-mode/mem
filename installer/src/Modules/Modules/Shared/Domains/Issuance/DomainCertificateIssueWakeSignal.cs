using System.Threading.Channels;

namespace Modules.Shared.Domains.Issuance;

public sealed class DomainCertificateIssueWakeSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropWrite,
        SingleReader = false,
        SingleWriter = false
    });

    public void Signal() => _channel.Writer.TryWrite(true);

    public ValueTask<bool> WaitAsync(CancellationToken cancellationToken) =>
        _channel.Reader.WaitToReadAsync(cancellationToken);

    public void Drain()
    {
        while (_channel.Reader.TryRead(out _))
        {
        }
    }
}
