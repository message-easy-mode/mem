using System.Threading.Channels;
using Modules.Integrations.Seq.Services;

namespace Modules.Operator.Diagnostics.Services;

public sealed class SeqBootstrapWorkItem : IDisposable
{
    public SeqBootstrapWorkItem(
        Guid operationId,
        SeqBootstrapReviewSnapshot review,
        char[] administratorPassword,
        Guid? requestedByUserId,
        string requestedBy,
        char[]? connectionAdministratorPassword = null)
    {
        OperationId = operationId;
        Review = review ?? throw new ArgumentNullException(nameof(review));
        AdministratorPassword = administratorPassword ?? throw new ArgumentNullException(nameof(administratorPassword));
        ConnectionAdministratorPassword = connectionAdministratorPassword ?? [];
        RequestedByUserId = requestedByUserId;
        RequestedBy = requestedBy;
    }

    public Guid OperationId { get; }

    public SeqBootstrapReviewSnapshot Review { get; }

    public char[] AdministratorPassword { get; }

    public char[] ConnectionAdministratorPassword { get; }

    public Guid? RequestedByUserId { get; }

    public string RequestedBy { get; }

    public void Dispose()
    {
        Array.Clear(AdministratorPassword, 0, AdministratorPassword.Length);
        Array.Clear(
            ConnectionAdministratorPassword,
            0,
            ConnectionAdministratorPassword.Length);
    }
}

public sealed class SeqBootstrapOperationQueue
{
    private readonly Channel<SeqBootstrapWorkItem> _channel =
        Channel.CreateBounded<SeqBootstrapWorkItem>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    public bool TryEnqueue(SeqBootstrapWorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return _channel.Writer.TryWrite(item);
    }

    public IAsyncEnumerable<SeqBootstrapWorkItem> ReadAllAsync(
        CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
