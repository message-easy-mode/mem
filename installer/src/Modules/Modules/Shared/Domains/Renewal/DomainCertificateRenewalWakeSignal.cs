using System.Threading.Channels;

namespace Modules.Shared.Domains.Renewal;

/// <summary>
/// Process-local coalescing wake signal for operator-requested renewal work.
/// The durable RuntimeOperation remains the authority; this only avoids waiting
/// for the normal hourly scan after Renew now is accepted.
/// </summary>
public sealed class DomainCertificateRenewalWakeSignal
{
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });

    public void Signal() => _signals.Writer.TryWrite(true);

    public async ValueTask WaitAsync(CancellationToken cancellationToken)
    {
        _ = await _signals.Reader.ReadAsync(cancellationToken);
    }
}
