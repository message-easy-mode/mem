namespace Mem.Migrate.Web.Assessment;

internal sealed class SourceStackSelectionStore
{
    private readonly object _sync = new();
    private string? _assessmentId;
    private string? _sourceStackId;

    public string? GetSelectedSourceStackId(string assessmentId)
    {
        lock (_sync)
        {
            return string.Equals(_assessmentId, assessmentId, StringComparison.Ordinal)
                ? _sourceStackId
                : null;
        }
    }

    public void Select(string assessmentId, string sourceStackId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assessmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceStackId);

        lock (_sync)
        {
            _assessmentId = assessmentId;
            _sourceStackId = sourceStackId;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _assessmentId = null;
            _sourceStackId = null;
        }
    }
}
