using Mem.Migrate.Application.Workflow;

namespace Mem.Migrate.Web.Workflow;

public sealed class SourceWorkflowCoordinator(
    ISourceMigrationApplicationService service,
    ILogger<SourceWorkflowCoordinator> logger) : IDisposable
{
    private readonly object _sync = new();
    private CancellationTokenSource? _activeCancellation;
    private Task? _activeTask;
    private string? _activeWorkflowId;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _activeTask is { IsCompleted: false };
            }
        }
    }

    public async Task<SourceWorkflowView> StartCaptureAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        var current = await RequireAsync(workflowId, cancellationToken);
        if (!current.Actions.CanCreatePreviewCapture)
        {
            throw new SourceWorkflowConflictException(
                current.Actions.BlockedReason ??
                "A fresh preview capture cannot be started from the current workflow state.");
        }

        Start(
            workflowId,
            token => service.RunCaptureAsync(workflowId, token),
            "source capture");

        return await WaitForOperationStartAsync(
            workflowId,
            current,
            cancellationToken);
    }

    public async Task<SourceWorkflowView> StartPackageAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        var current = await RequireAsync(workflowId, cancellationToken);
        if (!current.Actions.CanCreatePackage)
        {
            throw new SourceWorkflowConflictException(
                current.Actions.BlockedReason ??
                "An encrypted package cannot be created from the current workflow state.");
        }

        Start(
            workflowId,
            token => service.RunPackageAsync(workflowId, token),
            "package creation");

        return await WaitForOperationStartAsync(
            workflowId,
            current,
            cancellationToken);
    }

    public async Task<SourceWorkflowView> CancelAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        CancellationTokenSource? cancellation;
        lock (_sync)
        {
            if (_activeTask is not { IsCompleted: false } ||
                !string.Equals(
                    _activeWorkflowId,
                    workflowId,
                    StringComparison.Ordinal))
            {
                throw new SourceWorkflowConflictException(
                    "No active source operation exists for this workflow.");
            }

            cancellation = _activeCancellation;
        }

        var view = await service.RequestCancellationAsync(
            workflowId,
            cancellationToken);
        cancellation?.Cancel();
        return view;
    }

    private async Task<SourceWorkflowView> WaitForOperationStartAsync(
        string workflowId,
        SourceWorkflowView previous,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var current = await service.GetAsync(workflowId, cancellationToken);
            if (current is not null &&
                (string.Equals(current.Status, "Running", StringComparison.OrdinalIgnoreCase) ||
                 current.UpdatedAtUtc > previous.UpdatedAtUtc))
            {
                return current;
            }

            lock (_sync)
            {
                if (_activeTask?.IsCompleted == true)
                {
                    throw new SourceWorkflowConflictException(
                        "The source operation could not be started. Refresh the workflow and review its current state.");
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }

        throw new SourceWorkflowConflictException(
            "The source operation did not enter a durable running state.");
    }

    private async Task<SourceWorkflowView> RequireAsync(
        string workflowId,
        CancellationToken cancellationToken)
    {
        return await service.GetAsync(workflowId, cancellationToken)
            ?? throw new SourceWorkflowNotFoundException(
                "The source workflow was not found.");
    }

    private void Start(
        string workflowId,
        Func<CancellationToken, Task> operation,
        string operationName)
    {
        lock (_sync)
        {
            if (_activeTask is { IsCompleted: false })
            {
                throw new SourceWorkflowConflictException(
                    "Another source capture or packaging operation is already running.");
            }

            _activeCancellation?.Dispose();
            _activeCancellation = new CancellationTokenSource();
            _activeWorkflowId = workflowId;
            var cancellationToken = _activeCancellation.Token;
            _activeTask = Task.Run(async () =>
            {
                try
                {
                    await operation(cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Unexpected Source Assistant {OperationName} coordinator failure.",
                        operationName);
                }
                finally
                {
                    lock (_sync)
                    {
                        _activeWorkflowId = null;
                    }
                }
            });
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _activeCancellation?.Cancel();
            _activeCancellation?.Dispose();
            _activeCancellation = null;
        }
    }
}
