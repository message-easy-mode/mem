namespace Shared.Diagnostics;

/// <summary>
/// Best-effort workflow instrumentation. Diagnostic storage must never change
/// the outcome of the workflow being observed.
/// </summary>
public static class MemWorkflowDiagnosticWriterExtensions
{
    public static async Task<MemDiagnosticWriteResult?> TryWriteWorkflowEventAsync(
        this IMemDiagnosticEventWriter? writer,
        MemDiagnosticWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (writer is null)
        {
            return null;
        }

        try
        {
            return await writer.WriteAsync(request, CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is not StackOverflowException &&
            exception is not OutOfMemoryException)
        {
            return new MemDiagnosticWriteResult(
                Stored: false,
                EventId: $"evt_{Guid.NewGuid():N}",
                IncidentId: null,
                WarningCode: MemDiagnosticCodes.StoreWriteFailed);
        }
    }
}
