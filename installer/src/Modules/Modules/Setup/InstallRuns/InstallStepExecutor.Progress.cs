namespace Modules.Setup.InstallRuns;

public sealed partial class InstallStepExecutor
{
    private Task BeginProgressPhaseAsync(
        InstallStepContext context,
        string phaseCode,
        string safeSummary,
        CancellationToken cancellationToken) =>
        _progressReporter?.BeginPhaseAsync(
            context,
            phaseCode,
            safeSummary,
            cancellationToken) ?? Task.CompletedTask;

    private Task HeartbeatProgressAsync(
        InstallStepContext context,
        string phaseCode,
        string safeSummary,
        CancellationToken cancellationToken) =>
        _progressReporter?.HeartbeatCurrentAsync(
            context.InstallationId,
            phaseCode,
            safeSummary,
            cancellationToken) ?? Task.CompletedTask;

    private async Task<T> RunWithCurrentProgressHeartbeatAsync<T>(
        InstallStepContext context,
        string safeSummary,
        Func<Task<T>> operation,
        CancellationToken cancellationToken,
        TimeSpan? heartbeatInterval = null)
    {
        var task = operation();
        var interval = heartbeatInterval ?? TimeSpan.FromSeconds(5);

        while (!task.IsCompleted)
        {
            var delay = Task.Delay(interval, cancellationToken);
            var completed = await Task.WhenAny(task, delay);
            if (completed == task)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (_progressReporter is not null)
            {
                await _progressReporter.HeartbeatCurrentAsync(
                    context.InstallationId,
                    safeSummary,
                    cancellationToken);
            }
        }

        return await task;
    }

    private async Task<T> RunWithProgressHeartbeatAsync<T>(
        InstallStepContext context,
        string phaseCode,
        string safeSummary,
        Func<Task<T>> operation,
        CancellationToken cancellationToken,
        TimeSpan? heartbeatInterval = null)
    {
        await BeginProgressPhaseAsync(
            context,
            phaseCode,
            safeSummary,
            cancellationToken);

        var task = operation();
        var interval = heartbeatInterval ?? TimeSpan.FromSeconds(5);

        while (!task.IsCompleted)
        {
            var delay = Task.Delay(interval, cancellationToken);
            var completed = await Task.WhenAny(task, delay);
            if (completed == task)
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();

            await HeartbeatProgressAsync(
                context,
                phaseCode,
                safeSummary,
                cancellationToken);
        }

        return await task;
    }
}
