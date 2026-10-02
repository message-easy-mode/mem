namespace Modules.Setup.HostChecks;

public sealed class HostCheckRunStore
{
    private readonly object _lock = new();
    private HostCheckRunResponse? _latest;

    public HostCheckRunResponse? GetLatest()
    {
        lock (_lock)
        {
            return _latest;
        }
    }

    public HostCheckRunResponse? GetById(string id)
    {
        lock (_lock)
        {
            return _latest?.Id == id ? _latest : null;
        }
    }

    public void SetLatest(HostCheckRunResponse run)
    {
        lock (_lock)
        {
            _latest = run;
        }
    }
}