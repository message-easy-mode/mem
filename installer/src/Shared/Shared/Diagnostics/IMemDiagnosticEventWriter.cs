namespace Shared.Diagnostics;

public interface IMemDiagnosticEventWriter
{
    Task<MemDiagnosticWriteResult> WriteAsync(
        MemDiagnosticWriteRequest request,
        CancellationToken cancellationToken = default);
}
