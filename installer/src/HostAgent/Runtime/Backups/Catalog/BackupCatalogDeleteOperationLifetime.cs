using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HostAgent.Runtime.Backups.Catalog;

/// <summary>
/// Owns the bounded server-side lifetime for one accepted permanent Backup Catalog deletion.
///
/// Entry existence and active-restore preconditions are checked under the initiating HTTP
/// request. Once those preconditions pass, deleting the owned archive/export/payload files
/// and catalog row is a server-owned destructive mutation. Browser navigation, reverse-proxy
/// disconnect, or fetch cancellation must not strand that mutation halfway through.
/// </summary>
public sealed class BackupCatalogDeleteOperationLifetime
{
    internal static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(5);

    private readonly IHostApplicationLifetime _applicationLifetime;
    private readonly ILogger<BackupCatalogDeleteOperationLifetime> _logger;
    private readonly TimeSpan _operationTimeout;

    public BackupCatalogDeleteOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<BackupCatalogDeleteOperationLifetime> logger)
        : this(applicationLifetime, logger, DefaultOperationTimeout)
    {
    }

    internal BackupCatalogDeleteOperationLifetime(
        IHostApplicationLifetime applicationLifetime,
        ILogger<BackupCatalogDeleteOperationLifetime> logger,
        TimeSpan operationTimeout)
    {
        ArgumentNullException.ThrowIfNull(applicationLifetime);
        ArgumentNullException.ThrowIfNull(logger);

        if (operationTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(operationTimeout),
                operationTimeout,
                "Backup Catalog delete operation timeout must be greater than zero.");
        }

        _applicationLifetime = applicationLifetime;
        _logger = logger;
        _operationTimeout = operationTimeout;
    }

    internal BackupCatalogDeleteOperationScope BeginAfterAcceptance(
        string catalogEntryId,
        CancellationToken requestAborted)
    {
        var timeoutSource = new CancellationTokenSource(_operationTimeout);
        var operationSource = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutSource.Token,
            _applicationLifetime.ApplicationStopping);

        var requestAbortRegistration = requestAborted.Register(() =>
        {
            _logger.LogWarning(
                "Backup Catalog permanent-delete HTTP request was aborted after destructive deletion began. Deletion will continue until completion, operation timeout, or application shutdown. CatalogEntryId={CatalogEntryId} OperationTimeoutMinutes={OperationTimeoutMinutes}",
                catalogEntryId,
                _operationTimeout.TotalMinutes);
        });

        return new BackupCatalogDeleteOperationScope(
            operationSource,
            timeoutSource,
            requestAbortRegistration,
            requestAborted,
            _applicationLifetime.ApplicationStopping);
    }
}

internal sealed class BackupCatalogDeleteOperationScope : IDisposable
{
    private readonly CancellationTokenSource _operationSource;
    private readonly CancellationTokenSource _timeoutSource;
    private readonly CancellationTokenRegistration _requestAbortRegistration;
    private readonly CancellationToken _requestAborted;
    private readonly CancellationToken _applicationStopping;

    public BackupCatalogDeleteOperationScope(
        CancellationTokenSource operationSource,
        CancellationTokenSource timeoutSource,
        CancellationTokenRegistration requestAbortRegistration,
        CancellationToken requestAborted,
        CancellationToken applicationStopping)
    {
        _operationSource = operationSource;
        _timeoutSource = timeoutSource;
        _requestAbortRegistration = requestAbortRegistration;
        _requestAborted = requestAborted;
        _applicationStopping = applicationStopping;
    }

    public CancellationToken CancellationToken => _operationSource.Token;

    public bool RequestAborted => _requestAborted.IsCancellationRequested;

    public bool OperationTimeoutRequested => _timeoutSource.IsCancellationRequested;

    public bool ApplicationStoppingRequested => _applicationStopping.IsCancellationRequested;

    public void Dispose()
    {
        _requestAbortRegistration.Dispose();
        _operationSource.Dispose();
        _timeoutSource.Dispose();
    }
}
