using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics;

public sealed class MemDiagnosticEventReader : IMemDiagnosticEventReader
{
    private readonly MemDiagnosticsOptions _options;
    private readonly MemDiagnosticEventFileStore _store;
    private readonly MemDiagnosticHealthState _health;
    private readonly ILogger<MemDiagnosticEventReader> _logger;

    public MemDiagnosticEventReader(
        MemDiagnosticsOptions options,
        MemDiagnosticEventFileStore store,
        MemDiagnosticHealthState health,
        ILogger<MemDiagnosticEventReader> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MemDiagnosticEventPage> QueryAsync(
        MemDiagnosticQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_options.Enabled)
        {
            return _store.CreateEmptyPage(
                query,
                MemDiagnosticCodes.StoreDisabled);
        }

        try
        {
            return await _store.QueryAsync(query, cancellationToken);
        }
        catch (MemDiagnosticCursorException)
        {
            throw;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            _health.RecordReadWarning(MemDiagnosticCodes.StoreReadFailed);
            _logger.LogError(
                ex,
                "MEM safe diagnostic events could not be queried.");
            return _store.CreateEmptyPage(
                query,
                MemDiagnosticCodes.StoreReadFailed);
        }
    }
}
