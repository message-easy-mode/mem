namespace Api.Logging;

/// <summary>
/// Keeps a small in-memory record of Serilog internal failures. It is not yet
/// exposed through an API; a later diagnostics slice will project this state
/// through the authorized logging-health contract.
/// </summary>
public sealed class MemSerilogSelfLogState
{
    private const int Capacity = 100;
    private readonly object _gate = new();
    private readonly Queue<string> _messages = new();

    public void Record(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var normalized = message.Replace('\r', ' ').Replace('\n', ' ').Trim();

        lock (_gate)
        {
            _messages.Enqueue(normalized);
            while (_messages.Count > Capacity)
            {
                _messages.Dequeue();
            }
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return _messages.ToArray();
        }
    }
}
