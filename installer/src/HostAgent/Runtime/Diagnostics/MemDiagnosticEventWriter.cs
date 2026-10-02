using Microsoft.Extensions.Logging;
using Shared.Diagnostics;

namespace HostAgent.Runtime.Diagnostics;

public sealed class MemDiagnosticEventWriter : IMemDiagnosticEventWriter
{
    private readonly MemDiagnosticsOptions _options;
    private readonly MemDiagnosticEventFactory _factory;
    private readonly MemDiagnosticEventFileStore _store;
    private readonly MemDiagnosticHealthState _health;
    private readonly ILogger<MemDiagnosticEventWriter> _logger;

    public MemDiagnosticEventWriter(
        MemDiagnosticsOptions options,
        MemDiagnosticEventFactory factory,
        MemDiagnosticEventFileStore store,
        MemDiagnosticHealthState health,
        ILogger<MemDiagnosticEventWriter> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<MemDiagnosticWriteResult> WriteAsync(
        MemDiagnosticWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        MemDiagnosticEvent @event;
        try
        {
            @event = _factory.Create(request);
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            _logger.LogWarning(
                ex,
                "MEM diagnostic event projection failed. EventCode={EventCode}",
                MemDiagnosticRedactor.NormalizeIdentifier(request.EventCode, 200));

            try
            {
                @event = _factory.CreateFallback(request, ex);
            }
            catch (Exception fallbackException) when (fallbackException is not StackOverflowException and not OutOfMemoryException)
            {
                var eventId = $"evt_{Guid.NewGuid():N}";
                _health.RecordWriteFailure(MemDiagnosticCodes.EventMalformed);
                _logger.LogError(
                    fallbackException,
                    "MEM could not create even a minimal diagnostic event. EventId={EventId}",
                    eventId);
                return new MemDiagnosticWriteResult(
                    Stored: false,
                    EventId: eventId,
                    IncidentId: null,
                    WarningCode: MemDiagnosticCodes.EventMalformed,
                    TimestampUtc: null);
            }
        }

        if (!_options.Enabled)
        {
            return new MemDiagnosticWriteResult(
                Stored: false,
                EventId: @event.EventId,
                IncidentId: @event.IncidentId,
                WarningCode: MemDiagnosticCodes.StoreDisabled,
                TimestampUtc: @event.TimestampUtc);
        }

        try
        {
            await _store.AppendAsync(@event, cancellationToken);
            _health.RecordWriteSuccess(@event.TimestampUtc);
            return new MemDiagnosticWriteResult(
                Stored: true,
                EventId: @event.EventId,
                IncidentId: @event.IncidentId,
                WarningCode: null,
                TimestampUtc: @event.TimestampUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not StackOverflowException and not OutOfMemoryException)
        {
            _health.RecordWriteFailure(MemDiagnosticCodes.StoreWriteFailed);
            _logger.LogError(
                ex,
                "MEM safe diagnostic event could not be stored. EventId={EventId} EventCode={EventCode}",
                @event.EventId,
                @event.EventCode);
            return new MemDiagnosticWriteResult(
                Stored: false,
                EventId: @event.EventId,
                IncidentId: @event.IncidentId,
                WarningCode: MemDiagnosticCodes.StoreWriteFailed,
                TimestampUtc: @event.TimestampUtc);
        }
    }
}
