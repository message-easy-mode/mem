using Mem.Migrate.Application.Workflow;

namespace Mem.Migrate.Web.Workflow;

public sealed class SourcePackageDownloadCoordinator(
    ISourcePackageLifecycleService lifecycleService)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, int> _activeDownloads =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _deleting =
        new(StringComparer.Ordinal);

    public async Task<SourcePackageDownloadLease> BeginAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_deleting.Contains(workflowId))
            {
                throw new SourceWorkflowConflictException(
                    "The local encrypted package is currently being deleted.");
            }

            _activeDownloads.TryGetValue(workflowId, out var count);
            _activeDownloads[workflowId] = count + 1;
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            var descriptor = await lifecycleService.GetPackageDownloadAsync(
                workflowId,
                cancellationToken);
            return new SourcePackageDownloadLease(
                descriptor,
                () => Release(workflowId));
        }
        catch
        {
            Release(workflowId);
            throw;
        }
    }

    public async Task<SourceWorkflowView> DeleteAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_activeDownloads.TryGetValue(workflowId, out var count) &&
                count > 0)
            {
                throw new SourceWorkflowConflictException(
                    "The local encrypted package cannot be deleted while a browser download is active.");
            }

            if (!_deleting.Add(workflowId))
            {
                throw new SourceWorkflowConflictException(
                    "The local encrypted package is already being deleted.");
            }
        }
        finally
        {
            _gate.Release();
        }

        try
        {
            return await lifecycleService.DeletePackageAsync(
                workflowId,
                cancellationToken);
        }
        finally
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                _deleting.Remove(workflowId);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    private void Release(string workflowId)
    {
        _gate.Wait();
        try
        {
            if (!_activeDownloads.TryGetValue(workflowId, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                _activeDownloads.Remove(workflowId);
            }
            else
            {
                _activeDownloads[workflowId] = count - 1;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class SourcePackageDownloadLease(
    SourcePackageDownloadDescriptor descriptor,
    Action release) : IDisposable
{
    private Action? _release = release;

    public SourcePackageDownloadDescriptor Descriptor { get; } = descriptor;

    public void Dispose()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
