using Mem.Migrate.Application.Assessment;

namespace Mem.Migrate.Web.Assessment;

internal sealed class SourceAssessmentCoordinator(
    ISourceAssessmentApplicationService assessments,
    SourceStackSelectionStore selectionStore)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    public Task<SourceAssessmentView?> GetLatestAsync(CancellationToken cancellationToken) =>
        assessments.GetLatestAsync(cancellationToken);

    public Task<SourceAssessmentReport?> GetLatestReportAsync(CancellationToken cancellationToken) =>
        assessments.GetLatestReportAsync(cancellationToken);

    public async Task<SourceAssessmentView> RunAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
        {
            throw new SourceAssessmentAlreadyRunningException();
        }

        Interlocked.Exchange(ref _running, 1);
        try
        {
            var result = await assessments.RunAsync(cancellationToken);
            selectionStore.Clear();
            return result;
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
            _gate.Release();
        }
    }
}

internal sealed class SourceAssessmentAlreadyRunningException : InvalidOperationException
{
    public SourceAssessmentAlreadyRunningException()
        : base("A source assessment is already running.")
    {
    }
}
